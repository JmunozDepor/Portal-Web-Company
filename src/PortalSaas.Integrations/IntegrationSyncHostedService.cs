using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
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
                // No dejar que un fallo de un ciclo tumbe todo el host (comportamiento
                // por defecto de BackgroundServiceExceptionBehavior.StopHost) -- se
                // registra el error y se sigue intentando en el próximo ciclo.
                _logger.LogError(ex, "Error inesperado ejecutando el ciclo de sincronización de integraciones");
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
        List<Guid> pendientesIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var contexto = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
            var ahora = DateTimeOffset.UtcNow;
            pendientesIds = await contexto.IntegrationDefinitions
                .Where(d => d.Activo && d.NextRunAt != null && d.NextRunAt <= ahora)
                .Select(d => d.Id)
                .ToListAsync(cancellationToken);
        }

        foreach (var definicionId in pendientesIds)
        {
            // Un scope (y un DbContext) nuevo por definición -- evita que entidades
            // trackeadas o un error en una integración contaminen el estado usado por
            // las demás integraciones del mismo ciclo.
            using var scope = _scopeFactory.CreateScope();
            var contexto = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
            var mapeoServicio = scope.ServiceProvider.GetRequiredService<IIntegrationFieldMappingService>();
            var secretoServicio = scope.ServiceProvider.GetRequiredService<ISecretoCifradoService>();
            var conectores = scope.ServiceProvider.GetServices<IIntegrationConnector>().ToList();
            var readers = scope.ServiceProvider.GetServices<IIntegrationEntityReader>().ToList();

            var definicion = await contexto.IntegrationDefinitions
                .FirstOrDefaultAsync(d => d.Id == definicionId, cancellationToken);
            if (definicion is null)
            {
                continue;
            }

            await EjecutarIntegracionAsync(contexto, mapeoServicio, secretoServicio, conectores, readers, definicion, cancellationToken);
        }
    }

    private async Task EjecutarIntegracionAsync(
        PortalSaasDbContext contexto,
        IIntegrationFieldMappingService mapeoServicio,
        ISecretoCifradoService secretoServicio,
        List<IIntegrationConnector> conectores,
        List<IIntegrationEntityReader> readers,
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

            if (definicion.Direccion is IntegrationDireccion.Bajada or IntegrationDireccion.Ambas)
            {
                // La descarga (Bajada) todavía no está implementada -- ver
                // SapDocumentConnector.PullAsync, que lanza NotSupportedException por el
                // mismo motivo. No dejar caer en silencio a Exito: es preferible un
                // Error explícito a una sincronización "exitosa" que en realidad no bajó
                // nada.
                throw new NotSupportedException(
                    $"La dirección '{definicion.Direccion}' incluye descarga (Bajada), que todavía no está implementada para la integración '{definicion.Nombre}'.");
            }

            if (definicion.Direccion is IntegrationDireccion.Subida)
            {
                var conectorConfigJson = DescifrarConfigConector(secretoServicio, definicion);

                var reader = readers.FirstOrDefault(r => r.EntidadNegocio == definicion.EntidadNegocio)
                    ?? throw new InvalidOperationException($"No hay reader registrado para entidad '{definicion.EntidadNegocio}'.");

                var registrosLocales = await reader.LeerPendientesAsync(definicion.CompanyId, cancellationToken);
                var registrosMapeados = new List<IntegrationRecord>();
                foreach (var registroLocal in registrosLocales)
                {
                    registrosMapeados.Add(await mapeoServicio.MapToExternalAsync(definicion.Id, registroLocal));
                }

                Exception? excepcionDePush = null;
                try
                {
                    await conector.PushAsync(conectorConfigJson, registrosMapeados, cancellationToken);
                }
                catch (Exception ex)
                {
                    excepcionDePush = ex;
                }

                foreach (var registroLocal in registrosLocales)
                {
                    await reader.MarcarProcesadoAsync(definicion.CompanyId, registroLocal, exito: excepcionDePush is null, mensajeError: excepcionDePush?.Message, cancellationToken);
                }

                if (excepcionDePush is not null)
                {
                    throw excepcionDePush;
                }

                log.RegistrosProcesados = registrosLocales.Count;
            }

            log.Resultado = IntegrationRunResultado.Exito;
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

            try
            {
                await contexto.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                // Un fallo al persistir el log no debe enmascarar (ni propagar y tumbar
                // el host por) la excepción original del conector -- ya quedó registrada
                // arriba vía _logger.LogError. Este es un fallo aparte, de persistencia.
                _logger.LogError(saveEx, "Error guardando el log de ejecución para la integración {IntegrationDefinitionId}", definicion.Id);
            }
        }
    }

    private string DescifrarConfigConector(ISecretoCifradoService secretoServicio, IntegrationDefinition definicion)
    {
        try
        {
            return secretoServicio.Decrypt(definicion.ConectorConfigCifrado);
        }
        catch (Exception ex)
        {
            // Hoy nada en el codebase cifra este campo al escribirlo (ver
            // IntegrationDefinition.ConectorConfigCifrado) -- puede contener texto plano
            // legado o de pruebas. No fallar la integración por esto: se usa el valor
            // crudo como respaldo, pero se deja constancia explícita en el log de que no
            // pasó por descifrado.
            _logger.LogWarning(ex,
                "ConectorConfig no está cifrado o es inválido — ver ISecretoCifradoService. Integración {IntegrationDefinitionId}",
                definicion.Id);
            return definicion.ConectorConfigCifrado;
        }
    }
}
