namespace Modulo.AuditoriaInventario.Models;

/// <summary>
/// Tabla de INTERFAZ pura hacia SAP -- solo se escribe acá cuando
/// InventoryAdjustment.Status pasa a APPROVED. Este módulo nunca llama a SAP
/// directamente; quién consume esta cola (proceso batch, Servicios SAP, etc.) queda
/// fuera del alcance de este proyecto (decisión explícita del dueño del proyecto).
/// </summary>
public class SapAdjustmentQueueItem
{
    public long Id { get; set; }

    public required long AdjustmentId { get; set; }

    public required string SapCompanyCode { get; set; }

    public required string SapWarehouseCode { get; set; }

    public required string SapMaterialCode { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>READY | TAKEN | APPLIED | ERROR.</summary>
    public string Status { get; set; } = "READY";

    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ProcessedAt { get; set; }

    public string? SapDocumentNumber { get; set; }

    public string? ErrorMessage { get; set; }
}
