namespace Modulo.Wms.Models;

/// <summary>
/// Heartbeat de los processors de WmsSapIntegration.Service -- una fila por
/// (CompanyId, ProcessorKey), sobreescrita en cada ciclo, portado de
/// INT_SERVICE_HEARTBEAT (WMS_Suite). Responde "¿el servicio sigue corriendo?"
/// independiente de si hay documentos pendientes.
///
/// ConfigSourceEffective es nuevo (no existía en la tabla original) -- cierra el
/// footgun documentado en ARQUITECTURA.md: la UI real de WMS_Suite no tenía forma de
/// avisar que un cambio en WmsServiceConfig no tenía efecto porque la instancia real
/// seguía en modo "File". El servicio reporta acá su ConfigSource real en cada ciclo,
/// igual que ya reporta Status.
/// </summary>
public sealed class WmsServiceHeartbeat
{
    public Guid CompanyId { get; set; }
    public string ProcessorKey { get; set; } = null!;
    public DateTimeOffset? LastRunAt { get; set; }
    public string? Status { get; set; }
    public string? LastError { get; set; }
    public string? ConfigSourceEffective { get; set; }
}
