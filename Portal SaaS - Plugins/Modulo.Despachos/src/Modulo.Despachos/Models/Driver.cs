namespace Modulo.Despachos.Models;

public class Driver : AuditedEntity
{
    /// <summary>RUT normalizado ("12345678-5").</summary>
    public string TaxId { get; set; } = "";
    public string FullName { get; set; } = "";
    public long? CarrierId { get; set; }
    public string? LicenseClass { get; set; }
    public DateOnly? LicenseExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
}
