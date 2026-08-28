namespace PortalSaas.Data.Entities.Integraciones;

public class IntegrationFieldMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IntegrationDefinitionId { get; set; }
    public string CampoLocal { get; set; } = string.Empty;
    public string CampoExterno { get; set; } = string.Empty;
    public string? Transformacion { get; set; }
    public bool Obligatorio { get; set; }

    public IntegrationDefinition IntegrationDefinition { get; set; } = null!;
}
