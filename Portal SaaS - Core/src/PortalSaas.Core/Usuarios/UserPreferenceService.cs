using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Usuarios;

/// <summary>Implementación real de IUserPreferenceService.</summary>
public sealed class UserPreferenceService : IUserPreferenceService
{
    private readonly PortalSaasDbContext _db;

    public UserPreferenceService(PortalSaasDbContext db)
    {
        _db = db;
    }

    public async Task<UserPreferenceDto> GetOrCreateDefaultAsync(Guid userId, CancellationToken ct = default)
    {
        var preference = await _db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (preference is null)
        {
            preference = new UserPreference { UserId = userId };
            _db.UserPreferences.Add(preference);
            await _db.SaveChangesAsync(ct);
        }

        return ToDto(preference);
    }

    public async Task UpdateAsync(Guid userId, UserPreferenceDto preferences, CancellationToken ct = default)
    {
        var preference = await _db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (preference is null)
        {
            preference = new UserPreference { UserId = userId };
            _db.UserPreferences.Add(preference);
        }

        preference.Locale = preferences.Locale;
        preference.Timezone = preferences.Timezone;
        preference.Theme = preferences.Theme;
        preference.EmailNotificationsEnabled = preferences.EmailNotificationsEnabled;

        await _db.SaveChangesAsync(ct);
    }

    private static UserPreferenceDto ToDto(UserPreference preference) => new(
        preference.Locale, preference.Timezone, preference.Theme, preference.EmailNotificationsEnabled);
}
