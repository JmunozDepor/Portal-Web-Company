using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Componentes;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Compras.Pages;

/// <summary>
/// Crear/ver un documento de compra genérico -- solo creación, sin
/// IPurchaseDocumentService.UpdateAsync todavía (digitación directa: se crea una vez,
/// después es de solo lectura vía el portal). Mismo patrón que
/// DetailGenericSalesDocumentModelBase (Modulo.Ventas) -- copia deliberada, no una base
/// compartida entre plugins.
///
/// REGLA DURA (ver CLAUDE.md): todo nace del documento padre. OnGetAsync/OnPostAsync/
/// OnGetSearchItemsAsync NO son virtual a propósito -- ningún subtipo puede modificar
/// cómo se crea/valida/lee un documento, solo puede identificarse. Cualquier tipo de
/// documento de compra nuevo se agrega acá (PurchaseDocumentTypeCatalog + este
/// subtipo), nunca reimplementando esta clase.
/// </summary>
[Authorize]
public abstract class DetailGenericPurchaseDocumentModelBase : PageModel
{
    private const string NewId = "nuevo";

    private readonly IPurchaseDocumentService _documents;
    private readonly ICurrentUserContext _currentUser;
    private readonly ISupplierCatalogService _suppliers;
    private readonly IWarehouseCatalogService _warehouses;
    private readonly IItemCatalogService _items;
    private readonly IGeneralLedgerAccountCatalogService _accounts;
    private readonly ICostCenterCatalogService _costCenters;
    private readonly ISeriesCatalogService _series;

    protected DetailGenericPurchaseDocumentModelBase(
        IPurchaseDocumentService documents,
        ICurrentUserContext currentUser,
        ISupplierCatalogService suppliers,
        IWarehouseCatalogService warehouses,
        IItemCatalogService items,
        IGeneralLedgerAccountCatalogService accounts,
        ICostCenterCatalogService costCenters,
        ISeriesCatalogService series)
    {
        _documents = documents;
        _currentUser = currentUser;
        _suppliers = suppliers;
        _warehouses = warehouses;
        _items = items;
        _accounts = accounts;
        _costCenters = costCenters;
        _series = series;
    }

