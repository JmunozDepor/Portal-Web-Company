using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Componentes;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages.SalesOrders;

/// <summary>
/// Crear/ver una Orden de Venta. Solo creación -- sin ISalesOrderService.UpdateAsync
/// todavía (digitación directa: se crea una vez, después es de solo lectura vía el
/// portal; editarla de verdad se hace directo en SAP, mismo criterio que la
/// referencia para este tipo de documento sin workflow).
/// </summary>
[Authorize]
public sealed class DetailModel : PageModel
{
    private const string MenuCode = "Ventas.ordenes";
    private const string NewId = "nuevo";

    private readonly ISalesOrderService _salesOrders;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICustomerCatalogService _customers;
    private readonly IWarehouseCatalogService _warehouses;
    private readonly ISalesEmployeeCatalogService _salesEmployees;
    private readonly IItemCatalogService _items;

    public DetailModel(
        ISalesOrderService salesOrders,
        ICurrentUserContext currentUser,
        ICustomerCatalogService customers,
        IWarehouseCatalogService warehouses,
        ISalesEmployeeCatalogService salesEmployees,
        IItemCatalogService items)
    {
        _salesOrders = salesOrders;
        _currentUser = currentUser;
        _customers = customers;
        _warehouses = warehouses;
        _salesEmployees = salesEmployees;
        _items = items;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool IsNew { get; private set; }
    public int? DocEntry { get; private set; }
    public int? DocNum { get; private set; }
    public decimal? DocTotal { get; private set; }
    public string? Status { get; private set; }

    public List<SelectListItem> Customers { get; private set; } = [];
    public List<SelectListItem> Warehouses { get; private set; } = [];
    public List<SelectListItem> SalesEmployees { get; private set; } = [];

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
            if (!await _salesOrders.CanCreateAsync(ct))
            {
                return Forbid();
            }

            Input = new InputModel
            {
                DocDate = DateOnly.FromDateTime(DateTime.Today),
                DocDueDate = DateOnly.FromDateTime(DateTime.Today),
                TaxDate = DateOnly.FromDateTime(DateTime.Today),
            };
        }
        else
        {
            if (!int.TryParse(id, out var docEntry))
            {
                return NotFound();
            }

            var order = await _salesOrders.GetAsync(docEntry, ct);
            if (order is null)
            {
                return NotFound();
            }

            DocEntry = order.DocEntry;
            DocNum = order.DocNum;
            DocTotal = order.DocTotal;
            Status = order.Status;

            Input = new InputModel
            {
                CustomerCardCode = order.CustomerCardCode,
                SalesEmployeeCode = order.SalesEmployeeCode,
                Comments = order.Comments,
                DocDate = order.DocDate,
                DocDueDate = order.DocDueDate,
                TaxDate = order.TaxDate,
                CustomerReferenceNumber = order.CustomerReferenceNumber,
                Lines = order.Lines.Select(line => new LineInput
                {
                    ItemCode = line.ItemCode,
                    Description = line.Description,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    DiscountPercent = line.DiscountPercent,
                    WarehouseCode = line.WarehouseCode,
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

        if (!await _currentUser.HasActionAsync(MenuCode, PortalActions.Create, ct) || !await _salesOrders.CanCreateAsync(ct))
        {
            return Forbid();
        }

        Input.Lines = Input.Lines.Where(line => !string.IsNullOrWhiteSpace(line.ItemCode)).ToList();

        if (Input.Lines.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Agregá al menos una línea antes de crear la orden.");
        }

        for (var i = 0; i < Input.Lines.Count; i++)
        {
            var line = Input.Lines[i];
            if (string.IsNullOrWhiteSpace(line.WarehouseCode))
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Lines)}[{i}].{nameof(LineInput.WarehouseCode)}", "Elegí un almacén para esta línea.");
            }

            if (line.Quantity <= 0)
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

        var document = new SalesOrderDto(
            CustomerCardCode: Input.CustomerCardCode,
            CustomerName: null,
            SalesEmployeeCode: Input.SalesEmployeeCode,
            Comments: Input.Comments,
            DocDate: Input.DocDate,
            DocDueDate: Input.DocDueDate,
            TaxDate: Input.TaxDate,
            CustomerReferenceNumber: Input.CustomerReferenceNumber,
            Lines: Input.Lines.Select(line => new SalesOrderLineDto(
                ItemCode: line.ItemCode!,
                Description: line.Description,
                Quantity: line.Quantity,
                UnitPrice: line.UnitPrice,
                DiscountPercent: line.DiscountPercent,
                WarehouseCode: line.WarehouseCode!)).ToList());

        try
        {
            var docEntry = await _salesOrders.CreateAsync(_currentUser.Username, document, ct);
            return RedirectToPage(new { id = docEntry.ToString() });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"No se pudo crear la orden: {ex.Message}");
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

    private async Task LoadCatalogsAsync(CancellationToken ct)
    {
        var customers = await _customers.ListAsync(ct: ct);
        Customers = customers.Select(c => new SelectListItem($"{c.CardCode} — {c.CardName}", c.CardCode)).ToList();

        var warehouses = await _warehouses.ListAsync(ct);
        Warehouses = warehouses.Select(w => new SelectListItem($"{w.WarehouseCode} — {w.WarehouseName}", w.WarehouseCode)).ToList();

        var salesEmployees = await _salesEmployees.ListAsync(ct);
        SalesEmployees = salesEmployees.Select(s => new SelectListItem(s.SalesEmployeeName, s.SalesEmployeeCode.ToString())).ToList();
    }

    private void BuildDocumentViewModel()
    {
        Document = new DocumentFormViewModel
        {
            Title = IsNew ? "Nueva Orden de Venta" : $"Orden de Venta N° {DocNum}",
            ReadOnly = !IsNew,
            BackUrl = "/ventas/ordenes",
            StatusText = Status,
            StatusClass = Status == "Abierto" ? "bg-success" : "bg-secondary",
            Model = this,
            GeneralView = "~/Pages/Shared/_TabGeneral.cshtml",
            ContentView = "~/Pages/Shared/_TabContent.cshtml",
        };
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Elegí un cliente.")]
        [Display(Name = "Cliente")]
        public string CustomerCardCode { get; set; } = string.Empty;

        [Display(Name = "Vendedor")]
        public int? SalesEmployeeCode { get; set; }

        [Display(Name = "Comentarios")]
        public string? Comments { get; set; }

        [Display(Name = "Fecha de documento")]
        public DateOnly DocDate { get; set; }

        [Display(Name = "Fecha de vencimiento")]
        public DateOnly DocDueDate { get; set; }

        [Display(Name = "Fecha de contabilización")]
        public DateOnly TaxDate { get; set; }

        [Display(Name = "N° referencia cliente")]
        public string? CustomerReferenceNumber { get; set; }

        public List<LineInput> Lines { get; set; } = [new()];
    }

    public sealed class LineInput
    {
        public string? ItemCode { get; set; }
        public string? Description { get; set; }
        public decimal Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public string? WarehouseCode { get; set; }
    }
}
