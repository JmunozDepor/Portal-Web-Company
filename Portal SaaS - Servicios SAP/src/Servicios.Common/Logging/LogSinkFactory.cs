using Microsoft.Extensions.Options;
using Servicios.Common.Contratos;

namespace Servicios.Common.Logging;

/// <summary>
/// Resuelve la implementación de ILogSink según LogSinkOptions.LocalSink. Los valores
/// "SqlServer"/"PostgreSql" están documentados acá a propósito, con
/// NotImplementedException explícito -- el día que un servicio necesite loguear en base
/// de datos, esto es lo único que cambia (mismo criterio ya usado en PortalSAP_v2 para
/// marcar pendientes sin fingir que existen).
/// </summary>
public static class LogSinkFactory
{
    public static ILogSink Create(IOptions<LogSinkOptions> options, string prefijoServicio)
    {
        if (!options.Value.Enabled)
        {
            return new NullLogSink();
        }

        return options.Value.LocalSink switch
        {
            "File" => new FileLogSink(options, prefijoServicio),
            "SqlServer" => throw new NotImplementedException(
                "Logging:LocalSink=SqlServer todavía no está implementado -- ver Servicios.Common/Logging/LogSinkFactory.cs."),
            "PostgreSql" => throw new NotImplementedException(
                "Logging:LocalSink=PostgreSql todavía no está implementado -- ver Servicios.Common/Logging/LogSinkFactory.cs."),
            var otro => throw new InvalidOperationException(
                $"Logging:LocalSink '{otro}' desconocido. Valores válidos: File, SqlServer, PostgreSql.")
        };
    }
}
