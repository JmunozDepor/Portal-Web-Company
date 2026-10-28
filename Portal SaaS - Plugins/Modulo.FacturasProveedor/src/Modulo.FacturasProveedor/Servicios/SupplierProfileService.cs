using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulo.FacturasProveedor.Data;
using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.FacturasProveedor.Servicios;

public sealed record RuleOverrideInput(string RuleCode, string Mode, string? Action, int? ActionDays, IReadOnlyDictionary<string, decimal>? Parameters);

public sealed record SupplierProfileInput(
    long? Id, string TaxId, string Condition, long? ApproverGroupId, long? BackupGroupId, DateOnly ValidFrom, DateOnly? ValidTo,
    string? Note, bool IsActive, IReadOnlyList<RuleOverrideInput> Overrides);

public sealed record SupplierProfileDto(
    long Id, string CardCode, string TaxId, string SupplierName, string Condition, long? ApproverGroupId, long? BackupGroupId,
    DateOnly ValidFrom, DateOnly? ValidTo, string? Note, bool IsActive, IReadOnlyList<RuleOverrideInput> Overrides);

public interface ISupplierProfileService
{
    Task<IReadOnlyList<SupplierProfileDto>> ListAsync(CancellationToken ct);
    Task<SupplierProfileDto?> GetAsync(long id, CancellationToken ct);
    Task<long> SaveAsync(SupplierProfileInput input, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
    /// <summary>Excepción vigente del proveedor en la fecha (activa y dentro de su vigencia); null si no tiene.</summary>
    Task<SupplierProfileFacts?> GetActiveFactsAsync(string taxId, DateOnly onDate, CancellationToken ct);
    /// <summary>Grupo aprobador y suplente de la excepción vigente del proveedor.</summary>
    Task<(long? Approver, long? Backup)> GetApproverGroupsAsync(string taxId, DateOnly onDate, CancellationToken ct);
}

/// <summary>
/// Proveedores con excepción a las reglas de la empresa (gasto fijo mensual, siempre revisión manual o ajuste regla por
/// regla). El RUT se valida contra SAP (consulta "proveedor por RUT") y se guarda copia de su código y razón social.
/// </summary>
public sealed class SupplierProfileService : ISupplierProfileService
{
    private readonly FacturasProveedorDbContext _db;
    private readonly ISapLookupService _sap;
    private readonly InvoiceRuleEngine _engine;
    private readonly ICurrentCompanyAccessor _company;
    private readonly ICurrentUserContext _user;
    private readonly TimeProvider _clock;

    public SupplierProfileService(FacturasProveedorDbContext db, ISapLookupService sap, InvoiceRuleEngine engine,
        ICurrentCompanyAccessor company, ICurrentUserContext user, TimeProvider clock)
    {
        _db = db;
        _sap = sap;
        _engine = engine;
        _company = company;
        _user = user;
        _clock = clock;
    }

    private Guid CompanyId => _company.CompanyId;

    private IQueryable<FpSupplierProfile> Profiles => _db.SupplierProfiles.Include(p => p.Overrides).Where(p => p.CompanyId == CompanyId);

    public async Task<IReadOnlyList<SupplierProfileDto>> ListAsync(CancellationToken ct) =>
        (await Profiles.AsNoTracking().OrderBy(p => p.SupplierName).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<SupplierProfileDto?> GetAsync(long id, CancellationToken ct) =>
        await Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) is { } p ? ToDto(p) : null;

    public async Task<long> SaveAsync(SupplierProfileInput input, CancellationToken ct)
    {
        if (!SupplierConditions.All.Contains(input.Condition))
            throw new InvalidOperationException("Elija la condición del proveedor.");
        if (input.Condition is SupplierConditions.FixedMonthly or SupplierConditions.AlwaysManual && input.ApproverGroupId is null)
            throw new InvalidOperationException("Un proveedor de gasto fijo o de revisión manual necesita un grupo aprobador.");
        if (input.ValidTo is { } to && to < input.ValidFrom)
            throw new InvalidOperationException("La vigencia \"hasta\" no puede ser anterior a \"desde\".");
        await EnsureGroupAsync(input.ApproverGroupId, ct);
        await EnsureGroupAsync(input.BackupGroupId, ct);
        var overrides = input.Overrides.Select(ValidateOverride).ToList();

        var supplier = await _sap.FindSupplierByTaxIdAsync(input.TaxId, ct)
            ?? throw new InvalidOperationException($"El RUT {input.TaxId} no existe como proveedor en SAP.");
        var taxId = SapLookupService.NormalizeRut(supplier.TaxId) ?? supplier.TaxId;

        FpSupplierProfile profile;
        string? before = null;
        if (input.Id is { } id)
        {
            profile = await Profiles.FirstOrDefaultAsync(p => p.Id == id, ct)
                ?? throw new InvalidOperationException("El proveedor con excepción ya no existe.");
            before = JsonSerializer.Serialize(ToDto(profile));
            _db.SupplierRuleOverrides.RemoveRange(profile.Overrides);
            profile.Overrides.Clear();
        }
        else
        {
            profile = new FpSupplierProfile { CompanyId = CompanyId, CreatedAt = _clock.GetUtcNow(), CreatedBy = _user.UserId };
            _db.SupplierProfiles.Add(profile);
        }
        if (await _db.SupplierProfiles.AnyAsync(p => p.CompanyId == CompanyId && p.TaxId == taxId && p.Id != profile.Id, ct))
            throw new InvalidOperationException($"El proveedor {taxId} ya tiene una excepción: edítela en vez de crear otra.");

        profile.CardCode = supplier.CardCode;
        profile.TaxId = taxId;
        profile.SupplierName = Truncate(supplier.CardName, 100);
        profile.Condition = input.Condition;
        profile.ApproverGroupId = input.ApproverGroupId;
        profile.BackupGroupId = input.BackupGroupId;
        profile.ValidFrom = input.ValidFrom;
        profile.ValidTo = input.ValidTo;
        profile.Note = string.IsNullOrWhiteSpace(input.Note) ? null : Truncate(input.Note.Trim(), 300);
        profile.IsActive = input.IsActive;
        profile.UpdatedAt = _clock.GetUtcNow();
        profile.UpdatedBy = _user.UserId;
        profile.Overrides.AddRange(overrides);

        _db.ConfigEvents.Add(new FpConfigEvent
        {
            CompanyId = CompanyId, EntityType = "supplier", EntityKey = taxId, BeforeJson = before,
            AfterJson = JsonSerializer.Serialize(ToDto(profile)), UserId = _user.UserId, OccurredAt = _clock.GetUtcNow(),
        });
        await _db.SaveChangesAsync(ct);
        return profile.Id;
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        var profile = await Profiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null) return;
        _db.ConfigEvents.Add(new FpConfigEvent
        {
            CompanyId = CompanyId, EntityType = "supplier", EntityKey = profile.TaxId, BeforeJson = JsonSerializer.Serialize(ToDto(profile)),
            UserId = _user.UserId, OccurredAt = _clock.GetUtcNow(),
        });
        _db.SupplierProfiles.Remove(profile);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<SupplierProfileFacts?> GetActiveFactsAsync(string taxId, DateOnly onDate, CancellationToken ct)
    {
        var p = await FindActiveAsync(taxId, onDate, ct);
        return p is null ? null : new SupplierProfileFacts(
            p.Condition,
            p.Overrides.ToDictionary(o => o.RuleCode, o => new RuleOverrideFacts(o.Mode, o.Action, o.ActionDays, ReadParameters(o.ParametersJson))),
            p.ApproverGroupId);
    }

