namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Resultado de validar un gasto contra la política de su tipo y contra posibles
/// duplicados. Blocked=true significa que el tope de una política bloqueante se
/// superó -- el llamador debe rechazar el guardado. Warnings son mensajes que se
/// muestran igual, guardado o no.
/// </summary>
public sealed record ExpenseValidationResult(bool Blocked, IReadOnlyList<string> Warnings);
