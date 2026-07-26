using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Componentes;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Inventario.Pages;

/// <summary>
/// Listado de un documento de inventario genérico -- gate vía
/// ICurrentUserContext.HasActionAsync, mismo mecanismo de autorización ya establecido.
/// Mismo patrón que IndexGenericSalesDocumentModelBase (Modulo.Ventas) -- copia
/// deliberada, no una base compartida entre plugins (regla dura: un plugin nunca
/// referencia a otro).
///
/// REGLA DURA (ver CLAUDE.md): todo nace del documento padre. OnGetAsync NO es
/// virtual a propósito -- ningún subtipo puede modificar cómo se lista un documento,
/// solo puede identificarse. Cualquier tipo de documento de inventario nuevo se
/// agrega acá (InventoryDocumentTypeCatalog + este subtipo), nunca reimplementando
/// esta clase.
/// </summary>
[Authorize]
public abstract class IndexGenericInventoryDocumentModelBase : PageModel
{
    private readonly IInventoryDocumentService _documents;
    private readonly ICurrentUserContext _currentUser;

    protected IndexGenericInventoryDocumentModelBase(IInventoryDocumentService documents, ICurrentUserContext currentUser)
    {
        _documents = documents;
        _currentUser = currentUser;
    }

    protected abstract InventoryDocumentType Type { get; }
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

        var filter = new InventoryDocumentFilter(
            DateFrom: Filter.DateFrom,
            DateTo: Filter.DateTo,
            DocNum: Filter.DocNum);

        var result = await _documents.ListAsync(Type, filter, ct: ct);

        var canCreate = await _documents.CanCreateAsync(Type, ct) && await _currentUser.HasActionAsync(MenuCode, PortalActions.Create, ct);

        Listado = new DocumentListViewModel
        {
            Title = DocumentNamePlural,
            Columns =
            [
                new DocumentListColumn("N° documento"),
                new DocumentListColumn("Fecha"),
                new DocumentListColumn("Estado"),
            ],
            Rows = result.Items.Select(item => new DocumentListRow(
                Cells:
                [
                    item.DocNum.ToString(),
                    item.DocDate.ToString("yyyy-MM-dd"),
                    item.Status,
                ],
                DetailUrl: $"{RouteBase}/{item.DocEntry}")).ToList(),
            Filters =
            [
                new DocumentListFilter(nameof(FilterInput.DateFrom), "Desde", FilterFieldType.Date, Filter.DateFrom?.ToString("yyyy-MM-dd")),
                new DocumentListFilter(nameof(FilterInput.DateTo), "Hasta", FilterFieldType.Date, Filter.DateTo?.ToString("yyyy-MM-dd")),
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
        public int? DocNum { get; set; }
    }
}
