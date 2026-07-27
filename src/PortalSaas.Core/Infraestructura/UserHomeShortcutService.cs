using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Infraestructura;

public sealed class UserHomeShortcutService : IUserHomeShortcutService
{
    private readonly PortalSaasDbContext _db;

    public UserHomeShortcutService(PortalSaasDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<long>> ListMenuIdsAsync(Guid userId, CancellationToken ct = default)
    {
        return await _db.UserHomeShortcuts
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.Order)
            .ThenBy(s => s.Id)
            .Select(s => s.MenuId)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Guid userId, long menuId, CancellationToken ct = default)
    {
        var existing = await _db.UserHomeShortcuts
            .FirstOrDefaultAsync(s => s.UserId == userId && s.MenuId == menuId, ct);
        if (existing is not null)
        {
            return;
        }

        var nextOrder = await _db.UserHomeShortcuts
            .Where(s => s.UserId == userId)
            .Select(s => (int?)s.Order)
            .MaxAsync(ct) ?? -1;

        _db.UserHomeShortcuts.Add(new UserHomeShortcut
        {
            UserId = userId,
            MenuId = menuId,
            Order = nextOrder + 1,
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid userId, long menuId, CancellationToken ct = default)
    {
        await _db.UserHomeShortcuts
            .Where(s => s.UserId == userId && s.MenuId == menuId)
            .ExecuteDeleteAsync(ct);
    }
}
