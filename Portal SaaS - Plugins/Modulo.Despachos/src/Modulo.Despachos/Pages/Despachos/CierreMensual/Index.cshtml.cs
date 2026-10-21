using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.CierreMensual;

/// <summary>
/// Cierre mensual. Menú "cierre-mensual": VIEW ve el estado, APPROVE cierra el período; reabrir exige IsAdmin
/// (y el servicio lo vuelve a validar).
/// </summary>
public sealed class IndexModel : DespachosPageModelBase
{
    private readonly IMonthlyClosureService _closure;
    private readonly ICurrentUserContext _user;

    public IndexModel(IMonthlyClosureService closure, ICurrentUserContext currentUser) : base(currentUser)
    {
        _closure = closure;
        _user = currentUser;
    }

    protected override string MenuCode => "cierre-mensual";

    /// <summary>Período "yyyy-MM"; vacío = mes actual (hora Chile).</summary>
    [BindProperty(SupportsGet = true)] public string? Period { get; set; }

    public int Year { get; private set; }
    public int Month { get; private set; }
    public string PeriodText => $"{Year:0000}-{Month:00}";
    public PeriodStatus? Status { get; private set; }
    public bool CanClose { get; private set; }
    public bool CanReopen { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCerrarAsync()
    {
        if (!await HasActionAsync(PortalActions.Approve)) return Forbid();
        ResolvePeriod();
        try
        {
            var r = await _closure.CloseAsync(Year, Month);
            if (r.Closed) SuccessMessage = $"Período {PeriodText} cerrado.";
            else ErrorMessage = "No se puede cerrar el período: " + string.Join(" · ", r.Blockers);
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { period = PeriodText });
    }

    public async Task<IActionResult> OnPostReabrirAsync(string? reason)
    {
        if (!_user.IsAdmin) return Forbid();
        ResolvePeriod();
        try
        {
            await _closure.ReopenAsync(Year, Month, reason ?? "");
            SuccessMessage = $"Período {PeriodText} reabierto.";
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
        return RedirectToPage(new { period = PeriodText });
    }

    private async Task LoadAsync()
    {
        ResolvePeriod();
        CanClose = await HasActionAsync(PortalActions.Approve);
        CanReopen = _user.IsAdmin;
        try { Status = await _closure.GetStatusAsync(Year, Month); }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex); }
    }

    private void ResolvePeriod()
    {
        if (DateTime.TryParseExact(Period?.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            Year = d.Year;
            Month = d.Month;
        }
        else
        {
            var now = ChileClock.Now;
            Year = now.Year;
            Month = now.Month;
        }
        Period = PeriodText;
    }

    public bool IsClosed => Status?.Status == ClosureStatuses.Closed;

    public static string StatusLabel(string status) => status switch
    {
        ClosureStatuses.Closed => "Cerrado",
        ClosureStatuses.Reopened => "Reabierto",
        _ => "Abierto",
    };

    public static string StatusBadge(string status) => status switch
    {
        ClosureStatuses.Closed => "dsp-badge--success",
        ClosureStatuses.Reopened => "dsp-badge--warning",
        _ => "dsp-badge--neutral",
    };

    public static string Local(DateTime? utc) =>
        utc is { } v ? ChileClock.ToLocal(v).ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture) : "—";
}
