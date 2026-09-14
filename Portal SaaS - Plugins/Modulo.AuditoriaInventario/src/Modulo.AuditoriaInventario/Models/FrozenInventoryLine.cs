namespace Modulo.AuditoriaInventario.Models;

/// <summary>
/// Línea del congelado. Lleva UnitCost porque la valorización de diferencias
/// (InventoryDifference.AmountDiff) usa el costo vigente al momento del corte de
/// inventario, no un precio actual del maestro.
/// </summary>
public class FrozenInventoryLine
{
    public long Id { get; set; }

    public required long SnapshotId { get; set; }

    public required string Barcode { get; set; }

    public string? ProductCode { get; set; }

    public int Quantity { get; set; }

    public decimal? UnitCost { get; set; }
}
