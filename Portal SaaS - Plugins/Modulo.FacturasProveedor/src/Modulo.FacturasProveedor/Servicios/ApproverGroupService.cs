using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulo.FacturasProveedor.Data;
using Modulo.FacturasProveedor.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.FacturasProveedor.Servicios;

public sealed record ApproverGroupInput(long? Id, string Name, bool IsFinance, bool IsActive, IReadOnlyList<Guid> MemberUserIds);

public sealed record ApproverGroupDto(long Id, string Name, bool IsFinance, bool IsActive, IReadOnlyList<Guid> MemberUserIds, int SupplierCount);

public interface IApproverGroupService
{
    Task<IReadOnlyList<ApproverGroupDto>> ListAsync(CancellationToken ct);
    Task<long> SaveAsync(ApproverGroupInput input, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
    /// <summary>Grupos activos a los que pertenece el usuario.</summary>
    Task<IReadOnlySet<long>> GetGroupIdsForUserAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetMemberIdsAsync(long groupId, CancellationToken ct);
}

/// <summary>
/// Grupos de usuarios del portal que deciden las facturas en revisión manual. Uno solo puede ser el grupo de Finanzas
/// (supervisión): al marcarlo queda como fp_settings.finance_group_id.
/// </summary>
public sealed class ApproverGroupService : IApproverGroupService
{
    private readonly FacturasProveedorDbContext _db;
    private readonly ICurrentCompanyAccessor _company;
    private readonly ICurrentUserContext _user;
    private readonly TimeProvider _clock;

    public ApproverGroupService(FacturasProveedorDbContext db, ICurrentCompanyAccessor company, ICurrentUserContext user, TimeProvider clock)
    {
        _db = db;
        _company = company;
        _user = user;
        _clock = clock;
    }

    private Guid CompanyId => _company.CompanyId;

    private IQueryable<FpApproverGroup> Groups => _db.ApproverGroups.Include(g => g.Members).Where(g => g.CompanyId == CompanyId);

    public async Task<IReadOnlyList<ApproverGroupDto>> ListAsync(CancellationToken ct)
    {
        var groups = await Groups.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);
        var profiles = await _db.SupplierProfiles.AsNoTracking().Where(p => p.CompanyId == CompanyId)
            .Select(p => new { p.ApproverGroupId, p.BackupGroupId }).ToListAsync(ct);
        return groups.Select(g => new ApproverGroupDto(g.Id, g.Name, g.IsFinance, g.IsActive, g.Members.Select(m => m.UserId).ToList(),
            profiles.Count(p => p.ApproverGroupId == g.Id || p.BackupGroupId == g.Id))).ToList();
    }

    public async Task<long> SaveAsync(ApproverGroupInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new InvalidOperationException("Ingrese el nombre del grupo.");
        var name = input.Name.Trim();
        if (name.Length > 100) name = name[..100];
        await EnsureFinanceStaysUsableAsync(input, ct);

        FpApproverGroup group;
        string? before = null;
        if (input.Id is { } id)
        {
            group = await Groups.FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw new InvalidOperationException("El grupo ya no existe.");
            before = Snapshot(group);
            _db.ApproverGroupMembers.RemoveRange(group.Members);
            group.Members.Clear();
            group.UpdatedAt = _clock.GetUtcNow();
        }
        else
        {
            group = new FpApproverGroup { CompanyId = CompanyId, CreatedAt = _clock.GetUtcNow() };
            _db.ApproverGroups.Add(group);
        }
        if (await _db.ApproverGroups.AnyAsync(g => g.CompanyId == CompanyId && g.Name == name && g.Id != group.Id, ct))
            throw new InvalidOperationException($"Ya existe un grupo llamado «{name}».");

        group.Name = name;
        group.IsActive = input.IsActive;
        group.IsFinance = input.IsFinance;
        group.Members.AddRange(input.MemberUserIds.Distinct().Select(u => new FpApproverGroupMember { UserId = u }));
        await _db.SaveChangesAsync(ct);

        var settings = await _db.Settings.FirstOrDefaultAsync(s => s.CompanyId == CompanyId, ct);
        if (settings is null)
        {
            settings = new FpSettings { CompanyId = CompanyId };
            _db.Settings.Add(settings);
        }
        if (group.IsFinance)
        {
            foreach (var other in await _db.ApproverGroups.Where(g => g.CompanyId == CompanyId && g.IsFinance && g.Id != group.Id).ToListAsync(ct))
                other.IsFinance = false;
            settings.FinanceGroupId = group.Id;
        }
        else if (settings.FinanceGroupId == group.Id)
        {
            settings.FinanceGroupId = null;
        }

        _db.ConfigEvents.Add(new FpConfigEvent
        {
            CompanyId = CompanyId, EntityType = "group", EntityKey = group.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            BeforeJson = before, AfterJson = Snapshot(group), UserId = _user.UserId, OccurredAt = _clock.GetUtcNow(),
        });
        await _db.SaveChangesAsync(ct);
        return group.Id;
    }

    /// <summary>
    /// En modo activo siempre debe haber un grupo de Finanzas activo y con integrantes: es el respaldo de toda revisión sin
    /// aprobador y quien recibe los escalamientos. Para cambiarlo se marca otro grupo como Finanzas.
    /// </summary>
    private async Task EnsureFinanceStaysUsableAsync(ApproverGroupInput input, CancellationToken ct)
    {
        var settings = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == CompanyId, ct);
        if (settings?.Mode != ModuleModes.Active) return;
        var isCurrentFinance = input.Id is { } id && settings.FinanceGroupId == id;
        if (isCurrentFinance && !input.IsFinance)
            throw new InvalidOperationException("En modo Activo no se puede quitar el grupo de Finanzas: marque otro grupo como Finanzas.");
        if ((isCurrentFinance || input.IsFinance) && (!input.IsActive || input.MemberUserIds.Count == 0))
            throw new InvalidOperationException("En modo Activo el grupo de Finanzas debe estar activo y tener al menos un integrante.");
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        var group = await Groups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return;
        if (await _db.SupplierProfiles.AnyAsync(p => p.CompanyId == CompanyId && (p.ApproverGroupId == id || p.BackupGroupId == id), ct))
            throw new InvalidOperationException($"El grupo «{group.Name}» está asignado a proveedores con excepción: cámbielos antes de eliminarlo.");
        if (await _db.Settings.AnyAsync(s => s.CompanyId == CompanyId && s.FinanceGroupId == id, ct))
            throw new InvalidOperationException($"El grupo «{group.Name}» es el grupo de Finanzas: marque otro antes de eliminarlo.");
        _db.ApproverGroups.Remove(group);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlySet<long>> GetGroupIdsForUserAsync(Guid userId, CancellationToken ct) =>
        (await _db.ApproverGroups.AsNoTracking()
            .Where(g => g.CompanyId == CompanyId && g.IsActive && g.Members.Any(m => m.UserId == userId))
            .Select(g => g.Id).ToListAsync(ct)).ToHashSet();

    public async Task<IReadOnlyList<Guid>> GetMemberIdsAsync(long groupId, CancellationToken ct) =>
        await _db.ApproverGroupMembers.AsNoTracking()
            .Where(m => m.GroupId == groupId && _db.ApproverGroups.Any(g => g.Id == groupId && g.CompanyId == CompanyId && g.IsActive))
            .Select(m => m.UserId).ToListAsync(ct);

    private static string Snapshot(FpApproverGroup g) =>
        JsonSerializer.Serialize(new { g.Name, g.IsFinance, g.IsActive, Members = g.Members.Select(m => m.UserId) });
}
