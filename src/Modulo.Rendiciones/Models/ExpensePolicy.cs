namespace Modulo.Rendiciones.Models;

/// <summary>
/// Tope de monto por tipo de gasto -- bloqueante (impide guardar) o solo advertencia.
/// Un tipo de gasto tiene a lo sumo una política por compañía. Portado de
/// PoliticaGasto (PortalSAP_v2).
/// </summary>
public class ExpensePolicy
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required long ExpenseTypeId { get; set; }

    public ExpenseType? ExpenseType { get; set; }

    /// <summary>Null = sin tope de monto.</summary>
    public decimal? MaxAmount { get; set; }

    public bool IsBlocking { get; set; }

    public bool IsActive { get; set; } = true;
}
