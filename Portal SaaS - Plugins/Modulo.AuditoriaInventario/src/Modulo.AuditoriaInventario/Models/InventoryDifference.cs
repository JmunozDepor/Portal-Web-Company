namespace Modulo.AuditoriaInventario.Models;

/// <summary>
/// Resultado del motor de diferencias (capturado vs. congelado), MATERIALIZADO --
/// no se calcula al vuelo en el reporte, para no recalcular sobre inventarios
/// históricos cada vez que se abre una pantalla.
/// </summary>
public class InventoryDifference
{
    public long Id { get; set; }

    public required Guid SessionId { get; set; }

    public required long SnapshotId { get; set; }

    public long? SectorId { get; set; }

    public required string Barcode { get; set; }

    public int CapturedQuantity { get; set; }

    public int FrozenQuantity { get; set; }

    public int QuantityDiff { get; set; }

    public decimal AmountDiff { get; set; }

    public DateTimeOffset CalculatedAt { get; set; } = DateTimeOffset.UtcNow;
}
