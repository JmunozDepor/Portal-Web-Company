namespace Modulo.Despachos.Models;

/// <summary>Bitácora append-only del manifiesto: todo cambio de estado, documento, vehículo, costo o impresión.</summary>
public class ManifestEvent
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ManifestId { get; set; }
    public Guid? ManifestDocumentId { get; set; }
    public string EventType { get; set; } = "";
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? PayloadJson { get; set; }
    public Guid? UserId { get; set; }
    public DateTime OccurredAt { get; set; }
}
