namespace Servicios.Common.Contratos;

public enum NivelLog
{
    Info,
    Advertencia,
    Error
}

public sealed class LogEntry
{
    public required DateTimeOffset FechaHora { get; init; }

    public required NivelLog Nivel { get; init; }

    /// <summary>Código de la compañía a la que pertenece este evento (nunca null en un log multi-empresa -- ver CLAUDE.md).</summary>
    public required string CompanyCode { get; init; }

    public required string Mensaje { get; init; }

    /// <summary>Detalle libre (excepción serializada, payload, respuesta de SAP) -- opcional.</summary>
    public string? Detalle { get; init; }
}

/// <summary>
/// Destino del log local de un servicio de esta familia. Hoy la única implementación es
/// FileLogSink (archivo plano, JSON-lines, un archivo por día). El contrato ya deja lugar
/// para un sink de base de datos (SqlServer/PostgreSql) sin tocar a los consumidores --
/// ver Logging:LocalSink en appsettings.json.
/// </summary>
public interface ILogSink
{
    Task WriteAsync(LogEntry entry, CancellationToken ct);

    /// <summary>Borra el histórico más antiguo que retentionDays. Se ejecuta una vez al día, nunca en cada ciclo corto del worker.</summary>
    Task PurgeOlderThanAsync(int retentionDays, CancellationToken ct);
}
