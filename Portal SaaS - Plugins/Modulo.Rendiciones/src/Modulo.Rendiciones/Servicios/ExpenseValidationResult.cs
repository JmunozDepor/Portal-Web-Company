namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Resultado de validar un gasto contra la política de su tipo y contra posibles
/// duplicados. ReimbursementAmount es el monto efectivamente reembolsable, ya acotado al
/// tope de la política PerDocument aplicable (si la hay) -- nunca mayor al Amount pedido.
/// Ya NO bloquea el guardado: superar el tope siempre es solo una advertencia (removido
/// 2026-10-29, ver ExpensePolicyService.ValidateAsync). Warnings son mensajes que se
/// muestran igual, guardado o no.
/// </summary>
public sealed record ExpenseValidationResult(decimal ReimbursementAmount, IReadOnlyList<string> Warnings);
