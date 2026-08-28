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

    /// <summary>
    /// Payload de estado firmado (ECDSA) por el servidor central -- ver
    /// ILicenseTokenService. CheckLicenseAsync valida ESTO, no Status/ExpiresAt en
    /// crudo, porque esas dos columnas viven en la BD local de la instalación
    /// on-premise y un backup/restore o una edición manual podría falsificarlas sin
    /// dejar rastro. Null hasta la primera activación exitosa.
    /// </summary>
    public string? SignedStatusToken { get; set; }

    /// <summary>Última vez que se refrescó SignedStatusToken contra el central -- define la ventana de gracia offline (Licensing:OfflineGraceDays).</summary>
    public DateTimeOffset? SignedStatusUpdatedAt { get; set; }

    public ICollection<OnPremiseLicenseConflict> Conflicts { get; set; } = new List<OnPremiseLicenseConflict>();
}

public static class OnPremiseLicenseStatus
{
    public const string Active = "active";
    public const string Revoked = "revoked";
    public const string Expired = "expired";

    public static readonly IReadOnlyCollection<string> All = [Active, Revoked, Expired];
}
