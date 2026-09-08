using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public class WmsDashboardService : IWmsDashboardService
{
    private readonly WmsDbContext _contexto;

    public WmsDashboardService(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<WmsDashboardResumen> ObtenerResumenAsync(Guid companyId, DateTime desde, CancellationToken cancellationToken)
    {
        var heartbeats = await _contexto.ServiceHeartbeats
            .Where(h => h.CompanyId == companyId)
            .ToDictionaryAsync(h => h.ProcessorKey, h => h, cancellationToken);

        // "Movimiento en la ventana" = la fila se creó O se sincronizó dentro del período.
        // Filtrar solo por CreatedAt dejaba "Hoy" en cero cuando el grueso del staging es
        // una carga histórica (created_at viejo) y la actividad real del día son
        // transiciones de estado (Pendiente -> Enviado, que setean synced_at) sobre esas
        // filas ya existentes -- ver WmsSapStage*Reader (synced_at = UtcNow al enviar OK).
        var porTipo = new List<WmsEstadisticaTipo>
        {
            await ContarAsync(WmsTipoTransaccion.EnvioProducto,
                (await _contexto.WmsSapStageItems.Where(x => x.CompanyId == companyId && (x.CreatedAt >= desde || (x.SyncedAt != null && x.SyncedAt >= desde))).Select(x => x.Status).ToListAsync(cancellationToken)).Select(s => (int)s).ToList()),
            await ContarAsync(WmsTipoTransaccion.EnvioSucursal,
                (await _contexto.WmsSapStageStores.Where(x => x.CompanyId == companyId && (x.CreatedAt >= desde || (x.SyncedAt != null && x.SyncedAt >= desde))).Select(x => x.Status).ToListAsync(cancellationToken)).Select(s => (int)s).ToList()),
            await ContarAsync(WmsTipoTransaccion.EnvioOrdenes,
                (await _contexto.WmsSapStageOrderHdrs.Where(x => x.CompanyId == companyId && (x.CreatedAt >= desde || (x.SyncedAt != null && x.SyncedAt >= desde))).Select(x => x.Status).ToListAsync(cancellationToken)).Select(s => (int)s).ToList()),
            await ContarAsync(WmsTipoTransaccion.EnvioIngresoAsn,
                (await _contexto.WmsSapStageInboundHdrs.Where(x => x.CompanyId == companyId && (x.CreatedAt >= desde || (x.SyncedAt != null && x.SyncedAt >= desde))).Select(x => x.Status).ToListAsync(cancellationToken)).Select(s => (int)s).ToList()),
        };

        var confirmacionOrdenes = await ContarConfirmacionAsync(WmsTipoTransaccion.ConfirmacionOrdenes,
            (await (from f in _contexto.WmsOracleStageSlsh
                   join s in _contexto.WmsOracleInboundStages on f.ParentId equals s.Id
                   where s.CompanyId == companyId && (s.InsertedAt >= desde || (s.ProcessedAt != null && s.ProcessedAt >= desde))
                   select f.Status).ToListAsync(cancellationToken)).Select(s => (int)s).ToList());
        var confirmacionIngreso = await ContarConfirmacionAsync(WmsTipoTransaccion.ConfirmacionIngreso,
            (await (from f in _contexto.WmsOracleStageSvsh
                   join s in _contexto.WmsOracleInboundStages on f.ParentId equals s.Id
                   where s.CompanyId == companyId && (s.InsertedAt >= desde || (s.ProcessedAt != null && s.ProcessedAt >= desde))
                   select f.Status).ToListAsync(cancellationToken)).Select(s => (int)s).ToList());

        porTipo.Add(confirmacionOrdenes);
        porTipo.Add(confirmacionIngreso);

        // Opción A: "última ejecución" del envío al WMS por tipo. El pipeline Bajada/Subida
        // lo corre el motor de Integraciones del Core (integration_run_logs, en la BD de
        // plataforma), inalcanzable desde el plugin. Proxy: la marca de tiempo más reciente
        // de una fila de ese tipo que se movió -- synced_at si se envió, si no created_at.
        // Responde "¿corrió el envío de este tipo y cuándo?" sin cruzar a la BD del Core.
        var ultimoMovimientoEnvio = new Dictionary<WmsTipoTransaccion, DateTimeOffset?>
        {
            [WmsTipoTransaccion.EnvioProducto] = await _contexto.WmsSapStageItems
                .Where(x => x.CompanyId == companyId)
                .Select(x => (DateTimeOffset?)(x.SyncedAt ?? x.CreatedAt)).MaxAsync(cancellationToken),
            [WmsTipoTransaccion.EnvioSucursal] = await _contexto.WmsSapStageStores
                .Where(x => x.CompanyId == companyId)
                .Select(x => (DateTimeOffset?)(x.SyncedAt ?? x.CreatedAt)).MaxAsync(cancellationToken),
            [WmsTipoTransaccion.EnvioOrdenes] = await _contexto.WmsSapStageOrderHdrs
                .Where(x => x.CompanyId == companyId)
                .Select(x => (DateTimeOffset?)(x.SyncedAt ?? x.CreatedAt)).MaxAsync(cancellationToken),
            [WmsTipoTransaccion.EnvioIngresoAsn] = await _contexto.WmsSapStageInboundHdrs
                .Where(x => x.CompanyId == companyId)
                .Select(x => (DateTimeOffset?)(x.SyncedAt ?? x.CreatedAt)).MaxAsync(cancellationToken),
        };

        foreach (var estadistica in porTipo)
        {
            // Confirmaciones: heartbeat real del BackgroundService que las procesa.
            if (WmsTipoTransaccionInfo.ProcessorKeys.TryGetValue(estadistica.Tipo, out var processorKey)
                && heartbeats.TryGetValue(processorKey, out var heartbeat))
            {
                estadistica.UltimaEjecucion = heartbeat.LastRunAt;
                estadistica.UltimoEstado = heartbeat.Status;
            }
            // Envíos: proxy por marca de tiempo de la última fila movida. UltimoEstado se
            // deja en null a propósito -- el estado OK/error de la corrida del motor no vive
            // acá y los errores ya tienen su propia columna.
            else if (ultimoMovimientoEnvio.TryGetValue(estadistica.Tipo, out var ts) && ts is not null)
            {
                estadistica.UltimaEjecucion = ts;
            }
        }

        var itemsProximosAFallar = await _contexto.WmsExportValidations
            .CountAsync(v => v.CompanyId == companyId && v.TipoDoc == "Item" && v.ValidadoEn == null && v.Intentos >= 15, cancellationToken);

        return new WmsDashboardResumen
        {
            TotalTransacciones = porTipo.Sum(t => t.Total),
            TotalOk = porTipo.Sum(t => t.Ok),
            TotalError = porTipo.Sum(t => t.Error),
            TotalPendiente = porTipo.Sum(t => t.Pendiente),
            TotalEnviado = porTipo.Sum(t => t.Enviado),
            PorTipo = porTipo,
            // Solo los BackgroundService propios del módulo WMS (lista blanca fija), y
            // SIEMPRE los 4 -- si alguno todavía no latió se muestra como "sin ejecutar".
            // Una fila espuria/legada en wms_oracle_service_heartbeats no entra acá.
            Procesadores = WmsProcesadoresDelModulo.Todos
                .Select(info =>
                {
                    heartbeats.TryGetValue(info.Key, out var hb);
                    return new WmsProcesadorEstado
                    {
                        ProcessorKey = info.Key,
                        Nombre = info.Nombre,
                        Etapa = info.Etapa,
                        CadaSegundos = info.CadaSegundos,
                        Status = hb?.Status,
                        LastRunAt = hb?.LastRunAt,
                        LastError = hb?.LastError,
                    };
                })
                .ToList(),
            ItemsEnviadosSinConfirmarProximosAFallar = itemsProximosAFallar,
        };
    }

    private static Task<WmsEstadisticaTipo> ContarAsync(WmsTipoTransaccion tipo, List<int> statuses)
    {
        // WmsSapStageStatus: Pendiente=0, Enviado=1, ProcesadoWms=2, ErrorWms=3.
        var resultado = new WmsEstadisticaTipo
        {
            Tipo = tipo,
            Pendiente = statuses.Count(s => s == (int)WmsSapStageStatus.Pendiente),
            Enviado = statuses.Count(s => s == (int)WmsSapStageStatus.Enviado),
            Ok = statuses.Count(s => s == (int)WmsSapStageStatus.ProcesadoWms),
            Error = statuses.Count(s => s == (int)WmsSapStageStatus.ErrorWms),
        };
        return Task.FromResult(resultado);
    }

    private static Task<WmsEstadisticaTipo> ContarConfirmacionAsync(WmsTipoTransaccion tipo, List<int> statuses)
    {
        // WmsSlshStatus/WmsSvshStatus comparten forma: Pendiente=0, ProcesadoSap=1, ErrorSap=2.
        // No tienen un estado "Enviado" intermedio propio (ver enums), por eso Enviado queda en 0 acá.
        var resultado = new WmsEstadisticaTipo
        {
            Tipo = tipo,
            Pendiente = statuses.Count(s => s == 0),
            Ok = statuses.Count(s => s == 1),
            Error = statuses.Count(s => s == 2),
        };
        return Task.FromResult(resultado);
    }
}
