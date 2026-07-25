using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Componentes;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages.SalesOrders;

/// <summary>
/// Listado de Órdenes de Venta -- gate vía ICurrentUserContext.HasActionAsync (menú
/// "Ventas.ordenes"), mismo mecanismo de autorización ya establecido por
/// MenuGroup/Profile/UserMenuProfile, no una excepción nueva de este plugin.
/// </summary>
[Authorize]
public sealed class IndexModel : PageModel
{
    private const string MenuCode = "Ventas.ordenes";

    private readonly ISalesOrderService _salesOrders;
    private readonly ICurrentUserContext _currentUser;

    public IndexModel(ISalesOrderService salesOrders, ICurrentUserContext currentUser)
    {
        _salesOrders = salesOrders;
        _currentUser = currentUser;
    }

    [BindProperty(SupportsGet = true)]
    public FilterInput Filter { get; set; } = new();

    public DocumentListViewModel Listado { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await _currentUser.HasActionAsync(MenuCode, PortalActions.View, ct))
        {
            return Forbid();
        }

        var filter = new SalesOrderFilter(
            DateFrom: Filter.DateFrom,
            DateTo: Filter.DateTo,
            CustomerCardCode: Filter.CustomerCardCode,
            DocNum: Filter.DocNum);

        var result = await _salesOrders.ListAsync(filter, ct: ct);

        var canCreate = await _salesOrders.CanCreateAsync(ct) && await _currentUser.HasActionAsync(MenuCode, PortalActions.Create, ct);

        Listado = new DocumentListViewModel
        {
            Title = "Órdenes de Venta",
            Columns =
            [
                new DocumentListColumn("N° documento"),
                new DocumentListColumn("Cliente"),
                new DocumentListColumn("Fecha"),
                new DocumentListColumn("N° ref. cliente"),
                new DocumentListColumn("Vendedor"),
                new DocumentListColumn("Total", AlignRight: true),
                new DocumentListColumn("Estado"),
            ],
            Rows = result.Items.Select(item => new DocumentListRow(
                Cells:
                [
                    item.DocNum.ToString(),
                    $"{item.CustomerCardCode} — {item.CustomerName}",
                    item.DocDate.ToString("yyyy-MM-dd"),
                    item.CustomerReferenceNumber ?? "-",
                    item.SalesEmployeeName ?? "-",
                    item.DocTotal.ToString("N2"),
                    item.Status,
                ],
                DetailUrl: $"/ventas/ordenes/{item.DocEntry}")).ToList(),
            Filters =
            [
                new DocumentListFilter(nameof(FilterInput.DateFrom), "Desde", FilterFieldType.Date, Filter.DateFrom?.ToString("yyyy-MM-dd")),
                new DocumentListFilter(nameof(FilterInput.DateTo), "Hasta", FilterFieldType.Date, Filter.DateTo?.ToString("yyyy-MM-dd")),
                new DocumentListFilter(nameof(FilterInput.CustomerCardCode), "Código de cliente", FilterFieldType.Text, Filter.CustomerCardCode),
                new DocumentListFilter(nameof(FilterInput.DocNum), "N° documento", FilterFieldType.Number, Filter.DocNum?.ToString()),
            ],
            ShowFreeTextSearch = false,
            CreateUrl = canCreate ? "/ventas/ordenes/nuevo" : null,
            CreateDisabledTitle = canCreate ? null : "No tenés permiso para crear órdenes de venta",
            ShowPaging = true,
            TotalRecords = result.TotalRecords,
        };

        return Page();
    }

    public sealed class FilterInput
    {
        public DateOnly? DateFrom { get; set; }
        public DateOnly? DateTo { get; set; }
        public string? CustomerCardCode { get; set; }
        public int? DocNum { get; set; }
    }
}
