using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Servicios;

public sealed class UserCostCenterService : IUserCostCenterService
{
    private readonly RendicionesDbContext _db;

    public UserCostCenterService(RendicionesDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<UserCostCenter>> ListAssignedAsync(Guid companyId, Guid userId, CancellationToken ct = default) =>
        await _db.UserCostCenters
            .Where(c => c.CompanyId == companyId && c.UserId == userId)
            .OrderBy(c => c.CostCenterName)
            .ToListAsync(ct);

    public async Task AssignAsync(Guid companyId, Guid userId, string costCenterCode, string? costCenterName, CancellationToken ct = default)
    {
        var alreadyExists = await _db.UserCostCenters.AnyAsync(c =>
            c.CompanyId == companyId && c.UserId == userId && c.CostCenterCode == costCenterCode, ct);
        if (alreadyExists)
            return;

        _db.UserCostCenters.Add(new UserCostCenter
        {
            CompanyId = companyId,
            UserId = userId,
            CostCenterCode = costCenterCode,
            CostCenterName = costCenterName,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountAssignedByUserAsync(Guid companyId, CancellationToken ct = default)
    {
        var counts = await _db.UserCostCenters
            .Where(c => c.CompanyId == companyId)
            .GroupBy(c => c.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return counts.ToDictionary(x => x.UserId, x => x.Count);
    }

    public async Task RemoveAsync(long id, Guid companyId, CancellationToken ct = default)
    {
        var assignment = await _db.UserCostCenters.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId, ct);
        if (assignment is null)
            return;

        _db.UserCostCenters.Remove(assignment);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CostCenterDto>> GetAvailableAsync(Guid companyId, Guid userId, CancellationToken ct = default)
    {
        var assigned = await ListAssignedAsync(companyId, userId, ct);
        if (assigned.Count > 0)
        {
            return assigned
                .Select(a => new CostCenterDto { Code = a.CostCenterCode, Name = a.CostCenterName ?? a.CostCenterCode })
                .ToList();
        }

        var local = await _db.CostCenters
            .Where(c => c.CompanyId == companyId && c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        return local.Select(c => new CostCenterDto { Code = c.Code, Name = c.Name }).ToList();
    }
}
