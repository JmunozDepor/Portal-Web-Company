using Microsoft.Extensions.Options;
using Servicios.Common.Configuracion;
using Servicios.Common.Contratos;
using Servicios.Common.Seguridad;
using Servicios.TransferenciaAutomatica.Sap;
using Servicios.TransferenciaAutomatica.ServiceLayer;

namespace Servicios.TransferenciaAutomatica;

/// <summary>
/// Loop multi-compañía con aislamiento de errores por compañía. La lógica de negocio de
/// EjecutarTransferenciasDeCompaniaAsync es el mismo algoritmo de StrockTrans.cs del
/// servicio legado (ver Sap/DocumentTypeMapping.cs y ServiceLayer/ServiceLayerClient.cs
/// para el detalle de qué se portó igual y qué bug de plomería se corrigió en cada paso).
/// El acceso a datos es agnóstico de motor -- WarehouseTransferRepositoryFactory elige
/// HanaRepository o SqlServerRepository según compania.EngineType (ver
/// Sap/IWarehouseTransferRepository.cs).
///
/// Diferencia clave respecto al servicio legado (StrockTrans.cs): acá cada compañía tiene
/// su propio try/catch -- una excepción no controlada en una compañía JAMÁS aborta el
/// resto del batch, a diferencia del catch vacío que envolvía todo el foreach en el
/// servicio original.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ICompanyProvider _companyProvider;
    private readonly ISecretoCifradoService _secretoCifradoService;
    private readonly ILogSink _logSink;
    private readonly ILogger<Worker> _logger;
    private readonly WorkerOptions _options;

    public Worker(
        ICompanyProvider companyProvider,
        ISecretoCifradoService secretoCifradoService,
        ILogSink logSink,
        ILogger<Worker> logger,
        IOptions<WorkerOptions> options)
    {
        _companyProvider = companyProvider;
        _secretoCifradoService = secretoCifradoService;
        _logSink = logSink;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var companias = await _companyProvider.GetActiveCompaniesAsync(stoppingToken);
            _logger.LogInformation("Iniciando ciclo de transferencia automática ({Cantidad} compañías activas)", companias.Count);

            foreach (var compania in companias)
            {
                stoppingToken.ThrowIfCancellationRequested();

                try
                {
                    await EjecutarTransferenciasDeCompaniaAsync(compania, stoppingToken);
                }
                catch (Exception ex)
                {
                    // Aislado por compañía a propósito -- ver el comentario de la clase.
                    _logger.LogError(ex, "Falló el ciclo de transferencia para {CompanyCode}", compania.CompanyCode);
                    await _logSink.WriteAsync(new LogEntry
                    {
                        FechaHora = DateTimeOffset.UtcNow,
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
        var repositorio = WarehouseTransferRepositoryFactory.Crear(compania.EngineType);

        var connectionString = repositorio.ConnectionString(
            compania.Host, compania.Port, compania.Schema, compania.DbUserId,
            _secretoCifradoService.Descifrar(compania.DbSecretCifrado), compania.DatabaseEncryptada);

        var documentosPendientes = repositorio.ObtenerDocumentosPendientes(connectionString, compania.HeaderQuerySource);
        if (documentosPendientes.Count == 0)
        {
            return;
        }

        using var serviceLayer = new ServiceLayerClient(compania.ServiceLayerUrl, compania.ToleraNombreCertificadoServiceLayer);
        // Legado: SBOClases usaba SociedadSetting.CompanyDb para el login. En B1 sobre
        // HANA el schema de la compañía y el CompanyDB de Service Layer son el mismo
        // valor (ver appsettings de comercialdepor: Schema="CLPRDDEPOR" == CompanyDb del
        // legado) -- no se agrega un campo redundante a CompanyConnectionConfig por esto.
        await serviceLayer.LoginAsync(
            compania.Schema, compania.ServiceLayerUsername,
            _secretoCifradoService.Descifrar(compania.ServiceLayerSecretCifrado), ct);

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

            var huboPosteoExitoso = false;
            var huboRechazoDeSap = false;

            for (var interaccion = 1; interaccion <= compania.WarehousePriorityCount; interaccion++)
            {
                var lineasAsignadas = repositorio.ObtenerAsignacionBodega(
                    connectionString, compania.WarehouseAssignmentProcedure, documento.DocEntry,
                    tipoDocumento.TablaDetalle, interaccion);

                var lineasATransferir = lineasAsignadas
                    .Where(l => l.Transferencia is > 0)
                    .Select(l => new StockTransferLine
                    {
                        ItemCode = l.ItemCode
                            ?? throw new InvalidOperationException($"{compania.WarehouseAssignmentProcedure} devolvió Transferencia > 0 con ItemCode NULL (DocEntry {documento.DocEntry}, prioridad {interaccion})."),
                        Quantity = l.Transferencia!.Value,
                        WarehouseCode = l.WhsCode
                            ?? throw new InvalidOperationException($"{compania.WarehouseAssignmentProcedure} devolvió Transferencia > 0 con WhsCode NULL (DocEntry {documento.DocEntry}, prioridad {interaccion})."),
                        FromWarehouseCode = l.WhsCodeDesde
                            ?? throw new InvalidOperationException($"{compania.WarehouseAssignmentProcedure} devolvió Transferencia > 0 con WhsCodeDesde NULL (DocEntry {documento.DocEntry}, prioridad {interaccion}).")
                    })
                    .ToList();

                if (lineasATransferir.Count == 0)
                {
                    continue;
                }

                var stockTransfer = new StockTransferDocument
                {
                    CardCode = documento.CardCode,
                    StockTransferLines = lineasATransferir,
                    DocumentReferences =
                    {
                        new StockTransferDocumentReference
                        {
                            RefDocEntr = documento.DocEntry,
                            RefObjType = tipoDocumento.ServiceLayerRefObjType
                        }
                    }
                };

                try
                {
                    var creado = await serviceLayer.PostearStockTransferAsync(stockTransfer, ct);
                    huboPosteoExitoso = true;
                    await _logSink.WriteAsync(new LogEntry
                    {
                        FechaHora = DateTimeOffset.UtcNow,
                        Nivel = NivelLog.Info,
                        CompanyCode = compania.CompanyCode,
                        Mensaje = $"StockTransfer creado para DocEntry {documento.DocEntry} (prioridad {interaccion})",
                        Detalle = $"ServiceLayer DocEntry={creado.DocEntry} DocNum={creado.DocNum}"
                    }, ct);
                }
                catch (ServiceLayerPostException ex)
                {
                    // SAP rechazó esta transferencia (ej. -10 "Quantity falls into negative
                    // inventory"). Igual que el legado (StrockTrans.cs: PostSL devolvía el error
                    // por 'ref er' sin lanzar), NO se aborta el ciclo -- se registra y se sigue
                    // con las demás prioridades y documentos. Lo que cambia respecto al legado
                    // (que marcaba U_NX_Auto_ABS='N' siempre) es la decisión de cierre de abajo.
                    huboRechazoDeSap = true;
                    _logger.LogWarning(
                        "SAP rechazó StockTransfer de DocEntry {DocEntry} (prioridad {Prioridad}, compañía {CompanyCode}): code {Codigo} -- {Mensaje}",
                        documento.DocEntry, interaccion, compania.CompanyCode, ex.SapErrorCode, ex.SapMessage);
                    await _logSink.WriteAsync(new LogEntry
                    {
                        FechaHora = DateTimeOffset.UtcNow,
                        Nivel = NivelLog.Error,
                        CompanyCode = compania.CompanyCode,
                        Mensaje = $"SAP rechazó StockTransfer para DocEntry {documento.DocEntry} (prioridad {interaccion})",
                        Detalle = $"HTTP {ex.HttpStatus} -- code {ex.SapErrorCode}: {ex.SapMessage}"
                    }, ct);
                }
            }

            // Cierre del documento. Legado: siempre marcaba CompletionUdfFieldName='N'. Acá:
            //   - Si algo se posteó bien, o SAP no rechazó nada -> se marca igual (además
            //     evita re-postear en el próximo ciclo las prioridades que sí entraron).
            //   - Si SAP rechazó y nada entró -> se cierra solo si el documento YA está en un
            //     picking (PKL1/OPKL); si no, se deja pendiente para reintentar el próximo
            //     ciclo (el stock puede reponerse). Criterio definido con el usuario.
            var marcarCompletado = huboPosteoExitoso || !huboRechazoDeSap;

            if (!marcarCompletado)
            {
                var enPicking = repositorio.DocumentoEstaEnPicking(
                    connectionString, documento.DocEntry, documento.ObjType);
                marcarCompletado = enPicking;

                _logger.LogInformation(
                    enPicking
                        ? "DocEntry {DocEntry} rechazado por SAP pero ya está en picking -- se cierra (estado 'N')"
                        : "DocEntry {DocEntry} rechazado por SAP y sin picking -- queda pendiente para el próximo ciclo",
                    documento.DocEntry);
            }

            if (marcarCompletado)
            {
                repositorio.MarcarDocumentoCompletado(
                    connectionString, tipoDocumento.TablaCabecera, compania.CompletionUdfFieldName, documento.DocEntry);
            }
        }
    }
}
