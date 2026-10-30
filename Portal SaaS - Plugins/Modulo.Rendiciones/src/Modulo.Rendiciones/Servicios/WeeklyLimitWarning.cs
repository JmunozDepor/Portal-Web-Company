namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Advertencia de que la suma de ReimbursementAmount de un usuario para un ExpenseType,
/// dentro de una semana calendario (lunes a domingo), superó el MaxAmount de la
/// ExpensePolicy Period=Weekly aplicable (Task 8). Puramente informativa -- no bloquea
/// ni modifica ningún ExpenseReportLine, la calcula IWeeklyExpenseLimitService al vuelo.
/// </summary>
public sealed record WeeklyLimitWarning(
    string ExpenseTypeName,
    DateOnly WeekStart,
    DateOnly WeekEnd,
    decimal TotalReimbursed,
    decimal MaxAmount,
    decimal Excess);
