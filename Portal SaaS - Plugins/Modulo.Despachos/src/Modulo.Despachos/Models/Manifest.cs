namespace Modulo.Despachos.Models;

/// <summary>
/// Cabecera del manifiesto de despacho. Los campos "snapshot" (carrier_tax_id, driver_*, plate...)
/// se llenan al despachar con lo que realmente salió (Res. Ex. SII N°154): los maestros pueden cambiar
/// después sin alterar el manifiesto. Todas las fechas/horas absolutas son UTC.
/// </summary>
public class Manifest
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }

    /// <summary>Correlativo visible por compañía (índice único company_id + number).</summary>
    public int Number { get; set; }
    public string Status { get; set; } = ManifestStatuses.Draft;

    public DateOnly DispatchDate { get; set; }
    public long? ShiftId { get; set; }
    public long OriginId { get; set; }
    public long CarrierId { get; set; }
    public long VehicleId { get; set; }
    public long? TrailerId { get; set; }
    public long DriverId { get; set; }

    // Snapshot Res.154 (texto, no FK) tomado al despachar y renovado al cambiar vehículo/chofer.
    public string? CarrierTaxId { get; set; }
    public string? DriverTaxId { get; set; }
    public string? DriverName { get; set; }
    public string? Plate { get; set; }
    public string? TrailerPlate { get; set; }
    public string? OriginAddress { get; set; }
    public string? OriginCommune { get; set; }

    public DateTime? DepartedAt { get; set; }
    public TimeOnly? PlannedDepartureTime { get; set; }
    public DateOnly? EstimatedArrivalDate { get; set; }
    public TimeOnly? EstimatedArrivalTime { get; set; }
    public DateTime? ReturnedAt { get; set; }

    public int? OdometerStart { get; set; }
    public int? OdometerEnd { get; set; }
    public string? ServiceOrder { get; set; }
    public string? Remarks { get; set; }

    // Costo.
    public decimal? CostCalculated { get; set; }
    public long? RateCardId { get; set; }
    public decimal? CostOverride { get; set; }
    public string? CostOverrideReason { get; set; }
    public decimal? CostFinal { get; set; }
    public DateTime? CostFrozenAt { get; set; }
    public string CostStatus { get; set; } = CostStatuses.NoRate;

    // Totales snapshot (se fijan al despachar).
    public int TotalDocuments { get; set; }
    public decimal TotalUnits { get; set; }
    public int TotalPackages { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public List<ManifestDocument> Documents { get; set; } = new();
    public List<ManifestCostLine> CostLines { get; set; } = new();
    public List<ManifestEvent> Events { get; set; } = new();
}
