namespace Modulo.Rendiciones.Models;

/// <summary>
/// Período al que aplica el tope de una ExpensePolicy (Task 5). PerDocument es el
/// comportamiento histórico (tope por gasto individual); Weekly acumula contra un tope
/// semanal -- el cálculo real de acumulación es Task 7, fuera de alcance acá. Una política
/// Weekly no tiene sentido acotada a una ciudad puntual (un viaje de una semana cruza
/// ciudades), por eso ExpensePolicyService.CreateAsync fuerza CityId a null cuando
/// Period == Weekly, sin importar lo que llegue.
/// </summary>
public static class ExpensePolicyPeriod
{
    public const string PerDocument = "PerDocument";
    public const string Weekly = "Weekly";
}
