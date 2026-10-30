using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public interface IExpensePolicyService
{
    Task<IReadOnlyList<ExpensePolicy>> ListAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Crea una política para (companyId, expenseTypeId, cityId, policyProfileId) -- cityId y
    /// policyProfileId son opcionales (null = "cualquiera"). Si period es
    /// ExpensePolicyPeriod.Weekly, cityId se fuerza a null sin importar lo que llegue (un
    /// tope semanal no tiene sentido acotado a una ciudad puntual). Lanza
    /// InvalidOperationException si ya existe una política con exactamente esa combinación.
    /// </summary>
    Task<long> CreateAsync(Guid companyId, long expenseTypeId, long? cityId, long? policyProfileId, string period,
        decimal? maxAmount, CancellationToken ct = default);

    /// <summary>
    /// Solo MaxAmount/IsActive son editables -- CityId/PolicyProfileId/Period quedan fijos al
    /// crear la política (si hace falta otra combinación, se crea una política nueva).
    /// </summary>
    Task UpdateAsync(long id, Guid companyId, decimal? maxAmount, bool isActive, CancellationToken ct = default);

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

    /// <summary>
    /// Resuelve cuál fila de ExpensePolicy aplica a (expenseTypeId, cityId, userProfileId,
    /// period) para una compañía, según el orden de especificidad del spec (Decisión 4):
    /// 1. CityId = cityId Y PolicyProfileId = userProfileId (si el usuario tiene perfil).
    /// 2. CityId = cityId Y PolicyProfileId = null.
    /// 3. CityId = null Y PolicyProfileId = userProfileId (si el usuario tiene perfil).
    /// 4. CityId = null Y PolicyProfileId = null.
    /// Solo considera políticas activas (IsActive). Una fila con PolicyProfileId no-null
    /// nunca matchea un usuario sin perfil (userProfileId == null). Devuelve null si
    /// ninguna política aplica.
    /// </summary>
    Task<ExpensePolicy?> FindApplicablePolicyAsync(Guid companyId, long expenseTypeId, long? cityId,
        long? userProfileId, string period, CancellationToken ct = default);
}
