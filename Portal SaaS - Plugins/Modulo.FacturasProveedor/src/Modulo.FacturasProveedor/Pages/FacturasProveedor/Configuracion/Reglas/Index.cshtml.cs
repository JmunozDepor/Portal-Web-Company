using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Modulo.FacturasProveedor.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.FacturasProveedor.Pages.FacturasProveedor.Configuracion.Reglas;

/// <summary>Formulario de una regla (drawer). Los parámetros llegan como texto y se leen con cultura invariante.</summary>
public sealed class RuleForm
{
    public string RuleCode { get; set; } = "";
    public bool IsActive { get; set; }
    public string Action { get; set; } = "";
    public int? ActionDays { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
}

/// <summary>Parámetros generales y catálogo de reglas de revisión, cada regla editable en un panel lateral.</summary>
public sealed class IndexModel : FacturasProveedorPageModelBase
{
    private readonly IModuleSettingsService _settings;
    private readonly IDteInboxService _inbox;
    private readonly IApproverGroupService _groups;

    public IndexModel(IModuleSettingsService settings, IDteInboxService inbox, IApproverGroupService groups, ICurrentUserContext currentUser)
        : base(currentUser)
    {
        _settings = settings;
        _inbox = inbox;
        _groups = groups;
    }

    protected override string MenuCode => "reglas";

    public ModuleSettingsDto? Settings { get; private set; }
    public DteInboxStatus? Status { get; private set; }
    public string? FinanceGroupName { get; private set; }
    public string? CoverageWarning { get; private set; }
    public RuleSettingsDto? Selected { get; private set; }
    public bool CanEdit { get; private set; }

    [BindProperty] public GeneralSettingsInput General { get; set; } = new();
    [BindProperty] public RuleForm Rule { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string? regla, CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        CanEdit = await HasActionAsync(PortalActions.Edit);
        Settings = await _settings.GetAsync(ct);
        Status = await _inbox.GetStatusAsync(ct);
        var g = Settings.General;
        General = new GeneralSettingsInput
        {
            LegalDeadlineDays = g.LegalDeadlineDays, EscalateDaysBefore = g.EscalateDaysBefore, CutoffTime = g.CutoffTime,
            FinalActionWhenNoObjection = g.FinalActionWhenNoObjection, ProcessFromDate = g.ProcessFromDate, Mode = g.Mode,
            DailyEmailDigest = g.DailyEmailDigest,
        };
        FinanceGroupName = g.FinanceGroupId is { } fg ? (await _groups.ListAsync(ct)).FirstOrDefault(x => x.Id == fg)?.Name : null;
        Selected = regla is null ? null : Settings.Rules.FirstOrDefault(r => r.Code == regla);

        var age = Settings.Rules.FirstOrDefault(r => r.Code == "oc-antiguedad");
        if (age is { IsActive: true } && age.Parameters.TryGetValue("max_days", out var maxDays)
            && (int)maxDays + g.LegalDeadlineDays > Status.InitialHistoryDays)
        {
            CoverageWarning = $"La antigüedad máxima de OC ({maxDays:0} días) más el plazo legal supera los {Status.InitialHistoryDays} días " +
                "cargados del operador: las facturas anteriores no se cuentan en el saldo de la OC.";
        }
        return Page();
    }

    public async Task<IActionResult> OnPostGeneralAsync(CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            await _settings.SaveGeneralAsync(General, ct);
            SuccessMessage = "Parámetros guardados.";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReglaAsync(CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.Edit)) return Forbid();
        try
        {
            var parameters = new Dictionary<string, decimal>();
            foreach (var (key, value) in Rule.Parameters)
            {
                if (!decimal.TryParse(value?.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                    throw new InvalidOperationException($"El valor de «{key}» no es un número.");
                parameters[key] = number;
            }
            await _settings.SaveRuleAsync(new RuleSettingsInput(Rule.RuleCode, Rule.IsActive, Rule.Action, Rule.ActionDays, parameters), ct);
            SuccessMessage = "Regla guardada.";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
            return RedirectToPage(new { regla = Rule.RuleCode });
        }
        return RedirectToPage();
    }
}
