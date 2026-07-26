namespace Modulo.Rendiciones.Models;

/// <summary>
/// Quién puede solicitar rendiciones bajo este grupo (se asume 1 grupo por usuario).
/// Portado de GrupoAprobacionRendicionMiembro (PortalSAP_v2) -- ganó un `Id` propio
/// que el original no tenía (PK compuesta), por la regla dura de esta plataforma de
/// que toda tabla lleva `id` surrogate, ver docs/01-CONVENCION-NOMBRES-BD.md §3.
/// </summary>
public class ExpenseApprovalGroupMember
{
    public long Id { get; set; }

    public required long ExpenseApprovalGroupId { get; set; }

    public ExpenseApprovalGroup? ExpenseApprovalGroup { get; set; }

    public required Guid UserId { get; set; }
}
