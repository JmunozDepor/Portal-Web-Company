namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Resultado de un intento de activación/heartbeat contra el servidor central -- ver
/// ILicenseActivationService. SignedToken viene lleno cuando IsAccepted es true (o
/// cuando la licencia existe pero está revocada/vencida -- el on-premise necesita
/// PRUEBA firmada de eso, no solo un error HTTP que también podría ser un problema de
/// red). SignedToken es null solo cuando la ActivationKey no existe o hay un
/// conflicto de fingerprint sin resolver.
/// </summary>
public sealed record LicenseActivationResult(bool IsAccepted, string? SignedToken, string? Reason)
{
    public static LicenseActivationResult Accepted(string signedToken, string? reason = null) => new(true, signedToken, reason);

    public static LicenseActivationResult Rejected(string reason) => new(false, null, reason);
}
