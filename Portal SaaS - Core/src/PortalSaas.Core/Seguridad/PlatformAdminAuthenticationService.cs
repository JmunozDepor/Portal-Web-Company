using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;

namespace PortalSaas.Core.Seguridad;

/// <summary>
/// Implementación real de IPlatformAdminAuthenticationService -- ver el contrato para
/// la política de bloqueo completa. Calco de AuthenticationService, pero contra
/// PlatformAdmins (correo único global, sin organización que lo scope).
/// </summary>
public sealed class PlatformAdminAuthenticationService : IPlatformAdminAuthenticationService
{
    /// <summary>Intentos fallidos consecutivos antes de bloquear la cuenta sola.</summary>
    public const int MaxFailedAttempts = 5;

    private readonly PortalSaasDbContext _db;

    public PlatformAdminAuthenticationService(PortalSaasDbContext db)
    {
        _db = db;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(string email, string password, CancellationToken ct = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var admin = await _db.PlatformAdmins.FirstOrDefaultAsync(
            a => a.Email.ToLower() == normalizedEmail, ct);

        // Mensaje genérico a propósito -- mismo criterio anti-enumeración que
        // AuthenticationService.
        const string credencialesInvalidas = "Correo o contraseña incorrectos.";

        if (admin is null)
        {
            return AuthenticationResult.Failure(credencialesInvalidas);
        }

        if (!admin.IsActive)
        {
            return AuthenticationResult.Failure("La cuenta está inactiva.");
        }

        if (admin.IsLocked)
        {
            return AuthenticationResult.Failure("La cuenta está bloqueada por demasiados intentos fallidos.");
        }

        if (!PasswordHasher.Verify(password, admin.PasswordHash, admin.PasswordSalt))
        {
            admin.FailedLoginAttempts++;
            if (admin.FailedLoginAttempts >= MaxFailedAttempts)
            {
                admin.IsLocked = true;
            }

            await _db.SaveChangesAsync(ct);
            return AuthenticationResult.Failure(credencialesInvalidas);
        }

        admin.FailedLoginAttempts = 0;
        admin.LastLoginAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return AuthenticationResult.Success(admin.Id);
    }
}
