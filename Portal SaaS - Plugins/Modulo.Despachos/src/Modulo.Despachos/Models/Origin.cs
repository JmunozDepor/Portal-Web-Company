namespace Modulo.Despachos.Models;

/// <summary>Origen del traslado (Res.154 DirOrigen/CmnaOrigen).</summary>
public class Origin : AuditedEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public long? CommuneId { get; set; }
    public string? SapWarehouseCode { get; set; }
    public bool IsActive { get; set; } = true;
}
