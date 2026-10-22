using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.ImportacionGenerica.Pages.Importar;

/// <summary>
/// Wizard de importación masiva -- Parámetros + archivo -> Previsualizar (bitácora de
/// errores por fila, agrupada por documento) -> Confirmar. Portado de Pages/Importar/
/// Index.cshtml.cs (referencia-original/PortalSAP_v2, wizard compartido Venta/Compra),
/// generalizado a los 3 módulos (Venta/Compra/Inventario) de este proyecto.
///
/// Sin sesión de servidor para el archivo entre Previsualizar y Confirmar (a diferencia
/// del original, que corría en el mismo proceso mono-tenant) -- el archivo viaja de
/// vuelta al navegador como base64 en un campo oculto y se reprocesa (mismo archivo,
/// misma configuración = mismo resultado determinístico) al confirmar.
///
/// "Confirmar" es un POST por fetch, no un submit de página completa (ver
/// Index.cshtml, btnConfirmar) -- con archivos de varios miles de filas, un postback
/// tradicional deja al usuario con la pestaña "colgada" (sin ningún indicio de avance)
/// durante todo CreateDocumentsAsync. El cliente genera un jobId, dispara OnPostConfirmAsync
/// y en paralelo consulta OnGetProgressAsync(jobId) por polling contra
/// IGenericImportProgressStore (actualizado por lote de líneas, no por documento, ver
/// GenericImportService.CreateDocumentsAsync) -- mismo patrón que
/// referencia-original/PortalSAP_v2 (Pages/Importar/Index.cshtml.cs, OnPostConfirmarAsync +
/// OnGetProgreso).
/// </summary>
public sealed class IndexModel : PageModelBaseAdmin
{
    protected override string MenuCode => "ImportacionGenerica.importar";

    private readonly IGenericImportService _importService;
    private readonly IGenericImportProgressStore _progress;
    private readonly IGenericImportConfigService _configs;
    private readonly ISalesDocumentService _sales;
    private readonly IPurchaseDocumentService _purchase;
    private readonly IInventoryDocumentService _inventory;
    private readonly ICustomerCatalogService _customers;
    private readonly ISupplierCatalogService _suppliers;
    private readonly IGenericImportUserFieldService _userFields;
    private readonly INotificationService _notifications;
    private readonly ILogger<IndexModel> _logger;

    private readonly ISalesEmployeeCatalogService _salesEmployees;
    private readonly IBusinessPartnerDefaultsService _partnerDefaults;
    private readonly IGenericImportJobService _jobs;
    private readonly ICustomerShipToAddressService _shipToAddresses;
    private readonly IWarehouseCatalogService _warehouses;

