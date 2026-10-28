using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulo.FacturasProveedor.Data;
using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.FacturasProveedor.Servicios;

public sealed record GeneralSettingsDto(
    int LegalDeadlineDays, int EscalateDaysBefore, TimeOnly CutoffTime, string FinalActionWhenNoObjection, DateOnly? ProcessFromDate,
    string Mode, bool DailyEmailDigest, long? FinanceGroupId, DateTimeOffset? UpdatedAt);

/// <summary>Una regla del catálogo con su definición y lo configurado para la compañía.</summary>
public sealed record RuleSettingsDto(
    string Code, string Name, string Description, RuleScope Scope, IReadOnlyList<RuleParameterDefinition> ParameterDefinitions,
    IReadOnlyList<string> AllowedActions, bool CanBeDisabled, bool IsActive, string Action, int? ActionDays,
    IReadOnlyDictionary<string, decimal> Parameters, int SortOrder);

public sealed record ModuleSettingsDto(GeneralSettingsDto General, IReadOnlyList<RuleSettingsDto> Rules);

/// <summary>Formulario de parámetros generales (el grupo de Finanzas se define en Aprobadores).</summary>
public sealed record GeneralSettingsInput
{
    public int LegalDeadlineDays { get; set; } = 8;
    public int EscalateDaysBefore { get; set; } = 2;
    public TimeOnly CutoffTime { get; set; } = new(10, 0);
    public string FinalActionWhenNoObjection { get; set; } = RuleActions.Accept;
    public DateOnly? ProcessFromDate { get; set; }
    public string Mode { get; set; } = ModuleModes.Simulation;
    public bool DailyEmailDigest { get; set; }
}

public sealed record RuleSettingsInput(string RuleCode, bool IsActive, string Action, int? ActionDays, IReadOnlyDictionary<string, decimal> Parameters);

public interface IModuleSettingsService
{
    Task<ModuleSettingsDto> GetAsync(CancellationToken ct);
    Task SaveGeneralAsync(GeneralSettingsInput input, CancellationToken ct);
    Task SaveRuleAsync(RuleSettingsInput input, CancellationToken ct);
    Task<CompanyRuleConfiguration> GetEngineConfigurationAsync(CancellationToken ct);
}

/// <summary>
/// Parámetros generales y configuración de reglas de la compañía activa. Crea los valores por defecto la primera vez
/// (y una fila por cada regla nueva del catálogo, así aparece sola en la pantalla). Cada cambio queda en
/// fp_config_events con el antes y el después: es la "regla explícita aprobada" que exige la pasarela DTE.
/// </summary>
public sealed class ModuleSettingsService : IModuleSettingsService
{
    private readonly FacturasProveedorDbContext _db;
    private readonly InvoiceRuleEngine _engine;
    private readonly ICurrentCompanyAccessor _company;
    private readonly ICurrentUserContext _user;
    private readonly TimeProvider _clock;

    public ModuleSettingsService(FacturasProveedorDbContext db, InvoiceRuleEngine engine, ICurrentCompanyAccessor company,
        ICurrentUserContext user, TimeProvider clock)
    {
        _db = db;
        _engine = engine;
        _company = company;
        _user = user;
        _clock = clock;
    }

    private Guid CompanyId => _company.CompanyId;

    public async Task<ModuleSettingsDto> GetAsync(CancellationToken ct)
    {
        var (settings, rules) = await EnsureAsync(ct);
        var byCode = rules.ToDictionary(r => r.RuleCode);
        var dtos = _engine.Catalog.Select(rule =>
        {
            var row = byCode[rule.Code];
            return new RuleSettingsDto(rule.Code, rule.Name, rule.Description, rule.Scope, rule.Parameters, rule.AllowedActions,
                rule.CanBeDisabled, row.IsActive, row.Action, row.ActionDays, ReadParameters(rule, row.ParametersJson), row.SortOrder);
        }).OrderBy(r => r.SortOrder).ToList();
        return new ModuleSettingsDto(ToDto(settings), dtos);
    }

