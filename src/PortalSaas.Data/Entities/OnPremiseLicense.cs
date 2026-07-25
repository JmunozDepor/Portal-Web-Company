namespace PortalSaas.Data.Entities;

/// <summary>Estado comercial vigente de una Organization en modo instalado (on-premise).</summary>
public sealed class OnPremiseLicense
{
    public long Id { get; set; }

    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    /// <summary>
    /// Plan que gobierna los límites de esta instalación on-premise -- sin esto
    /// ContractLimitService no tenía de dónde sacar el límite de usuarios/compañías
    /// para una organización on_premise bien configurada (con licencia, sin
    /// Subscription), y siempre denegaba (ver CLAUDE.md, "Hueco real detectado").
    /// </summary>
    public long PlanId { get; set; }
    public Plan Plan { get; set; } = null!;

    public string ActivationKey { get; set; } = null!;
    public string? InstallationFingerprint { get; set; }

    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>"active" | "revoked" | "expired" -- ver OnPremiseLicenseStatus.</summary>
    public string Status { get; set; } = OnPremiseLicenseStatus.Active;
}

public static class OnPremiseLicenseStatus
{
    public const string Active = "active";
    public const string Revoked = "revoked";
    public const string Expired = "expired";

    public static readonly IReadOnlyCollection<string> All = [Active, Revoked, Expired];
}
