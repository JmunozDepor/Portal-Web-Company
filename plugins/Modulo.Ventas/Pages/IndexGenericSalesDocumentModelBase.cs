using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Componentes;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas.Pages;

/// <summary>
/// Listado de un documento de venta genérico -- gate vía
/// ICurrentUserContext.HasActionAsync, mismo mecanismo de autorización ya establecido
/// por MenuGroup/Profile/UserMenuProfile. Generaliza el antiguo IndexModel de
/// SalesOrders/ (ahí vivía toda esta lógica hardcodeada a un solo tipo) -- portado de
/// IndexGenericoVentaModelBase en referencia-original/PortalSAP_v2: cada subtipo
/// concreto (SalesOrders/IndexModel, CreditNotes/IndexModel, ...) solo declara Type/
/// MenuCode/DocumentName/DocumentNamePlural/RouteBase, toda la lógica vive acá.
/// </summary>
[Authorize]
public abstract class IndexGenericSalesDocumentModelBase : PageModel
{
    private readonly ISalesDocumentService _documents;
    private readonly ICurrentUserContext _currentUser;

    protected IndexGenericSalesDocumentModelBase(ISalesDocumentService documents, ICurrentUserContext currentUser)
    {
        _documents = documents;
        _currentUser = currentUser;
    }

    protected abstract SalesDocumentType Type { get; }
    protected abstract string MenuCode { get; }
    public abstract string DocumentNamePlural { get; }
    public abstract string RouteBase { get; }

    [BindProperty(SupportsGet = true)]
    public FilterInput Filter { get; set; } = new();

    public DocumentListViewModel Listado { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await _currentUser.HasActionAsync(MenuCode, PortalActions.View, ct))
        {
            return Forbid();
        }

        var filter = new SalesDocumentFilter(
            DateFrom: Filter.DateFrom,
            DateTo: Filter.DateTo,
            CustomerCardCode: Filter.CustomerCardCode,
            DocNum: Filter.DocNum);

        var result = await _documents.ListAsync(Type, filter, ct: ct);

        var canCreate = await _documents.CanCreateAsync(Type, ct) && await _currentUser.HasActionAsync(MenuCode, PortalActions.Create, ct);

        Listado = new DocumentListViewModel
        {
            Title = DocumentNamePlural,
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
                DetailUrl: $"{RouteBase}/{item.DocEntry}")).ToList(),
            Filters =
            [
                new DocumentListFilter(nameof(FilterInput.DateFrom), "Desde", FilterFieldType.Date, Filter.DateFrom?.ToString("yyyy-MM-dd")),
                new DocumentListFilter(nameof(FilterInput.DateTo), "Hasta", FilterFieldType.Date, Filter.DateTo?.ToString("yyyy-MM-dd")),
                new DocumentListFilter(nameof(FilterInput.CustomerCardCode), "Código de cliente", FilterFieldType.Text, Filter.CustomerCardCode),
                new DocumentListFilter(nameof(FilterInput.DocNum), "N° documento", FilterFieldType.Number, Filter.DocNum?.ToString()),
            ],
            ShowFreeTextSearch = false,
            CreateUrl = canCreate ? $"{RouteBase}/nuevo" : null,
            CreateDisabledTitle = canCreate ? null : $"No tenés permiso para crear {DocumentNamePlural.ToLowerInvariant()}",
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
