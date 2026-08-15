namespace PortalSaas.Data.Entities.Integraciones;

public enum IntegrationRunResultado { Exito, Error, Parcial }
public enum IntegrationRunDisparadoPor { Programado, Manual }

public class IntegrationRunLog
{
    public long Id { get; set; }
    public Guid IntegrationDefinitionId { get; set; }
    public DateTimeOffset IniciadoEn { get; set; }
    public DateTimeOffset? FinalizadoEn { get; set; }
    public IntegrationRunResultado Resultado { get; set; }
    public int RegistrosProcesados { get; set; }
    public int RegistrosConError { get; set; }
    public string? DetalleError { get; set; }
    public IntegrationRunDisparadoPor DisparadoPor { get; set; }
}
