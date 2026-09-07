using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.ImportacionGenerica.Pages.Configuracion;

/// <summary>
/// Configuración de importación genérica (estándar de la organización o excepción de
/// un socio de negocio puntual) -- solo administrador. Portado de Pages/Configuracion/
/// Index.cshtml.cs (referencia-original/PortalSAP_v2). Los campos núcleo se editan en
/// una grilla fija (sin "agregar fila" -- mismo criterio que las líneas de documento de
/// los 3 motores genéricos); los campos de usuario, en cambio, son ilimitados --
/// "Agregar campo" suma una fila, "Quitar" saca esa puntual, ambos por postback en
/// memoria (nada se persiste hasta "Guardar") -- mismo patrón que
/// IGrupoAprobacionAdminService.AgregarNivelAsync/QuitarUltimoNivelAsync en
/// Modulo.Compras. Antes tenía un tope fijo de 5 filas en código
/// (UserFieldMappingRows) -- bug real ya documentado y corregido en la referencia
/// (sección 7.5): una configuración con más de 5 campos de usuario los perdía en
/// silencio al re-guardar (Update reemplaza TODO el detalle de campos, ver
/// IGenericImportConfigService.UpdateAsync).
/// </summary>
public sealed class IndexModel : PageModelBaseAdmin
{
    private readonly IGenericImportConfigService _configs;
    private readonly IGenericImportUserFieldService _userFields;
    private readonly ICustomerCatalogService _customers;
    private readonly ISupplierCatalogService _suppliers;
    private readonly ISalesDocumentService _sales;
    private readonly IPurchaseDocumentService _purchase;
    private readonly IInventoryDocumentService _inventory;

    public IndexModel(ICurrentUserContext currentUser, IGenericImportConfigService configs, IGenericImportUserFieldService userFields,
        ICustomerCatalogService customers, ISupplierCatalogService suppliers,
        ISalesDocumentService sales, IPurchaseDocumentService purchase, IInventoryDocumentService inventory)
        : base(currentUser)
    {
        _configs = configs;
        _userFields = userFields;
        _customers = customers;
        _suppliers = suppliers;
        _sales = sales;
        _purchase = purchase;
        _inventory = inventory;
    }

    [BindProperty(SupportsGet = true)]
    public int? EditId { get; set; }

    /// <summary>Precarga Input desde una configuración existente pero SIN fijar EditId --
    /// "Guardar" cae en CreateAsync (rama normal de OnPostSaveAsync), así que el resultado
    /// es un template nuevo con los mismos valores, no una edición del original.</summary>
    [BindProperty(SupportsGet = true)]
    public int? CopyId { get; set; }

    /// <summary>Sin esto, no habría forma de abrir el panel de "Nueva configuración" -- a
    /// diferencia de Editar/Copiar (que traen su Id), crear desde cero no tiene id que
    /// pasar. Ver MostrarFormulario.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Nuevo { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Solo lista de documentos por default (pedido explícito del dueño del
    /// proyecto) -- el panel de alta/edición/copia se muestra únicamente cuando el usuario
    /// pidió explícitamente crear/editar/copiar algo. EditId/CopyId/Nuevo se preservan como
    /// campos ocultos del propio formulario (ver Index.cshtml) para que sigan viajando en
    /// los postbacks de "Agregar campo"/"Quitar campo" -- sin esto, esos dos handlers (que
    /// no reenvían editId por route value, solo por el body del form) harían que el panel
    /// se cerrara solo apenas se tocara un campo de usuario en medio de una edición.</summary>
    public bool MostrarFormulario => EditId is not null || CopyId is not null || Nuevo;

    public IReadOnlyList<GenericImportConfigDto> Configs { get; private set; } = [];
    public List<SelectListItem> UserFieldOptions { get; private set; } = [];

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

    // Antes Input.DocumentType era texto libre ("ej. SalesOrder, PurchaseQuotation,
    // StockTransfer") -- bug real: nada impedía tipear "StockTranfer" (typo) y guardar
    // una configuración que ResolveAsync nunca iba a encontrar. Un <select> por Módulo,
    // filtrado por CanCreateAsync -- mismo criterio que Pages/Importar/Index.cshtml.cs
    // (SalesDocumentTypes/PurchaseDocumentTypes/InventoryDocumentTypes) -- el JS del
    // .cshtml muestra solo el que corresponde al Módulo elegido.
    public List<SelectListItem> SalesDocumentTypes { get; private set; } = [];
    public List<SelectListItem> PurchaseDocumentTypes { get; private set; } = [];
    public List<SelectListItem> InventoryDocumentTypes { get; private set; } = [];

