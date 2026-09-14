namespace Modulo.AuditoriaInventario.Models;

/// <summary>
/// Catálogo de sectores de captura (Sala de Venta, Bodega, etc.) -- reemplaza
/// deliberadamente el texto libre que tenía la versión MAUI (NroUbicacion), para que
/// el reporte de diferencias pueda agrupar por sector de forma confiable.
/// </summary>
public class InventorySector
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    /// <summary>Null = catálogo global de la compañía; con valor = específico de esa sucursal.</summary>
    public long? BranchId { get; set; }

    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;
}