    protected abstract PurchaseDocumentType Type { get; }
    protected abstract string MenuCode { get; }
    public abstract string DocumentName { get; }
    public abstract string RouteBase { get; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>
    /// URL del listado que llevó acá (con su página/filtros actuales) -- IndexGeneric*ModelBase
    /// la arma al construir cada DetailUrl, para que "Volver" no resetee la paginación
    /// (bug real reportado: "Volver" desde la página 3 del listado volvía siempre a la
    /// página 1, porque BackUrl era RouteBase fijo, sin querystring). Url.IsLocalUrl,
    /// mismo criterio anti-open-redirect que Account/Login.cshtml.cs.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    public bool IsNew { get; private set; }
    public int? DocEntry { get; private set; }
    public int? DocNum { get; private set; }
    public decimal? DocTotal { get; private set; }
    public string? Status { get; private set; }

    /// <summary>Ver el doc-comment de CustomerName en DetailGenericSalesDocumentModelBase (Modulo.Ventas) -- mismo criterio, lado proveedor.</summary>
    public string? SupplierName { get; private set; }

    public List<SelectListItem> Series { get; private set; } = [];

    /// <summary>
    /// "Resumen total" del tab Contenido -- mismo cálculo que
    /// DetailGenericSalesDocumentModelBase (Modulo.Ventas), portado ahí primero de
    /// DetalleGenericoVentaModelBase (referencia-original/PortalSAP_v2), replicado
    /// acá por la regla de paridad: Compras también tiene precio/descuento por línea.
    /// </summary>
    public decimal TotalBeforeDiscount => Input.Lines.Sum(l => (l.Quantity ?? 0) * (l.UnitPrice ?? 0));
    public decimal Discount => Input.Lines.Sum(l => (l.Quantity ?? 0) * (l.UnitPrice ?? 0) * (l.DiscountPercent ?? 0) / 100m);
    public decimal AdditionalExpenses => 0m;
    public decimal DocumentTotal => DocTotal ?? (TotalBeforeDiscount - Discount);
    public decimal Tax => DocumentTotal - (TotalBeforeDiscount - Discount) - AdditionalExpenses;

    public DocumentFormViewModel Document { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken ct)
    {
        IsNew = id == NewId;

        var requiredAction = IsNew ? PortalActions.Create : PortalActions.View;
        if (!await _currentUser.HasActionAsync(MenuCode, requiredAction, ct))
        {
            return Forbid();
        }

        if (IsNew)
        {
            if (!await _documents.CanCreateAsync(Type, ct))
            {
                return Forbid();
            }

            Input = new InputModel
            {
                DocDate = DateOnly.FromDateTime(DateTime.Today),
                DocDueDate = DateOnly.FromDateTime(DateTime.Today),
                TaxDate = DateOnly.FromDateTime(DateTime.Today),
                LineType = DocumentLineType.Item,
                Series = null,
            };
        }
        else
        {
            if (!int.TryParse(id, out var docEntry))
            {
                return NotFound();
            }

            var document = await _documents.GetAsync(Type, docEntry, ct);
            if (document is null)
            {
                return NotFound();
            }

            DocEntry = document.DocEntry;
            DocNum = document.DocNum;
            DocTotal = document.DocTotal;
            Status = document.Status;
            SupplierName = document.SupplierName;

            Input = new InputModel
            {
                SupplierCardCode = document.SupplierCardCode,
                Comments = document.Comments,
                DocDate = document.DocDate,
                DocDueDate = document.DocDueDate,
                TaxDate = document.TaxDate,
                SupplierReferenceNumber = document.SupplierReferenceNumber,
                LineType = document.Lines.Count > 0 ? document.Lines[0].Type : DocumentLineType.Item,
                Series = document.Series,
                Lines = document.Lines.Select(line => new LineInput
                {
                    ItemCode = line.ItemCode,
                    Description = line.Description,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    DiscountPercent = line.DiscountPercent,
                    WarehouseCode = line.WarehouseCode,
                    AccountCode = line.AccountCode,
                    CostCenterCode = line.CostCenterCode,
                    CostCenterCode2 = line.CostCenterCode2,
                    CostCenterCode3 = line.CostCenterCode3,
                }).ToList(),
            };
        }

        await LoadCatalogsAsync(ct);
        BuildDocumentViewModel();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        IsNew = true;

        if (!await _currentUser.HasActionAsync(MenuCode, PortalActions.Create, ct) || !await _documents.CanCreateAsync(Type, ct))
        {
            return Forbid();
        }

        var isService = Input.LineType == DocumentLineType.Service;

        Input.Lines = Input.Lines
            .Where(line => isService ? !string.IsNullOrWhiteSpace(line.Description) : !string.IsNullOrWhiteSpace(line.ItemCode))
            .ToList();

        if (Input.Lines.Count == 0)
        {
            ModelState.AddModelError(string.Empty, $"Agregá al menos una línea antes de crear {DocumentName.ToLowerInvariant()}.");
        }

        for (var i = 0; i < Input.Lines.Count; i++)
        {
            var line = Input.Lines[i];

            if (isService)
            {
                if (string.IsNullOrWhiteSpace(line.AccountCode))
                {
                    ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Lines)}[{i}].{nameof(LineInput.AccountCode)}", "Elegí una cuenta mayor para esta línea.");
                }

                if (string.IsNullOrWhiteSpace(line.CostCenterCode))
                {
                    ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Lines)}[{i}].{nameof(LineInput.CostCenterCode)}", "Elegí un centro de costos para esta línea.");
                }
            }
            else if (string.IsNullOrWhiteSpace(line.WarehouseCode))
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Lines)}[{i}].{nameof(LineInput.WarehouseCode)}", "Elegí un almacén para esta línea.");
            }

            if (line.Quantity is null or <= 0)
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Lines)}[{i}].{nameof(LineInput.Quantity)}", "La cantidad debe ser mayor a 0.");
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadCatalogsAsync(ct);
            BuildDocumentViewModel();
            return Page();
        }

        var document = new PurchaseDocumentDto(
            SupplierCardCode: Input.SupplierCardCode,
            SupplierName: null,
            Comments: Input.Comments,
            DocDate: Input.DocDate,
            DocDueDate: Input.DocDueDate,
            TaxDate: Input.TaxDate,
            SupplierReferenceNumber: Input.SupplierReferenceNumber,
            Lines: Input.Lines.Select(line => new PurchaseDocumentLineDto(
                Type: Input.LineType,
                ItemCode: isService ? null : line.ItemCode,
                Description: line.Description,
                Quantity: line.Quantity ?? 0,
                UnitPrice: line.UnitPrice,
                DiscountPercent: line.DiscountPercent ?? 0,
                WarehouseCode: isService ? null : line.WarehouseCode,
                AccountCode: isService ? line.AccountCode : null,
                CostCenterCode: isService ? line.CostCenterCode : null,
                CostCenterCode2: isService ? line.CostCenterCode2 : null,
                CostCenterCode3: isService ? line.CostCenterCode3 : null)).ToList(),
            Series: Input.Series);

        try
        {
            var docEntry = await _documents.CreateAsync(Type, _currentUser.Username, document, ct);
            return RedirectToPage(new { id = docEntry.ToString() });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo crear el documento: {ex.Message}");
            await LoadCatalogsAsync(ct);
            BuildDocumentViewModel();
            return Page();
        }
    }

    /// <summary>Búsqueda en vivo del artículo -- nunca se precarga el catálogo completo (ver IItemCatalogService).</summary>
    public async Task<JsonResult> OnGetSearchItemsAsync(string text, CancellationToken ct)
    {
        var items = await _items.SearchAsync(text ?? string.Empty, ct: ct);
        return new JsonResult(items.Select(i => new { i.ItemCode, i.ItemName }));
    }

    /// <summary>Ver el comentario completo en OnGetValidateItemCodesAsync (DetailGenericSalesDocumentModelBase, Modulo.Ventas), mismo criterio.</summary>
    public async Task<JsonResult> OnGetValidateItemCodesAsync(string[] codes, CancellationToken ct)
    {
        var distinctCodes = (codes ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var items = await _items.GetByCodesAsync(distinctCodes, ct);
        return new JsonResult(items.Select(i => new { i.ItemCode, i.ItemName }));
    }

    /// <summary>Búsqueda en vivo del proveedor -- ver el comentario completo en OnGetSearchCustomersAsync (DetailGenericSalesDocumentModelBase, Modulo.Ventas), mismo criterio lado proveedor.</summary>
    public async Task<JsonResult> OnGetSearchSuppliersAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonResult(Array.Empty<object>());
        }

        var suppliers = await _suppliers.ListAsync(new SupplierFilter(text, Limit: 30), ct);
        return new JsonResult(suppliers.Select(s => new { s.CardCode, s.CardName }));
    }

    /// <summary>Ver el comentario completo en OnGetSearchWarehousesAsync (DetailGenericSalesDocumentModelBase, Modulo.Ventas) -- catálogo chico, sin guard de texto vacío.</summary>
    public async Task<JsonResult> OnGetSearchWarehousesAsync(string text, CancellationToken ct)
    {
        var warehouses = await _warehouses.ListAsync(text, 30, ct);
        return new JsonResult(warehouses.Select(w => new { w.WarehouseCode, w.WarehouseName }));
    }

    public async Task<JsonResult> OnGetSearchAccountsAsync(string text, CancellationToken ct)
    {
        var accounts = await _accounts.ListAsync(text, 30, ct);
        return new JsonResult(accounts.Select(a => new { a.AccountCode, a.AccountName }));
    }

    public async Task<JsonResult> OnGetSearchCostCentersAsync(string text, CancellationToken ct)
    {
        var costCenters = await _costCenters.ListAsync(text, 30, ct);
        return new JsonResult(costCenters.Select(c => new { c.Code, c.Name }));
    }

    public async Task<JsonResult> OnGetSearchCostCenters2Async(string text, CancellationToken ct)
    {
        var costCenters = await _costCenters.ListByDimensionAsync(2, text, 30, ct);
        return new JsonResult(costCenters.Select(c => new { c.Code, c.Name }));
    }

    public async Task<JsonResult> OnGetSearchCostCenters3Async(string text, CancellationToken ct)
    {
        var costCenters = await _costCenters.ListByDimensionAsync(5, text, 30, ct);
        return new JsonResult(costCenters.Select(c => new { c.Code, c.Name }));
    }

    private async Task LoadCatalogsAsync(CancellationToken ct)
    {
        var series = await _series.ListAsync(_documents.GetSapObjectCode(Type).ToString(), ct: ct);
        Series = series.Select(s => new SelectListItem(s.SeriesName, s.SeriesCode.ToString())).ToList();
    }

    private void BuildDocumentViewModel()
    {
        Document = new DocumentFormViewModel
        {
            Title = IsNew ? $"Nuevo/a {DocumentName}" : $"{DocumentName} N° {DocNum}",
            ReadOnly = !IsNew,
            BackUrl = Url.IsLocalUrl(ReturnUrl) && ReturnUrl is not null ? ReturnUrl : Request.PathBase + RouteBase,
            StatusText = Status,
            StatusClass = Status == "Abierto" ? "bg-success" : "bg-secondary",
            Model = this,
            GeneralView = "~/Pages/Shared/_TabGeneralCompras.cshtml",
            ContentView = "~/Pages/Shared/_TabContentCompras.cshtml",
        };

        // Consumido por _ModuloBackLink.cshtml (breadcrumb del layout) -- ver el
        // comentario equivalente en DetailGenericSalesDocumentModelBase.cs.
        ViewData["ModuleBreadcrumbBackUrl"] = Document.BackUrl;
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Elegí un proveedor.")]
        [Display(Name = "Proveedor")]
        public string SupplierCardCode { get; set; } = string.Empty;

        [Display(Name = "Comentarios")]
        public string? Comments { get; set; }

        [Display(Name = "Fecha de documento")]
        public DateOnly DocDate { get; set; }

        [Display(Name = "Fecha de vencimiento")]
        public DateOnly DocDueDate { get; set; }

        [Display(Name = "Fecha de contabilización")]
        public DateOnly TaxDate { get; set; }

        [Display(Name = "N° referencia proveedor")]
        public string? SupplierReferenceNumber { get; set; }

        [Display(Name = "Serie")]
        public int? Series { get; set; }

        [Display(Name = "Tipo de línea")]
        public DocumentLineType LineType { get; set; } = DocumentLineType.Item;

        public List<LineInput> Lines { get; set; } = [new()];
    }

    public sealed class LineInput
    {
        public string? ItemCode { get; set; }
        public string? Description { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? DiscountPercent { get; set; }
        public string? WarehouseCode { get; set; }
        public string? AccountCode { get; set; }
        public string? CostCenterCode { get; set; }
        public string? CostCenterCode2 { get; set; }
        public string? CostCenterCode3 { get; set; }
    }
}