    public List<SelectListItem> PriceSources { get; } =
    [
        new("Socio de negocio (default)", GenericImportPriceSource.BusinessPartner.ToString()),
        new("Lista de precio del sistema", GenericImportPriceSource.System.ToString()),
    ];

    /// <summary>Campos núcleo editables en la grilla -- todos menos el marcador UserField.</summary>
    public static IReadOnlyList<GenericImportLogicalField> CoreFields { get; } =
        Enum.GetValues<GenericImportLogicalField>().Where(f => f != GenericImportLogicalField.UserField).ToList();

    /// <summary>Las 6 reglas configurables por Formato -- las 2 estructurales (PositiveQuantity/ValidDiscountPercent) no aparecen acá, siempre están activas en Bloqueante.</summary>
    public static readonly IReadOnlyList<GenericImportValidationRuleType> ConfigurableRuleTypes =
    [
        GenericImportValidationRuleType.CustomerActiveInSap,
        GenericImportValidationRuleType.ItemActiveInSap,
        GenericImportValidationRuleType.PriceVsFixedList,
        GenericImportValidationRuleType.PriceVsCustomerList,
        GenericImportValidationRuleType.StockAvailable,
        GenericImportValidationRuleType.CustomerBranchValid,
    ];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Configs = await _configs.ListAsync(ct: ct);
        await LoadUserFieldOptionsAsync(ct);

        if (EditId is { } id)
        {
            var existing = await _configs.GetAsync(id, ct);
            if (existing is not null)
            {
                Input = MapToInput(existing);
            }
        }
        else if (CopyId is { } copyId)
        {
            var existing = await _configs.GetAsync(copyId, ct);
            if (existing is not null)
            {
                Input = MapToInput(existing);
                Input.Alias = existing.Alias + " (copia)";
            }
        }
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        await LoadUserFieldOptionsAsync(ct);

        // DocumentType es una propiedad calculada (ver InputModel.DocumentType) -- no la
        // valida [Required] de DataAnnotations, se valida a mano acá.
        if (string.IsNullOrWhiteSpace(Input.DocumentType))
        {
            ModelState.AddModelError("Input.DocumentType", "Elegí un tipo de documento.");
        }

        // Carga multi-socio: el campo núcleo BusinessPartnerCardCode tiene que tener una
        // columna Excel mapeada -- si no, el motor no tiene de dónde leer el socio de
        // cada fila. Validado acá (antes de guardar), no en el motor -- mismo criterio
        // que el resto de las validaciones de esta pantalla.
        if (Input.BusinessPartnerFromFile
            && !Input.Fields.Any(f => f.LogicalField == GenericImportLogicalField.BusinessPartnerCardCode && !string.IsNullOrWhiteSpace(f.ExcelColumn)))
        {
            ModelState.AddModelError(string.Empty,
                "Carga multi-socio activada -- mapeá una columna Excel para el campo núcleo \"BusinessPartnerCardCode\" en la grilla de abajo.");
        }

        // "Precio vs. lista fija" y "Precio vs. lista del cliente" son mutuamente
        // excluyentes -- SaveValidationRulesAsync lo rechaza igual server-side, pero
        // chequearlo acá evita guardar la cabecera y recién fallar en las reglas.
        var activePriceRules = Input.ValidationRules.Count(r => r.IsActive
            && r.RuleType is GenericImportValidationRuleType.PriceVsFixedList or GenericImportValidationRuleType.PriceVsCustomerList);
        if (activePriceRules > 1)
        {
            ModelState.AddModelError(string.Empty,
                "Solo se puede activar una regla de precio a la vez (\"Precio vs. lista fija\" o \"Precio vs. lista del cliente\").");
        }

        if (!ModelState.IsValid)
        {
            Configs = await _configs.ListAsync(ct: ct);
            return Page();
        }

        var fields = BuildFieldDtos();

