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

    /// <summary>
    /// 1-based -- ver DocumentList/Default.cshtml, se postea vía "pageNumber" en la
    /// querystring. NO "page" -- colisiona con una route-value reservada de Razor
    /// Pages (ver el comentario completo en IndexGenericSalesDocumentModelBase,
    /// Modulo.Ventas, mismo bug real confirmado ahí).
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "pageNumber")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true, Name = "pageSize")]
    public int PageSize { get; set; } = 25;

    public DocumentListViewModel Listado { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await _currentUser.HasActionAsync(MenuCode, PortalActions.View, ct))
        {
            return Forbid();
        }

        // Por defecto, de ayer a hoy -- mismo criterio que IndexGenericSalesDocumentModelBase
        // (regla de paridad entre los 3 motores genéricos): sin esto, el primer ingreso
        // trae todo el histórico de una sola vez.
        Filter.DateFrom ??= DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        Filter.DateTo ??= DateOnly.FromDateTime(DateTime.Today);

        var filter = new InventoryDocumentFilter(
            DateFrom: Filter.DateFrom,
            DateTo: Filter.DateTo,
            DocNum: Filter.DocNum,
            BusinessPartnerCardCode: Filter.BusinessPartnerCardCode,
            BusinessPartnerName: Filter.BusinessPartnerName,
            WarehouseDestinationCode: Filter.WarehouseDestinationCode);

        // Recién se consulta al SAP de la organización cuando el usuario apretó
        // "Filtrar" (o paginó/cambió el tamaño de página) -- ver el comentario completo
        // en IndexGenericSalesDocumentModelBase (Modulo.Ventas), regla de paridad entre
        // los 3 motores genéricos.
        var hasSearched = Request.Query.Count > 0;
        var result = hasSearched
            ? await _documents.ListAsync(Type, filter, page: PageNumber, pageSize: PageSize, ct: ct)
            : new InventoryDocumentListResult([], 0);

        var canCreate = await _documents.CanCreateAsync(Type, ct) && await _currentUser.HasActionAsync(MenuCode, PortalActions.Create, ct);

        // Se propaga como returnUrl del detalle -- sin esto, "Volver" desde un documento
        // siempre vuelve a la página 1 del listado, perdiendo la página/filtros actuales
        // (bug real reportado: "Volver" desde la página 3 volvía a la página 1).
        // Request.PathBase + Request.Path -- ver el mismo fix (y su porqué) en
        // IndexGenericSalesDocumentModelBase.cs (regla de paridad entre motores
        // genéricos, bug real de subaplicación IIS, 2026-08-02).
        var returnUrl = Uri.EscapeDataString(Request.PathBase + Request.Path + Request.QueryString);

        // DocEntry como primera columna, etiquetada "Id" -- paridad con Ventas/Compras
        // (mismo label, mismo valor real: DocEntry) y con
        // IndexGenericoInventarioModelBase del original (mismo orden exacto: N.° Socio,
        // Socio de negocios, N.° documento, Almacén destino, Fecha desde, Fecha hasta /
        // Id, N.° documento, Socio de negocios, Nombre socio, Sucursal entrega,
        // Fecha, N.° ref., Almacén destino, Estado). Socio de negocios/almacén destino
        // de cabecera SÍ existen en SAP aunque el formulario de creación no los pida
        // (ver el doc-comment de InventoryDocumentSummaryDto) -- un documento creado
        // directo en SAP o importado puede traerlos poblados.
        Listado = new DocumentListViewModel
        {
            Title = DocumentNamePlural,
            Columns =
            [
                new DocumentListColumn("Id"),
                new DocumentListColumn("N° documento"),
                new DocumentListColumn("Socio de negocios"),
                new DocumentListColumn("Nombre socio"),
                new DocumentListColumn("Sucursal entrega"),
                new DocumentListColumn("Fecha"),
                new DocumentListColumn("N.° ref."),
                new DocumentListColumn("Almacén origen"),
                new DocumentListColumn("Almacén destino"),
                new DocumentListColumn("Estado"),
            ],
            Rows = result.Items.Select(item => new DocumentListRow(
                Cells:
                [
                    item.DocEntry.ToString(),
                    item.DocNum.ToString(),
                    item.BusinessPartnerCardCode ?? "-",
                    item.BusinessPartnerName ?? "-",
                    item.DeliveryAddress ?? "-",
                    item.DocDate.ToString("dd/MM/yyyy"),
                    item.CustomerReferenceNumber ?? "-",
                    item.WarehouseSourceCode ?? "-",
                    item.WarehouseDestinationCode ?? "-",
                    item.Status,
                ],
                DetailUrl: $"{Request.PathBase}{RouteBase}/{item.DocEntry}?returnUrl={returnUrl}")).ToList(),
            Filters =
            [
                new DocumentListFilter(nameof(FilterInput.BusinessPartnerCardCode), "N.° Socio", FilterFieldType.Text, Filter.BusinessPartnerCardCode),
                new DocumentListFilter(nameof(FilterInput.BusinessPartnerName), "Socio de negocios", FilterFieldType.Text, Filter.BusinessPartnerName),
                new DocumentListFilter(nameof(FilterInput.DocNum), "N.° documento", FilterFieldType.Text, Filter.DocNum),
                new DocumentListFilter(nameof(FilterInput.WarehouseDestinationCode), "Almacén destino", FilterFieldType.Text, Filter.WarehouseDestinationCode),
                new DocumentListFilter(nameof(FilterInput.DateFrom), "Fecha desde", FilterFieldType.Date, Filter.DateFrom?.ToString("yyyy-MM-dd")),
                new DocumentListFilter(nameof(FilterInput.DateTo), "Fecha hasta", FilterFieldType.Date, Filter.DateTo?.ToString("yyyy-MM-dd")),
            ],
            ShowFreeTextSearch = false,
            CreateUrl = canCreate ? $"{Request.PathBase}{RouteBase}/nuevo" : null,
            CreateDisabledTitle = canCreate ? null : $"No tenés permiso para crear {DocumentNamePlural.ToLowerInvariant()}",
            ShowPaging = true,
            CurrentPage = PageNumber,
            PageSize = PageSize,
            TotalRecords = result.TotalRecords,
            HasSearched = hasSearched,
        };

        return Page();
    }

    public sealed class FilterInput
    {
        public DateOnly? DateFrom { get; set; }
        public DateOnly? DateTo { get; set; }
        public string? DocNum { get; set; }
        public string? BusinessPartnerCardCode { get; set; }
        public string? BusinessPartnerName { get; set; }
        public string? WarehouseDestinationCode { get; set; }
    }
}
