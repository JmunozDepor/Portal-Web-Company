namespace Modulo.AuditoriaInventario.Models;

/// <summary>
/// Dominio de ajuste propuesto sobre una diferencia detectada. Aprobación manual
/// OBLIGATORIA antes de que algo llegue a SapAdjustmentQueueItem -- decisión
/// explícita del dueño del proyecto, nunca envío automático a SAP.
/// </summary>
public class InventoryAdjustment
{
    public long Id { get; set; }

    public required long DifferenceId { get; set; }

    /// <summary>PROPOSED -> APPROVED -> QUEUED -> APPLIED | REJECTED.</summary>
    public string Status { get; set; } = "PROPOSED";

    public required Guid ProposedByUserId { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public DateTimeOffset ProposedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ApprovedAt { get; set; }
}
