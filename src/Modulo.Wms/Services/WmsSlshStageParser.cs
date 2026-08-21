using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos;

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
        List<PortalSaas.Abstractions.Modelos.ModuleCompanyDto> companias;
        using (var scope = _scopeFactory.CreateScope())
        {
            var externalDb = scope.ServiceProvider.GetRequiredService<IExternalDatabaseConnectionService>();
            companias = (await externalDb.ListActiveCompanyIdsAsync("Wms", cancellationToken)).ToList();
        }

        foreach (var compania in companias)
        {
            await ProcesarCompaniaAsync(compania.CompanyId, cancellationToken);
        }
    }

    private async Task ProcesarCompaniaAsync(Guid companyId, CancellationToken cancellationToken)
    {
        // El override de compañía ambiente debe fijarse ANTES de resolver WmsDbContext --
        // su fábrica (ver ModuloWms.cs) depende de ICurrentCompanyAccessor.HasCompany/.CompanyId,
        // que sin HttpContext (BackgroundService) no tiene de dónde más leer la compañía. Mismo
        // patrón que IntegrationSyncHostedService.EjecutarCicloAsync.
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var heartbeat = scope.ServiceProvider.GetRequiredService<IWmsServiceHeartbeatRecorder>();

        try
        {
            var pendientes = await contexto.WmsOracleInboundStages
                .Where(s => s.Estado == WmsInboundEstado.Pendiente && s.TipoDoc == "SLSH" && s.Formato == WmsInboundFormato.Xml)
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

                try
                {
                    await contexto.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error guardando el resultado del aplanado para el archivo {NombreArchivo}", entry.NombreArchivo);
                    await RegistrarFalloDePersistenciaAsync(companyId, entry.Id, ex, cancellationToken);
                }
            }

            await heartbeat.RecordAsync(companyId, "Wms.SlshStageParser", "OK", cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await heartbeat.RecordAsync(companyId, "Wms.SlshStageParser", "ERROR", ex.Message, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Registra un fallo de guardado en una escritura separada, usando un scope/contexto nuevo
    /// para no arrastrar estado corrupto o parcialmente rastreado del contexto que acaba de fallar.
    /// Incrementa Intentos y, al alcanzar el umbral, deja la fila en estado terminal ErrorStaging
    /// para que deje de reintentarse indefinidamente.
    /// </summary>
    private async Task RegistrarFalloDePersistenciaAsync(Guid companyId, long entryId, Exception fallo, CancellationToken cancellationToken)
    {
        const int MaxIntentos = 3;

        try
        {
            using var recoveryScope = _scopeFactory.CreateScope();
            recoveryScope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
            var recoveryContexto = recoveryScope.ServiceProvider.GetRequiredService<WmsDbContext>();

            var entryFresco = await recoveryContexto.WmsOracleInboundStages
                .FirstOrDefaultAsync(s => s.Id == entryId, cancellationToken);

            if (entryFresco is null)
            {
                return;
            }

            entryFresco.Intentos++;
            entryFresco.ProcessedAt = DateTimeOffset.UtcNow;

            if (entryFresco.Intentos >= MaxIntentos)
            {
                entryFresco.Estado = WmsInboundEstado.ErrorStaging;
                entryFresco.MensajeError = $"Fallo de persistencia tras {entryFresco.Intentos} intentos: {fallo.Message}";
            }

            await recoveryContexto.SaveChangesAsync(cancellationToken);
        }
        catch (Exception recoveryEx)
        {
            _logger.LogError(recoveryEx, "Error registrando el fallo de persistencia para la fila {EntryId}", entryId);
        }
    }
}
