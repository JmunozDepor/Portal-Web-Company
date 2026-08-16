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
            var secretoServicio = scope.ServiceProvider.GetRequiredService<ISecretoCifradoService>();

            var definicion = await contexto.IntegrationDefinitions
                .FirstOrDefaultAsync(d => d.Id == definicionId, cancellationToken);
            if (definicion is null)
            {
                continue;
            }

            // El override de compañía ambiente debe fijarse ANTES de resolver
            // conectores/readers -- un DbContext de plugin (ej. WmsDbContext) que dependa
            // de ICurrentCompanyAccessor.HasCompany/.CompanyId en su propio constructor
            // (vía la fábrica de connection string) necesita verlo ya fijado en el
            // momento en que el contenedor lo construye. Sin HttpContext (BackgroundService),
            // ICurrentCompanyAccessor no tiene de dónde más leer la compañía.
            scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(definicion.CompanyId);

            var conectores = scope.ServiceProvider.GetServices<IIntegrationConnector>().ToList();
            var readers = scope.ServiceProvider.GetServices<IIntegrationEntityReader>().ToList();
            var writers = scope.ServiceProvider.GetServices<IIntegrationEntityWriter>().ToList();

            await EjecutarIntegracionAsync(contexto, secretoServicio, conectores, readers, writers, definicion, cancellationToken);
        }
    }

    private async Task EjecutarIntegracionAsync(
        PortalSaasDbContext contexto,
        ISecretoCifradoService secretoServicio,
        List<IIntegrationConnector> conectores,
        List<IIntegrationEntityReader> readers,
        List<IIntegrationEntityWriter> writers,
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

            if (definicion.Direccion is IntegrationDireccion.Ambas)
            {
                // 'Ambas' (Bajada + Subida combinadas) todavía no está implementada --
                // ninguna IntegrationDefinition de esta ronda la usa. No dejar caer en
                // silencio a Exito: es preferible un Error explícito a una sincronización
                // "exitosa" que en realidad no hizo lo que la definición pedía.
                throw new NotSupportedException(
                    $"La dirección 'Ambas' todavía no está implementada para la integración '{definicion.Nombre}'.");
            }

            if (definicion.Direccion is IntegrationDireccion.Bajada)
            {
                var conectorConfigJson = DescifrarConfigConector(secretoServicio, definicion);

                var writer = writers.FirstOrDefault(w => w.EntidadNegocio == definicion.EntidadNegocio)
                    ?? throw new InvalidOperationException($"No hay writer registrado para entidad '{definicion.EntidadNegocio}'.");

                var registrosExternos = await conector.PullAsync(conectorConfigJson, cancellationToken);
                await writer.EscribirAsync(definicion.CompanyId, registrosExternos, cancellationToken);

                log.RegistrosProcesados = registrosExternos.Count;
            }

            if (definicion.Direccion is IntegrationDireccion.Subida)
            {
                var conectorConfigJson = DescifrarConfigConector(secretoServicio, definicion);

                var reader = readers.FirstOrDefault(r => r.EntidadNegocio == definicion.EntidadNegocio)
                    ?? throw new InvalidOperationException($"No hay reader registrado para entidad '{definicion.EntidadNegocio}'.");

                var registrosLocales = await reader.LeerPendientesAsync(definicion.CompanyId, cancellationToken);

                // NO se pasa por IIntegrationFieldMappingService acá -- el mapeo campo-a-campo
                // (IntegrationFieldMapping en BD) sirve para traducir NOMBRES de campo entre el
                // sistema local y el externo, pero los readers de este flujo (ej.
                // WmsSlshInventoryReader) ya devuelven registros estructurados con los nombres
                // fijos que SapDocumentConnector.PushAsync espera ('TipoDocumento'/'Lineas'/etc.).
                // Sin ninguna fila de mapeo configurada para esta integración (no la hay todavía
                // para WMS), MapToExternalAsync devolvía un IntegrationRecord vacío -- el
                // conector recibía TipoDocumento=null y todo terminaba en NotSupportedException.
                IReadOnlyList<IntegrationPushResult> resultados;
                try
                {
                    resultados = await conector.PushAsync(conectorConfigJson, registrosLocales, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Todo el lote falló catastróficamente (ver SapDocumentConnector.PushAsync) --
                    // marcar todos los registros locales como fallidos antes de relanzar, para que
                    // el reader pueda reintentarlos en el próximo ciclo.
                    foreach (var registroLocal in registrosLocales)
                    {
                        await reader.MarcarProcesadoAsync(definicion.CompanyId, registroLocal, exito: false, mensajeError: ex.Message, cancellationToken);
                    }

                    throw;
                }

                foreach (var resultado in resultados)
                {
                    await reader.MarcarProcesadoAsync(definicion.CompanyId, resultado.Registro, resultado.Exito, resultado.MensajeError, cancellationToken);
                }

                log.RegistrosProcesados = resultados.Count(r => r.Exito);
                log.RegistrosConError = resultados.Count(r => !r.Exito);
            }

            log.Resultado = log.RegistrosConError > 0 ? IntegrationRunResultado.Parcial : IntegrationRunResultado.Exito;
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
