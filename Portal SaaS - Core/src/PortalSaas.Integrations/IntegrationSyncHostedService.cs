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

    /// <summary>Red de seguridad: un conector puede colgarse en cualquier punto de la llamada
    /// externa (login SAP, GetAllAsync paginado, POST a WMS Cloud, etc.) sin respetar
    /// CancellationToken ni tener su propio timeout -- encontrado 21 ago 2026: el ciclo quedó
    /// congelado para siempre procesando una sola IntegrationDefinition, bloqueando TODAS las
    /// demás de la compañía porque este es un loop secuencial único. En vez de perseguir cada
    /// punto de la cadena de llamadas (SapConnectionProvider ya tiene su propio timeout de login
    /// de 30s, pero no alcanzó), este timeout global por integración es la garantía real de que
    /// el ciclo siempre sigue adelante. 5 minutos (no 90s, valor inicial insuficiente) porque
    /// una sincronización real de Items puede traer decenas de miles de filas (confirmado 21 ago
    /// 2026: 29.087 filas, ~50s solo el fetch a SAP + escritura a staging) -- 90s cortaba
    /// corridas legítimas, no solo cuelgues reales.</summary>
    private static readonly TimeSpan TimeoutPorIntegracion = TimeSpan.FromMinutes(5);

    /// <summary>El PageSize configurable del conector Sap (ver SapDocumentConnector) solo
    /// controla el $top de la consulta HTTP contra SAP -- PullAsync igual acumula TODAS las
    /// filas (ej. 29.083) en una sola lista antes de devolverlas. Sin este chunking, ese único
    /// lote completo se pasaba de una sola vez a writer.EscribirAsync, que hace un solo
    /// SaveChangesAsync con miles de filas -- exactamente el escenario "saturar la BD" que se
    /// pidió evitar con lotes (22 ago 2026). Se trocea acá, en el único punto donde se invoca
    /// EscribirAsync, para que aplique a cualquier writer de Bajada sin que cada uno tenga que
    /// implementarlo.</summary>
    private const int TamanoLoteEscritura = 200;

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

            try
            {
                await EjecutarIntegracionAsync(contexto, secretoServicio, conectores, readers, writers, definicion, cancellationToken)
                    .WaitAsync(TimeoutPorIntegracion, cancellationToken);
            }
            catch (TimeoutException)
            {
                // WaitAsync no cancela la tarea abandonada -- sigue corriendo en segundo plano
                // contra 'contexto' (no thread-safe), así que a partir de acá 'contexto' queda
                // envenenado y NO se debe volver a usar. Se abre uno nuevo, en un scope aparte,
                // solo para dejar constancia y destrabar la próxima corrida.
                _logger.LogError(
                    "La integración '{Nombre}' ({Id}) no terminó en {Segundos}s -- se abandona esta corrida para no bloquear el resto del ciclo. Puede seguir un proceso huérfano en segundo plano.",
                    definicion.Nombre, definicion.Id, TimeoutPorIntegracion.TotalSeconds);
                await RegistrarTimeoutEnScopeNuevoAsync(definicion.Id, TimeoutPorIntegracion, cancellationToken);
            }
        }
    }

    private async Task RegistrarTimeoutEnScopeNuevoAsync(Guid definicionId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var contexto = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();

            var definicion = await contexto.IntegrationDefinitions.FirstOrDefaultAsync(d => d.Id == definicionId, cancellationToken);
            if (definicion is not null)
            {
                definicion.NextRunAt = CalcularProximaCorrida(definicion);
            }

            contexto.IntegrationRunLogs.Add(new IntegrationRunLog
            {
                IntegrationDefinitionId = definicionId,
                IniciadoEn = DateTimeOffset.UtcNow,
                FinalizadoEn = DateTimeOffset.UtcNow,
                Resultado = IntegrationRunResultado.Error,
                DetalleError = $"Timeout: no terminó en {timeout.TotalSeconds}s. Ver logs del Host para más detalle.",
                DisparadoPor = IntegrationRunDisparadoPor.Programado,
            });

            await contexto.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registrando el timeout de la integración {IntegrationDefinitionId}", definicionId);
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
                var cursorIncremental = definicion.UltimaSincronizacionExitosa;
                log.DetalleConsulta = DescribirConsultaSinFallar(conector, conectorConfigJson, cursorIncremental);

                var writer = writers.FirstOrDefault(w => w.EntidadNegocio == definicion.EntidadNegocio)
                    ?? throw new InvalidOperationException($"No hay writer registrado para entidad '{definicion.EntidadNegocio}'.");

                var registrosExternos = await conector.PullAsync(conectorConfigJson, cursorIncremental, cancellationToken);
                foreach (var lote in registrosExternos.Chunk(TamanoLoteEscritura))
                {
                    await writer.EscribirAsync(definicion.CompanyId, lote, cancellationToken);
                }

                log.RegistrosProcesados = registrosExternos.Count;

                // Cursor de sincronización incremental (ver IntegrationDefinition.UltimaSincronizacionExitosa
                // y IIntegrationConnector.PullAsync) -- se avanza SOLO si esta corrida llega hasta acá sin
                // excepción (si el catch de abajo se dispara, esta línea nunca corrió y el cursor no avanza,
                // así la próxima corrida reintenta desde el mismo punto en vez de perder el rango con error).
                definicion.UltimaSincronizacionExitosa = DateTimeOffset.UtcNow;
            }

            if (definicion.Direccion is IntegrationDireccion.Subida)
            {
                var conectorConfigJson = DescifrarConfigConector(secretoServicio, definicion);
                log.DetalleConsulta = DescribirConsultaSinFallar(conector, conectorConfigJson);

                var reader = readers.FirstOrDefault(r => r.EntidadNegocio == definicion.EntidadNegocio)
                    ?? throw new InvalidOperationException($"No hay reader registrado para entidad '{definicion.EntidadNegocio}'.");

                var limiteMaximo = LeerLimiteMaximoDeConfig(conectorConfigJson);
                var registrosLocales = await reader.LeerPendientesAsync(definicion.CompanyId, limiteMaximo, cancellationToken);

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
            definicion.NextRunAt = CalcularProximaCorrida(definicion);
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

    /// <summary>Cuándo vuelve a correr una integración después de terminar (con éxito, error o
    /// timeout): si sigue activa y tiene un intervalo configurado, se reprograma para dentro de
    /// ese intervalo contado desde AHORA -- no desde la hora teórica de disparo, así una corrida
    /// que tardó más que el intervalo no genera una cola de corridas encimadas. Sin intervalo,
    /// queda en null: solo vuelve a correr si alguien la dispara a mano ("Ejecutar ahora" fija
    /// NextRunAt).</summary>
    private static DateTimeOffset? CalcularProximaCorrida(IntegrationDefinition definicion) =>
        definicion is { Activo: true, IntervaloMinutos: { } minutos } && minutos > 0
            ? DateTimeOffset.UtcNow.AddMinutes(minutos)
            : null;

    /// <summary>IIntegrationConnector.DescribirConsulta no debería lanzar (ver contrato de la
    /// interfaz), pero un conector de terceros podría no respetarlo -- no dejar que describir
    /// la consulta para el log tumbe la ejecución real.</summary>
    private string? DescribirConsultaSinFallar(IIntegrationConnector conector, string conectorConfigJson, DateTimeOffset? cursorIncremental = null)
    {
        try
        {
            return conector.DescribirConsulta(conectorConfigJson, cursorIncremental);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error describiendo la consulta del conector '{Tipo}'", conector.Tipo);
            return null;
        }
    }

    /// <summary>
    /// "MaxRecordsPerCycle" es un campo opcional dentro de la config del conector de Subida
    /// (ej. WmsCloudConfig, definido en el plugin -- no accesible como tipo desde Core). Se
    /// lee genéricamente vía JsonDocument en vez de un tipo fuerte, mismo criterio que
    /// DescribirConsultaSinFallar de arriba: nunca debe tumbar la corrida real si la config no
    /// trae el campo o no es JSON válido, y no es propiedad de este proyecto conocer el shape
    /// completo de la config de cada conector. Null si no está configurado -- cada reader
    /// (ver WmsSapStageItemReader.MaximoPorCicloPorDefecto) decide su propio default.
    /// </summary>
    private int? LeerLimiteMaximoDeConfig(string conectorConfigJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(conectorConfigJson);
            return doc.RootElement.TryGetProperty("MaxRecordsPerCycle", out var prop) && prop.TryGetInt32(out var valor)
                ? valor
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error leyendo MaxRecordsPerCycle de la config del conector, se usa el default del reader.");
            return null;
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