    public async Task<(long? Approver, long? Backup)> GetApproverGroupsAsync(string taxId, DateOnly onDate, CancellationToken ct) =>
        await FindActiveAsync(taxId, onDate, ct) is { } p ? (p.ApproverGroupId, p.BackupGroupId) : (null, null);

    private async Task<FpSupplierProfile?> FindActiveAsync(string taxId, DateOnly onDate, CancellationToken ct)
    {
        var rut = SapLookupService.NormalizeRut(taxId);
        return await Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.TaxId == rut && p.IsActive && p.ValidFrom <= onDate
            && (p.ValidTo == null || p.ValidTo >= onDate), ct);
    }

    private FpSupplierRuleOverride ValidateOverride(RuleOverrideInput o)
    {
        var rule = _engine.Find(o.RuleCode) ?? throw new InvalidOperationException($"La regla «{o.RuleCode}» no existe.");
        if (o.Mode == RuleOverrideModes.NotApplicable)
        {
            if (!rule.CanBeDisabled) throw new InvalidOperationException($"La regla «{rule.Name}» no se puede quitar a un proveedor.");
            return new FpSupplierRuleOverride { RuleCode = rule.Code, Mode = o.Mode };
        }
        if (o.Mode != RuleOverrideModes.Custom) throw new InvalidOperationException($"Ajuste inválido para la regla «{rule.Name}».");
        if (o.Action is { } action && !rule.AllowedActions.Contains(action))
            throw new InvalidOperationException($"La acción elegida no está permitida para la regla «{rule.Name}».");
        if (o.ActionDays is < 1) throw new InvalidOperationException($"Los días de la regla «{rule.Name}» deben ser al menos 1.");
        foreach (var (key, value) in o.Parameters ?? new Dictionary<string, decimal>())
        {
            var def = rule.Parameters.FirstOrDefault(p => p.Key == key)
                ?? throw new InvalidOperationException($"La regla «{rule.Name}» no tiene el parámetro «{key}».");
            if (value < def.Min || value > def.Max)
                throw new InvalidOperationException($"«{def.Label}» de la regla «{rule.Name}» debe estar entre {def.Min:0.##} y {def.Max:0.##} {def.Unit}.");
        }
        return new FpSupplierRuleOverride
        {
            RuleCode = rule.Code, Mode = o.Mode, Action = o.Action, ActionDays = o.ActionDays,
            ParametersJson = o.Parameters is { Count: > 0 } p ? JsonSerializer.Serialize(p) : null,
        };
    }

    private async Task EnsureGroupAsync(long? groupId, CancellationToken ct)
    {
        if (groupId is { } id && !await _db.ApproverGroups.AnyAsync(g => g.Id == id && g.CompanyId == CompanyId && g.IsActive, ct))
            throw new InvalidOperationException("El grupo aprobador elegido no existe o está inactivo.");
    }

    private static IReadOnlyDictionary<string, decimal>? ReadParameters(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Dictionary<string, decimal>>(json);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static SupplierProfileDto ToDto(FpSupplierProfile p) => new(
        p.Id, p.CardCode, p.TaxId, p.SupplierName, p.Condition, p.ApproverGroupId, p.BackupGroupId, p.ValidFrom, p.ValidTo, p.Note,
        p.IsActive, p.Overrides.Select(o => new RuleOverrideInput(o.RuleCode, o.Mode, o.Action, o.ActionDays, ReadParameters(o.ParametersJson))).ToList());
}
