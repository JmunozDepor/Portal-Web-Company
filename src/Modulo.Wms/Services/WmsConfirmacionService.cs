using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

/// <summary>
/// A diferencia de WmsTransaccionService (una fila por línea), acá se agrupa
/// por documento (order_hdr_cust_field_4 para SLSH / shipment_nbr para SVSH)
/// porque las confirmaciones se resetean/monitorean a nivel de documento
/// completo, no de línea individual.
/// </summary>
public class WmsConfirmacionService : IWmsConfirmacionService
{
    private readonly WmsDbContext _contexto;

    public WmsConfirmacionService(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<WmsPagedResult<WmsConfirmacionRow>> BuscarAsync(Guid companyId, WmsConfirmacionFiltro filtro, CancellationToken cancellationToken)
    {
        List<WmsConfirmacionRow> agrupado;

        if (filtro.Tipo == WmsTipoTransaccion.ConfirmacionOrdenes)
        {
            var desde = DateTimeOffset.UtcNow.AddDays(-7);
            var filas = await (from f in _contexto.WmsOracleStageSlsh
                                join s in _contexto.WmsOracleInboundStages on f.ParentId equals s.Id
                                where s.CompanyId == companyId && s.InsertedAt >= desde
                                select f).ToListAsync(cancellationToken);

            agrupado = filas.GroupBy(f => f.order_hdr_cust_field_4 ?? "(sin número)")
                .Select(g => new WmsConfirmacionRow
                {
                    Documento = g.Key,
                    Status = g.First().Status.ToString(),
                    LineasCount = g.Count(),
                    ErrorMsg = g.FirstOrDefault(x => x.ErrorMsg != null)?.ErrorMsg,
                    LineIds = g.Select(x => x.LineId).ToList(),
                }).ToList();
        }
        else if (filtro.Tipo == WmsTipoTransaccion.ConfirmacionIngreso)
        {
            var desde = DateTimeOffset.UtcNow.AddDays(-7);
            var filas = await (from f in _contexto.WmsOracleStageSvsh
                                join s in _contexto.WmsOracleInboundStages on f.ParentId equals s.Id
                                where s.CompanyId == companyId && s.InsertedAt >= desde
                                select f).ToListAsync(cancellationToken);

            agrupado = filas.GroupBy(f => f.shipment_nbr ?? "(sin número)")
                .Select(g => new WmsConfirmacionRow
                {
                    Documento = g.Key,
                    Status = g.First().Status.ToString(),
                    LineasCount = g.Count(),
                    ErrorMsg = g.FirstOrDefault(x => x.ErrorMsg != null)?.ErrorMsg,
                    LineIds = g.Select(x => x.LineId).ToList(),
                }).ToList();
        }
        else
        {
            throw new NotSupportedException($"WmsConfirmacionService no soporta el tipo '{filtro.Tipo}'.");
        }

        if (!string.IsNullOrWhiteSpace(filtro.Estado))
        {
            agrupado = agrupado.Where(r => r.Status == filtro.Estado).ToList();
        }
        if (!string.IsNullOrWhiteSpace(filtro.Documento))
        {
            agrupado = agrupado.Where(r => r.Documento.Contains(filtro.Documento, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        agrupado = agrupado.Take(200).ToList();

        return new WmsPagedResult<WmsConfirmacionRow> { Items = agrupado, TotalCount = agrupado.Count, Page = 1, PageSize = Math.Max(agrupado.Count, 1) };
    }

    public async Task ResetearAsync(Guid companyId, WmsTipoTransaccion tipo, string documento, CancellationToken cancellationToken)
    {
        if (tipo == WmsTipoTransaccion.ConfirmacionOrdenes)
        {
            var filas = await (from f in _contexto.WmsOracleStageSlsh
                                join s in _contexto.WmsOracleInboundStages on f.ParentId equals s.Id
                                where s.CompanyId == companyId && f.order_hdr_cust_field_4 == documento
                                select f).ToListAsync(cancellationToken);
            foreach (var fila in filas) { fila.Status = WmsSlshStatus.Pendiente; fila.ErrorMsg = null; }
        }
        else if (tipo == WmsTipoTransaccion.ConfirmacionIngreso)
        {
            var filas = await (from f in _contexto.WmsOracleStageSvsh
                                join s in _contexto.WmsOracleInboundStages on f.ParentId equals s.Id
                                where s.CompanyId == companyId && f.shipment_nbr == documento
                                select f).ToListAsync(cancellationToken);
            foreach (var fila in filas) { fila.Status = WmsSvshStatus.Pendiente; fila.ErrorMsg = null; }
        }
        else
        {
            throw new NotSupportedException($"WmsConfirmacionService no soporta resetear el tipo '{tipo}'.");
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }
}
