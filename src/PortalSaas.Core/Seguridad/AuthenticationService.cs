using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;

namespace PortalSaas.Core.Seguridad;

/// <summary>
/// Implementación real de IAuthenticationService -- ver el contrato para la política
/// de bloqueo completa.
/// </summary>
public sealed class AuthenticationService : IAuthenticationService
{
    /// <summary>Intentos fallidos consecutivos antes de bloquear la cuenta sola.</summary>
    public const int MaxFailedAttempts = 5;

    private readonly PortalSaasDbContext _db;

    public AuthenticationService(PortalSaasDbContext db)
    {
        _db = db;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(Guid organizationId, string emailOrUsername, string password, CancellationToken ct = default)
    {
        var normalizedInput = emailOrUsername.Trim().ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.OrganizationId == organizationId
                && (u.Username.ToLower() == normalizedInput || u.Email.ToLower() == normalizedInput),
            ct);

        // Mensaje genérico a propósito -- no distinguir "no existe" de "contraseña
        // incorrecta" (mismo criterio anti-enumeración que PortalSAP_v2 planeaba para
        // su login, ver ARCHITECTURE.md de ese proyecto).
        const string credencialesInvalidas = "Usuario o contraseña incorrectos.";

        if (user is null)
        {
            return AuthenticationResult.Failure(credencialesInvalidas);
        }

        if (!user.IsActive)
        {
            return AuthenticationResult.Failure("La cuenta está inactiva.");
        }

        if (user.IsLocked)
        {
            return AuthenticationResult.Failure("La cuenta está bloqueada por demasiados intentos fallidos.");
        }

        if (!PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt))
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.IsLocked = true;
            }

            await _db.SaveChangesAsync(ct);
            return AuthenticationResult.Failure(credencialesInvalidas);
        }

        user.FailedLoginAttempts = 0;
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return AuthenticationResult.Success(user.Id);
    }
}