    public async Task SaveGeneralAsync(GeneralSettingsInput input, CancellationToken ct)
    {
        var (settings, rules) = await EnsureAsync(ct);
        if (input.LegalDeadlineDays is < 1 or > 30)
            throw new InvalidOperationException("El plazo legal de respuesta debe estar entre 1 y 30 días.");
        if (input.EscalateDaysBefore < 0 || input.EscalateDaysBefore >= input.LegalDeadlineDays)
            throw new InvalidOperationException("El escalamiento a Finanzas debe ser antes del fin del plazo (entre 0 y el plazo − 1 días).");
        if (input.FinalActionWhenNoObjection is not (RuleActions.Accept or RuleActions.ManualReview))
            throw new InvalidOperationException("Si ninguna regla objeta, la factura solo puede aceptarse o ir a revisión manual.");
        if (input.Mode is not (ModuleModes.Simulation or ModuleModes.Active))
            throw new InvalidOperationException("Modo inválido: use simulación o activo.");
        if (input.Mode == ModuleModes.Active && (settings.FinanceGroupId is null || input.ProcessFromDate is null))
            throw new InvalidOperationException(
                "Para pasar a modo activo hay que definir el grupo de Finanzas (en Aprobadores) y la fecha \"procesar desde\".");
        var tooLong = rules.FirstOrDefault(r => r.Action == RuleActions.WarnThenReject && (r.ActionDays ?? 0) >= input.LegalDeadlineDays);
        if (tooLong is not null)
            throw new InvalidOperationException(
                $"La regla «{_engine.Find(tooLong.RuleCode)?.Name ?? tooLong.RuleCode}» avisa {tooLong.ActionDays} días: deben ser menos que el plazo legal.");

        var before = JsonSerializer.Serialize(ToDto(settings));
        settings.LegalDeadlineDays = input.LegalDeadlineDays;
        settings.EscalateDaysBefore = input.EscalateDaysBefore;
        settings.CutoffTime = input.CutoffTime;
        settings.FinalActionWhenNoObjection = input.FinalActionWhenNoObjection;
        settings.ProcessFromDate = input.ProcessFromDate;
        settings.Mode = input.Mode;
        settings.DailyEmailDigest = input.DailyEmailDigest;
        settings.UpdatedAt = _clock.GetUtcNow();
        settings.UpdatedBy = _user.UserId;
        Audit("settings", "general", before, JsonSerializer.Serialize(ToDto(settings)));
        await _db.SaveChangesAsync(ct);
    }

