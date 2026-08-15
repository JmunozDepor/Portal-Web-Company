using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Data;
using PortalSaas.Data.Entities.Integraciones;

namespace PortalSaas.Integrations;

public sealed class IntegrationSyncHostedService : BackgroundService
{
    private static readonly TimeSpan IntervaloCiclo = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IntegrationSyncHostedService> _logger;

    public IntegrationSyncHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<IntegrationSyncHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await EjecutarCicloAsync(stoppingToken);

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
        var contexto = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
        var mapeoServicio = scope.ServiceProvider.GetRequiredService<IIntegrationFieldMappingService>();
        var conectores = scope.ServiceProvider.GetServices<IIntegrationConnector>().ToList();

        var ahora = DateTimeOffset.UtcNow;
        var pendientes = await contexto.IntegrationDefinitions
            .Where(d => d.Activo && d.NextRunAt != null && d.NextRunAt <= ahora)
            .ToListAsync(cancellationToken);

        foreach (var definicion in pendientes)
        {
            await EjecutarIntegracionAsync(contexto, mapeoServicio, conectores, definicion, cancellationToken);
        }
    }

    private async Task EjecutarIntegracionAsync(
        PortalSaasDbContext contexto,
        IIntegrationFieldMappingService mapeoServicio,
        List<IIntegrationConnector> conectores,
        IntegrationDefinition definicion,
        CancellationToken cancellationToken)
    {
        var log = new IntegrationRunLog
        {
            IntegrationDefinitionId = definicion.Id,
            IniciadoEn = DateTimeOffset.UtcNow,
            DisparadoPor = IntegrationRunDisparadoPor.Programado,
        };

        try
        {
            var conector = conectores.FirstOrDefault(c => c.Tipo == definicion.ConectorTipo.ToString())
                ?? throw new InvalidOperationException($"No hay conector registrado para tipo '{definicion.ConectorTipo}'.");

            if (definicion.Direccion is IntegrationDireccion.Subida or IntegrationDireccion.Ambas)
            {
                // La lectura de pendientes desde el módulo origen (IIntegrationEntityReader<T>)
                // se resuelve en el plan de migración del módulo consumidor (ver spec, fuera de alcance aquí).
                var registrosExternos = new List<IntegrationRecord>();
                await conector.PushAsync(definicion.ConectorConfigCifrado, registrosExternos, cancellationToken);
            }

            log.Resultado = IntegrationRunResultado.Exito;
            log.RegistrosProcesados = 0;
        }
        catch (Exception ex)
        {
            log.Resultado = IntegrationRunResultado.Error;
            log.DetalleError = ex.Message;
            _logger.LogError(ex, "Error ejecutando integración {IntegrationDefinitionId}", definicion.Id);
        }
        finally
        {
            log.FinalizadoEn = DateTimeOffset.UtcNow;
            definicion.NextRunAt = null;
            contexto.IntegrationRunLogs.Add(log);
            await contexto.SaveChangesAsync(cancellationToken);
        }
    }
}
