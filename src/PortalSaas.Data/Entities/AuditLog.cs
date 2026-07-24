namespace PortalSaas.Data.Entities;

/// <summary>
/// ACOTADA a eventos de seguridad/administración (no transacciones de negocio -- esas
/// quedan en el UDF del documento SAP, ver ARCHITECTURE.md) -- portado de PortalSAP_v2
/// (`LOG_AUDITORIA`). `UserId`/`CompanyId` nullable porque un evento puede no tener
/// usuario asociado (ej. un intento de login con un usuario que no existe).
/// </summary>
public sealed class AuditLog
{
    public long Id { get; set; }

    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }

    /// <summary>'LOGIN_OK' | 'LOGIN_FAILED' | 'LOCKOUT' | 'PERMISSION_CHANGE' | 'ACCESS_DENIED', etc.</summary>
    public string Event { get; set; } = null!;

    public string? Detail { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
