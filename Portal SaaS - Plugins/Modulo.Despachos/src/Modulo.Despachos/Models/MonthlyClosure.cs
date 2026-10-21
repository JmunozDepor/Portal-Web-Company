namespace Modulo.Despachos.Models;

/// <summary>
/// Cierre mensual de costos. Una fila por compañía y período (yyyy-MM, por dispatch_date de los manifiestos);
/// reabrir y volver a cerrar actualiza la misma fila.
/// </summary>
public class MonthlyClosure : AuditedEntity
{
    /// <summary>Período "yyyy-MM".</summary>
    public string Period { get; set; } = "";
    public string Status { get; set; } = ClosureStatuses.Open;
    public DateTime? ClosedAt { get; set; }
    public Guid? ClosedBy { get; set; }
    public DateTime? ReopenedAt { get; set; }
    public Guid? ReopenedBy { get; set; }
    public string? ReopenReason { get; set; }

    /// <summary>Manifiestos (no anulados) y costo total congelados en el último cierre.</summary>
    public int ManifestCount { get; set; }
    public decimal TotalCost { get; set; }
}
