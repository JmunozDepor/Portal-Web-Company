namespace Modulo.Despachos.Models;

/// <summary>Valor base del tarifario. Condición nula = "cualquiera". Se mapea en el DbContext (Task 6).</summary>
public class RateCard : AuditedEntity
{
    public long? CarrierId { get; set; }
    public long? ShiftId { get; set; }
    public string? RegionCode { get; set; }
    public long? ZoneId { get; set; }
    public long? CommuneId { get; set; }
    public long? VehicleTypeId { get; set; }
    public decimal? M3From { get; set; }
    public decimal? M3To { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    /// <summary>fixed_per_trip | per_m3 | per_km | per_package | per_document | no_charge.</summary>
    public string CalculationMethod { get; set; } = "fixed_per_trip";
    public decimal Amount { get; set; }
    public decimal? MinAmount { get; set; }
    public string? ParametersJson { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
}
