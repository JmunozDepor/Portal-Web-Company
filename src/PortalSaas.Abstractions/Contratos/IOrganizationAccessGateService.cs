using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Gate comercial de acceso -- ver docs/03-MODELO-CORE-COMERCIAL.md §5. Distinto de
/// IContractLimitService (que verifica cupo dentro de un plan vigente): esto verifica
/// si la organización tiene DERECHO a operar en absoluto, según su modo:
/// - "saas": requiere una `subscriptions` con status 'trial' o 'active'.
/// - "on_premise": requiere una `on_premise_licenses` con status 'active' y
///   `expires_at` en el futuro.
/// Mismo criterio "falla hacia lo más estricto" que IContractLimitService -- si no
/// hay fila (ninguna suscripción/licencia), se deniega, nunca se asume acceso libre.
/// </summary>
public interface IOrganizationAccessGateService
{
    Task<LimitCheckResult> CheckAccessAsync(Guid organizationId, CancellationToken ct = default);
}