    public IndexModel(ICurrentUserContext currentUser, IGenericImportService importService, IGenericImportProgressStore progress,
        IGenericImportConfigService configs, ISalesDocumentService sales, IPurchaseDocumentService purchase, IInventoryDocumentService inventory,
        ICustomerCatalogService customers, ISupplierCatalogService suppliers, IGenericImportUserFieldService userFields,
        INotificationService notifications, ISalesEmployeeCatalogService salesEmployees, IBusinessPartnerDefaultsService partnerDefaults,
        IGenericImportJobService jobs, ICustomerShipToAddressService shipToAddresses, IWarehouseCatalogService warehouses,
        ILogger<IndexModel> logger) : base(currentUser)
    {
        _jobs = jobs;
        _shipToAddresses = shipToAddresses;
        _warehouses = warehouses;
        _salesEmployees = salesEmployees;
        _partnerDefaults = partnerDefaults;
        _importService = importService;
        _progress = progress;
        _configs = configs;
        _sales = sales;
        _purchase = purchase;
        _inventory = inventory;
        _customers = customers;
        _suppliers = suppliers;
        _userFields = userFields;
        _notifications = notifications;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> Modules { get; } =
    [
        new(ImportDisplayNames.Of(GenericImportModule.Sales), GenericImportModule.Sales.ToString()),
        new(ImportDisplayNames.Of(GenericImportModule.Purchase), GenericImportModule.Purchase.ToString()),
        new(ImportDisplayNames.Of(GenericImportModule.Inventory), GenericImportModule.Inventory.ToString()),
    ];

    public List<SelectListItem> LineTypes { get; } =
    [
        new(ImportDisplayNames.Of(GenericImportLineType.Item), GenericImportLineType.Item.ToString()),
        new(ImportDisplayNames.Of(GenericImportLineType.Service), GenericImportLineType.Service.ToString()),
    ];

    public List<SelectListItem> SalesDocumentTypes { get; private set; } = [];
    public List<SelectListItem> PurchaseDocumentTypes { get; private set; } = [];
    public List<SelectListItem> InventoryDocumentTypes { get; private set; } = [];

    public GenericImportResultDto? PreviewResult { get; private set; }

    /// <summary>
    /// Archivo ya cargado en el borrador del trabajo (Input.JobId) -- "Volver a analizar"
    /// lo reprocesa con los parámetros actuales sin subirlo de nuevo (ver OnPostProcessAsync).
    /// Null si no hay borrador vigente (sin procesar todavía, o ya confirmado).
    /// </summary>
    public string? LoadedFileName { get; private set; }

    /// <summary>
    /// Formatos disponibles para Módulo+Tipo de documento+Tipo de línea+Socio actuales
    /// (los del socio primero, después los estándar) -- un socio puede tener varios. El
    /// &lt;select&gt; se recarga por AJAX (OnGetFormatsAsync) al cambiar cualquiera de esos
    /// 4 campos; esto es solo el render inicial / tras un POST.
    /// </summary>
    public List<SelectListItem> Formats { get; private set; } = [];

    /// <summary>true = la configuración vigente para Módulo+TipoDocumento+TipoLínea trae el socio por columna del Excel (carga multi-socio) -- el selector de socio del wizard queda informativo, ver Index.cshtml.</summary>
    public bool BusinessPartnerFromFile { get; private set; }

    /// <summary>
    /// Carga guardada sin enviar que se está revalidando (?job={id}, borradores etapa 2) --
    /// sus parámetros quedan fijos (sin edición) y la vista previa se recalcula contra SAP
    /// con el mismo archivo. Null en el uso normal del asistente.
    /// </summary>
    public GenericImportJobDto? SavedJob { get; private set; }

    public async Task<IActionResult> OnGetAsync([FromQuery(Name = "job")] Guid? job, CancellationToken ct)
    {
        if (job is { } savedJobId)
        {
            var saved = await _jobs.GetAsync(savedJobId, ct);
            if (saved is null || !saved.IsSaved)
            {
                ErrorMessage = saved is null
                    ? "La carga guardada ya no existe (otro usuario la descartó o venció)."
                    : "Esa carga ya no está guardada: ya se envió a SAP.";
                return RedirectToPage("/Trabajos/ImportacionTrabajos", new { view = "guardadas" });
            }

            await PrepareSavedJobAsync(saved, ct);
            if (await _jobs.GetFileAsync(savedJobId, ct) is not { Length: > 0 } savedFile)
            {
                ErrorMessage = "El archivo de esta carga ya no está disponible.";
                return RedirectToPage("/Trabajos/ImportacionTrabajoDetalle", new { id = savedJobId });
            }

            using var savedStream = new MemoryStream(savedFile);
            PreviewResult = await _importService.ProcessFileAsync(BuildParameters(), savedStream, ct);
            await LoadPreviewHeaderInfoAsync(ct);
            return Page();
        }

        await LoadWizardAsync(ct);
        return Page();
    }

    private async Task LoadWizardAsync(CancellationToken ct)
    {
        await LoadCreatableDocumentTypesAsync(ct);
        await LoadFormatsAsync(ct);
        await LoadSalesPersonAsync(ct);
        await ResolveBusinessPartnerFromFileAsync(ct);
        await LoadScreenHeaderAsync(ct);
    }

    /// <summary>
    /// Fija el asistente con los parámetros de la carga guardada. Se aplican antes y después
    /// de cargar los combos: los combos pueden "corregir" una elección (ej. el vendedor por
    /// defecto del socio cambió en SAP), pero una carga guardada se revalida y se envía
    /// EXACTAMENTE con lo que se guardó.
    /// </summary>
    private async Task PrepareSavedJobAsync(GenericImportJobDto saved, CancellationToken ct)
    {
        SavedJob = saved;
        ApplyJobToInput(saved);
        await LoadWizardAsync(ct);
        ApplyJobToInput(saved);
    }

    private void ApplyJobToInput(GenericImportJobDto job)
    {
        var p = job.Parameters;
        Input.Module = p.Module;
        Input.SalesDocumentType = p.Module == GenericImportModule.Sales ? p.DocumentType : null;
        Input.PurchaseDocumentType = p.Module == GenericImportModule.Purchase ? p.DocumentType : null;
        Input.InventoryDocumentType = p.Module == GenericImportModule.Inventory ? p.DocumentType : null;
        Input.LineType = p.LineType;
        Input.BusinessPartnerCardCode = p.BusinessPartnerCardCode;
        Input.ConfigId = p.ConfigId;
        Input.SalesPersonCode = p.SalesPersonCode;
        Input.ShipToCode = p.ShipToCode;
        Input.WarehouseCode = p.WarehouseCode;
        Input.JobId = job.Id;
        ModelState.Clear();
    }

    /// <summary>
    /// Formatos disponibles para la combinación + socio que el usuario tiene elegidos en
    /// pantalla -- llamado por fetch desde Index.cshtml (cargarFormatos) cada vez que
    /// cambia Módulo, Tipo de documento, Tipo de línea o Socio.
    /// </summary>
    public async Task<JsonResult> OnGetFormatsAsync(GenericImportModule module, string? documentType, GenericImportLineType lineType,
        string? businessPartnerCardCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(documentType))
        {
            return new JsonResult(Array.Empty<object>());
        }

        var formats = await _configs.ListAvailableAsync(module, documentType, lineType, businessPartnerCardCode, ct);
        return new JsonResult(formats.Select(f => new { f.Id, Label = FormatLabel(f) }));
    }

