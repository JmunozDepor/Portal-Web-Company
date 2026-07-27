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
/// misma configuración = mismo resultado determinístico) al confirmar. Sin polling de
/// progreso vía JS -- CreateDocumentsAsync corre síncrono en el mismo request y el
/// resultado final se muestra directo (IGenericImportProgressStore queda listo para
/// una futura mejora con polling si el volumen de documentos lo justifica).
/// </summary>
public sealed class IndexModel : PageModelBaseAdmin
{
    private readonly IGenericImportService _importService;
    private readonly ISalesDocumentService _sales;
    private readonly IPurchaseDocumentService _purchase;
    private readonly IInventoryDocumentService _inventory;
    private readonly ICustomerCatalogService _customers;
    private readonly ISupplierCatalogService _suppliers;

    public IndexModel(ICurrentUserContext currentUser, IGenericImportService importService,
        ISalesDocumentService sales, IPurchaseDocumentService purchase, IInventoryDocumentService inventory,
        ICustomerCatalogService customers, ISupplierCatalogService suppliers) : base(currentUser)
    {
        _importService = importService;
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
    public GenericImportProgressDto? FinalProgress { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadCreatableDocumentTypesAsync(ct);
    }

    public async Task<IActionResult> OnPostProcessAsync(IFormFile file, CancellationToken ct)
    {
        await LoadCreatableDocumentTypesAsync(ct);

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

    public async Task<IActionResult> OnPostConfirmAsync(CancellationToken ct)
    {
        await LoadCreatableDocumentTypesAsync(ct);

        if (string.IsNullOrEmpty(Input.Base64File))
        {
            ModelState.AddModelError(string.Empty, "Volvé a procesar el archivo antes de confirmar.");
            return Page();
        }

        var parameters = BuildParameters();
        var bytes = Convert.FromBase64String(Input.Base64File);
        using var stream = new MemoryStream(bytes);
        PreviewResult = await _importService.ProcessFileAsync(parameters, stream, ct);

        if (!PreviewResult.HasValidConfig)
        {
            return Page();
        }

        var jobId = Guid.NewGuid().ToString("N");
        FinalProgress = await _importService.CreateDocumentsAsync(jobId, CurrentUser.Username, parameters, PreviewResult.Documents, ct);
        return Page();
    }

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

    public async Task<IActionResult> OnGetTemplateAsync(GenericImportModule module, string documentType, GenericImportLineType lineType,
        string? businessPartnerCardCode, CancellationToken ct)
    {
        var parameters = new GenericImportParametersDto(CurrentUser.OrganizationId, module, documentType, lineType, businessPartnerCardCode);
        var bytes = await _importService.GenerateTemplateAsync(parameters, ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "plantilla-importacion.xlsx");
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

        return new GenericImportParametersDto(CurrentUser.OrganizationId, Input.Module, documentType ?? string.Empty,
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
