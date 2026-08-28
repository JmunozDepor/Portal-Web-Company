namespace Modulo.Rendiciones.Models;

/// <summary>
/// Equipo + cadena de aprobadores de Rendiciones. Réplica independiente del patrón de
/// grupos de aprobación de Compras -- sin dependencia de código hacia ese módulo (cada
/// plugin es aislado). Portado de GrupoAprobacionRendicion (PortalSAP_v2).
/// </summary>
public class ExpenseApprovalGroup
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;
}
