using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using PortalSaas.Abstractions.Componentes;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Compras.Pages;

/// <summary>
/// Listado de un documento de compra genérico -- gate vía
/// ICurrentUserContext.HasActionAsync, mismo mecanismo de autorización ya establecido.
/// Mismo patrón que IndexGenericSalesDocumentModelBase (Modulo.Ventas) -- copia
/// deliberada, no una base compartida entre plugins.
///
/// REGLA DURA (ver CLAUDE.md): todo nace del documento padre. OnGetAsync NO es
/// virtual a propósito -- ningún subtipo puede modificar cómo se lista un documento,
/// solo puede identificarse. Cualquier tipo de documento de compra nuevo se agrega
/// acá (PurchaseDocumentTypeCatalog + este subtipo), nunca reimplementando esta clase.
/// </summary>
[Authorize]
public abstract class IndexGenericPurchaseDocumentModelBase : PageModel
{
    private readonly IPurchaseDocumentService _documents;
    private readonly ICurrentUserContext _currentUser;

    protected IndexGenericPurchaseDocumentModelBase(IPurchaseDocumentService documents, ICurrentUserContext currentUser)
    {
        _documents = documents;
        _currentUser = currentUser;
    }

    protected abstract PurchaseDocumentType Type { get; }
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

        if (string.Equals(View, DraftsView, StringComparison.OrdinalIgnoreCase))
        {
            return await OnGetDraftsAsync(ct);
        }

        // Por defecto, de ayer a hoy -- mismo criterio que IndexGenericSalesDocumentModelBase
        // (regla de paridad entre los 3 motores genéricos): sin esto, el primer ingreso
        // trae todo el histórico de una sola vez.
        Filter.DateFrom ??= DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        Filter.DateTo ??= DateOnly.FromDateTime(DateTime.Today);

        var filter = new PurchaseDocumentFilter(
            DateFrom: Filter.DateFrom,
            DateTo: Filter.DateTo,
            SupplierCardCode: Filter.SupplierCardCode,
            SupplierName: Filter.SupplierName,
            SupplierReferenceNumber: Filter.SupplierReferenceNumber,
            DocNum: Filter.DocNum,
            Folio: Filter.Folio);

        // Facturas: columna y filtro Folio (documento tributario del proveedor) -- lo
        // decide el catálogo, no el subtipo (ver PurchaseDocumentTypeInfo).
        var hasTaxDocument = _documents.Describe(Type).HasTaxDocument;

        // Recién se consulta al SAP de la organización cuando el usuario apretó
        // "Filtrar" (o paginó/cambió el tamaño de página) -- ver el comentario completo
        // en IndexGenericSalesDocumentModelBase (Modulo.Ventas), regla de paridad entre
        // los 3 motores genéricos.
        var hasSearched = Request.Query.Count > 0;
        var result = hasSearched
            ? await _documents.ListAsync(Type, filter, page: PageNumber, pageSize: PageSize, ct: ct)
            : new PurchaseDocumentListResult([], 0);

        var canCreate = await _documents.CanCreateAsync(Type, ct) && await _currentUser.HasActionAsync(MenuCode, PortalActions.Create, ct);

        // Se propaga como returnUrl del detalle -- sin esto, "Volver" desde un documento
        // siempre vuelve a la página 1 del listado, perdiendo la página/filtros actuales
        // (bug real reportado: "Volver" desde la página 3 volvía a la página 1).
        // Request.PathBase + Request.Path -- ver el mismo fix (y su porqué) en
        // IndexGenericSalesDocumentModelBase.cs (regla de paridad entre motores
        // genéricos, bug real de subaplicación IIS, 2026-08-02).
        var returnUrl = Uri.EscapeDataString(Request.PathBase + Request.Path + Request.QueryString);

