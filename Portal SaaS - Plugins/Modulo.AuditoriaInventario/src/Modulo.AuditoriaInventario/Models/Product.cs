namespace Modulo.AuditoriaInventario.Models;

/// <summary>
/// Maestro de producto PROPIO del módulo -- deliberadamente no reutiliza
/// Modulo.Inventario.ProductMaster (decisión explícita del dueño del proyecto: este
/// módulo es autocontenido, ver PENDIENTE.md). Se carga manual o vía integración
/// (Source) y es la fuente que la PWA de captura sincroniza a su IndexedDB local.
/// No lleva precio/costo -- la valorización de diferencias usa
/// FrozenInventoryLine.UnitCost, tomado del inventario congelado al momento del
/// corte, no de este maestro.
/// </summary>
public class Product
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required string Barcode { get; set; }

    public required string ProductCode { get; set; }

    public string? Description { get; set; }

    public string? Brand { get; set; }

    public string? Line { get; set; }

    /// <summary>MANUAL | INTEGRACION.</summary>
    public string Source { get; set; } = "MANUAL";

    public DateTimeOffset LoadedAt { get; set; } = DateTimeOffset.UtcNow;
}
