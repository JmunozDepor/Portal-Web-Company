using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Modulo.FacturasProveedor.Dominio;
using Modulo.FacturasProveedor.Models;
using Modulo.FacturasProveedor.Servicios;
using PortalSaas.Abstractions.Componentes;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.FacturasProveedor.Pages.FacturasProveedor.Bandeja;

/// <summary>
/// Bandeja de facturas recibidas: KPI, filtros y lista paginada con el plazo de cada una (estructura de Despachos ›
/// Manifiestos; paginación como los listados genéricos). Vista previa del documento en un drawer y exportación a Excel.
/// </summary>
public sealed class IndexModel : FacturasProveedorPageModelBase
{
    private readonly IReviewQueryService _queries;
    private readonly IDteInboxService _inbox;
    private readonly IInvoiceReviewService _reviews;
    private readonly IModuleSettingsService _settings;

    public IndexModel(IReviewQueryService queries, IDteInboxService inbox, IInvoiceReviewService reviews, IModuleSettingsService settings,
        ICurrentUserContext currentUser) : base(currentUser)
    {
        _queries = queries;
        _inbox = inbox;
        _reviews = reviews;
        _settings = settings;
    }

    protected override string MenuCode => "bandeja";

    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }
    [BindProperty(SupportsGet = true)] public string Estado { get; set; } = "pending";
    [BindProperty(SupportsGet = true)] public string? Texto { get; set; }

    /// <summary>"pageNumber" y no "page": "page" es un valor de ruta reservado de Razor Pages (ver DocumentList).</summary>
    [BindProperty(SupportsGet = true, Name = "pageNumber")] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true, Name = "pageSize")] public int PageSize { get; set; } = DefaultPageSize;

    private const int DefaultPageSize = 25;

    public IReadOnlyList<ReviewRow> Rows { get; private set; } = Array.Empty<ReviewRow>();
    public int TotalRecords { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)PageSize));
    /// <summary>Plazo legal configurado (días): el tooltip del estado cuenta lo que queda desde la hora de registro en el SII.</summary>
    public int LegalDeadlineDays { get; private set; }
    public DateTimeOffset Now { get; private set; }
    public ReviewKpis Kpis { get; private set; } = new(0, 0, 0, 0, 0);
    public DteInboxStatus? Status { get; private set; }
    public bool IsSimulation { get; private set; }
    public DateOnly Today { get; private set; }

    public static readonly IReadOnlyList<(string Value, string Label)> Groups = new[]
    {
        ("pending", "Pendientes (aviso, por decidir, por enviar)"), ("all", "Todas"), ("accepted", "Aceptadas"),
        ("rejected", "Rechazadas"), ("simulation", "Simulación (comparar con lo respondido)"),
    };

    public async Task<IActionResult> OnGetAsync(CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        Now = DateTimeOffset.UtcNow;
        Today = DateOnly.FromDateTime(ChileClock.Now);
        var general = (await _settings.GetAsync(ct)).General;
        IsSimulation = general.Mode != ModuleModes.Active;
        LegalDeadlineDays = general.LegalDeadlineDays;
        Status = await _inbox.GetStatusAsync(ct);
        Kpis = await _queries.GetKpisAsync(ct);
        if (!DocumentListViewModel.AvailablePageSizes.Contains(PageSize)) PageSize = DefaultPageSize;
        PageNumber = Math.Max(1, PageNumber);
        var page = await _queries.ListPageAsync(Filter(), PageNumber, PageSize, ct);
        TotalRecords = page.Total;
        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
            page = await _queries.ListPageAsync(Filter(), PageNumber, PageSize, ct);
        }
        Rows = page.Rows;
        return Page();
    }

    /// <summary>Contenido del drawer de vista previa (fetch desde la lista).</summary>
    public async Task<IActionResult> OnGetVistaPreviaAsync(long id, CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        var detail = await _queries.GetDetailAsync(id, ct);
        if (detail is null) return NotFound();
        return new PartialViewResult { ViewName = "_VistaPrevia", ViewData = new ViewDataDictionary<ReviewDetail>(ViewData, detail) };
    }

    /// <summary>Todo lo filtrado (no solo la página) en Excel.</summary>
    public async Task<IActionResult> OnGetExcelAsync(CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        var rows = await _queries.ListAsync(Filter(), ct);
        return File(BandejaExcel.Build(rows), BandejaExcel.ContentType,
            $"facturas-recibidas-{ChileClock.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.xlsx");
    }

    private ReviewListFilter Filter() => new(ParseDate(From), ParseDate(To), Estado, Texto);

    public async Task<IActionResult> OnPostLeerAhoraAsync(CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        try
        {
            await _inbox.RequestSyncAsync(ct);
            var cycle = await _reviews.EvaluatePendingAsync(ct);
            SuccessMessage = cycle.Created == 0 ? "Lectura completa: no llegaron facturas nuevas." : $"Lectura completa: {cycle.Created} factura(s) nueva(s).";
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            ErrorMessage = GetErrorMessage(ex);
        }
        return RedirectToPage(new { From, To, Estado, Texto });
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
