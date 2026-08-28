using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Hace cumplir los límites de `plans` contra el consumo real de una `organizations`
/// -- ver docs/03-MODELO-CORE-COMERCIAL.md §5. NO existía en PortalSAP_v2 -- es
/// exclusivo de la capa comercial de este proyecto.
///
/// Regla dura (ver CLAUDE.md "Los límites de plan se hacen cumplir en código"): si no
/// se puede determinar el límite vigente (sin suscripción activa, sin plan asociado),
/// el resultado es SIEMPRE `Denied` -- nunca se asume "sin límite" por falta de datos.
/// Mismo criterio de "falla hacia lo más estricto" que ya usa el motor de aprobación
/// de PortalSAP_v2 cuando una condición de aprobación no se puede evaluar.
/// </summary>
public interface IContractLimitService
{
    /// <summary>Verifica si la organización puede dar de alta un usuario más.</summary>
    Task<LimitCheckResult> CheckUserLimitAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>Verifica si la organización puede dar de alta una compañía SAP más.</summary>
    Task<LimitCheckResult> CheckCompanyLimitAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Verifica si la organización puede sumar una transacción más en el período dado
    /// (formato "YYYYMM") contra `plans.monthly_transaction_limit`, sumando
    /// `usage_metrics.value` de ese período para la métrica indicada.
    /// </summary>
    Task<LimitCheckResult> CheckMonthlyTransactionLimitAsync(Guid organizationId, string period, string metricName, CancellationToken ct = default);
}
