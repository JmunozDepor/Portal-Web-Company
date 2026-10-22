using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.Reportes;

/// <summary>
/// Reporte de documentos despachados y su estado de entrega, agrupado por manifiesto, con filtro de fechas.
/// Menú "reporte-documentos": VIEW ve el reporte, EXPORT descarga el Excel.
/// </summary>
public sealed class DocumentosModel : DespachosPageModelBase
{
    private readonly IDocumentReportService _report;
    private readonly IMasterDataService _data;

    public DocumentosModel(IDocumentReportService report, IMasterDataService data, ICurrentUserContext currentUser)
        : base(currentUser)
    {
        _report = report;
        _data = data;
    }

    protected override string MenuCode => "reporte-documentos";

    /// <summary>Desde / hasta (yyyy-MM-dd). Vacío = del día 1 del mes actual a hoy (hora Chile).</summary>
    [BindProperty(SupportsGet = true)] public string? Desde { get; set; }
    [BindProperty(SupportsGet = true)] public string? Hasta { get; set; }
    [BindProperty(SupportsGet = true)] public string? Estado { get; set; }
    [BindProperty(SupportsGet = true)] public long? Transportista { get; set; }
    [BindProperty(SupportsGet = true)] public string? Buscar { get; set; }

    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
    public bool CanExport { get; private set; }
    public IReadOnlyList<Carrier> Carriers { get; private set; } = Array.Empty<Carrier>();
    public DocumentReport? Report { get; private set; }

    /// <summary>Estados de documento que se pueden filtrar (un documento despachado ya no está "pendiente").</summary>
    public static readonly IReadOnlyList<string> FilterStatuses = new[]
    {
        DeliveryStatuses.Dispatched, DeliveryStatuses.Delivered, DeliveryStatuses.PartiallyDelivered,
        DeliveryStatuses.Rejected, DeliveryStatuses.NotDelivered,
    };

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        ResolveDates();
        CanExport = await HasActionAsync(PortalActions.Export);
        Carriers = await _data.ListCarriersAsync(includeInactive: true);
        try { Report = await _report.GetAsync(Filter()); }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return Page();
    }

    public async Task<IActionResult> OnGetExportarAsync()
    {
        if (!await HasActionAsync(PortalActions.Export)) return Forbid();
        ResolveDates();
        try
        {
            var bytes = await _report.ExportExcelAsync(Filter());
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"documentos-despachados-{From:yyyyMMdd}-{To:yyyyMMdd}.xlsx");
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            return RedirectToPage(new { desde = Desde, hasta = Hasta, estado = Estado, transportista = Transportista, buscar = Buscar });
        }
    }

    /// <summary>Query string actual (para el enlace de exportación).</summary>
    public string QueryString() =>
        string.Join("&", new[]
        {
            $"desde={From:yyyy-MM-dd}", $"hasta={To:yyyy-MM-dd}",
            string.IsNullOrWhiteSpace(Estado) ? null : "estado=" + Uri.EscapeDataString(Estado),
            Transportista is null ? null : "transportista=" + Transportista.Value.ToString(CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(Buscar) ? null : "buscar=" + Uri.EscapeDataString(Buscar.Trim()),
        }.Where(x => x != null));

    private DocumentReportFilter Filter() =>
        new(From, To, FilterStatuses.Contains(Estado) ? Estado : null, Transportista, Buscar);

    private void ResolveDates()
    {
        var today = DateOnly.FromDateTime(ChileClock.Now);
        From = Parse(Desde) ?? new DateOnly(today.Year, today.Month, 1);
        To = Parse(Hasta) ?? today;
    }

    private static DateOnly? Parse(string? value) =>
        DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
}
