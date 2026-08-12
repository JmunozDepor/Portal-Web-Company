using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// CRUD de gastos SUELTOS -- un gasto se captura libre (manual, import masivo u OCR de
/// foto) antes de pertenecer a un informe. Ver Models/ExpenseReportLine.Status. La
/// gestión de qué gastos entran a qué informe vive en IExpenseReportService
/// (CreateReportAsync/AttachExpensesAsync/DetachExpenseAsync), no acá.
/// </summary>
public interface IExpenseService
{
    Task<IReadOnlyList<ExpenseReportLine>> ListLooseAsync(Guid companyId, Guid userId, CancellationToken ct = default);

    /// <summary>Sueltos + los que ya están en un informe -- para la pantalla "Mis Gastos" completa.</summary>
    Task<IReadOnlyList<ExpenseReportLine>> ListAllAsync(Guid companyId, Guid userId, CancellationToken ct = default);

    Task<ExpenseReportLine?> GetAsync(long id, Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Valida contra IExpensePolicyService antes de guardar -- si el tope de una
    /// política bloqueante se supera, lanza InvalidOperationException y no guarda nada.
    /// El resultado trae las advertencias no bloqueantes (tope no bloqueante superado,
    /// posible duplicado por número de documento) para que la página las muestre.
    /// </summary>
    Task<ExpenseSavedResult> CreateAsync(ExpenseReportLine expense, CancellationToken ct = default);

    /// <summary>Solo mientras el gasto sigue Loose -- si ya está en un informe, se edita desde ahí (o se desvincula primero). Misma validación de políticas que CreateAsync; devuelve las advertencias.</summary>
    Task<IReadOnlyList<string>> UpdateAsync(long id, Guid companyId, ExpenseReportLine data, CancellationToken ct = default);

    /// <summary>Solo mientras el gasto sigue Loose. Borra también el comprobante adjunto si tiene.</summary>
    Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default);
}