        try
        {
            int configId;
            if (EditId is { } id)
            {
                await _configs.UpdateAsync(id, Input.GroupingColumn, Input.SkuIsCustomerOwn, Input.Alias, Input.IsActive,
                    fields, Input.PriceSource, Input.SystemPriceListCode, Input.BusinessPartnerFromFile, ct);
                configId = id;
                SuccessMessage = "Configuración actualizada.";
            }
            else
            {
                configId = await _configs.CreateAsync(Input.Module, Input.DocumentType, Input.LineType, Input.BusinessPartnerCardCode,
                    Input.GroupingColumn, Input.SkuIsCustomerOwn, Input.Alias, fields, Input.PriceSource, Input.SystemPriceListCode,
                    Input.BusinessPartnerFromFile, ct);
                SuccessMessage = "Configuración creada.";
            }

            await _configs.SaveValidationRulesAsync(configId, BuildValidationRuleAssignments(), ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    private List<GenericImportValidationRuleAssignmentDto> BuildValidationRuleAssignments() =>
        Input.ValidationRules.Where(r => r.IsActive).Select(r =>
        {
            var parameters = new Dictionary<string, object?>();
            if (r.RuleType is GenericImportValidationRuleType.PriceVsFixedList)
            {
                parameters["priceListNum"] = r.PriceListNum ?? 0;
            }
            if (r.RuleType is GenericImportValidationRuleType.PriceVsFixedList
                or GenericImportValidationRuleType.PriceVsCustomerList
                or GenericImportValidationRuleType.StockAvailable)
            {
                parameters["tolerancePercent"] = r.TolerancePercent ?? 0m;
            }
            return new GenericImportValidationRuleAssignmentDto(0, r.RuleType, r.Severity, true, parameters);
        }).ToList();

    /// <summary>Ver el comentario completo en OnGetSearchBusinessPartnersAsync (Pages/Importar/Index.cshtml.cs), mismo criterio.</summary>
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

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        await _configs.DeleteAsync(id, ct);
        SuccessMessage = "Configuración eliminada.";
        return RedirectToPage();
    }

    /// <summary>Suma una fila vacía a Campos de usuario, en memoria -- no persiste nada hasta "Guardar". Preserva todo lo demás ya tipeado en el formulario (model binding trae Input completo del POST).</summary>
    public async Task<IActionResult> OnPostAgregarCampoUsuarioAsync(CancellationToken ct)
    {
        await LoadUserFieldOptionsAsync(ct);
        Input.UserFieldMappings.Add(new UserFieldMappingInput());
        Configs = await _configs.ListAsync(ct: ct);
        return Page();
    }

    /// <summary>Saca la fila puntual de Campos de usuario -- asp-page-handler va en el &lt;button&gt;, no en el &lt;form&gt;, para no pisar el resto del submit (ver Index.cshtml).</summary>
    public async Task<IActionResult> OnPostQuitarCampoUsuarioAsync(int index, CancellationToken ct)
    {
        await LoadUserFieldOptionsAsync(ct);
        if (index >= 0 && index < Input.UserFieldMappings.Count)
        {
            Input.UserFieldMappings.RemoveAt(index);
        }
        Configs = await _configs.ListAsync(ct: ct);
        return Page();
    }

    private List<GenericImportConfigFieldDto> BuildFieldDtos()
    {
        var fields = new List<GenericImportConfigFieldDto>();

        foreach (var row in Input.Fields)
        {
            if (string.IsNullOrWhiteSpace(row.ExcelColumn) && string.IsNullOrWhiteSpace(row.FixedValue))
            {
                continue;
            }

            fields.Add(new GenericImportConfigFieldDto(0, row.LogicalField, row.ExcelColumn, row.IsRequired, row.FixedValue, null));
        }

        foreach (var mapping in Input.UserFieldMappings)
        {
            // Mismo criterio que los campos núcleo -- una fila cuenta si tiene columna
            // Excel O valor fijo (el motor ya resuelve FixedValue antes que ExcelColumn,
            // ver GenericImportService.ReadRawRows). Antes exigía ExcelColumn siempre,
            // así que un campo de usuario "siempre este valor fijo, sin columna" no se
            // podía guardar aunque el motor ya lo soportara del lado del procesamiento.
            if (mapping.UserFieldId is null || (string.IsNullOrWhiteSpace(mapping.ExcelColumn) && string.IsNullOrWhiteSpace(mapping.FixedValue)))
            {
                continue;
            }

            fields.Add(new GenericImportConfigFieldDto(0, GenericImportLogicalField.UserField, mapping.ExcelColumn, mapping.IsRequired, mapping.FixedValue, mapping.UserFieldId));
        }

        return fields;
    }

    private static InputModel MapToInput(GenericImportConfigDto config)
    {
        var input = new InputModel
        {
            Module = config.Module,
            LineType = config.LineType,
            BusinessPartnerCardCode = config.BusinessPartnerCardCode,
            GroupingColumn = config.GroupingColumn,
            SkuIsCustomerOwn = config.SkuIsCustomerOwn,
            Alias = config.Alias,
            IsActive = config.IsActive,
            PriceSource = config.PriceSource,
            SystemPriceListCode = config.SystemPriceListCode,
            BusinessPartnerFromFile = config.BusinessPartnerFromFile,
        };

        switch (config.Module)
        {
            case GenericImportModule.Sales:
                input.SalesDocumentType = config.DocumentType;
                break;
            case GenericImportModule.Purchase:
                input.PurchaseDocumentType = config.DocumentType;
                break;
            case GenericImportModule.Inventory:
                input.InventoryDocumentType = config.DocumentType;
                break;
        }

        foreach (var logicalField in CoreFields)
        {
            var existing = config.Fields.FirstOrDefault(f => f.LogicalField == logicalField);
            input.Fields.Add(new FieldInput
            {
                LogicalField = logicalField,
                ExcelColumn = existing?.ExcelColumn,
                IsRequired = existing?.IsRequired ?? false,
                FixedValue = existing?.FixedValue,
            });
        }

        // Una fila por cada campo de usuario YA guardado, sin tope -- antes rellenaba el
        // primer slot vacío de 5 fijos, por lo que una configuración con más de 5 los
        // perdía en silencio al re-guardar (bug real, ver el doc-comment de la clase).
        var userFieldRows = config.Fields.Where(f => f.LogicalField == GenericImportLogicalField.UserField).ToList();
        foreach (var existing in userFieldRows)
        {
            input.UserFieldMappings.Add(new UserFieldMappingInput
            {
                UserFieldId = existing.UserFieldId,
                ExcelColumn = existing.ExcelColumn,
                IsRequired = existing.IsRequired,
                FixedValue = existing.FixedValue,
            });
        }

        input.ValidationRules = BuildValidationRuleInputs(config);

        return input;
    }

    /// <summary>
    /// Una fila por cada una de las 6 reglas configurables, prellenada con la asignación
    /// existente si la hay (config null / regla no asignada -> fila inactiva). Se usa
    /// tanto al editar (MapToInput) como al crear desde cero (LoadUserFieldOptionsAsync).
    /// </summary>
    private static List<ValidationRuleInput> BuildValidationRuleInputs(GenericImportConfigDto? config) =>
        ConfigurableRuleTypes.Select(type =>
        {
            var existing = config?.ValidationRules.FirstOrDefault(r => r.RuleType == type);
            return new ValidationRuleInput
            {
                RuleType = type,
                IsActive = existing?.IsActive ?? false,
                Severity = existing?.Severity ?? GenericImportValidationSeverity.Warning,
                PriceListNum = existing is not null && existing.Parameters.TryGetValue("priceListNum", out var pl) && pl is not null
                    ? Convert.ToInt32(pl)
                    : null,
                TolerancePercent = existing is not null && existing.Parameters.TryGetValue("tolerancePercent", out var tp) && tp is not null
                    ? Convert.ToDecimal(tp)
                    : null,
            };
        }).ToList();

    private async Task LoadUserFieldOptionsAsync(CancellationToken ct)
    {
        var fields = await _userFields.ListAsync(ct: ct);
        UserFieldOptions = fields.Select(f => new SelectListItem($"[{f.Module}] {f.Label} ({f.SapFieldName})", f.Id.ToString())).ToList();

        if (Input.Fields.Count == 0)
        {
            Input.Fields = CoreFields.Select(f => new FieldInput { LogicalField = f }).ToList();
        }

        // Una fila vacía para arrancar (no 5) -- "Agregar campo" suma el resto, ver
        // OnPostAgregarCampoUsuarioAsync.
        if (Input.UserFieldMappings.Count == 0)
        {
            Input.UserFieldMappings = [new UserFieldMappingInput()];
        }

        // Catálogo fijo de 6 reglas configurables -- si el POST no las trajo (alta nueva
        // sin cargar todavía), poblarlas todas inactivas.
        if (Input.ValidationRules.Count == 0)
        {
            Input.ValidationRules = BuildValidationRuleInputs(null);
        }

        await LoadDocumentTypesAsync(ct);
    }

    /// <summary>Un <select> por Módulo, cada uno solo con los tipos que CanCreateAsync permite -- ver el doc-comment de SalesDocumentTypes. Se llama junto con LoadUserFieldOptionsAsync en cada handler.</summary>
    private async Task LoadDocumentTypesAsync(CancellationToken ct)
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

        // 3 propiedades separadas, una por Módulo -- NO un único campo con 3 <select>
        // del mismo name (los 3 SIEMPRE viajan en el POST aunque solo uno esté visible,
        // pisándose entre sí). Se resuelve a un único DocumentType recién al usarlo, ver
        // ResolvedDocumentType -- mismo criterio que Pages/Importar/Index.cshtml.cs
        // (BuildParameters).
        [Display(Name = "Tipo de documento (Venta)")]
        public string? SalesDocumentType { get; set; }

        [Display(Name = "Tipo de documento (Compra)")]
        public string? PurchaseDocumentType { get; set; }

        [Display(Name = "Tipo de documento (Inventario)")]
        public string? InventoryDocumentType { get; set; }

        public string DocumentType => Module switch
        {
            GenericImportModule.Sales => SalesDocumentType ?? string.Empty,
            GenericImportModule.Purchase => PurchaseDocumentType ?? string.Empty,
            GenericImportModule.Inventory => InventoryDocumentType ?? string.Empty,
            _ => string.Empty,
        };

        [Required(ErrorMessage = "Elegí un tipo de línea.")]
        [Display(Name = "Tipo de línea")]
        public GenericImportLineType LineType { get; set; }

        [Display(Name = "Socio de negocio (vacío = estándar de la organización)")]
        public string? BusinessPartnerCardCode { get; set; }

        [Display(Name = "Columna de agrupación (vacío = un único documento)")]
        public string? GroupingColumn { get; set; }

        [Display(Name = "El código de artículo del archivo es el SKU del cliente/proveedor")]
        public bool SkuIsCustomerOwn { get; set; }

        [Display(Name = "Carga multi-socio: cada fila trae su propio código de socio de negocio (columna BusinessPartnerCardCode mapeada en la grilla)")]
        public bool BusinessPartnerFromFile { get; set; }

        [Required(ErrorMessage = "El alias es obligatorio.")]
        [Display(Name = "Alias")]
        public string Alias { get; set; } = string.Empty;

        [Display(Name = "Activo")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Origen del precio")]
        public GenericImportPriceSource PriceSource { get; set; } = GenericImportPriceSource.BusinessPartner;

        [Display(Name = "Lista de precio del sistema")]
        public int? SystemPriceListCode { get; set; }

        public List<FieldInput> Fields { get; set; } = [];
        public List<UserFieldMappingInput> UserFieldMappings { get; set; } = [];
        public List<ValidationRuleInput> ValidationRules { get; set; } = [];
    }

    public sealed class ValidationRuleInput
    {
        public GenericImportValidationRuleType RuleType { get; set; }
        public bool IsActive { get; set; }
        public GenericImportValidationSeverity Severity { get; set; } = GenericImportValidationSeverity.Warning;
        public int? PriceListNum { get; set; }
        public decimal? TolerancePercent { get; set; }
    }

    public sealed class FieldInput
    {
        public GenericImportLogicalField LogicalField { get; set; }
        public string? ExcelColumn { get; set; }
        public bool IsRequired { get; set; }
        public string? FixedValue { get; set; }
    }

    public sealed class UserFieldMappingInput
    {
        public int? UserFieldId { get; set; }
        public string? ExcelColumn { get; set; }
        public bool IsRequired { get; set; }
        public string? FixedValue { get; set; }
    }
}
