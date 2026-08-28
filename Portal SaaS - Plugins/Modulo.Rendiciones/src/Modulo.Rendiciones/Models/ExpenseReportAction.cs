namespace Modulo.Rendiciones.Models;

/// <summary>
/// Historial de aprobar/rechazar -- fuente de verdad de la bitácora de aprobación.
/// Portado de RendicionGastoAccion (PortalSAP_v2).
/// </summary>
public class ExpenseReportAction
{
    public long Id { get; set; }

    public required long ExpenseReportId { get; set; }

    public required int Level { get; set; }

    public required Guid UserId { get; set; }

    /// <summary>Approved | Rejected.</summary>
    public required string Decision { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
