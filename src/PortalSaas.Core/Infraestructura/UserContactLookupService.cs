using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;

namespace PortalSaas.Core.Infraestructura;

/// <summary>Implementación real de IUserContactLookupService -- lee directo de Users/UserPreferences, sin acotar por organización del llamador (ver el contrato para el porqué).</summary>
public sealed class UserContactLookupService : IUserContactLookupService
{
    private readonly PortalSaasDbContext _db;

    public UserContactLookupService(PortalSaasDbContext db)
    {
        _db = db;
    }

    public async Task<UserContactDto?> GetContactAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Preference)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
            return null;

        return new UserContactDto(
            user.OrganizationId,
            user.Email,
            user.Preference?.EmailNotificationsEnabled ?? true);
    }
}
