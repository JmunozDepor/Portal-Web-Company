using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Modulo.FacturasProveedor.Dominio;
using Modulo.FacturasProveedor.Models;
using Modulo.FacturasProveedor.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.FacturasProveedor.Pages.FacturasProveedor.Bandeja;

/// <summary>Bandeja de facturas recibidas: KPI, filtros y lista con el plazo de cada una (estructura de Despachos › Manifiestos).</summary>
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

    public IReadOnlyList<ReviewRow> Rows { get; private set; } = Array.Empty<ReviewRow>();
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
        Today = DateOnly.FromDateTime(ChileClock.Now);
        IsSimulation = (await _settings.GetAsync(ct)).General.Mode != ModuleModes.Active;
        Status = await _inbox.GetStatusAsync(ct);
        Kpis = await _queries.GetKpisAsync(ct);
        Rows = await _queries.ListAsync(new ReviewListFilter(ParseDate(From), ParseDate(To), Estado, Texto), ct);
        return Page();
    }

    public async Task<IActionResult> OnPostLeerAhoraAsync(CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        try
        {
            await _inbox.RequestSyncAsync(ct);
            var cycle = await _reviews.EvaluatePendingAsync(ct);
            SuccessMessage = cycle.Created == 0 ? "Lectura completa: no llegaron facturas nuevas." : $"Lectura completa: {cycle.Created} factura(s) nueva(s).";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        return RedirectToPage(new { From, To, Estado, Texto });
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
