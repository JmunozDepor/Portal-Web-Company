namespace Modulo.Rendiciones.Models;

/// <summary>
/// Anticipo entregado a un colaborador (viaje, caja chica). Portado de
/// FondoPorRendir (PortalSAP_v2).
/// </summary>
public class ExpenseFund
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required Guid UserId { get; set; }

    /// <summary>Snapshot del código/nombre de centro de costo (dimensión 1 de SAP) al momento de crear el fondo -- no es un FK local.</summary>
    public string? CostCenterCode { get; set; }

    public string? CostCenterName { get; set; }

    public required string Currency { get; set; }

    public decimal Amount { get; set; }

    public DateTimeOffset DeliveredAt { get; set; }

    public DateTimeOffset? SettlementDueAt { get; set; }

    /// <summary>Open | Settled | Overdue.</summary>
    public string Status { get; set; } = "Open";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
