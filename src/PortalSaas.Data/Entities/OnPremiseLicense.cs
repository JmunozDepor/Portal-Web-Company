namespace PortalSaas.Data.Entities;

/// <summary>Estado comercial vigente de una Organization en modo instalado (on-premise).</summary>
public sealed class OnPremiseLicense
{
    public long Id { get; set; }

    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

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
