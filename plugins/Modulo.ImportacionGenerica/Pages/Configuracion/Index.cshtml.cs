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
/// los 3 motores genéricos); los campos de usuario se mapean en una sección aparte con
/// un puñado de filas pre-renderizadas en blanco.
/// </summary>
public sealed class IndexModel : PageModelBaseAdmin
{
    private const int UserFieldMappingRows = 5;

    private readonly IGenericImportConfigService _configs;
    private readonly IGenericImportUserFieldService _userFields;
    private readonly ICustomerCatalogService _customers;
    private readonly ISupplierCatalogService _suppliers;

    public IndexModel(ICurrentUserContext currentUser, IGenericImportConfigService configs, IGenericImportUserFieldService userFields,
        ICustomerCatalogService customers, ISupplierCatalogService suppliers)
        : base(currentUser)
    {
        _configs = configs;
        _userFields = userFields;
        _customers = customers;
        _suppliers = suppliers;
    }

    [BindProperty(SupportsGet = true)]
    public int? EditId { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

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

    public List<SelectListItem> PriceSources { get; } =
    [
        new("Socio de negocio (default)", GenericImportPriceSource.BusinessPartner.ToString()),
        new("Lista de precio del sistema", GenericImportPriceSource.System.ToString()),
    ];

    /// <summary>Campos núcleo editables en la grilla -- todos menos el marcador UserField.</summary>
    public static IReadOnlyList<GenericImportLogicalField> CoreFields { get; } =
        Enum.GetValues<GenericImportLogicalField>().Where(f => f != GenericImportLogicalField.UserField).ToList();

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
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        await LoadUserFieldOptionsAsync(ct);

        if (!ModelState.IsValid)
        {
            Configs = await _configs.ListAsync(ct: ct);
            return Page();
        }

        var fields = BuildFieldDtos();

        try
        {
            if (EditId is { } id)
            {
                await _configs.UpdateAsync(id, Input.GroupingColumn, Input.SkuIsCustomerOwn, Input.Alias, Input.IsActive,
                    fields, Input.PriceSource, Input.SystemPriceListCode, ct);
                SuccessMessage = "Configuración actualizada.";
            }
            else
            {
                await _configs.CreateAsync(Input.Module, Input.DocumentType, Input.LineType, Input.BusinessPartnerCardCode,
                    Input.GroupingColumn, Input.SkuIsCustomerOwn, Input.Alias, fields, Input.PriceSource, Input.SystemPriceListCode, ct);
                SuccessMessage = "Configuración creada.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

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
            if (mapping.UserFieldId is null || string.IsNullOrWhiteSpace(mapping.ExcelColumn))
            {
                continue;
            }

            fields.Add(new GenericImportConfigFieldDto(0, GenericImportLogicalField.UserField, mapping.ExcelColumn, mapping.IsRequired, null, mapping.UserFieldId));
        }

        return fields;
    }

    private static InputModel MapToInput(GenericImportConfigDto config)
    {
        var input = new InputModel
        {
            Module = config.Module,
            DocumentType = config.DocumentType,
            LineType = config.LineType,
            BusinessPartnerCardCode = config.BusinessPartnerCardCode,
            GroupingColumn = config.GroupingColumn,
            SkuIsCustomerOwn = config.SkuIsCustomerOwn,
            Alias = config.Alias,
            IsActive = config.IsActive,
            PriceSource = config.PriceSource,
            SystemPriceListCode = config.SystemPriceListCode,
        };

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

        var userFieldRows = config.Fields.Where(f => f.LogicalField == GenericImportLogicalField.UserField).ToList();
        for (var i = 0; i < UserFieldMappingRows; i++)
        {
            var existing = i < userFieldRows.Count ? userFieldRows[i] : null;
            input.UserFieldMappings.Add(new UserFieldMappingInput
            {
                UserFieldId = existing?.UserFieldId,
                ExcelColumn = existing?.ExcelColumn,
                IsRequired = existing?.IsRequired ?? false,
            });
        }

        return input;
    }

    private async Task LoadUserFieldOptionsAsync(CancellationToken ct)
    {
        var fields = await _userFields.ListAsync(ct: ct);
        UserFieldOptions = fields.Select(f => new SelectListItem($"[{f.Module}] {f.Label} ({f.SapFieldName})", f.Id.ToString())).ToList();

        if (Input.Fields.Count == 0)
        {
            Input.Fields = CoreFields.Select(f => new FieldInput { LogicalField = f }).ToList();
        }

        if (Input.UserFieldMappings.Count == 0)
        {
            Input.UserFieldMappings = Enumerable.Range(0, UserFieldMappingRows).Select(_ => new UserFieldMappingInput()).ToList();
        }
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Elegí un módulo.")]
        [Display(Name = "Módulo")]
        public GenericImportModule Module { get; set; }

        [Required(ErrorMessage = "El tipo de documento es obligatorio.")]
        [Display(Name = "Tipo de documento")]
        public string DocumentType { get; set; } = string.Empty;

        [Required(ErrorMessage = "Elegí un tipo de línea.")]
        [Display(Name = "Tipo de línea")]
        public GenericImportLineType LineType { get; set; }

        [Display(Name = "Socio de negocio (vacío = estándar de la organización)")]
        public string? BusinessPartnerCardCode { get; set; }

        [Display(Name = "Columna de agrupación (vacío = un único documento)")]
        public string? GroupingColumn { get; set; }

        [Display(Name = "El código de artículo del archivo es el SKU del cliente/proveedor")]
        public bool SkuIsCustomerOwn { get; set; }

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
    }
}
