namespace Modulo.Rendiciones.Models;

/// <summary>
/// Cabecera de una rendición de gastos. El centro de costo es uno solo por rendición,
/// no por línea (decisión de negocio heredada del original). La resolución de
/// aprobación (grupo/nivel) es fase siguiente -- Status por ahora solo transiciona
/// Draft -> Pending. Portado de RendicionGasto (PortalSAP_v2).
/// </summary>
public class ExpenseReport
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required Guid UserId { get; set; }

    public long? ExpenseFundId { get; set; }

    /// <summary>Snapshot del centro de costo (dimensión 1 de SAP) para esta rendición.</summary>
    public string? CostCenterCode { get; set; }

    public string? CostCenterName { get; set; }

    public int Round { get; set; } = 1;

    public long? ExpenseApprovalGroupId { get; set; }

    /// <summary>Nivel de ExpenseApprovalGroupLevel que debe resolver a continuación. Null si no está Pending.</summary>
    public int? CurrentLevel { get; set; }

    /// <summary>Draft | Pending | Approved | Rejected.</summary>
    public string Status { get; set; } = "Draft";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public List<ExpenseReportLine> Lines { get; set; } = new();
}
