using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public sealed class RendicionesUserRoleService : IRendicionesUserRoleService
{
    private readonly RendicionesDbContext _db;

    public RendicionesUserRoleService(RendicionesDbContext db)
    {
        _db = db;
    }

    public async Task<bool> HasRoleAsync(Guid companyId, Guid userId, string role, bool isPlatformAdmin, CancellationToken ct = default)
    {
        if (isPlatformAdmin)
            return true;

        return await _db.RendicionesUserRoles.AsNoTracking()
            .AnyAsync(r => r.CompanyId == companyId && r.UserId == userId && r.Role == role, ct);
    }

    public async Task<bool> HasAnyRoleAsync(Guid companyId, Guid userId, IReadOnlyList<string> roles, bool isPlatformAdmin, CancellationToken ct = default)
    {
        if (isPlatformAdmin)
            return true;

        return await _db.RendicionesUserRoles.AsNoTracking()
            .AnyAsync(r => r.CompanyId == companyId && r.UserId == userId && roles.Contains(r.Role), ct);
    }

    public async Task<IReadOnlyList<string>> ListRolesAsync(Guid companyId, Guid userId, CancellationToken ct = default) =>
        await _db.RendicionesUserRoles.AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.UserId == userId)
            .Select(r => r.Role)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> ListAllAsync(Guid companyId, CancellationToken ct = default)
    {
        var rows = await _db.RendicionesUserRoles.AsNoTracking()
            .Where(r => r.CompanyId == companyId)
            .ToListAsync(ct);

        return rows.GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Role).ToList());
    }

    public async Task SetRoleAsync(Guid companyId, Guid userId, string role, bool granted, CancellationToken ct = default)
    {
        var existing = await _db.RendicionesUserRoles
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.UserId == userId && r.Role == role, ct);

        if (granted && existing is null)
        {
            _db.RendicionesUserRoles.Add(new RendicionesUserRole { CompanyId = companyId, UserId = userId, Role = role });
        }
        else if (!granted && existing is not null)
        {
            _db.RendicionesUserRoles.Remove(existing);
        }
        else
        {
            return;
        }

        await _db.SaveChangesAsync(ct);
    }
}
