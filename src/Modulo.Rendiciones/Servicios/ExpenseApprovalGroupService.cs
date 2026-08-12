using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public sealed class ExpenseApprovalGroupService : IExpenseApprovalGroupService
{
    private readonly RendicionesDbContext _db;

    public ExpenseApprovalGroupService(RendicionesDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ExpenseApprovalGroup>> ListAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.ExpenseApprovalGroups
            .Where(g => g.CompanyId == companyId)
            .OrderBy(g => g.Name)
            .ToListAsync(ct);

    public async Task<ExpenseApprovalGroup?> GetAsync(long id, Guid companyId, CancellationToken ct = default) =>
        await _db.ExpenseApprovalGroups.FirstOrDefaultAsync(g => g.Id == id && g.CompanyId == companyId, ct);

    public async Task<ExpenseApprovalGroup?> GetUserGroupAsync(Guid companyId, Guid userId, CancellationToken ct = default)
    {
        var groupId = await _db.ExpenseApprovalGroupMembers
            .Where(m => m.UserId == userId)
            .Join(_db.ExpenseApprovalGroups.Where(g => g.CompanyId == companyId && g.IsActive),
                m => m.ExpenseApprovalGroupId, g => g.Id, (m, g) => g.Id)
            .FirstOrDefaultAsync(ct);

        return groupId == 0 ? null : await GetAsync(groupId, companyId, ct);
    }

    public async Task<IReadOnlyList<ExpenseApprovalGroupMember>> ListMembersAsync(long groupId, CancellationToken ct = default) =>
        await _db.ExpenseApprovalGroupMembers.Where(m => m.ExpenseApprovalGroupId == groupId).ToListAsync(ct);

    public async Task<IReadOnlyDictionary<int, Guid>> GetLevelsAsync(long groupId, CancellationToken ct = default) =>
        await _db.ExpenseApprovalGroupLevels
            .Where(n => n.ExpenseApprovalGroupId == groupId)
            .ToDictionaryAsync(n => n.Level, n => n.UserId, ct);

    public async Task<long> CreateAsync(Guid companyId, string name, CancellationToken ct = default)
    {
        var group = new ExpenseApprovalGroup { CompanyId = companyId, Name = name, IsActive = true };
        _db.ExpenseApprovalGroups.Add(group);
        await _db.SaveChangesAsync(ct);
        return group.Id;
    }

    public async Task AddMemberAsync(long groupId, Guid companyId, Guid userId, CancellationToken ct = default)
    {
        await RequireCompanyGroupAsync(groupId, companyId, ct);

        var alreadyExists = await _db.ExpenseApprovalGroupMembers
            .AnyAsync(m => m.ExpenseApprovalGroupId == groupId && m.UserId == userId, ct);
        if (alreadyExists)
            return;

        _db.ExpenseApprovalGroupMembers.Add(new ExpenseApprovalGroupMember
        {
            ExpenseApprovalGroupId = groupId,
            UserId = userId,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveMemberAsync(long groupId, Guid companyId, Guid userId, CancellationToken ct = default)
    {
        await RequireCompanyGroupAsync(groupId, companyId, ct);

        var member = await _db.ExpenseApprovalGroupMembers
            .FirstOrDefaultAsync(m => m.ExpenseApprovalGroupId == groupId && m.UserId == userId, ct);
        if (member is null)
            return;

        _db.ExpenseApprovalGroupMembers.Remove(member);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetLevelAsync(long groupId, Guid companyId, int level, Guid userId, CancellationToken ct = default)
    {
        if (level is not (1 or 2))
            throw new InvalidOperationException("Solo se admiten los niveles 1 y 2.");

        await RequireCompanyGroupAsync(groupId, companyId, ct);

        var existing = await _db.ExpenseApprovalGroupLevels
            .FirstOrDefaultAsync(n => n.ExpenseApprovalGroupId == groupId && n.Level == level, ct);

        if (existing is not null)
        {
            existing.UserId = userId;
        }
        else
        {
            _db.ExpenseApprovalGroupLevels.Add(new ExpenseApprovalGroupLevel
            {
                ExpenseApprovalGroupId = groupId,
                Level = level,
                UserId = userId,
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveLevelAsync(long groupId, Guid companyId, int level, CancellationToken ct = default)
    {
        await RequireCompanyGroupAsync(groupId, companyId, ct);

        var existing = await _db.ExpenseApprovalGroupLevels
            .FirstOrDefaultAsync(n => n.ExpenseApprovalGroupId == groupId && n.Level == level, ct);
        if (existing is null)
            return;

        _db.ExpenseApprovalGroupLevels.Remove(existing);
        await _db.SaveChangesAsync(ct);
    }

    private async Task RequireCompanyGroupAsync(long groupId, Guid companyId, CancellationToken ct)
    {
        var exists = await _db.ExpenseApprovalGroups.AnyAsync(g => g.Id == groupId && g.CompanyId == companyId, ct);
        if (!exists)
            throw new InvalidOperationException("El grupo de aprobación no existe o no pertenece a esta compañía.");
    }
}