        // Columnas/filtros -- paridad exacta con IndexGenericoCompraModelBase (referencia-
        // original/PortalSAP_v2): Id (DocEntry) primero, Proveedor (código) y Nombre
        // proveedor como columnas SEPARADAS (antes venían combinadas en una sola celda),
        // mismo orden de filtros que el original. Regla de paridad entre motores (ver
        // CLAUDE.md) -- mismo cambio ya aplicado a Modulo.Ventas.
        Listado = new DocumentListViewModel
        {
            Title = DocumentNamePlural,
            Tabs = await BuildTabsAsync(draftsActive: false, ct),
            Columns =
            [
                new DocumentListColumn("Id"),
                new DocumentListColumn("N° documento"),
                new DocumentListColumn("Proveedor"),
                new DocumentListColumn("Nombre proveedor"),
                new DocumentListColumn("Fecha"),
                new DocumentListColumn("N° ref. proveedor"),
                .. (hasTaxDocument ? new[] { new DocumentListColumn("Folio") } : []),
                new DocumentListColumn("Total", AlignRight: true),
                new DocumentListColumn("Estado"),
            ],
            Rows = result.Items.Select(item => new DocumentListRow(
                Cells:
                [
                    item.DocEntry.ToString(),
                    item.DocNum.ToString(),
                    item.SupplierCardCode,
                    item.SupplierName ?? "-",
                    item.DocDate.ToString("yyyy-MM-dd"),
                    item.SupplierReferenceNumber ?? "-",
                    .. (hasTaxDocument ? new[] { FolioLabel(item) } : []),
                    item.DocTotal.ToString("N2"),
                    item.Status,
                ],
                DetailUrl: $"{Request.PathBase}{RouteBase}/{item.DocEntry}?returnUrl={returnUrl}")).ToList(),
            Filters =
            [
                new DocumentListFilter(nameof(FilterInput.SupplierCardCode), "N.° Proveedor", FilterFieldType.Text, Filter.SupplierCardCode),
                new DocumentListFilter(nameof(FilterInput.SupplierName), "Proveedor", FilterFieldType.Text, Filter.SupplierName),
                new DocumentListFilter(nameof(FilterInput.SupplierReferenceNumber), "N.° ref. proveedor", FilterFieldType.Text, Filter.SupplierReferenceNumber),
                .. (hasTaxDocument ? new[] { new DocumentListFilter(nameof(FilterInput.Folio), "Folio", FilterFieldType.Text, Filter.Folio) } : []),
                new DocumentListFilter(nameof(FilterInput.DocNum), "N.° documento", FilterFieldType.Text, Filter.DocNum),
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

    // ---- Pestaña Borradores (2026-10-20) -- ver IDocumentDraftService. Todos los
    // usuarios de la compañía ven todos los borradores; por defecto se muestran los
    // propios ("creador=mios") y pendientes ("estado=pendientes").
    private const string DraftsView = "borradores";

    [BindProperty(SupportsGet = true, Name = "view")]
    public string? View { get; set; }

    [BindProperty(SupportsGet = true, Name = "creador")]
    public string? DraftCreator { get; set; }

    [BindProperty(SupportsGet = true, Name = "estado")]
    public string? DraftStatusGroup { get; set; }

    private IDocumentDraftService Drafts => HttpContext.RequestServices.GetRequiredService<IDocumentDraftService>();

    private async Task<IReadOnlyList<DocumentListTab>> BuildTabsAsync(bool draftsActive, CancellationToken ct)
    {
        var mine = await Drafts.ListAsync(new DocumentDraftQuery(DocumentDraftEngine.Purchase, Type.ToString(), OnlyMine: true, PageSize: 25), ct);
        return
        [
            new DocumentListTab("Documentos SAP", $"{Request.PathBase}{RouteBase}", !draftsActive),
            new DocumentListTab("Borradores", $"{Request.PathBase}{RouteBase}?view={DraftsView}", draftsActive, mine.TotalRecords),
        ];
    }

    private async Task<IActionResult> OnGetDraftsAsync(CancellationToken ct)
    {
        var onlyMine = !string.Equals(DraftCreator, "todos", StringComparison.OrdinalIgnoreCase);
        var statusGroup = DraftStatusGroup is "enviados" or "todos" ? DraftStatusGroup : "pendientes";
        var result = await Drafts.ListAsync(new DocumentDraftQuery(DocumentDraftEngine.Purchase, Type.ToString(), onlyMine, statusGroup,
            PageNumber, PageSize), ct);

        var canCreate = await _documents.CanCreateAsync(Type, ct) && await _currentUser.HasActionAsync(MenuCode, PortalActions.Create, ct);

        Listado = new DocumentListViewModel
        {
            Title = $"{DocumentNamePlural} -- borradores",
            Tabs = await BuildTabsAsync(draftsActive: true, ct),
            Columns =
            [
                new DocumentListColumn("Modificado"),
                new DocumentListColumn("Creado por"),
                new DocumentListColumn("Socio"),
                new DocumentListColumn("Referencia"),
                new DocumentListColumn("Fecha doc."),
                new DocumentListColumn("Líneas", AlignRight: true),
                new DocumentListColumn("Total", AlignRight: true),
                new DocumentListColumn("Motivo"),
                new DocumentListColumn("Estado"),
            ],
            Rows = result.Items.Select(d => new DocumentListRow(
                Cells:
                [
                    d.UpdatedAt.ToLocalTime().ToString("dd-MM-yyyy HH:mm"),
                    d.CreatedByUsername,
                    d.PartnerCode ?? "-",
                    d.Reference ?? "-",
                    d.DocDate?.ToString("yyyy-MM-dd") ?? "-",
                    d.LineCount.ToString(),
                    d.Total?.ToString("N0") ?? "-",
                    d.Reason,
                    d.Status == DocumentDraftStatus.Sent && d.SapDocEntry is not null
                        ? $"{DocumentDraftStatus.Label(d.Status)} (DocEntry {d.SapDocEntry})"
                        : DocumentDraftStatus.Label(d.Status),
                ],
                DetailUrl: d.Status == DocumentDraftStatus.Sent && d.SapDocEntry is not null
                    ? $"{Request.PathBase}{RouteBase}/{d.SapDocEntry}"
                    : $"{Request.PathBase}{RouteBase}/nuevo?draft={d.Id}")).ToList(),
            Filters =
            [
                new DocumentListFilter("creador", "Creados por", FilterFieldType.Select, onlyMine ? "mios" : "todos",
                    [new DocumentListFilterOption("mios", "Mis borradores"), new DocumentListFilterOption("todos", "Todos los usuarios")],
                    AllowEmpty: false),
                new DocumentListFilter("estado", "Estado", FilterFieldType.Select, statusGroup,
                    [new DocumentListFilterOption("pendientes", "Pendientes"), new DocumentListFilterOption("enviados", "Enviados"),
                        new DocumentListFilterOption("todos", "Todos")],
                    AllowEmpty: false),
            ],
            HiddenFilterFields = new Dictionary<string, string> { ["view"] = DraftsView },
            ShowFreeTextSearch = false,
            CreateUrl = canCreate ? $"{Request.PathBase}{RouteBase}/nuevo" : null,
            CreateDisabledTitle = canCreate ? null : $"No tenés permiso para crear {DocumentNamePlural.ToLowerInvariant()}",
            ShowPaging = true,
            CurrentPage = PageNumber,
            PageSize = PageSize,
            TotalRecords = result.TotalRecords,
            HasSearched = true,
        };

        return Page();
    }

    /// <summary>"43-1157501" (prefijo-número) o "-" sin folio.</summary>
    private static string FolioLabel(PurchaseDocumentSummaryDto item) =>
        item.FolioNumber is { } number && number > 0
            ? (string.IsNullOrWhiteSpace(item.FolioPrefix) ? number.ToString() : $"{item.FolioPrefix}-{number}")
            : "-";

    public sealed class FilterInput
    {
        /// <summary>Número de folio del proveedor -- solo facturas.</summary>
        public string? Folio { get; set; }

        public string? SupplierCardCode { get; set; }
        public string? SupplierName { get; set; }
        public string? SupplierReferenceNumber { get; set; }
        public string? DocNum { get; set; }
        public DateOnly? DateFrom { get; set; }
        public DateOnly? DateTo { get; set; }
    }
}
