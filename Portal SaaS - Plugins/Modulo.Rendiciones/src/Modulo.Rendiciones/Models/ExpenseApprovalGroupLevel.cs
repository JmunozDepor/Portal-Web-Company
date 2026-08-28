namespace Modulo.Rendiciones.Models;

/// <summary>
/// Cadena de aprobadores del grupo. Nivel 1 = jefe, Nivel 2 = gerente final. Nunca en
/// paralelo. Portado de GrupoAprobacionRendicionNivel (PortalSAP_v2).
/// </summary>
public class ExpenseApprovalGroupLevel
{
    public long Id { get; set; }

    public required long ExpenseApprovalGroupId { get; set; }

    public ExpenseApprovalGroup? ExpenseApprovalGroup { get; set; }

    public required int Level { get; set; }

    public required Guid UserId { get; set; }
}
