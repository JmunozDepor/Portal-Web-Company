namespace PortalSaas.Abstractions.Contratos;

using PortalSaas.Abstractions.Modelos;

/// <summary>
/// Lado CENTRAL del licenciamiento remoto -- valida un intento de activación/heartbeat
/// de una instalación on-premise contra `on_premise_licenses` y decide si el
/// InstallationFingerprint reportado coincide con el vinculado. Primera activación con
/// una ActivationKey gana el fingerprint; un fingerprint distinto después de eso se
/// registra como conflicto (`on_premise_license_conflicts`) en vez de rebindear solo,
/// y requiere resolución manual de un platform admin.
/// </summary>
public interface ILicenseActivationService
{
    Task<LicenseActivationResult> ActivateOrHeartbeatAsync(string activationKey, string fingerprint, string? sourceIp, CancellationToken ct = default);
}
