using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public interface IExpensePolicyService
{
    Task<IReadOnlyList<ExpensePolicy>> ListAsync(Guid companyId, CancellationToken ct = default);

    Task<long> CreateAsync(Guid companyId, long expenseTypeId, decimal? maxAmount, bool isBlocking, CancellationToken ct = default);

    Task UpdateAsync(long id, Guid companyId, decimal? maxAmount, bool isBlocking, bool isActive, CancellationToken ct = default);

    Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Evalúa un gasto contra la política de su tipo (si existe y está activa) y busca
    /// un posible duplicado (mismo número de documento + RUT/tax id proveedor, del
    /// mismo usuario, en otro gasto). No persiste nada -- IExpenseService decide qué
    /// hacer con el resultado. expenseIdToExclude: al editar, no comparar el gasto
    /// contra sí mismo.
    /// </summary>
    Task<ExpenseValidationResult> ValidateAsync(Guid companyId, Guid userId, long? expenseTypeId, decimal amount,
        string? documentNumber, string? supplierTaxId, long? expenseIdToExclude, CancellationToken ct = default);
}
