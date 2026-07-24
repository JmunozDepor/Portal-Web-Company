namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Resultado de verificar un límite de plan contra el consumo real de una
/// organización -- ver IContractLimitService. `Reason` siempre viene lleno cuando
/// `IsAllowed` es false, para poder mostrarlo tal cual en la UI/API sin adivinar el
/// motivo del bloqueo.
/// </summary>
public sealed record LimitCheckResult(bool IsAllowed, string? Reason)
{
    public static LimitCheckResult Allowed() => new(true, null);
    public static LimitCheckResult Denied(string reason) => new(false, reason);
}
