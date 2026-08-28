using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Lector del motor genérico (IIntegrationEntityReader) para el staging de Artículos
/// pendientes de enviar a Oracle WMS Cloud (etapa Subida de la Ronda C). Usa el
/// LineId de la propia fila como "_StagingLineIds" de una sola posición -- mismo
/// campo interno que WmsSlshInventoryReader (Ronda B), reutilizado acá aunque el
/// grupo siempre tenga tamaño 1 (no hay agrupación como en Traslados).
/// </summary>
public class WmsSapStageItemReader : IIntegrationEntityReader
{
    /// <summary>
    /// El legado (WmsOutbound_ItemProcessor) nunca procesaba el backlog completo de una
    /// sola vez -- cada ciclo de su propio loop (cada MasterDataIntervalSeconds, ej. 300s)
    /// traía como máximo BatchSize filas (`LIMIT {batchSize}` en el SQL) y dejaba el resto
    /// para el ciclo siguiente, repartiendo la carga en el tiempo. El motor genérico nuevo
    /// no tiene un loop propio por entidad -- usa el mismo timeout de 300s compartido con
    /// Bajada (IntegrationSyncHostedService.TimeoutPorIntegracion) para TODA la corrida de
    /// PushAsync. Sin este límite, un backlog grande (ej. 29.387 items tras un backfill
    /// completo) hace que PushAsync nunca retorne dentro de los 300s -- el timeout aborta
    /// la corrida ANTES de que se llame MarcarProcesadoAsync ni una sola vez, así que
    /// ningún envío exitoso queda registrado aunque Oracle WMS Cloud ya los haya aceptado
    /// (confirmado 22 ago 2026: ~14.850 HTTP 200 reales, 0 filas marcadas Enviado). Topar
    /// acá replica la misma filosofía del legado -- el polling de 1 minuto ya existente
    /// reparte un backlog grande en varios ciclos en vez de intentar vaciarlo de una vez.
    /// Configurable por integración (WmsCloudConfig.MaxRecordsPerCycle, ver
    /// IntegrationSyncHostedService); este valor es solo el fallback cuando no se configuró.
    /// </summary>
    private const int MaximoPorCicloPorDefecto = 500;

    private readonly WmsDbContext _contexto;

    public WmsSapStageItemReader(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Item.Subida";

    public async Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, int? limiteMaximo, CancellationToken cancellationToken)
    {
        var maximoPorCiclo = limiteMaximo is > 0 ? limiteMaximo.Value : MaximoPorCicloPorDefecto;
        var filas = await _contexto.WmsSapStageItems
            .Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Pendiente)
            .OrderBy(f => f.CreatedAt)
            .Take(maximoPorCiclo)
            .ToListAsync(cancellationToken);

        return filas
            .Select(f =>
            {
                var campos = new Dictionary<string, object?>
                {
                    ["TipoDocumento"] = "Item",
                    ["item_alternate_code"] = f.ItemCode,
                    ["description"] = f.ItemName,
                    ["barcode"] = f.BarCode,
                    ["_StagingLineIds"] = new List<long> { f.LineId },
                };

                if (!string.IsNullOrEmpty(f.ExtraFieldsJson))
                {
                    var extra = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(f.ExtraFieldsJson)!;
                    foreach (var (clave, valor) in extra)
                    {
                        campos[clave] = valor;
                    }
                }

                return new IntegrationRecord(campos);
            })
            .ToList();
    }

    public async Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken)
    {
        var idsDeLinea = (List<long>)registro["_StagingLineIds"]!;

        var filas = await _contexto.WmsSapStageItems
            .Where(f => idsDeLinea.Contains(f.LineId))
            .ToListAsync(cancellationToken);

        foreach (var fila in filas)
        {
            fila.Status = exito ? WmsSapStageStatus.Enviado : WmsSapStageStatus.ErrorWms;
            fila.ErrorMsg = exito ? null : mensajeError;
            fila.SyncedAt = exito ? DateTimeOffset.UtcNow : fila.SyncedAt;

            if (exito)
            {
                await ResetearValidacionAsync(companyId, fila.ItemCode, cancellationToken);
            }
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Al (re)enviar exitosamente, resetea la fila de validación existente (clave de negocio
    /// estable entre reenvíos) para que WmsExistsReconciler no herede Intentos de un ciclo
    /// anterior y dispare ErrorWms de inmediato en el primer chequeo del reenvío.
    /// </summary>
    private async Task ResetearValidacionAsync(Guid companyId, string clave, CancellationToken cancellationToken)
    {
        var validacion = await _contexto.WmsExportValidations
            .FirstOrDefaultAsync(v => v.CompanyId == companyId && v.TipoDoc == "Item" && v.Clave == clave, cancellationToken);
        if (validacion is null)
        {
            validacion = new WmsExportValidation { CompanyId = companyId, TipoDoc = "Item", Clave = clave };
            _contexto.WmsExportValidations.Add(validacion);
        }

        validacion.Intentos = 0;
        validacion.WmsErrorMsg = null;
        validacion.ValidadoEn = null;
        validacion.WmsStatusId = null;
        validacion.EnviadoEn = DateTimeOffset.UtcNow;
    }
}
