using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulo.Wms.Data;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public sealed class WmsSlshStageParser : BackgroundService
{
    private static readonly TimeSpan IntervaloCiclo = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WmsSlshStageParser> _logger;

    public WmsSlshStageParser(IServiceScopeFactory scopeFactory, ILogger<WmsSlshStageParser> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EjecutarCicloAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado ejecutando el ciclo de aplanado SLSH");
            }

            try
            {
                await Task.Delay(IntervaloCiclo, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task EjecutarCicloAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();

        var pendientes = await contexto.WmsOracleInboundStages
            .Where(s => s.Estado == WmsInboundEstado.Pendiente && s.TipoDoc == "SLSH")
            .ToListAsync(cancellationToken);

        foreach (var entry in pendientes)
        {
            try
            {
                var filas = WmsSlshXmlParser.Parse(entry.Contenido);

                if (filas.Count == 0)
                {
                    entry.Estado = WmsInboundEstado.ErrorEstructura;
                    entry.MensajeError = "No se encontraron nodos ob_stop válidos.";
                }
                else
                {
                    foreach (var fila in filas)
                    {
                        fila.ParentId = entry.Id;
                        contexto.WmsOracleStageSlsh.Add(fila);
                    }
                    entry.Estado = WmsInboundEstado.Aplanado;
                }
            }
            catch (Exception ex)
            {
                entry.Estado = WmsInboundEstado.ErrorStaging;
                entry.MensajeError = ex.Message;
                _logger.LogError(ex, "Error aplanando el archivo {NombreArchivo}", entry.NombreArchivo);
            }
            finally
            {
                entry.ProcessedAt = DateTimeOffset.UtcNow;
            }
        }

        if (pendientes.Count > 0)
        {
            await contexto.SaveChangesAsync(cancellationToken);
        }
    }
}
