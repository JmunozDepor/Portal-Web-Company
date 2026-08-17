using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Services;

/// <summary>
/// Confirma el éxito real de un registro en estado Enviado consultando la entidad
/// FINAL en LGFAPI (item/facility/order_hdr/ib_shipment, no las stage_* -- Oracle saca
/// el registro de stage_* una vez que termina de procesarlo). Puerto directo de
/// WmsOutbound_ExistsProcessor del legado (WMS_Suite, fuera de este repo). Reintenta
/// hasta MaxIntentos veces (mismo tope que el legado) antes de dar error definitivo.
/// Mismo patrón de scoping que WmsSlshStageParser.
/// </summary>
public sealed class WmsExistsReconciler : BackgroundService
{
    private static readonly TimeSpan IntervaloCiclo = TimeSpan.FromSeconds(300);
    private const int MaxIntentos = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WmsExistsReconciler> _logger;

    public WmsExistsReconciler(IServiceScopeFactory scopeFactory, ILogger<WmsExistsReconciler> logger)
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
                _logger.LogError(ex, "Error inesperado en el ciclo de confirmación de éxito WMS");
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado al procesar confirmación de éxito WMS para la compañía {CompanyId}", compania.CompanyId);
            }
        }
    }

    private async Task ProcesarCompaniaAsync(Guid companyId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IIntegrationConnectorConfigService>();
        var configJson = await configService.GetDecryptedConfigAsync(companyId, "Wms", "WmsCloud", cancellationToken);
        if (configJson is null)
        {
            return;
        }

        var config = System.Text.Json.JsonSerializer.Deserialize<WmsCloudConfigParaReconciliacion>(configJson);
        if (config is null || string.IsNullOrWhiteSpace(config.LgfApiBaseUrl))
        {
            return;
        }

        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var validador = scope.ServiceProvider.GetRequiredService<IWmsValidationApiClient>();

        await ProcesarEntidadAsync(contexto, validador, config, companyId, "Item", "item", "stage_item", "item_alternate_code",
            contexto.WmsSapStageItems.Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Enviado).ToList,
            f => f.ItemCode, (f) => { f.Status = WmsSapStageStatus.ProcesadoWms; f.SyncedAt = DateTimeOffset.UtcNow; },
            (f) => f.Status = WmsSapStageStatus.ErrorWms, cancellationToken);

        await ProcesarEntidadAsync(contexto, validador, config, companyId, "Store", "facility", "stage_store", "code",
            contexto.WmsSapStageStores.Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Enviado).ToList,
            f => f.CardCode, (f) => { f.Status = WmsSapStageStatus.ProcesadoWms; f.SyncedAt = DateTimeOffset.UtcNow; },
            (f) => f.Status = WmsSapStageStatus.ErrorWms, cancellationToken);

        await ProcesarEntidadAsync(contexto, validador, config, companyId, "Order", "order_hdr", "stage_order_hdr", "order_nbr",
            contexto.WmsSapStageOrderHdrs.Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Enviado).ToList,
            f => f.OrderNbr, (f) => { f.Status = WmsSapStageStatus.ProcesadoWms; f.SyncedAt = DateTimeOffset.UtcNow; },
            (f) => f.Status = WmsSapStageStatus.ErrorWms, cancellationToken);

        await ProcesarEntidadAsync(contexto, validador, config, companyId, "IbShipment", "ib_shipment", "stage_ib_shipment", "shipment_nbr",
            contexto.WmsSapStageInboundHdrs.Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Enviado).ToList,
            f => f.SapDocEntry.ToString(), (f) => { f.Status = WmsSapStageStatus.ProcesadoWms; f.SyncedAt = DateTimeOffset.UtcNow; },
            (f) => f.Status = WmsSapStageStatus.ErrorWms, cancellationToken);
    }

    private async Task ProcesarEntidadAsync<TFila>(
        WmsDbContext contexto, IWmsValidationApiClient validador, WmsCloudConfigParaReconciliacion config, Guid companyId,
        string tipoDoc, string entidadFinal, string entidadStage, string keyField,
        Func<List<TFila>> obtenerPendientes, Func<TFila, string> obtenerClave,
        Action<TFila> marcarConfirmado, Action<TFila> marcarErrorDefinitivo,
        CancellationToken cancellationToken)
        where TFila : class
    {
        var pendientes = obtenerPendientes();
        foreach (var fila in pendientes)
        {
            var clave = obtenerClave(fila);

            // Paso 1: si el registro TODAVÍA está en stage_* (LGFAPI, filtrado por URL igual
            // que WmsStageErrorReconciler), Oracle no terminó de procesar este envío/reenvío
            // -- no hay nada que confirmar todavía. Se salta el ciclo sin tocar Intentos para
            // no penalizar un reenvío que recién empieza.
            var enStage = await validador.CheckStageRecordAsync(
                config.LgfApiBaseUrl!, config.Usuario, config.Clave, entidadStage, keyField, clave, config.ParentCompanyCode,
                filtrarPorUrl: true, cancellationToken);
            if (enStage.Found)
            {
                continue;
            }

            // Paso 2: ya no está en stage_* -- Oracle terminó de procesarlo. Recién ahora tiene
            // sentido confirmar contra la entidad FINAL (o contar como intento fallido si
            // tampoco aparece ahí).
            var resultado = await validador.CheckStageRecordAsync(
                config.LgfApiBaseUrl!, config.Usuario, config.Clave, entidadFinal, keyField, clave, config.ParentCompanyCode,
                filtrarPorUrl: false, cancellationToken);

            var validacion = await contexto.WmsExportValidations
                .FirstOrDefaultAsync(v => v.CompanyId == companyId && v.TipoDoc == tipoDoc && v.Clave == clave, cancellationToken);
            if (validacion is null)
            {
                validacion = new WmsExportValidation { CompanyId = companyId, TipoDoc = tipoDoc, Clave = clave };
                contexto.WmsExportValidations.Add(validacion);
            }

            if (resultado.Found)
            {
                marcarConfirmado(fila);
                validacion.WmsStatusId = resultado.StatusId;
                validacion.ValidadoEn = DateTimeOffset.UtcNow;
                continue;
            }

            validacion.Intentos++;
            if (validacion.Intentos >= MaxIntentos)
            {
                marcarErrorDefinitivo(fila);
                validacion.WmsErrorMsg = $"No confirmado en Oracle WMS Cloud tras {validacion.Intentos} intentos.";
                validacion.ValidadoEn = DateTimeOffset.UtcNow;
            }
        }

        await contexto.SaveChangesAsync(cancellationToken);
    }

    private sealed record WmsCloudConfigParaReconciliacion(string Usuario, string Clave, string ParentCompanyCode, string? LgfApiBaseUrl);
}
