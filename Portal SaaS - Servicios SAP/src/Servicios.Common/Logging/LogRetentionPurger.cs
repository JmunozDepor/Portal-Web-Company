using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Servicios.Common.Contratos;

namespace Servicios.Common.Logging;

/// <summary>
/// Purga el histórico local una vez al día -- nunca en cada ciclo corto del worker
/// principal (ver regla dura en CLAUDE.md). Se registra como IHostedService propio en
/// cada servicio, independiente del worker de negocio.
/// </summary>
public sealed class LogRetentionPurger
{
    private readonly ILogSink _sink;
    private readonly LogSinkOptions _options;
    private readonly ILogger<LogRetentionPurger> _logger;

    public LogRetentionPurger(ILogSink sink, IOptions<LogSinkOptions> options, ILogger<LogRetentionPurger> logger)
    {
        _sink = sink;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PurgeAsync(CancellationToken ct)
    {
        try
        {
            await _sink.PurgeOlderThanAsync(_options.RetentionDays, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló la purga de retención de logs (RetentionDays={RetentionDays})", _options.RetentionDays);
        }
    }
}
