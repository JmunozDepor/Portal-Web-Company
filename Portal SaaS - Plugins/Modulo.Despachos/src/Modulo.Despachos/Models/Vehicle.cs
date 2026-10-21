namespace Modulo.Despachos.Models;

/// <summary>Vehículo; carros/remolques son vehículos con IsTrailer=true.</summary>
public class Vehicle : AuditedEntity
{
    /// <summary>Patente normalizada (mayúsculas, sin guiones ni espacios).</summary>
    public string Plate { get; set; } = "";
    public long? VehicleTypeId { get; set; }
    public long? CarrierId { get; set; }
    /// <summary>Capacidad propia; si es null se usa la del tipo de vehículo.</summary>
    public decimal? CapacityM3 { get; set; }
    public bool IsTrailer { get; set; }
    public bool IsActive { get; set; } = true;
}
