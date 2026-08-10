namespace PortalSaas.Data.Entities;

/// <summary>
/// Un intento de activación/heartbeat con un InstallationFingerprint distinto al que
/// ya tiene vinculado la licencia -- típicamente un backup de la BD on-premise
/// restaurado en otro servidor. El servidor central registra esto en vez de rebindear
/// automáticamente; un platform admin resuelve manualmente (ver
/// Pages/Admin/Organizations/Licenses/Conflicts).
/// </summary>
public sealed class OnPremiseLicenseConflict
{
    public long Id { get; set; }

    public long OnPremiseLicenseId { get; set; }
    public OnPremiseLicense OnPremiseLicense { get; set; } = null!;

    public string ReportedFingerprint { get; set; } = null!;
    public DateTimeOffset ReportedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ReportedIp { get; set; }

    public bool Resolved { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedByAdminEmail { get; set; }
}
