using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Services;

/// <summary>
/// Detecta rechazos explícitos de Oracle WMS Cloud para registros en estado Enviado --
/// consulta LGFAPI (entidades stage_*) filtrando status_id=101 (Failed, terminal, sin
/// reintentos). Puerto directo de WmsOutbound_StageErrorProcessor del legado
/// (WMS_Suite, fuera de este repo). Mismo patrón de scoping que WmsSlshStageParser
/// (ICurrentCompanyOverride fijado antes de resolver WmsDbContext -- ver ese archivo
/// para el porqué, ya corregido en este repo).
/// </summary>
public sealed class WmsStageErrorReconciler : BackgroundService
{
    private static readonly TimeSpan IntervaloCiclo = TimeSpan.FromSeconds(60);
    private const int StatusIdRechazado = 101;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WmsStageErrorReconciler> _logger;

    public WmsStageErrorReconciler(IServiceScopeFactory scopeFactory, ILogger<WmsStageErrorReconciler> logger)
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
                _logger.LogError(ex, "Error inesperado en el ciclo de detección de rechazos WMS");
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

        await ProcesarEntidadAsync(contexto, validador, config, companyId, "Item", "stage_item", "item_alternate_code",
            contexto.WmsSapStageItems.Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Enviado).ToList,
            f => f.ItemCode, (f, msg) => { f.Status = WmsSapStageStatus.ErrorWms; f.ErrorMsg = msg; }, cancellationToken);

        await ProcesarEntidadAsync(contexto, validador, config, companyId, "Store", "stage_store", "code",
            contexto.WmsSapStageStores.Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Enviado).ToList,
            f => f.CardCode, (f, msg) => { f.Status = WmsSapStageStatus.ErrorWms; f.ErrorMsg = msg; }, cancellationToken);

        await ProcesarEntidadAsync(contexto, validador, config, companyId, "Order", "stage_order_hdr", "order_nbr",
            contexto.WmsSapStageOrderHdrs.Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Enviado).ToList,
            f => f.OrderNbr, (f, msg) => { f.Status = WmsSapStageStatus.ErrorWms; f.ErrorMsg = msg; }, cancellationToken);

        await ProcesarEntidadAsync(contexto, validador, config, companyId, "IbShipment", "stage_ib_shipment", "shipment_nbr",
            contexto.WmsSapStageInboundHdrs.Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Enviado).ToList,
            f => f.SapDocEntry.ToString(), (f, msg) => { f.Status = WmsSapStageStatus.ErrorWms; f.ErrorMsg = msg; }, cancellationToken);
    }

    private async Task ProcesarEntidadAsync<TFila>(
        WmsDbContext contexto, IWmsValidationApiClient validador, WmsCloudConfigParaReconciliacion config, Guid companyId,
        string tipoDoc, string entidadStage, string keyField,
        Func<List<TFila>> obtenerPendientes, Func<TFila, string> obtenerClave, Action<TFila, string?> marcarError,
        CancellationToken cancellationToken)
        where TFila : class
    {
        var pendientes = obtenerPendientes();
        foreach (var fila in pendientes)
        {
            var clave = obtenerClave(fila);
            var resultado = await validador.CheckStageRecordAsync(
                config.LgfApiBaseUrl!, config.Usuario, config.Clave, entidadStage, keyField, clave, config.ParentCompanyCode,
                filtrarPorUrl: true, cancellationToken);

            if (!resultado.Found || resultado.StatusId != StatusIdRechazado)
            {
                continue;
            }

            marcarError(fila, resultado.ErrorMessage);

            var validacion = await contexto.WmsExportValidations
                .FirstOrDefaultAsync(v => v.CompanyId == companyId && v.TipoDoc == tipoDoc && v.Clave == clave, cancellationToken);
            if (validacion is null)
            {
                validacion = new Models.WmsExportValidation { CompanyId = companyId, TipoDoc = tipoDoc, Clave = clave };
                contexto.WmsExportValidations.Add(validacion);
            }
            validacion.WmsStatusId = resultado.StatusId;
            validacion.WmsErrorMsg = resultado.ErrorMessage;
            validacion.ValidadoEn = DateTimeOffset.UtcNow;
        }

        await contexto.SaveChangesAsync(cancellationToken);
    }

    private sealed record WmsCloudConfigParaReconciliacion(string Usuario, string Clave, string ParentCompanyCode, string? LgfApiBaseUrl);
}