    public async Task SaveRuleAsync(RuleSettingsInput input, CancellationToken ct)
    {
        var rule = _engine.Find(input.RuleCode)
            ?? throw new InvalidOperationException($"La regla «{input.RuleCode}» no existe en el catálogo.");
        var (settings, rules) = await EnsureAsync(ct);
        if (!rule.AllowedActions.Contains(input.Action))
            throw new InvalidOperationException($"La acción elegida no está permitida para la regla «{rule.Name}».");
        if (!input.IsActive && !rule.CanBeDisabled)
            throw new InvalidOperationException($"La regla «{rule.Name}» no se puede desactivar.");
        if (input.Action == RuleActions.WarnThenReject && (input.ActionDays is null || input.ActionDays < 1 || input.ActionDays >= settings.LegalDeadlineDays))
            throw new InvalidOperationException($"Los días de aviso deben estar entre 1 y {settings.LegalDeadlineDays - 1} (menos que el plazo legal).");
        var parameters = ValidateParameters(rule, input.Parameters);

        var row = rules.Single(r => r.RuleCode == rule.Code);
        var before = Snapshot(row);
        row.IsActive = input.IsActive;
        row.Action = input.Action;
        row.ActionDays = input.Action == RuleActions.WarnThenReject ? input.ActionDays : row.ActionDays;
        row.ParametersJson = JsonSerializer.Serialize(parameters);
        row.UpdatedAt = _clock.GetUtcNow();
        row.UpdatedBy = _user.UserId;
        Audit("rule", rule.Code, before, Snapshot(row));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<CompanyRuleConfiguration> GetEngineConfigurationAsync(CancellationToken ct)
    {
        var (settings, rules) = await EnsureAsync(ct);
        var byCode = rules.ToDictionary(r => r.RuleCode);
        var byRule = new Dictionary<string, EffectiveRuleSettings>();
        var order = new Dictionary<string, int>();
        foreach (var rule in _engine.Catalog)
        {
            var row = byCode[rule.Code];
            byRule[rule.Code] = new EffectiveRuleSettings(row.IsActive || !rule.CanBeDisabled, row.Action, row.ActionDays,
                ReadParameters(rule, row.ParametersJson));
            order[rule.Code] = row.SortOrder;
        }
        return new CompanyRuleConfiguration(byRule, order, settings.FinalActionWhenNoObjection);
    }

    /// <summary>Fila de parámetros y una fila por regla del catálogo; crea lo que falte con los valores por defecto.</summary>
    private async Task<(FpSettings Settings, List<FpRuleSetting> Rules)> EnsureAsync(CancellationToken ct)
    {
        var settings = await _db.Settings.FirstOrDefaultAsync(s => s.CompanyId == CompanyId, ct);
        var rules = await _db.RuleSettings.Where(r => r.CompanyId == CompanyId).ToListAsync(ct);
        var changed = false;
        if (settings is null)
        {
            settings = new FpSettings { CompanyId = CompanyId };
            _db.Settings.Add(settings);
            changed = true;
        }
        foreach (var rule in _engine.Catalog.Where(r => rules.All(x => x.RuleCode != r.Code)))
        {
            var row = new FpRuleSetting
            {
                CompanyId = CompanyId, RuleCode = rule.Code, IsActive = rule.DefaultActive, Action = rule.DefaultAction,
                ActionDays = rule.DefaultActionDays, SortOrder = rule.DefaultOrder,
                ParametersJson = JsonSerializer.Serialize(rule.Parameters.ToDictionary(p => p.Key, p => p.DefaultValue)),
            };
            _db.RuleSettings.Add(row);
            rules.Add(row);
            changed = true;
        }
        if (changed) await _db.SaveChangesAsync(ct);
        return (settings, rules);
    }

    private static Dictionary<string, decimal> ValidateParameters(ISupplierInvoiceRule rule, IReadOnlyDictionary<string, decimal> input)
    {
        var unknown = input.Keys.FirstOrDefault(k => rule.Parameters.All(p => p.Key != k));
        if (unknown is not null) throw new InvalidOperationException($"La regla «{rule.Name}» no tiene el parámetro «{unknown}».");
        var result = new Dictionary<string, decimal>();
        foreach (var p in rule.Parameters)
        {
            var value = input.TryGetValue(p.Key, out var v) ? v : p.DefaultValue;
            if (value < p.Min || value > p.Max)
                throw new InvalidOperationException($"«{p.Label}» de la regla «{rule.Name}» debe estar entre {p.Min:0.##} y {p.Max:0.##} {p.Unit}.");
            result[p.Key] = value;
        }
        return result;
    }

    internal static IReadOnlyDictionary<string, decimal> ReadParameters(ISupplierInvoiceRule rule, string? json)
    {
        var stored = string.IsNullOrWhiteSpace(json) ? new Dictionary<string, decimal>()
            : JsonSerializer.Deserialize<Dictionary<string, decimal>>(json) ?? new Dictionary<string, decimal>();
        return rule.Parameters.ToDictionary(p => p.Key, p => stored.TryGetValue(p.Key, out var v) ? v : p.DefaultValue);
    }

    private static string Snapshot(FpRuleSetting r) =>
        JsonSerializer.Serialize(new { r.IsActive, r.Action, r.ActionDays, Parameters = r.ParametersJson });

    private void Audit(string entityType, string key, string? before, string? after) =>
        _db.ConfigEvents.Add(new FpConfigEvent
        {
            CompanyId = CompanyId, EntityType = entityType, EntityKey = key, BeforeJson = before, AfterJson = after,
            UserId = _user.UserId, OccurredAt = _clock.GetUtcNow(),
        });

    private static GeneralSettingsDto ToDto(FpSettings s) => new(
        s.LegalDeadlineDays, s.EscalateDaysBefore, s.CutoffTime, s.FinalActionWhenNoObjection, s.ProcessFromDate, s.Mode,
        s.DailyEmailDigest, s.FinanceGroupId, s.UpdatedAt);
}
