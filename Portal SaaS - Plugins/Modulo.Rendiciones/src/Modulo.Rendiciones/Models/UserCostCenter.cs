namespace Modulo.Rendiciones.Models;

/// <summary>
/// Centro de costo (dimensión 1 de SAP) que un colaborador puede usar al declarar
/// gastos/fondos. Snapshot de código+nombre al momento de asignarlo -- el catálogo
/// maestro sigue siendo SAP, esto es solo la lista de "cuáles le permito a este
/// usuario". Portado de CentroCostoUsuario (PortalSAP_v2).
/// </summary>
public class UserCostCenter
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required Guid UserId { get; set; }

    public required string CostCenterCode { get; set; }

    public string? CostCenterName { get; set; }
}
