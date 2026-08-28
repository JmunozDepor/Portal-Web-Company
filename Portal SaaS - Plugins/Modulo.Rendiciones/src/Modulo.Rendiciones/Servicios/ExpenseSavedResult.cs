namespace Modulo.Rendiciones.Servicios;

/// <summary>Resultado de IExpenseService.CreateAsync: el id del gasto nuevo + advertencias no bloqueantes de política/duplicado.</summary>
public sealed record ExpenseSavedResult(long Id, IReadOnlyList<string> Warnings);
