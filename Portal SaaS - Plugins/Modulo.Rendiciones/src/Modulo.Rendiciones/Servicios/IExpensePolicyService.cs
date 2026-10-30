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
    /// Evalúa un gasto contra la política PerDocument aplicable (resuelta vía
    /// FindApplicablePolicyAsync, según ciudad y perfil asignado al usuario) y busca un
    /// posible duplicado (mismo número de documento + RUT/tax id proveedor, del mismo
    /// usuario, en otro gasto). No persiste nada -- IExpenseService decide qué hacer con
    /// el resultado. cityId/quantity vienen del gasto (quantity ya resuelto a 1 por el
    /// llamador si el tipo no la requiere). expenseIdToExclude: al editar, no comparar el
    /// gasto contra sí mismo. Solo considera políticas PerDocument -- el acumulado Weekly
    /// es una advertencia aparte (Task 8), nunca toca ReimbursementAmount acá.
    /// </summary>
    Task<ExpenseValidationResult> ValidateAsync(Guid companyId, Guid userId, long? expenseTypeId, long? cityId,
        decimal amount, decimal quantity, string? documentNumber, string? supplierTaxId, long? expenseIdToExclude,
        CancellationToken ct = default);

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
