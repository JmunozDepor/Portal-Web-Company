namespace PortalSaas.Data.Entities;

public class ApiClientCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string ApiKeyHash { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
}
