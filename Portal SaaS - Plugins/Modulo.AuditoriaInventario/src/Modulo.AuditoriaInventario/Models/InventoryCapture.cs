namespace Modulo.AuditoriaInventario.Models;

/// <summary>
/// Detalle de captura (un scan, o la acumulación de scans repetidos del mismo
/// código en el mismo sector/sesión). Id GUID generado en el cliente -- ver el
/// comentario de InventorySession.Id, mismo motivo.
/// </summary>
public class InventoryCapture
{
    public Guid Id { get; set; }

    public required Guid SessionId { get; set; }

    public required long SectorId { get; set; }

    public required string Barcode { get; set; }

    public string? ProductCode { get; set; }

    public int Quantity { get; set; }

    /// <summary>
    /// Null cuando InventorySession.ValidateAgainstMaster es false (no se evaluó).
    /// true/false solo cuando sí se validó contra Product.
    /// </summary>
    public bool? InMaster { get; set; }

    public required long CapturedByUserId { get; set; }

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Null hasta que el backend confirma la recepción del batch subido por la PWA.</summary>
    public DateTimeOffset? SyncedAt { get; set; }
}
