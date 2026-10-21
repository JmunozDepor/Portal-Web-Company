namespace Modulo.Despachos.Servicios;

/// <summary>Resultado de cerrar un período: cerrado, o la lista de bloqueos que lo impiden.</summary>
public sealed record ClosureResult(bool Closed, IReadOnlyList<string> Blockers);

/// <summary>Estado de un período para la pantalla de cierre.</summary>
public sealed record PeriodStatus(
    string Status, DateTime? ClosedAt, DateTime? ReopenedAt, string? ReopenReason,
    int ManifestCount, decimal TotalCost, IReadOnlyList<string> Blockers);

/// <summary>
/// Cierre mensual por dispatch_date. Cerrar congela cost_final de los manifiestos (no anulados) y deja el período
/// en solo lectura; reabrir exige administrador y motivo. Consulta el DbContext directamente (nunca IManifestService)
/// para que ManifestService pueda depender de IsClosedAsync sin ciclos.
/// </summary>
public interface IMonthlyClosureService
{
    Task<ClosureResult> CloseAsync(int year, int month);

    /// <summary>Solo IsAdmin, con motivo. El período queda "reopened" y se puede cerrar de nuevo.</summary>
    Task ReopenAsync(int year, int month, string reason);

    Task<bool> IsClosedAsync(DateOnly date);

    Task<PeriodStatus> GetStatusAsync(int year, int month);
}
