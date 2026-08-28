using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Seguridad;

/// <summary>
/// Implementación real de IPasswordResetService. El token real (alta entropía, 256
/// bits) viaja por correo -- acá solo se guarda su hash SHA-256 (no PBKDF2: a
/// diferencia de una contraseña elegida por una persona, el token ya es aleatorio de
/// 256 bits, un hash rápido alcanza y no hay necesidad de una KDF lenta).
/// </summary>
public sealed class PasswordResetService : IPasswordResetService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    private readonly PortalSaasDbContext _db;

    public PasswordResetService(PortalSaasDbContext db)
    {
        _db = db;
    }

    public async Task<string?> RequestResetAsync(Guid organizationId, string email, CancellationToken ct = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.OrganizationId == organizationId && u.IsActive && u.Email.ToLower() == normalizedEmail,
            ct);

        if (user is null)
        {
            return null;
        }

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime),
        });

        await _db.SaveChangesAsync(ct);

        return rawToken;
    }

    public async Task<bool> ResetPasswordAsync(string rawToken, string newPassword, CancellationToken ct = default)
    {
        var tokenHash = HashToken(rawToken);

        var resetToken = await _db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (resetToken is null || resetToken.IsUsed || resetToken.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return false;
        }

        var (hash, salt) = PasswordHasher.Hash(newPassword);
        resetToken.User.PasswordHash = hash;
        resetToken.User.PasswordSalt = salt;
        resetToken.User.FailedLoginAttempts = 0;
        resetToken.User.IsLocked = false;
        resetToken.IsUsed = true;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static string HashToken(string rawToken) =>
        Convert.ToBase64String(SHA256.HashData(Convert.FromBase64String(rawToken)));
}