    private static string FormatLabel(GenericImportConfigDto format) =>
        format.BusinessPartnerCardCode is null
            ? $"{format.Alias} (estándar)"
            : $"{format.Alias} ({format.BusinessPartnerCardCode})";

    private async Task LoadFormatsAsync(CancellationToken ct)
    {
        var documentType = SelectedDocumentType();
        if (string.IsNullOrWhiteSpace(documentType))
        {
            Formats = [];
            return;
        }

        var formats = await _configs.ListAvailableAsync(Input.Module, documentType, Input.LineType, Input.BusinessPartnerCardCode, ct);
        Formats = formats.Select(f => new SelectListItem(FormatLabel(f), f.Id.ToString())).ToList();

        // Sin elección previa (o la elección ya no aplica a esta combinación/socio): el
        // primero de la lista -- mismo que elegiría la resolución automática.
        if (Input.ConfigId is null || formats.All(f => f.Id != Input.ConfigId))
        {
            Input.ConfigId = formats.Count > 0 ? formats[0].Id : null;
            ModelState.Remove($"{nameof(Input)}.{nameof(Input.ConfigId)}");
        }
    }

    // ------------------------------------------------------------------ vendedor (Ventas)

    /// <summary>Vendedores de SAP (OSLP) -- vacío si SAP no responde.</summary>
    public List<SelectListItem> SalesPersons { get; private set; } = [];

    /// <summary>true solo en modo Multivendedor -- en Fijo / Del socio el selector se muestra bloqueado con el valor que se usará.</summary>
    public bool SalesPersonEditable { get; private set; }

    public string? SalesPersonModeLabel { get; private set; }

    private static string ModeLabel(GenericImportSalesPersonMode mode) => mode switch
    {
        GenericImportSalesPersonMode.Fixed => "Vendedor fijo del formato",
        GenericImportSalesPersonMode.MultiSeller => "Se pide en pantalla: elegí el vendedor de esta carga",
        _ => "Vendedor del socio de negocio",
    };

    /// <summary>Modo + vendedor que se usará, según el formato elegido y el socio (mismo criterio que GenericImportService.ResolveSalesPerson).</summary>
    private async Task<(GenericImportSalesPersonMode Mode, int? Code)> ResolveSalesPersonAsync(int? configId, string? cardCode, int? chosen, CancellationToken ct)
    {
        var config = configId is { } id ? await _configs.GetAsync(id, ct) : null;
        var mode = config?.DocumentOptions.SalesPersonMode ?? GenericImportSalesPersonMode.BusinessPartner;
        int? partnerDefault = null;
        if (mode != GenericImportSalesPersonMode.Fixed && !string.IsNullOrWhiteSpace(cardCode))
        {
            try
            {
                partnerDefault = (await _partnerDefaults.GetAsync(cardCode.Trim(), ct))?.SalesEmployeeCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo leer el vendedor por defecto del socio {CardCode}.", cardCode);
            }
        }

        var code = mode switch
        {
            GenericImportSalesPersonMode.Fixed => config?.DocumentOptions.SalesPersonCode,
            GenericImportSalesPersonMode.MultiSeller => chosen ?? partnerDefault,
            _ => partnerDefault,
        };
        return (mode, code);
    }

