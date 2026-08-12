using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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
    private readonly IGenericImportService _importService;
    private readonly IGenericImportProgressStore _progress;
    private readonly IGenericImportConfigService _configs;
    private readonly ISalesDocumentService _sales;
    private readonly IPurchaseDocumentService _purchase;
    private readonly IInventoryDocumentService _inventory;
    private readonly ICustomerCatalogService _customers;
    private readonly ISupplierCatalogService _suppliers;

    public IndexModel(ICurrentUserContext currentUser, IGenericImportService importService, IGenericImportProgressStore progress,
        IGenericImportConfigService configs, ISalesDocumentService sales, IPurchaseDocumentService purchase, IInventoryDocumentService inventory,
        ICustomerCatalogService customers, ISupplierCatalogService suppliers) : base(currentUser)
    {
        _importService = importService;
        _progress = progress;
        _configs = configs;
        _sales = sales;
        _purchase = purchase;
        _inventory = inventory;
        _customers = customers;
        _suppliers = suppliers;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> Modules { get; } =
    [
        new(GenericImportModule.Sales.ToString(), GenericImportModule.Sales.ToString()),
        new(GenericImportModule.Purchase.ToString(), GenericImportModule.Purchase.ToString()),
        new(GenericImportModule.Inventory.ToString(), GenericImportModule.Inventory.ToString()),
    ];

    public List<SelectListItem> LineTypes { get; } =
    [
        new("Artículo", GenericImportLineType.Item.ToString()),
        new("Servicio", GenericImportLineType.Service.ToString()),
    ];

    public List<SelectListItem> SalesDocumentTypes { get; private set; } = [];
    public List<SelectListItem> PurchaseDocumentTypes { get; private set; } = [];
    public List<SelectListItem> InventoryDocumentTypes { get; private set; } = [];

    public GenericImportResultDto? PreviewResult { get; private set; }

    /// <summary>true = la configuración vigente para Módulo+TipoDocumento+TipoLínea trae el socio por columna del Excel (carga multi-socio) -- el selector de socio del wizard queda informativo, ver Index.cshtml.</summary>
    public bool BusinessPartnerFromFile { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadCreatableDocumentTypesAsync(ct);
        await ResolveBusinessPartnerFromFileAsync(ct);
    }

    public async Task<IActionResult> OnPostProcessAsync(IFormFile file, CancellationToken ct)
    {
        await LoadCreatableDocumentTypesAsync(ct);
        await ResolveBusinessPartnerFromFileAsync(ct);

        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Elegí un archivo antes de procesar.");
            return Page();
        }

        using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        Input.Base64File = Convert.ToBase64String(bytes);

        var parameters = BuildParameters();
        using var processStream = new MemoryStream(bytes);
        PreviewResult = await _importService.ProcessFileAsync(parameters, processStream, ct);

        return Page();
    }

    /// <summary>
    /// Devuelve JSON (no Page()) -- este handler se llama por fetch, nunca por un
    /// submit de formulario tradicional (ver Index.cshtml, btnConfirmar). Reprocesa el
    /// archivo (mismo criterio que antes: el archivo no queda en sesión de servidor)
    /// para obtener GenericImportDocumentDto con los DocumentLineType/valores ya
    /// resueltos, y recién ahí llama CreateDocumentsAsync con el jobId que generó el
    /// cliente -- ese mismo jobId es la clave que OnGetProgressAsync consulta por
    /// polling mientras este método sigue corriendo.
    /// </summary>
    public async Task<JsonResult> OnPostConfirmAsync(string jobId, CancellationToken ct)
    {
        var parameters = BuildParameters();

        if (string.IsNullOrEmpty(Input.Base64File))
        {
            return new JsonResult(new GenericImportProgressDto("Volvé a procesar el archivo antes de confirmar.", 0, 0, false, null, true))
            {
                StatusCode = StatusCodes.Status400BadRequest,
            };
        }

        var bytes = Convert.FromBase64String(Input.Base64File);
        using var stream = new MemoryStream(bytes);
        var preview = await _importService.ProcessFileAsync(parameters, stream, ct);

        if (!preview.HasValidConfig || !preview.Documents.Any(d => d.CanCreate))
        {
            var blocked = new GenericImportProgressDto(
                preview.HasValidConfig ? "No hay documentos válidos para crear -- volvé a la vista previa." : preview.ErrorMessage ?? "Configuración inválida.",
                0, 0, false, null, true);
            return new JsonResult(blocked) { StatusCode = StatusCodes.Status400BadRequest };
        }

        var result = await _importService.CreateDocumentsAsync(jobId, CurrentUser.Username, parameters, preview.Documents, ct);
        return new JsonResult(result);
    }

    /// <summary>Consultado por polling desde el cliente mientras OnPostConfirmAsync sigue corriendo -- ver IGenericImportProgressStore.</summary>
    public async Task<JsonResult> OnGetProgressAsync(string jobId) => new(await _progress.GetAsync(jobId));

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

        if (module == GenericImportModule.Sales)
        {
            var customers = await _customers.ListAsync(new CustomerFilter(text, Limit: 30), ct);
            return new JsonResult(customers.Select(c => new { c.CardCode, c.CardName }));
        }

        var suppliers = await _suppliers.ListAsync(new SupplierFilter(text, Limit: 30), ct);
        return new JsonResult(suppliers.Select(s => new { s.CardCode, s.CardName }));
    }

    // POST porque el archivo (Input.Base64File) puede superar el límite práctico de una
    // URL en GET -- mismo motivo que OnPostConfirmAsync. Reprocesa el archivo (mismo
    // criterio: no queda en sesión de servidor) para tener la bitácora de errores de
    // cada fila antes de generar el .xlsx.
    public async Task<IActionResult> OnPostDownloadWithErrorsAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(Input.Base64File))
        {
            ModelState.AddModelError(string.Empty, "Volvé a procesar el archivo antes de descargar.");
            await LoadCreatableDocumentTypesAsync(ct);
            await ResolveBusinessPartnerFromFileAsync(ct);
            return Page();
        }

        var parameters = BuildParameters();
        var bytes = Convert.FromBase64String(Input.Base64File);
        using var stream = new MemoryStream(bytes);
        var preview = await _importService.ProcessFileAsync(parameters, stream, ct);

        var fileBytes = await _importService.GenerateFileWithErrorsAsync(parameters, preview.Documents, ct);
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Errores_{Input.Module}_{parameters.DocumentType}.xlsx");
    }

    public async Task<IActionResult> OnGetTemplateAsync(GenericImportModule module, string documentType, GenericImportLineType lineType,
        string? businessPartnerCardCode, CancellationToken ct)
    {
        var parameters = new GenericImportParametersDto(module, documentType, lineType, businessPartnerCardCode);
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

        var documentType = Input.Module switch
        {
            GenericImportModule.Sales => Input.SalesDocumentType,
            GenericImportModule.Purchase => Input.PurchaseDocumentType,
            GenericImportModule.Inventory => Input.InventoryDocumentType,
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(documentType))
        {
            return;
        }

        var config = await _configs.ResolveAsync(Input.Module, documentType, Input.LineType, Input.BusinessPartnerCardCode, ct);
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
            Input.LineType, Input.BusinessPartnerCardCode);
    }

    private async Task LoadCreatableDocumentTypesAsync(CancellationToken ct)
    {
        var salesTypes = new List<SelectListItem>();
        foreach (var type in Enum.GetValues<SalesDocumentType>())
        {
            if (await _sales.CanCreateAsync(type, ct))
            {
                salesTypes.Add(new SelectListItem(type.ToString(), type.ToString()));
            }
        }
        SalesDocumentTypes = salesTypes;

        var purchaseTypes = new List<SelectListItem>();
        foreach (var type in Enum.GetValues<PurchaseDocumentType>())
        {
            if (await _purchase.CanCreateAsync(type, ct))
            {
                purchaseTypes.Add(new SelectListItem(type.ToString(), type.ToString()));
            }
        }
        PurchaseDocumentTypes = purchaseTypes;

        var inventoryTypes = new List<SelectListItem>();
        foreach (var type in Enum.GetValues<InventoryDocumentType>())
        {
            if (await _inventory.CanCreateAsync(type, ct))
            {
                inventoryTypes.Add(new SelectListItem(type.ToString(), type.ToString()));
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

        public string? Base64File { get; set; }
    }
}
