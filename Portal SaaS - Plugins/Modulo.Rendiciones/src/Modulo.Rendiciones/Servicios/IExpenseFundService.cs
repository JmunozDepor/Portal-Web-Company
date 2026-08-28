using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public interface IExpenseFundService
{
    Task<IReadOnlyList<ExpenseFund>> ListByUserAsync(Guid companyId, Guid userId, CancellationToken ct = default);

    Task<long> CreateAsync(ExpenseFund fund, CancellationToken ct = default);

    /// <summary>
    /// Monto del fondo menos la suma de líneas de rendiciones Approved asociadas a él.
    /// Positivo = saldo a favor de la compañía (colaborador debe devolver), negativo = a
    /// favor del colaborador (compañía debe reembolsar). No convierte moneda -- asume
    /// que las líneas están en la misma moneda que el fondo (limitación conocida,
    /// heredada del original).
    /// </summary>
    Task<decimal> CalculatePendingBalanceAsync(long fundId, Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Recalcula el saldo y marca el fondo Settled si queda en 0 o a favor del
    /// colaborador; si no, lo deja/vuelve a Open. No toca Overdue (fuera de alcance del
    /// MVP -- requeriría un job por fecha de compromiso, no un recálculo por evento).
    /// </summary>
    Task RecalculateStatusAsync(long fundId, Guid companyId, CancellationToken ct = default);
}
