using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.FacturasProveedor.Pages.FacturasProveedor.Configuracion.Proveedores;

/// <summary>Ajuste de una regla en el formulario: Mode vacío = como la empresa.</summary>
public sealed class OverrideForm
{
    public string RuleCode { get; set; } = "";
    public string? Mode { get; set; }
    public string? Action { get; set; }
    public int? ActionDays { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public sealed class ProfileForm
{
    public long? Id { get; set; }
    public string TaxId { get; set; } = "";
    public string Condition { get; set; } = SupplierConditions.FixedMonthly;
    public long? ApproverGroupId { get; set; }
    public long? BackupGroupId { get; set; }
    public string ValidFrom { get; set; } = "";
    public string? ValidTo { get; set; }
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;
    public List<OverrideForm> Overrides { get; set; } = new();
}

/// <summary>Proveedores que se apartan de las reglas de la empresa (estructura de Despachos › Transportistas).</summary>
public sealed class IndexModel : FacturasProveedorPageModelBase
{
    private readonly ISupplierProfileService _profiles;
    private readonly IApproverGroupService _groups;
    private readonly ISapLookupService _sap;
    private readonly IModuleSettingsService _settings;
    private readonly IDteInboxService _inbox;
    private readonly InvoiceRuleEngine _engine;

    public IndexModel(ISupplierProfileService profiles, IApproverGroupService groups, ISapLookupService sap, IModuleSettingsService settings,
        IDteInboxService inbox, InvoiceRuleEngine engine, ICurrentUserContext currentUser) : base(currentUser)
    {
        _profiles = profiles;
        _groups = groups;
        _sap = sap;
        _settings = settings;
        _inbox = inbox;
        _engine = engine;
    }

    protected override string MenuCode => "proveedores";

    public IReadOnlyList<SupplierProfileDto> Items { get; private set; } = Array.Empty<SupplierProfileDto>();
    public IReadOnlyList<ApproverGroupDto> Groups { get; private set; } = Array.Empty<ApproverGroupDto>();
    /// <summary>Reglas que se pueden ajustar por proveedor (todas salvo las temporales, más "plazo por vencer").</summary>
    public IReadOnlyList<ISupplierInvoiceRule> AdjustableRules { get; private set; } = Array.Empty<ISupplierInvoiceRule>();
    public SupplierProfileDto? Selected { get; private set; }
    public bool OpenNew { get; private set; }
    public bool CanCreate { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanDelete { get; private set; }
    public string? CoverageWarning { get; private set; }

    [BindProperty] public ProfileForm Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(long? editar, bool nuevo = false, CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        CanCreate = await HasActionAsync(PortalActions.Create);
        CanEdit = await HasActionAsync(PortalActions.Edit);
        CanDelete = await HasActionAsync(PortalActions.Delete);
        Items = await _profiles.ListAsync(ct);
        Groups = await _groups.ListAsync(ct);
        AdjustableRules = _engine.Catalog.Where(r => r.Scope != RuleScope.Pending || r.Code == "plazo-por-vencer").ToList();
        Selected = editar is { } id ? Items.FirstOrDefault(p => p.Id == id) : null;
        OpenNew = nuevo && CanCreate;

        var legal = (await _settings.GetAsync(ct)).General.LegalDeadlineDays;
        var history = (await _inbox.GetStatusAsync(ct)).InitialHistoryDays;
        var wide = Items.Where(p => p.Overrides.Any(o => o.RuleCode == "oc-antiguedad" && o.Parameters is { } prm
            && prm.TryGetValue("max_days", out var days) && (int)days + legal > history)).Select(p => p.SupplierName).ToList();
        if (wide.Count > 0)
            CoverageWarning = $"{string.Join(", ", wide)}: la antigüedad de OC ajustada más el plazo legal supera los {history} días cargados " +
                "del operador; las facturas anteriores no se cuentan en el saldo de la OC.";
        return Page();
    }

    public async Task<IActionResult> OnGetBuscarProveedorAsync(string? q, CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        try
        {
            var found = await _sap.SearchSuppliersAsync(q ?? "", ct);
            return new JsonResult(found.Select(s => new { s.CardCode, s.CardName, s.TaxId, s.IsActive }));
        }
        catch (InvalidOperationException ex)
        {
            return new JsonResult(new { error = ex.Message }) { StatusCode = 400 };
        }
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct = default)
    {
        if (!await HasActionAsync(Input.Id is null ? PortalActions.Create : PortalActions.Edit)) return Forbid();
        try
        {
            if (!DateOnly.TryParseExact(Input.ValidFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
                throw new InvalidOperationException("Indique desde cuándo rige la excepción.");
            DateOnly? to = DateOnly.TryParseExact(Input.ValidTo, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
            var overrides = Input.Overrides.Where(o => !string.IsNullOrEmpty(o.Mode)).Select(o => new RuleOverrideInput(
                o.RuleCode, o.Mode!, string.IsNullOrEmpty(o.Action) ? null : o.Action, o.ActionDays,
                o.Parameters.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => ParseNumber(kv.Key, kv.Value))))
                .ToList();
            await _profiles.SaveAsync(new SupplierProfileInput(Input.Id, Input.TaxId, Input.Condition, Input.ApproverGroupId, Input.BackupGroupId,
                from, to, Input.Note, Input.IsActive, overrides), ct);
            SuccessMessage = "Proveedor guardado.";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
            return Input.Id is { } id ? RedirectToPage(new { editar = id }) : RedirectToPage(new { nuevo = true });
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id, CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.Delete)) return Forbid();
        await _profiles.DeleteAsync(id, ct);
        SuccessMessage = "Proveedor eliminado de las excepciones.";
        return RedirectToPage();
    }

    private static decimal ParseNumber(string key, string value) =>
        decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
            ? n : throw new InvalidOperationException($"El valor de «{key}» no es un número.");
}
