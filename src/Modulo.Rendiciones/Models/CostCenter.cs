namespace Modulo.Rendiciones.Models;

public class CostCenter
{
    public long Id { get; set; }
    public required Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public CatalogEntrySource Source { get; set; } = CatalogEntrySource.Manual;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
