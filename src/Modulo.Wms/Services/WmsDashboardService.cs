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

        var porTipo = new List<WmsEstadisticaTipo>
        {
            await ContarAsync(WmsTipoTransaccion.EnvioProducto,
                await _contexto.WmsSapStageItems.Where(x => x.CompanyId == companyId && x.CreatedAt >= desde).Select(x => (int)x.Status).ToListAsync(cancellationToken)),
            await ContarAsync(WmsTipoTransaccion.EnvioSucursal,
                await _contexto.WmsSapStageStores.Where(x => x.CompanyId == companyId && x.CreatedAt >= desde).Select(x => (int)x.Status).ToListAsync(cancellationToken)),
            await ContarAsync(WmsTipoTransaccion.EnvioOrdenes,
                await _contexto.WmsSapStageOrderHdrs.Where(x => x.CompanyId == companyId && x.CreatedAt >= desde).Select(x => (int)x.Status).ToListAsync(cancellationToken)),
            await ContarAsync(WmsTipoTransaccion.EnvioIngresoAsn,
                await _contexto.WmsSapStageInboundHdrs.Where(x => x.CompanyId == companyId && x.CreatedAt >= desde).Select(x => (int)x.Status).ToListAsync(cancellationToken)),
        };

        var confirmacionOrdenes = await ContarConfirmacionAsync(WmsTipoTransaccion.ConfirmacionOrdenes,
            await (from f in _contexto.WmsOracleStageSlsh
                   join s in _contexto.WmsOracleInboundStages on f.ParentId equals s.Id
                   where s.CompanyId == companyId && s.InsertedAt >= desde
                   select (int)f.Status).ToListAsync(cancellationToken));
        var confirmacionIngreso = await ContarConfirmacionAsync(WmsTipoTransaccion.ConfirmacionIngreso,
            await (from f in _contexto.WmsOracleStageSvsh
                   join s in _contexto.WmsOracleInboundStages on f.ParentId equals s.Id
                   where s.CompanyId == companyId && s.InsertedAt >= desde
                   select (int)f.Status).ToListAsync(cancellationToken));

        porTipo.Add(confirmacionOrdenes);
        porTipo.Add(confirmacionIngreso);

        foreach (var estadistica in porTipo)
        {
            if (WmsTipoTransaccionInfo.ProcessorKeys.TryGetValue(estadistica.Tipo, out var processorKey)
                && heartbeats.TryGetValue(processorKey, out var heartbeat))
            {
                estadistica.UltimaEjecucion = heartbeat.LastRunAt;
                estadistica.UltimoEstado = heartbeat.Status;
            }
        }

        return new WmsDashboardResumen
        {
            TotalTransacciones = porTipo.Sum(t => t.Total),
            TotalOk = porTipo.Sum(t => t.Ok),
            TotalError = porTipo.Sum(t => t.Error),
            TotalPendiente = porTipo.Sum(t => t.Pendiente),
            PorTipo = porTipo,
        };
    }

    private static Task<WmsEstadisticaTipo> ContarAsync(WmsTipoTransaccion tipo, List<int> statuses)
    {
        // WmsSapStageStatus: Pendiente=0, Enviado=1, ProcesadoWms=2, ErrorWms=3.
        var resultado = new WmsEstadisticaTipo
        {
            Tipo = tipo,
            Pendiente = statuses.Count(s => s == (int)WmsSapStageStatus.Pendiente || s == (int)WmsSapStageStatus.Enviado),
            Ok = statuses.Count(s => s == (int)WmsSapStageStatus.ProcesadoWms),
            Error = statuses.Count(s => s == (int)WmsSapStageStatus.ErrorWms),
        };
        return Task.FromResult(resultado);
    }

    private static Task<WmsEstadisticaTipo> ContarConfirmacionAsync(WmsTipoTransaccion tipo, List<int> statuses)
    {
        // WmsSlshStatus/WmsSvshStatus comparten forma: Pendiente=0, ProcesadoSap=1, ErrorSap=2.
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
