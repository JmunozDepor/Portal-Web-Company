using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
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
///
/// REGLA DURA (ver CLAUDE.md): todo nace del documento padre. OnGetAsync NO es
/// virtual a propósito -- ningún subtipo puede modificar cómo se lista un documento,
/// solo puede identificarse. Cualquier tipo de documento de venta nuevo se agrega acá
/// (SalesDocumentTypeCatalog + este subtipo), nunca reimplementando esta clase.
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

    /// <summary>
    /// 1-based -- ver DocumentList/Default.cshtml, se postea vía "pageNumber" en la
    /// querystring de paginación/filtro. NO llamar a esta query key "page" -- Razor
    /// Pages usa internamente una route-value reservada llamada "page" (registra qué
    /// .cshtml resolvió la ruta, visible en RouteData.Values["page"]) que le gana al
    /// query string en el CompositeValueProvider -- con Name="page" el binding
    /// siempre queda en 1 en silencio, sin excepción, aunque el query string tenga
    /// "page=2" (bug real confirmado con una app Razor Pages mínima aislada, sin
    /// SAP/DB de por medio -- el mismo <see cref="PageModel"/> ya expone RouteData con
    /// esa clave, cualquier [BindProperty(Name="page")] queda tapado por eso).
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

        // Por defecto, de ayer a hoy -- portado tal cual de IndexGenericoVentaModelBase
        // (referencia-original/PortalSAP_v2): sin esto, el primer ingreso trae todo el
        // histórico de una sola vez. Rige para los 3 motores genéricos (Venta/Compra/
        // Inventario, regla de paridad), no solo Venta.
        Filter.DateFrom ??= DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        Filter.DateTo ??= DateOnly.FromDateTime(DateTime.Today);

        var filter = new SalesDocumentFilter(
            DateFrom: Filter.DateFrom,
            DateTo: Filter.DateTo,
            CustomerCardCode: Filter.CustomerCardCode,
            CustomerName: Filter.CustomerName,
            CustomerReferenceNumber: Filter.CustomerReferenceNumber,
            SalesEmployeeName: Filter.SalesEmployeeName,
            DocNum: Filter.DocNum);

        // Recién se consulta al SAP de la organización cuando el usuario apretó
        // "Filtrar" (o paginó/cambió el tamaño de página) -- una carga inicial SIN
        // querystring no dispara ninguna consulta remota, solo muestra el formulario
        // con los filtros por defecto. Bajo conexión de mala calidad hacia el SAP del
        // cliente, esto evita pagar el costo de la consulta con solo abrir el módulo
        // -- rige para los 3 motores genéricos (Venta/Compra/Inventario, regla de
        // paridad), no solo Venta.
        var hasSearched = Request.Query.Count > 0;
        var result = hasSearched
            ? await _documents.ListAsync(Type, filter, page: PageNumber, pageSize: PageSize, ct: ct)
            : new SalesDocumentListResult([], 0);

        var canCreate = await _documents.CanCreateAsync(Type, ct) && await _currentUser.HasActionAsync(MenuCode, PortalActions.Create, ct);

        // Se propaga como returnUrl del detalle -- sin esto, "Volver" desde un documento
        // siempre vuelve a la página 1 del listado, perdiendo la página/filtros actuales
        // (bug real reportado: "Volver" desde la página 3 volvía a la página 1).
        // Request.PathBase + Request.Path (no Request.Path solo) -- hosteado como
        // subaplicación de IIS (ej. /portalsaas-comercialdepor), Request.Path YA viene
        // sin ese prefijo; omitirlo generaba un returnUrl que caía fuera de la app
        // (404 real, primer piloto en IIS, 2026-08-02). Mismo criterio en DetailUrl/
        // CreateUrl más abajo y en BackUrl de DetailGenericSalesDocumentModelBase.
        var returnUrl = Uri.EscapeDataString(Request.PathBase + Request.Path + Request.QueryString);

        // Columnas/filtros -- paridad exacta con IndexGenericoVentaModelBase (referencia-
        // original/PortalSAP_v2): DocEntry primero, Cliente (código) y Nombre cliente
        // como columnas SEPARADAS (antes venían combinadas en una sola celda), Sucursal
        // entrega (Address2 de cabecera) agregada -- no existía acá.
        Listado = new DocumentListViewModel
        {
            Title = DocumentNamePlural,
            Tabs = await BuildTabsAsync(draftsActive: false, ct),
            Columns =
            [
                new DocumentListColumn("Id"),
                new DocumentListColumn("N° documento"),
                new DocumentListColumn("Cliente"),
                new DocumentListColumn("Nombre cliente"),
                new DocumentListColumn("Sucursal entrega"),
                new DocumentListColumn("Fecha"),
                new DocumentListColumn("N° ref. cliente"),
                new DocumentListColumn("Total", AlignRight: true),
                new DocumentListColumn("Estado"),
                new DocumentListColumn("Vendedor"),
            ],
            Rows = result.Items.Select(item => new DocumentListRow(
                Cells:
                [
                    item.DocEntry.ToString(),
                    item.DocNum.ToString(),
                    item.CustomerCardCode,
                    item.CustomerName ?? "-",
                    item.DeliveryAddress ?? "-",
                    item.DocDate.ToString("yyyy-MM-dd"),
                    item.CustomerReferenceNumber ?? "-",
                    item.DocTotal.ToString("N2"),
                    item.Status,
                    item.SalesEmployeeName ?? "-",
                ],
                DetailUrl: $"{Request.PathBase}{RouteBase}/{item.DocEntry}?returnUrl={returnUrl}")).ToList(),
            Filters =
            [
                new DocumentListFilter(nameof(FilterInput.CustomerCardCode), "N.° Cliente", FilterFieldType.Text, Filter.CustomerCardCode),
                new DocumentListFilter(nameof(FilterInput.CustomerName), "Cliente", FilterFieldType.Text, Filter.CustomerName),
                new DocumentListFilter(nameof(FilterInput.CustomerReferenceNumber), "N.° ref. cliente", FilterFieldType.Text, Filter.CustomerReferenceNumber),
                new DocumentListFilter(nameof(FilterInput.DocNum), "N.° documento", FilterFieldType.Text, Filter.DocNum),
                new DocumentListFilter(nameof(FilterInput.DateFrom), "Fecha desde", FilterFieldType.Date, Filter.DateFrom?.ToString("yyyy-MM-dd")),
                new DocumentListFilter(nameof(FilterInput.DateTo), "Fecha hasta", FilterFieldType.Date, Filter.DateTo?.ToString("yyyy-MM-dd")),
                new DocumentListFilter(nameof(FilterInput.SalesEmployeeName), "Vendedor", FilterFieldType.Text, Filter.SalesEmployeeName),
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
        var mine = await Drafts.ListAsync(new DocumentDraftQuery(DocumentDraftEngine.Sales, Type.ToString(), OnlyMine: true, PageSize: 25), ct);
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
        var result = await Drafts.ListAsync(new DocumentDraftQuery(DocumentDraftEngine.Sales, Type.ToString(), onlyMine, statusGroup,
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

    public sealed class FilterInput
    {
        public string? CustomerCardCode { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerReferenceNumber { get; set; }
        public string? DocNum { get; set; }
        public DateOnly? DateFrom { get; set; }
        public DateOnly? DateTo { get; set; }
        public string? SalesEmployeeName { get; set; }
    }
}
