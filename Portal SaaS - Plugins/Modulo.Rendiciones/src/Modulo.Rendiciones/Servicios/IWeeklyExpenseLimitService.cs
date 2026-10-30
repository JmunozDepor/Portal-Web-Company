namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Advertencia semanal agregada (Task 8): además del tope por documento (ver
/// IExpensePolicyService, Period=PerDocument), una ExpensePolicy puede tener
/// Period=Weekly -- un tope acumulado por semana calendario para un ExpenseType,
/// aplicable a un perfil de política o a cualquiera. Este servicio es de solo lectura
/// y puramente informativo: no modifica ReimbursementAmount de ningún
/// ExpenseReportLine, solo suma lo ya calculado por Task 7 y avisa si se pasó.
/// </summary>
public interface IWeeklyExpenseLimitService
{
    /// <summary>
    /// Devuelve una advertencia por cada ExpenseType con una política Weekly aplicable al
    /// perfil del usuario (o sin perfil específico) cuya suma de ReimbursementAmount en la
    /// semana (lunes a domingo) que contiene <paramref name="anyDateInWeek"/> supera el
    /// MaxAmount de la política. Lista vacía si el usuario no tiene perfil con política
    /// semanal, o si ninguna suma excede su tope.
    /// </summary>
    Task<IReadOnlyList<WeeklyLimitWarning>> GetWarningsAsync(Guid companyId, Guid userId, DateOnly anyDateInWeek,
        CancellationToken ct = default);
}
