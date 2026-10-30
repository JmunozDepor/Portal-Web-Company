using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public sealed class PolicyProfileService : IPolicyProfileService
{
    private readonly RendicionesDbContext _db;

    public PolicyProfileService(RendicionesDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PolicyProfile>> ListActiveAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.PolicyProfiles
            .Where(p => p.CompanyId == companyId && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PolicyProfile>> ListAllAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.PolicyProfiles
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

    public async Task<long> CreateAsync(Guid companyId, string name, CancellationToken ct = default)
    {
        var exists = await _db.PolicyProfiles
            .AnyAsync(p => p.CompanyId == companyId && p.Name == name, ct);
        if (exists)
            throw new InvalidOperationException("Ya existe un perfil de política con ese nombre en esta compañía.");

        var profile = new PolicyProfile
        {
            CompanyId = companyId,
            Name = name,
            IsActive = true,
        };
        _db.PolicyProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);
        return profile.Id;
    }

    public async Task UpdateAsync(long id, Guid companyId, string name, bool isActive, CancellationToken ct = default)
    {
        var profile = await _db.PolicyProfiles.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("El perfil de política no existe o no pertenece a esta compañía.");

        profile.Name = name;
        profile.IsActive = isActive;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<long?> GetAssignedProfileIdAsync(Guid companyId, Guid userId, CancellationToken ct = default)
    {
        var assignment = await _db.UserPolicyProfiles
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.UserId == userId, ct);
        return assignment?.PolicyProfileId;
    }

    public async Task AssignAsync(Guid companyId, Guid userId, long? policyProfileId, CancellationToken ct = default)
    {
        var existing = await _db.UserPolicyProfiles
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.UserId == userId, ct);

        if (policyProfileId is null)
        {
            if (existing is not null)
            {
                _db.UserPolicyProfiles.Remove(existing);
                await _db.SaveChangesAsync(ct);
            }
            return;
        }

        if (existing is not null)
        {
            existing.PolicyProfileId = policyProfileId.Value;
        }
        else
        {
            _db.UserPolicyProfiles.Add(new UserPolicyProfile
            {
                CompanyId = companyId,
                UserId = userId,
                PolicyProfileId = policyProfileId.Value,
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, PolicyProfile>> ListAssignmentsAsync(Guid companyId, CancellationToken ct = default)
    {
        var assignments = await _db.UserPolicyProfiles
            .Where(a => a.CompanyId == companyId)
            .Include(a => a.PolicyProfile)
            .ToListAsync(ct);

        return assignments
            .Where(a => a.PolicyProfile is not null)
            .ToDictionary(a => a.UserId, a => a.PolicyProfile!);
    }
}
