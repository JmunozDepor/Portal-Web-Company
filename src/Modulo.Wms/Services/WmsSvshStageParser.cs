using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Services;

public sealed class WmsSvshStageParser : BackgroundService
{
    private static readonly TimeSpan IntervaloCiclo = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WmsSvshStageParser> _logger;

    public WmsSvshStageParser(IServiceScopeFactory scopeFactory, ILogger<WmsSvshStageParser> logger)
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
                _logger.LogError(ex, "Error inesperado ejecutando el ciclo de aplanado SVSH");
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
            try
            {
                await ProcesarCompaniaAsync(compania.CompanyId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando el ciclo SVSH de la compañía {CompanyId}", compania.CompanyId);
            }
        }
    }

    private async Task ProcesarCompaniaAsync(Guid companyId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();

        var pendientes = await contexto.WmsOracleInboundStages
            .Where(s => s.Estado == WmsInboundEstado.Pendiente && s.TipoDoc == "SVSH" && s.Formato == WmsInboundFormato.Xml)
            .ToListAsync(cancellationToken);

        foreach (var entry in pendientes)
        {
            try
            {
                var filas = WmsSvshXmlParser.Parse(entry.Contenido);

                if (filas.Count == 0)
                {
                    entry.Estado = WmsInboundEstado.ErrorEstructura;
                    entry.MensajeError = "No se encontraron nodos ib_shipment_dtl válidos.";
                }
                else
                {
                    foreach (var fila in filas)
                    {
                        fila.ParentId = entry.Id;
                        contexto.WmsOracleStageSvsh.Add(fila);
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
                _logger.LogError(ex, "Error guardando el resultado del aplanado SVSH para el archivo {NombreArchivo}", entry.NombreArchivo);
                await RegistrarFalloDePersistenciaAsync(companyId, entry.Id, ex, cancellationToken);
            }
        }
    }

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
            _logger.LogError(recoveryEx, "Error registrando el fallo de persistencia SVSH para la fila {EntryId}", entryId);
        }
    }
}
