using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.GuiasManuales;

/// <summary>
/// Lista de TODAS las guías del módulo (del manifiesto y manuales) con filtros. Menú "guias-manuales" (permisos:
/// Despachos.guias-manuales): VIEW ver, CREATE nueva guía manual.
/// </summary>
public sealed class IndexModel : DespachosPageModelBase
{
    private readonly IManualGuideService _guides;

    public IndexModel(IManualGuideService guides, ICurrentUserContext currentUser) : base(currentUser)
    {
        _guides = guides;
    }

    protected override string MenuCode => "guias-manuales";

    public bool CanCreate { get; private set; }
    public IReadOnlyList<GuideListItem> Items { get; private set; } = Array.Empty<GuideListItem>();

    [BindProperty(SupportsGet = true)] public string? Source { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    /// <summary>Rango de fechas (yyyy-MM-dd); vacío = sin límite.</summary>
    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Receiver { get; set; }
    [BindProperty(SupportsGet = true)] public string? Folio { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        CanCreate = await HasActionAsync(PortalActions.Create);
        try
        {
            Items = await _guides.ListAsync(new GuideFilter(
                string.IsNullOrWhiteSpace(Source) ? null : Source, string.IsNullOrWhiteSpace(Status) ? null : Status,
                Date(From), Date(To), string.IsNullOrWhiteSpace(Receiver) ? null : Receiver.Trim(),
                long.TryParse(Folio?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var folio) ? folio : null));
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }
        return Page();
    }

    private static DateOnly? Date(string? s) =>
        DateOnly.TryParseExact(s?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
