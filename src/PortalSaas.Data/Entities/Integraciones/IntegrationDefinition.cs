namespace PortalSaas.Data.Entities.Integraciones;

public enum IntegrationConectorTipo { Sap, Rest, Archivo, WmsCloud, Sql }
public enum IntegrationDireccion { Subida, Bajada, Ambas }

public class IntegrationDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string ModuloOrigen { get; set; } = string.Empty;
    public string EntidadNegocio { get; set; } = string.Empty;
    public IntegrationConectorTipo ConectorTipo { get; set; }
    public string ConectorConfigCifrado { get; set; } = string.Empty;
    public IntegrationDireccion Direccion { get; set; }
    public bool Activo { get; set; } = true;
    public string? ProgramacionCron { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }

    public ICollection<IntegrationFieldMapping> Mapeos { get; set; } = new List<IntegrationFieldMapping>();
}