    private async Task LoadSalesPersonAsync(CancellationToken ct)
    {
        if (Input.Module != GenericImportModule.Sales)
        {
            return;
        }

        var (mode, code) = await ResolveSalesPersonAsync(Input.ConfigId, Input.BusinessPartnerCardCode, Input.SalesPersonCode, ct);
        SalesPersonEditable = mode == GenericImportSalesPersonMode.MultiSeller;
        SalesPersonModeLabel = ModeLabel(mode);
        Input.SalesPersonCode = code;
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.SalesPersonCode)}");

        try
        {
            // Text = lo que ve y escribe el usuario (el nombre); Value = el código que se envía.
            // Nombres repetidos llevan el código para poder distinguirlos.
            var employees = await _salesEmployees.ListAsync(ct: ct);
            var repeated = employees.GroupBy(s => s.SalesEmployeeName.Trim(), StringComparer.CurrentCultureIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            SalesPersons = employees
                .OrderBy(s => s.SalesEmployeeName)
                .Select(s => new SelectListItem(
                    repeated.Contains(s.SalesEmployeeName.Trim()) ? $"{s.SalesEmployeeName.Trim()} ({s.SalesEmployeeCode})" : s.SalesEmployeeName.Trim(),
                    s.SalesEmployeeCode.ToString()))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo cargar la lista de vendedores.");
            SalesPersons = [];
        }
    }

    /// <summary>Llamado por fetch al cambiar Formato o Socio (Index.cshtml, cargarVendedor).</summary>
    public async Task<JsonResult> OnGetSalesPersonAsync(int? configId, string? businessPartnerCardCode, int? salesPersonCode, CancellationToken ct)
    {
        var (mode, code) = await ResolveSalesPersonAsync(configId, businessPartnerCardCode, salesPersonCode, ct);
        return new JsonResult(new { Code = code, Editable = mode == GenericImportSalesPersonMode.MultiSeller, Label = ModeLabel(mode) });
    }

    // ------------------------------------------------- sucursal y almacén "en pantalla"

    /// <summary>true = el formato pide la sucursal en la cabecera (GenericImportBranchSource.Screen).</summary>
    public bool BranchAskedOnScreen { get; private set; }

    /// <summary>true = el formato pide el almacén en la cabecera (GenericImportWarehouseSource.Screen).</summary>
    public bool WarehouseAskedOnScreen { get; private set; }

    /// <summary>Direcciones de despacho del cliente elegido (CRD1) -- solo con BranchAskedOnScreen.</summary>
    public List<SelectListItem> ShipToOptions { get; private set; } = [];

    /// <summary>Almacenes de SAP (OWHS) para el selector de almacén en pantalla; vacío si SAP no responde.</summary>
    public List<SelectListItem> Warehouses { get; private set; } = [];

    private sealed record ScreenHeader(bool BranchAsked, IReadOnlyList<SelectListItem> ShipTos, string? ShipToCode,
        bool WarehouseAsked, string? WarehouseCode);

    /// <summary>
    /// Qué datos pide el formato en pantalla y sus valores: la sucursal se precarga con la
    /// dirección de despacho por defecto del cliente (OCRD.ShipToDef) y el almacén con el
    /// del cliente (OCRD.&lt;WarehouseUserField&gt;, ej. U_NX_WhsCode). Una elección previa que
    /// sigue siendo válida se respeta (cambiar de cliente sí la reinicia).
    /// </summary>
    private async Task<ScreenHeader> ResolveScreenHeaderAsync(int? configId, GenericImportModule module, GenericImportLineType lineType,
        string? cardCode, string? shipToCode, string? warehouseCode, bool partnerChanged, CancellationToken ct)
    {
        var config = configId is { } id ? await _configs.GetAsync(id, ct) : null;
        if (config is null)
        {
            return new ScreenHeader(false, [], null, false, null);
        }

        cardCode = string.IsNullOrWhiteSpace(cardCode) ? null : cardCode.Trim();
        var branchAsked = config.DocumentOptions.BranchSource == GenericImportBranchSource.Screen
            && module != GenericImportModule.Purchase && !config.BusinessPartnerFromFile;
        var warehouseAsked = config.DocumentOptions.WarehouseSource == GenericImportWarehouseSource.Screen
            && module != GenericImportModule.Inventory && lineType == GenericImportLineType.Item;

        List<SelectListItem> shipTos = [];
        string? shipTo = null;
        if (branchAsked && cardCode is not null)
        {
            try
            {
                shipTos = (await _shipToAddresses.ListShipToAddressesAsync(cardCode, ct))
                    .Select(a => new SelectListItem(string.IsNullOrEmpty(a.FullAddress) ? a.Code : $"{a.Code} - {a.FullAddress}", a.Code))
                    .ToList();
                shipTo = !partnerChanged && shipTos.Any(o => string.Equals(o.Value, shipToCode, StringComparison.OrdinalIgnoreCase))
                    ? shipToCode
                    : (await _partnerDefaults.GetAsync(cardCode, ct))?.ShipToDefault;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudieron leer las direcciones de despacho del socio {CardCode}.", cardCode);
            }
        }

        string? warehouse = null;
        if (warehouseAsked)
        {
            warehouse = !partnerChanged && !string.IsNullOrWhiteSpace(warehouseCode) ? warehouseCode.Trim() : null;
            if (warehouse is null && cardCode is not null && !config.BusinessPartnerFromFile)
            {
                try
                {
                    warehouse = await _importService.GetBusinessPartnerWarehouseAsync(cardCode, config.DocumentOptions.WarehouseUserField, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo leer el almacén del socio {CardCode}.", cardCode);
                }
            }
        }

        return new ScreenHeader(branchAsked, shipTos, shipTo, warehouseAsked, warehouse);
    }

    private async Task LoadScreenHeaderAsync(CancellationToken ct)
    {
        var header = await ResolveScreenHeaderAsync(Input.ConfigId, Input.Module, Input.LineType, Input.BusinessPartnerCardCode,
            Input.ShipToCode, Input.WarehouseCode, partnerChanged: false, ct);
        BranchAskedOnScreen = header.BranchAsked;
        WarehouseAskedOnScreen = header.WarehouseAsked;
        ShipToOptions = header.ShipTos.ToList();
        Input.ShipToCode = header.ShipToCode;
        Input.WarehouseCode = header.WarehouseCode;
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.ShipToCode)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.WarehouseCode)}");

        // Siempre (no solo si el formato actual lo pide): el formato se cambia en pantalla
        // por fetch y la lista no depende del cliente. OWHS es una tabla chica.
        {
            try
            {
                Warehouses = (await _warehouses.ListAsync(ct: ct))
                    .OrderBy(w => w.WarehouseCode)
                    .Select(w => new SelectListItem($"{w.WarehouseCode} - {w.WarehouseName}", w.WarehouseCode))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo cargar la lista de almacenes.");
                Warehouses = [];
            }
        }
    }

    /// <summary>
    /// Llamado por fetch al cambiar Formato, Socio, Módulo o Tipo de línea (Index.cshtml,
    /// cargarCabecera). partnerChanged = el usuario cambió de cliente: se vuelve a precargar
    /// sucursal y almacén con los de ese cliente.
    /// </summary>
    public async Task<JsonResult> OnGetScreenHeaderAsync(int? configId, GenericImportModule module, GenericImportLineType lineType,
        string? businessPartnerCardCode, string? shipToCode, string? warehouseCode, bool partnerChanged, CancellationToken ct)
    {
        var header = await ResolveScreenHeaderAsync(configId, module, lineType, businessPartnerCardCode, shipToCode, warehouseCode, partnerChanged, ct);
        return new JsonResult(new
        {
            header.BranchAsked,
            ShipTos = header.ShipTos.Select(o => new { Code = o.Value, Label = o.Text }),
            header.ShipToCode,
            header.WarehouseAsked,
            header.WarehouseCode,
        });
    }

    private string? SelectedDocumentType() => Input.Module switch
    {
        GenericImportModule.Sales => Input.SalesDocumentType,
        GenericImportModule.Purchase => Input.PurchaseDocumentType,
        GenericImportModule.Inventory => Input.InventoryDocumentType,
        _ => null,
    };

    public async Task<IActionResult> OnPostProcessAsync(IFormFile? file, CancellationToken ct)
    {
        // Una carga guardada no se reprocesa con otros parámetros (sin edición): se vuelve
        // a abrir en modo revalidación.
        if (file is not { Length: > 0 } && Input.JobId is { } postedJob && await _jobs.GetAsync(postedJob, ct) is { IsSaved: true })
        {
            return RedirectToPage(new { job = postedJob });
        }

        await LoadWizardAsync(ct);

        // El archivo se guarda UNA vez en el servidor como borrador de trabajo
        // (IGenericImportJobService, 2026-10-15) y los pasos siguientes (Confirmar,
        // descargas) lo referencian por Input.JobId -- antes viajaba en base64 dentro de 4
        // formularios ocultos (límite de 4 MB por campo, varios MB de ida y vuelta).
        // Sin archivo nuevo pero con un borrador vigente: se reprocesa el mismo archivo
        // con los parámetros nuevos (ej. cambió el formato o el vendedor).
        byte[] bytes;
        if (file is { Length: > 0 })
        {
            using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
            Input.JobId = await _jobs.CreateDraftAsync(BuildParameters(), file.FileName, bytes, ct);
        }
        else if (Input.JobId is { } existingJob && await _jobs.GetFileAsync(existingJob, ct) is { Length: > 0 } existingBytes)
        {
            bytes = existingBytes;
            await _jobs.UpdateDraftParametersAsync(existingJob, BuildParameters(), ct);
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Elegí un archivo antes de procesar.");
            return Page();
        }

        // Los tag helpers (asp-for) priorizan el valor de ModelState sobre el del modelo al
        // re-renderizar -- sin sacar la entrada, los <input hidden> de JobId quedarían con
        // el valor viejo del POST. Mismo patrón que Account/Login.cshtml.cs.
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.JobId)}");

        var parameters = BuildParameters();
        using var processStream = new MemoryStream(bytes);
        PreviewResult = await _importService.ProcessFileAsync(parameters, processStream, ct);
        await LoadPreviewHeaderInfoAsync(ct);
        LoadedFileName = Input.JobId is { } loadedJob && await _jobs.GetAsync(loadedJob, ct) is { Status: GenericImportJobStatus.Draft } draft
            ? draft.FileName
            : null;

        return Page();
    }

    /// <summary>
    /// Nombre del socio del wizard (carga de un solo socio) -- en ese modo las filas no
    /// traen BusinessPartnerName (solo en multi-socio, ver GenericImportRowDto), así que
    /// la cabecera de la vista previa lo resuelve acá UNA vez. Null si no hay socio o SAP
    /// no responde (solo informativo, la vista previa igual se muestra con el código).
    /// </summary>
    public string? BusinessPartnerName { get; private set; }

    /// <summary>Nombre de la categoría de un hallazgo (panel "Otras observaciones" de la vista previa).</summary>
    public static string IssueCategoryLabel(GenericImportIssueCategory category) => category switch
    {
        GenericImportIssueCategory.BusinessPartner => "Socio de negocio",
        GenericImportIssueCategory.Branch => "Sucursal",
        GenericImportIssueCategory.Item => "Artículo",
        GenericImportIssueCategory.Warehouse => "Almacén",
        GenericImportIssueCategory.Quantity => "Cantidad",
        GenericImportIssueCategory.Price => "Precio",
        GenericImportIssueCategory.Stock => "Stock",
        GenericImportIssueCategory.OrderTotal => "Descuento total OC",
        GenericImportIssueCategory.DuplicateOrder => "OC ya cargada",
        GenericImportIssueCategory.Accounting => "Contabilidad",
        _ => "Otro",
    };

    /// <summary>SapFieldName -> Etiqueta de los campos de usuario del módulo, para mostrar los de cabecera con su nombre legible en la vista previa.</summary>
    public IReadOnlyDictionary<string, string> UserFieldLabels { get; private set; } = new Dictionary<string, string>();

    private async Task LoadPreviewHeaderInfoAsync(CancellationToken ct)
    {
        if (PreviewResult is null || !PreviewResult.HasValidConfig)
        {
            return;
        }

        UserFieldLabels = (await _userFields.ListAsync(Input.Module, ct))
            .GroupBy(f => f.SapFieldName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Label, StringComparer.OrdinalIgnoreCase);

        var cardCode = Input.BusinessPartnerCardCode?.Trim();
        if (string.IsNullOrEmpty(cardCode))
        {
            return;
        }

        try
        {
            BusinessPartnerName = Input.Module == GenericImportModule.Purchase
                ? (await _suppliers.GetAsync(cardCode, ct))?.CardName
                : (await _customers.GetAsync(cardCode, ct))?.CardName;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo resolver el nombre del socio {CardCode} para la vista previa.", cardCode);
        }
    }

    /// <summary>
    /// "Confirmar creación en SAP" (2026-10-15): ya NO crea los pedidos dentro de esta
    /// petición -- deja el trabajo en la cola (IGenericImportJobService.EnqueueAsync) y
    /// responde al instante con su Id. La creación la hace GenericImportJobWorker en
    /// segundo plano: cerrar la pestaña, perder la conexión o un timeout del proxy ya no la
    /// cortan a mitad. La pantalla sigue el avance por polling (OnGetProgressAsync) y el
    /// resultado queda en "Mis importaciones"; la campana avisa al terminar. Siempre JSON,
    /// también en error (el cliente lo muestra tal cual).
    /// </summary>
    public async Task<JsonResult> OnPostConfirmAsync(CancellationToken ct)
    {
        try
        {
            if (Input.JobId is not { } jobId)
            {
                return new JsonResult(new { Message = "Volvé a procesar el archivo antes de confirmar." }) { StatusCode = StatusCodes.Status400BadRequest };
            }

            await _jobs.EnqueueAsync(jobId, BuildParameters(), ct);
            return new JsonResult(new
            {
                JobId = jobId,
                Message = "Importación enviada: se procesa en segundo plano. Podés cerrar esta página -- el resultado queda en Mis importaciones y te avisamos en la campana.",
                ReportUrl = $"/importacion-generica/trabajos/{jobId}",
            });
        }
        catch (InvalidOperationException ex)
        {
            return new JsonResult(new { Message = ex.Message }) { StatusCode = StatusCodes.Status400BadRequest };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error no previsto al encolar la importación {JobId}.", Input.JobId);
            return new JsonResult(new { Message = GetErrorMessage(ex) }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    /// <summary>
    /// "Guardar sin enviar" (borradores etapa 2, 2026-10-21): el archivo ya procesado queda
    /// guardado con sus parámetros y el motivo, sin crear nada en SAP -- para corregir
    /// precio/stock en SAP o esperar confirmación. Se revalida y se envía después desde Mis
    /// importaciones (cualquier usuario con permiso). JSON siempre, como Confirmar.
    /// </summary>
    public async Task<JsonResult> OnPostSaveAsync(string? reason, CancellationToken ct)
    {
        try
        {
            if (Input.JobId is not { } jobId)
            {
                return new JsonResult(new { Message = "Volvé a procesar el archivo antes de guardarlo." }) { StatusCode = StatusCodes.Status400BadRequest };
            }

            await _jobs.SaveAsync(jobId, BuildParameters(), reason ?? string.Empty, ct);
            return new JsonResult(new
            {
                JobId = jobId,
                Message = "Carga guardada sin enviar. La encontrás en Mis importaciones, pestaña Guardadas: desde ahí se revalida contra SAP y se envía.",
                ReportUrl = $"/importacion-generica/trabajos/{jobId}",
            });
        }
        catch (InvalidOperationException ex)
        {
            return new JsonResult(new { Message = ex.Message }) { StatusCode = StatusCodes.Status400BadRequest };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error no previsto al guardar la importación {JobId}.", Input.JobId);
            return new JsonResult(new { Message = GetErrorMessage(ex) }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    /// <summary>Polling de la pantalla: en vivo mientras corre y desde la base al terminar (también tras un reinicio).</summary>
    public async Task<JsonResult> OnGetProgressAsync(Guid jobId, CancellationToken ct) =>
        new(await _jobs.GetProgressAsync(jobId, ct));

    /// <summary>
    /// Búsqueda en vivo del Socio de negocio -- mismo modelo que Cliente/Proveedor en
    /// los 3 motores genéricos (ver CLAUDE.md), pero acá el catálogo a consultar
    /// depende del Módulo elegido en el formulario (Venta -> Cliente, Compra/
    /// Inventario -> Proveedor) -- el cliente (Pages/Importar/Index.cshtml) manda el
    /// valor actual de Input.Module como parámetro extra (extraParams, ver
    /// catalog-search.js) en cada búsqueda.
    /// </summary>
    public async Task<JsonResult> OnGetSearchBusinessPartnersAsync(string text, GenericImportModule module, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonResult(Array.Empty<object>());
        }

        // Compra -> Proveedor; Venta e Inventario -> Cliente. Mismo criterio que la
        // validación del motor (GenericImportService.ProcessFileAsync, validBusinessPartners)
        // y que el selector de socio de Modulo.Inventario (OnGetSearchCustomersAsync).
        // Antes Inventario buscaba en Proveedores: un cliente (ej. "C76") nunca aparecía
        // en la ayuda pese a ser el socio que el motor sí acepta.
        if (module == GenericImportModule.Purchase)
        {
            var suppliers = await _suppliers.ListAsync(new SupplierFilter(text, Limit: 30), ct);
            return new JsonResult(suppliers.Select(s => new { s.CardCode, s.CardName }));
        }

        var customers = await _customers.ListAsync(new CustomerFilter(text, Limit: 30), ct);
        return new JsonResult(customers.Select(c => new { c.CardCode, c.CardName }));
    }

    // POST porque el archivo (se descarga por POST, no GET) -- el Id del trabajo viaja en el body; mismo
    // URL en GET -- mismo motivo que OnPostConfirmAsync. Reprocesa el archivo (mismo
    // criterio: no queda en sesión de servidor) para tener la bitácora de errores de
    // cada fila antes de generar el .xlsx.
    public async Task<IActionResult> OnPostDownloadWithErrorsAsync(CancellationToken ct)
    {
        var jobFile = Input.JobId is { } downloadJobId ? await _jobs.GetFileAsync(downloadJobId, ct) : null;
        if (jobFile is null)
        {
            ModelState.AddModelError(string.Empty, "Volvé a procesar el archivo antes de descargar.");
            await LoadCreatableDocumentTypesAsync(ct);
            await LoadFormatsAsync(ct);
            await LoadSalesPersonAsync(ct);
            await ResolveBusinessPartnerFromFileAsync(ct);
            await LoadScreenHeaderAsync(ct);
            return Page();
        }

        var parameters = BuildParameters();
        var bytes = jobFile;
        using var stream = new MemoryStream(bytes);
        var preview = await _importService.ProcessFileAsync(parameters, stream, ct);

        var fileBytes = await _importService.GenerateFileWithErrorsAsync(parameters, preview.Documents, ct);
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Errores_{Input.Module}_{parameters.DocumentType}.xlsx");
    }

    // Reporte de validación pre-carga (Errores + Advertencias por línea + hoja de stock)
    // -- disponible siempre que haya vista previa, no solo cuando hay errores. Reprocesa
    // el archivo, mismo criterio que OnPostDownloadWithErrorsAsync.
    public async Task<IActionResult> OnPostDownloadValidationReportAsync(CancellationToken ct)
    {
        var jobFile = Input.JobId is { } downloadJobId ? await _jobs.GetFileAsync(downloadJobId, ct) : null;
        if (jobFile is null)
        {
            ModelState.AddModelError(string.Empty, "Volvé a procesar el archivo antes de descargar el reporte.");
            await LoadCreatableDocumentTypesAsync(ct);
            await LoadFormatsAsync(ct);
            await LoadSalesPersonAsync(ct);
            await ResolveBusinessPartnerFromFileAsync(ct);
            await LoadScreenHeaderAsync(ct);
            return Page();
        }

        var parameters = BuildParameters();
        var bytes = jobFile;
        using var stream = new MemoryStream(bytes);
        var preview = await _importService.ProcessFileAsync(parameters, stream, ct);

        var fileBytes = await _importService.GenerateValidationReportAsync(parameters, preview.Documents, ct);
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ReporteValidacion_{Input.Module}_{parameters.DocumentType}.xlsx");
    }

    public async Task<IActionResult> OnGetTemplateAsync(GenericImportModule module, string documentType, GenericImportLineType lineType,
        string? businessPartnerCardCode, int? configId, CancellationToken ct)
    {
        var parameters = new GenericImportParametersDto(module, documentType, lineType, businessPartnerCardCode, configId);
        var bytes = await _importService.GenerateTemplateAsync(parameters, ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "plantilla-importacion.xlsx");
    }

    /// <summary>
    /// Resuelve la configuración vigente para Módulo+TipoDocumento+TipoLínea (si ya se
    /// conoce) para saber si es multi-socio -- necesario ANTES de validar si el socio es
    /// obligatorio (se relaja esa validación en modo multi-socio) y para mostrarle al
    /// usuario que puede dejar el selector de socio vacío.
    /// </summary>
    private async Task ResolveBusinessPartnerFromFileAsync(CancellationToken ct)
    {
        BusinessPartnerFromFile = false;

        var documentType = SelectedDocumentType();

        if (string.IsNullOrWhiteSpace(documentType))
        {
            return;
        }

        var config = await _configs.ResolveAsync(Input.Module, documentType, Input.LineType, Input.BusinessPartnerCardCode, Input.ConfigId, ct);
        BusinessPartnerFromFile = config?.BusinessPartnerFromFile ?? false;
    }

    private GenericImportParametersDto BuildParameters()
    {
        var documentType = Input.Module switch
        {
            GenericImportModule.Sales => Input.SalesDocumentType,
            GenericImportModule.Purchase => Input.PurchaseDocumentType,
            GenericImportModule.Inventory => Input.InventoryDocumentType,
            _ => throw new InvalidOperationException("Módulo no soportado."),
        };

        return new GenericImportParametersDto(Input.Module, documentType ?? string.Empty,
            Input.LineType, Input.BusinessPartnerCardCode, Input.ConfigId,
            Input.Module == GenericImportModule.Sales ? Input.SalesPersonCode : null,
            Input.ShipToCode, Input.WarehouseCode);
    }

    private async Task LoadCreatableDocumentTypesAsync(CancellationToken ct)
    {
        var salesTypes = new List<SelectListItem>();
        foreach (var type in Enum.GetValues<SalesDocumentType>())
        {
            if (await _sales.CanCreateAsync(type, ct))
            {
                salesTypes.Add(new SelectListItem(ImportDisplayNames.Of(type), type.ToString()));
            }
        }
        SalesDocumentTypes = salesTypes;

        var purchaseTypes = new List<SelectListItem>();
        foreach (var type in Enum.GetValues<PurchaseDocumentType>())
        {
            if (await _purchase.CanCreateAsync(type, ct))
            {
                purchaseTypes.Add(new SelectListItem(ImportDisplayNames.Of(type), type.ToString()));
            }
        }
        PurchaseDocumentTypes = purchaseTypes;

        var inventoryTypes = new List<SelectListItem>();
        foreach (var type in Enum.GetValues<InventoryDocumentType>())
        {
            if (await _inventory.CanCreateAsync(type, ct))
            {
                inventoryTypes.Add(new SelectListItem(ImportDisplayNames.Of(type), type.ToString()));
            }
        }
        InventoryDocumentTypes = inventoryTypes;
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Elegí un módulo.")]
        [Display(Name = "Módulo")]
        public GenericImportModule Module { get; set; }

        [Display(Name = "Tipo de documento (Venta)")]
        public string? SalesDocumentType { get; set; }

        [Display(Name = "Tipo de documento (Compra)")]
        public string? PurchaseDocumentType { get; set; }

        [Display(Name = "Tipo de documento (Inventario)")]
        public string? InventoryDocumentType { get; set; }

        [Required(ErrorMessage = "Elegí un tipo de línea.")]
        [Display(Name = "Tipo de línea")]
        public GenericImportLineType LineType { get; set; }

        [Display(Name = "Socio de negocio (Cliente/Proveedor -- vacío usa el estándar de la organización)")]
        public string? BusinessPartnerCardCode { get; set; }

        [Display(Name = "Formato")]
        public int? ConfigId { get; set; }

        [Display(Name = "Vendedor")]
        public int? SalesPersonCode { get; set; }

        /// <summary>Solo si el formato pide la sucursal en pantalla -- ver GenericImportBranchSource.Screen.</summary>
        [Display(Name = "Sucursal de entrega")]
        public string? ShipToCode { get; set; }

        /// <summary>Solo si el formato pide el almacén en pantalla -- ver GenericImportWarehouseSource.Screen.</summary>
        [Display(Name = "Almacén")]
        public string? WarehouseCode { get; set; }

        /// <summary>Trabajo (borrador) con el archivo ya guardado en el servidor -- ver IGenericImportJobService.</summary>
        public Guid? JobId { get; set; }
    }
}
