using Servicios.Common.Contratos;

namespace Servicios.Common.Logging;

/// <summary>
/// Se usa cuando Logging:Enabled = false. No escribe nada a disco -- el servicio sigue
/// logueando a la consola/Event Log vía ILogger de Microsoft.Extensions.Logging como
/// siempre, esto solo apaga el log local propio de la familia Servicios SAP.
/// </summary>
public sealed class NullLogSink : ILogSink
{
    public Task WriteAsync(LogEntry entry, CancellationToken ct) => Task.CompletedTask;

    public Task PurgeOlderThanAsync(int retentionDays, CancellationToken ct) => Task.CompletedTask;
}
