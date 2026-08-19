using Microsoft.Extensions.Options;
using Servicios.Common.Configuracion;
using Servicios.Common.Contratos;
using Servicios.Common.Seguridad;
using Servicios.TransferenciaAutomatica_v2.Db;
using Servicios.TransferenciaAutomatica_v2.Domain;
using Servicios.TransferenciaAutomatica_v2.Estado;
using Servicios.TransferenciaAutomatica_v2.ServiceLayer;

namespace Servicios.TransferenciaAutomatica_v2;

/// <summary>
/// Loop multi-compañía con aislamiento de errores por compañía (mismo chasis que v1). El
/// algoritmo de negocio es el rediseño acordado (ver CLAUDE.md de este proyecto), no el
/// portado literal de SP_DEP_ORDER_ABS que usa v1:
///   1. El disponible/prioridad de bodegas se lee con IStockRepository (HANA o SQL Server,
///      ver StockRepositoryFactory) -- lecturas simples, sin lógica de negocio en SQL.
///   2. La cascada de asignación corre en Domain/AllocationEngine.cs (C# puro, testeable).
///   3. Un "ledger" por ciclo evita comprometer el mismo stock de una bodega origen a dos
///      documentos distintos procesados en el mismo ciclo.
///   4. compania.HeaderQuerySource ya trae los documentos sin picking pendiente y
///      ordenados nuevo→viejo (convención de configuración, ver CLAUDE.md) -- acá se
///      reverifica el picking puntualmente por si el documento entró a preparación a
///      mitad del ciclo.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ICompanyProvider _companyProvider;
    private readonly ISecretoCifradoService _secretoCifradoService;
    private readonly ILogSink _logSink;
    private readonly ILogger<Worker> _logger;
    private readonly WorkerOptions _options;
    private readonly IntentosAsignacionStore _intentosStore;
    private readonly AsignacionParcialOptions _asignacionParcialOptions;

    public Worker(
        ICompanyProvider companyProvider,
        ISecretoCifradoService secretoCifradoService,
        ILogSink logSink,
        ILogger<Worker> logger,
        IOptions<WorkerOptions> options,
        IntentosAsignacionStore intentosStore,
        IOptions<AsignacionParcialOptions> asignacionParcialOptions)
    {
        _companyProvider = companyProvider;
        _secretoCifradoService = secretoCifradoService;
        _logSink = logSink;
        _logger = logger;
        _options = options.Value;
        _intentosStore = intentosStore;
        _asignacionParcialOptions = asignacionParcialOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var companias = await _companyProvider.GetActiveCompaniesAsync(stoppingToken);
            _logger.LogInformation("Iniciando ciclo de transferencia automática v2 ({Cantidad} compañías activas)", companias.Count);

            foreach (var compania in companias)
            {
                stoppingToken.ThrowIfCancellationRequested();

                try
                {
                    await EjecutarTransferenciasDeCompaniaAsync(compania, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falló el ciclo de transferencia para {CompanyCode}", compania.CompanyCode);
                    await _logSink.WriteAsync(new LogEntry
                    {
                        FechaHora = DateTimeOffset.Now,
                        Nivel = NivelLog.Error,
                        CompanyCode = compania.CompanyCode,
                        Mensaje = "Falló el ciclo de transferencia automática",
                        Detalle = ex.ToString()
                    }, stoppingToken);
                }
            }

            await Task.Delay(_options.CicloIntervalo, stoppingToken);
        }
    }

    private async Task EjecutarTransferenciasDeCompaniaAsync(CompanyConnectionConfig compania, CancellationToken ct)
    {
        var warehousePriorityTable = compania.WarehousePriorityTable
            ?? throw new InvalidOperationException($"{compania.CompanyCode}: falta configurar WarehousePriorityTable.");
        var pickingPendingQueryTemplate = compania.PickingPendingQuery
            ?? throw new InvalidOperationException($"{compania.CompanyCode}: falta configurar PickingPendingQuery.");

        var repositorio = StockRepositoryFactory.Crear(compania.EngineType);
        var connectionString = repositorio.ConnectionString(
            compania.Host, compania.Port, compania.Schema, compania.DbUserId,
            _secretoCifradoService.Descifrar(compania.DbSecretCifrado), compania.DatabaseEncryptada,
            compania.ConfiaCertificadoBaseDatos);

        var documentosPendientes = repositorio.ObtenerDocumentosPendientes(connectionString, compania.HeaderQuerySource);
        if (documentosPendientes.Count == 0)
        {
            // Señal explícita de que el ciclo corrió bien (conectó, autenticó, consultó)
            // y no había nada pendiente -- sin esto, un ciclo exitoso sin trabajo es
            // indistinguible de un servicio que dejó de correr silenciosamente.
            await _logSink.WriteAsync(new LogEntry
            {
                FechaHora = DateTimeOffset.Now,
                Nivel = NivelLog.Info,
                CompanyCode = compania.CompanyCode,
                Mensaje = "Ciclo completado -- sin documentos pendientes",
                Detalle = null
            }, ct);
            return;
        }

        using var serviceLayer = new ServiceLayerClient(
            compania.ServiceLayerUrl, compania.ToleraNombreCertificadoServiceLayer, compania.ConfiaCertificadoServiceLayer);
        // Mismo criterio que v1: en B1 sobre HANA/SQL el schema/base de la compañía y el
        // CompanyDB de Service Layer son el mismo valor -- no se agrega un campo redundante.
        await serviceLayer.LoginAsync(
            compania.Schema, compania.ServiceLayerUsername,
            _secretoCifradoService.Descifrar(compania.ServiceLayerSecretCifrado), ct);

        // Ledger de lo ya comprometido en ESTE ciclo, para esta compañía -- evita que dos
        // documentos distintos procesados en el mismo ciclo se lleven el mismo stock de
        // una bodega origen escasa (ver Domain/AllocationEngine.cs).
        var ledgerCiclo = new Dictionary<(string WhsCode, string ItemCode), decimal>();

        foreach (var documento in documentosPendientes)
        {
            ct.ThrowIfCancellationRequested();

            if (!DocumentTypeMapping.TryResolver(documento.ObjType, out var tipoDocumento))
            {
                _logger.LogWarning(
                    "ObjType {ObjType} sin mapeo conocido (DocEntry {DocEntry}, compañía {CompanyCode}) -- se omite",
                    documento.ObjType, documento.DocEntry, compania.CompanyCode);
                continue;
            }

            var pickingQuery = pickingPendingQueryTemplate.Replace("{TablaDetalle}", tipoDocumento.TablaDetalle);

            if (repositorio.TienePickingPendiente(connectionString, pickingQuery, documento.DocEntry))
            {
                // Ya entró a preparación -- no se le sigue asignando stock (requisito 2).
                // Se marca completado igual para que deje de aparecer como pendiente.
                repositorio.MarcarDocumentoCompletado(connectionString, tipoDocumento.TablaCabecera, compania.CompletionUdfFieldName, documento.DocEntry);
                continue;
            }

            var lineasTransferir = new List<StockTransferLine>();
            // Si alguna línea con necesidad real no queda 100% cubierta (stock insuficiente
            // en las bodegas origen de la cascada, o bodega destino sin cascada configurada),
            // el documento NO se marca completado -- debe reintentarse en el próximo ciclo
            // para transferir el faltante en cuanto haya stock disponible.
            var necesidadTotalmenteCubierta = true;

            foreach (var linea in repositorio.ObtenerLineas(connectionString, tipoDocumento.TablaDetalle, tipoDocumento.ColumnaBodegaDestino, documento.DocEntry))
            {
                var disponibleDestino = repositorio
                    .ObtenerDisponible(connectionString, linea.ItemCode, [linea.WhsCode])
                    .GetValueOrDefault(linea.WhsCode, 0m);

                var necesidad = disponibleDestino < 0
                    ? Math.Min(linea.Cantidad, -disponibleDestino)
                    : 0m;

                if (necesidad <= 0)
                {
                    continue;
                }

                var prioridades = repositorio
                    .ObtenerPrioridadBodegas(connectionString, warehousePriorityTable, linea.WhsCode)
                    .OrderBy(p => p.Prioridad)
                    .ToList();

                if (prioridades.Count == 0)
                {
                    // No hay cascada configurada para esta bodega destino -- no se puede
                    // cubrir la necesidad, el documento debe seguir pendiente.
                    necesidadTotalmenteCubierta = false;
                    continue;
                }

                var whsOrigenes = prioridades.Select(p => p.WhsCodeOrigen).ToList();
                var disponiblesOrigen = repositorio.ObtenerDisponible(connectionString, linea.ItemCode, whsOrigenes);

                var candidatos = whsOrigenes
                    .Select(whs => (
                        WhsCode: whs,
                        Disponible: disponiblesOrigen.GetValueOrDefault(whs, 0m) - ledgerCiclo.GetValueOrDefault((whs, linea.ItemCode), 0m)))
                    .ToList();

                var asignaciones = AllocationEngine.Asignar(necesidad, candidatos);
                var necesidadAsignada = asignaciones.Sum(a => a.Cantidad);
                if (necesidadAsignada < necesidad)
                {
                    // No había suficiente stock en la cascada de bodegas origen para cubrir
                    // esta línea -- lo que sí se pudo se transfiere, pero el documento queda
                    // pendiente para completar el faltante en un próximo ciclo.
                    necesidadTotalmenteCubierta = false;
                }

                foreach (var (whsCodeOrigen, cantidad) in asignaciones)
                {
                    ledgerCiclo[(whsCodeOrigen, linea.ItemCode)] =
                        ledgerCiclo.GetValueOrDefault((whsCodeOrigen, linea.ItemCode), 0m) + cantidad;

                    lineasTransferir.Add(new StockTransferLine
                    {
                        ItemCode = linea.ItemCode,
                        Quantity = cantidad,
                        WarehouseCode = linea.WhsCode,
                        FromWarehouseCode = whsCodeOrigen
                    });
                }
            }

            var entroAPickingDuranteProceso = false;

            if (lineasTransferir.Count > 0)
            {
                // Reverificación puntual justo antes de postear -- cierra la ventana de
                // carrera de un documento que entró a picking mientras se procesaba.
                if (repositorio.TienePickingPendiente(connectionString, pickingQuery, documento.DocEntry))
                {
                    entroAPickingDuranteProceso = true;
                    _logger.LogInformation(
                        "DocEntry {DocEntry} entró a picking durante el ciclo -- se omite la transferencia",
                        documento.DocEntry);
                }
                else
                {
                    var stockTransfer = new StockTransferDocument
                    {
                        CardCode = documento.CardCode,
                        StockTransferLines = lineasTransferir,
                        DocumentReferences =
                        {
                            new StockTransferDocumentReference
                            {
                                RefDocEntr = documento.DocEntry,
                                RefObjType = tipoDocumento.ServiceLayerRefObjType
                            }
                        }
                    };

                    var creado = await serviceLayer.PostearStockTransferAsync(stockTransfer, ct);
                    await _logSink.WriteAsync(new LogEntry
                    {
                        FechaHora = DateTimeOffset.Now,
                        Nivel = NivelLog.Info,
                        CompanyCode = compania.CompanyCode,
                        Mensaje = $"StockTransfer creado para DocEntry {documento.DocEntry} (DocNum {documento.DocNum})",
                        Detalle = $"ServiceLayer DocEntry={creado.DocEntry} DocNum={creado.DocNum}"
                    }, ct);
                }
            }

            // Solo se marca completado si toda la necesidad quedó cubierta, o si el
            // documento entró a picking (regla 2: una vez en preparación, se deja de
            // asignar stock aunque haya quedado faltante) -- de lo contrario el documento
            // debe seguir apareciendo como pendiente para completar el faltante en el
            // próximo ciclo, en vez de quedar marcado como resuelto sin estarlo.
            if (necesidadTotalmenteCubierta || entroAPickingDuranteProceso)
            {
                repositorio.MarcarDocumentoCompletado(connectionString, tipoDocumento.TablaCabecera, compania.CompletionUdfFieldName, documento.DocEntry);
                _intentosStore.Limpiar(compania.CompanyCode, documento.ObjType, documento.DocEntry);
            }
            else
            {
                // Tope de reintentos: si el faltante nunca se puede cubrir (p.ej. la
                // bodega origen configurada en la cascada nunca tiene stock suficiente),
                // no tiene sentido reintentar para siempre sin que nadie se entere -- se
                // deja lo que sí se pudo transferir y se marca completado igual, dejando
                // registro explícito en el log para que se revise a mano.
                var intentos = _intentosStore.RegistrarIntentoParcial(compania.CompanyCode, documento.ObjType, documento.DocEntry);
                if (intentos >= _asignacionParcialOptions.MaxIntentos)
                {
                    _logger.LogWarning(
                        "DocEntry {DocEntry} (DocNum {DocNum}, compañía {CompanyCode}) alcanzó el máximo de {MaxIntentos} intentos con asignación parcial -- se marca completado con el faltante sin cubrir",
                        documento.DocEntry, documento.DocNum, compania.CompanyCode, _asignacionParcialOptions.MaxIntentos);
                    await _logSink.WriteAsync(new LogEntry
                    {
                        FechaHora = DateTimeOffset.Now,
                        Nivel = NivelLog.Error,
                        CompanyCode = compania.CompanyCode,
                        Mensaje = $"DocEntry {documento.DocEntry} (DocNum {documento.DocNum}) alcanzó el máximo de {_asignacionParcialOptions.MaxIntentos} intentos con asignación parcial -- requiere revisión manual",
                        Detalle = null
                    }, ct);

                    repositorio.MarcarDocumentoCompletado(connectionString, tipoDocumento.TablaCabecera, compania.CompletionUdfFieldName, documento.DocEntry);
                    _intentosStore.Limpiar(compania.CompanyCode, documento.ObjType, documento.DocEntry);
                }
            }
        }
    }
}
