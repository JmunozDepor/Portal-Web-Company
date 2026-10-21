namespace Modulo.Despachos.Models;

public class VehicleType : AuditedEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? CapacityM3 { get; set; }
    public decimal? CapacityKg { get; set; }
    public bool IsActive { get; set; } = true;
}
