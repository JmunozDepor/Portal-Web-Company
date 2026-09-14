namespace Modulo.AuditoriaInventario.Models;

/// <summary>
/// Resultado de una carga de congelado (Excel) contra un Nro. de Inventario --
/// hoy el congelado se extrae del punto de venta y se sube manualmente desde el
/// portal. Cabecera del lote; el detalle línea a línea vive en FrozenInventoryLine.
/// </summary>
public class FrozenInventorySnapshot
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required long BranchId { get; set; }

    public required string InventoryNumber { get; set; }

    public DateTimeOffset LoadedAt { get; set; } = DateTimeOffset.UtcNow;

    public required Guid LoadedByUserId { get; set; }

    public required string FileName { get; set; }
}
