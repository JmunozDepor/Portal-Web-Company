using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Seguridad;

/// <summary>
/// Implementación real de IUserSessionService. Mismo criterio de hash que
/// PasswordResetService: el token ya es aleatorio de 256 bits (RandomNumberGenerator),
/// un hash rápido (SHA-256) alcanza -- no hace falta una KDF lenta como con una
/// contraseña elegida por una persona.
/// </summary>
public sealed class UserSessionService : IUserSessionService
{
    // Igual que el ExpireTimeSpan de la cookie de sesión de tenant (Program.cs,
    // AddCookie) -- una fila user_sessions se crea por cada login y solo se marca
    // IsRevoked en el logout explícito, así que quien cierra el navegador sin
    // desloguearse deja la fila viva para siempre. Para "clientes conectados" del
    // backoffice se descartan las más viejas que la vida de la cookie: si nadie tocó
    // esa sesión en 8 h, la cookie ya caducó y el usuario no está realmente conectado.
    private static readonly TimeSpan SessionCookieLifetime = TimeSpan.FromHours(8);

    /// <summary>Cada cuánto, como mucho, se reescribe LastSeenAt en un request autenticado
    /// (ver ValidateAndTouchAsync) -- chico frente a la vida de la cookie para que la
    /// purga nunca alcance a una sesión activa, grande para no escribir en cada click.</summary>
    private static readonly TimeSpan TouchThrottle = TimeSpan.FromMinutes(5);

    private readonly PortalSaasDbContext _db;

    public UserSessionService(PortalSaasDbContext db)
    {
        _db = db;
    }

    public async Task<string> CreateAsync(Guid userId, Guid organizationId, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var ahora = DateTimeOffset.UtcNow;
        _db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            OrganizationId = organizationId,
            TokenHash = HashToken(rawToken),
            CreatedAt = ahora,
            LastSeenAt = ahora,
            IpAddress = ipAddress,
            // Recortado a la misma longitud máxima de la columna -- un User-Agent real
            // nunca debería pasar los 300 caracteres, pero un valor mal formado no debe
            // tirar la creación de la sesión (login) por un error de longitud de columna.
            UserAgent = userAgent is { Length: > 300 } ? userAgent[..300] : userAgent,
        });

        await _db.SaveChangesAsync(ct);
        return rawToken;
    }

    public async Task SetCompanyAsync(string rawToken, Guid companyId, CancellationToken ct = default)
    {
        var tokenHash = HashToken(rawToken);
        var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);
        if (session is null)
        {
            return;
        }

        session.CompanyId = companyId;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<SessionValidationStatus> ValidateAndTouchAsync(string rawToken, CancellationToken ct = default)
    {
        string tokenHash;
        try
        {
            tokenHash = HashToken(rawToken);
        }
        catch (FormatException)
        {
            // Claim "SessionToken" con un valor que no es base64 (cookie manipulada o
            // corrupta) -- se trata como token inexistente, nunca tira una excepción
            // hacia el middleware de autenticación.
            return SessionValidationStatus.NotFound;
        }

        var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);

        if (session is null)
        {
            return SessionValidationStatus.NotFound;
        }

        if (session.IsRevoked)
        {
            return SessionValidationStatus.Revoked;
        }

        // Refresco acotado -- solo si pasó el umbral desde la última vez, para no
        // escribir en cada request. Un fallo al guardar la marca de actividad NUNCA
        // debe voltear un request de una sesión que sí es válida: se ignora y se
        // reintenta en el próximo request.
        var ahora = DateTimeOffset.UtcNow;
        if (ahora - session.LastSeenAt >= TouchThrottle)
        {
            session.LastSeenAt = ahora;
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                _db.Entry(session).State = EntityState.Detached;
            }
        }

        return SessionValidationStatus.Active;
    }

    public async Task<IReadOnlyList<UserSessionDto>> ListActiveAsync(Guid? organizationId = null, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - SessionCookieLifetime;

        var query = _db.UserSessions
            .AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.Organization)
            .Include(s => s.Company)
            // Contra LastSeenAt, no CreatedAt -- la cookie usa SlidingExpiration, así que
            // "conectado" es "tuvo actividad dentro de la vida de la cookie", no "se logueó
            // hace menos de 8 h" (ver UserSession.LastSeenAt).
            .Where(s => !s.IsRevoked && s.LastSeenAt >= cutoff);

        if (organizationId is { } orgId)
        {
            query = query.Where(s => s.OrganizationId == orgId);
        }

        var sessions = await query
            .OrderBy(s => s.Organization.LegalName)
            .ThenBy(s => s.User.Username)
            .ToListAsync(ct);

        return sessions.Select(s => new UserSessionDto(
            SessionId: s.Id,
            OrganizationId: s.OrganizationId,
            OrganizationName: s.Organization.LegalName,
            UserId: s.UserId,
            Username: s.User.Username,
            Email: s.User.Email,
            CompanyId: s.CompanyId,
            CompanyName: s.Company?.Name,
            IpAddress: s.IpAddress,
            UserAgent: s.UserAgent,
            CreatedAt: s.CreatedAt,
            LastSeenAt: s.LastSeenAt)).ToList();
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || session.IsRevoked)
        {
            return;
        }

        session.IsRevoked = true;
        session.RevokedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeByTokenAsync(string rawToken, CancellationToken ct = default)
    {
        var tokenHash = HashToken(rawToken);
        var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);
        if (session is null || session.IsRevoked)
        {
            return;
        }

        session.IsRevoked = true;
        session.RevokedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static string HashToken(string rawToken) =>
        Convert.ToBase64String(SHA256.HashData(Convert.FromBase64String(rawToken)));
}
