using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public class WmsTransaccionService : IWmsTransaccionService
{
    private readonly WmsDbContext _contexto;

    public WmsTransaccionService(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<WmsPagedResult<WmsTransaccionRow>> BuscarAsync(Guid companyId, WmsTransaccionFiltro filtro, CancellationToken cancellationToken)
    {
        var query = filtro.Tipo switch
        {
            WmsTipoTransaccion.EnvioProducto => _contexto.WmsSapStageItems.Where(x => x.CompanyId == companyId)
                .Select(x => new WmsTransaccionRow { LineId = x.LineId, Documento = x.ItemCode, Status = x.Status.ToString(), RetryCount = x.RetryCount, ErrorMsg = x.ErrorMsg, CreatedAt = x.CreatedAt, SyncedAt = x.SyncedAt }),
            WmsTipoTransaccion.EnvioSucursal => _contexto.WmsSapStageStores.Where(x => x.CompanyId == companyId)
                .Select(x => new WmsTransaccionRow { LineId = x.LineId, Documento = x.Pk, Status = x.Status.ToString(), RetryCount = x.RetryCount, ErrorMsg = x.ErrorMsg, CreatedAt = x.CreatedAt, SyncedAt = x.SyncedAt }),
            WmsTipoTransaccion.EnvioOrdenes => _contexto.WmsSapStageOrderHdrs.Where(x => x.CompanyId == companyId)
                .Select(x => new WmsTransaccionRow { LineId = x.LineId, Documento = x.OrderNbr, Status = x.Status.ToString(), RetryCount = x.RetryCount, ErrorMsg = x.ErrorMsg, CreatedAt = x.CreatedAt, SyncedAt = x.SyncedAt }),
            WmsTipoTransaccion.EnvioIngresoAsn => _contexto.WmsSapStageInboundHdrs.Where(x => x.CompanyId == companyId)
                .Select(x => new WmsTransaccionRow { LineId = x.LineId, Documento = x.SapDocEntry.ToString(), Status = x.Status.ToString(), RetryCount = x.RetryCount, ErrorMsg = x.ErrorMsg, CreatedAt = x.CreatedAt, SyncedAt = x.SyncedAt }),
            _ => throw new NotSupportedException($"WmsTransaccionService no soporta el tipo '{filtro.Tipo}' (usar WmsConfirmacionService para confirmaciones)."),
        };

        if (!string.IsNullOrWhiteSpace(filtro.Estado))
        {
            query = query.Where(r => r.Status == filtro.Estado);
        }
        if (!string.IsNullOrWhiteSpace(filtro.Documento))
        {
            query = query.Where(r => r.Documento.Contains(filtro.Documento));
        }
        if (filtro.Desde.HasValue)
        {
            var desde = new DateTimeOffset(filtro.Desde.Value, TimeSpan.Zero);
            query = query.Where(r => r.CreatedAt >= desde);
        }
        if (filtro.Hasta.HasValue)
        {
            var hasta = new DateTimeOffset(filtro.Hasta.Value.AddDays(1), TimeSpan.Zero);
            query = query.Where(r => r.CreatedAt < hasta);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((filtro.Page - 1) * filtro.PageSize)
            .Take(filtro.PageSize)
            .ToListAsync(cancellationToken);

        return new WmsPagedResult<WmsTransaccionRow> { Items = items, TotalCount = total, Page = filtro.Page, PageSize = filtro.PageSize };
    }

    public async Task ResetearAsync(Guid companyId, WmsTipoTransaccion tipo, IReadOnlyList<long> lineIds, CancellationToken cancellationToken)
    {
        switch (tipo)
        {
            case WmsTipoTransaccion.EnvioProducto:
                await ResetearTablaAsync(_contexto.WmsSapStageItems.Where(x => x.CompanyId == companyId && lineIds.Contains(x.LineId)), cancellationToken);
                break;
            case WmsTipoTransaccion.EnvioSucursal:
                await ResetearTablaAsync(_contexto.WmsSapStageStores.Where(x => x.CompanyId == companyId && lineIds.Contains(x.LineId)), cancellationToken);
                break;
            case WmsTipoTransaccion.EnvioOrdenes:
                await ResetearTablaAsync(_contexto.WmsSapStageOrderHdrs.Where(x => x.CompanyId == companyId && lineIds.Contains(x.LineId)), cancellationToken);
                break;
            case WmsTipoTransaccion.EnvioIngresoAsn:
                await ResetearTablaAsync(_contexto.WmsSapStageInboundHdrs.Where(x => x.CompanyId == companyId && lineIds.Contains(x.LineId)), cancellationToken);
                break;
            default:
                throw new NotSupportedException($"WmsTransaccionService no soporta resetear el tipo '{tipo}'.");
        }
    }

    private async Task ResetearTablaAsync(IQueryable<WmsSapStageItem> filas, CancellationToken ct)
    {
        foreach (var fila in await filas.ToListAsync(ct)) { fila.Status = WmsSapStageStatus.Pendiente; fila.ErrorMsg = null; }
        await _contexto.SaveChangesAsync(ct);
    }

    private async Task ResetearTablaAsync(IQueryable<WmsSapStageStore> filas, CancellationToken ct)
    {
        foreach (var fila in await filas.ToListAsync(ct)) { fila.Status = WmsSapStageStatus.Pendiente; fila.ErrorMsg = null; }
        await _contexto.SaveChangesAsync(ct);
    }

    private async Task ResetearTablaAsync(IQueryable<WmsSapStageOrderHdr> filas, CancellationToken ct)
    {
        foreach (var fila in await filas.ToListAsync(ct)) { fila.Status = WmsSapStageStatus.Pendiente; fila.ErrorMsg = null; }
        await _contexto.SaveChangesAsync(ct);
    }

    private async Task ResetearTablaAsync(IQueryable<WmsSapStageInboundHdr> filas, CancellationToken ct)
    {
        foreach (var fila in await filas.ToListAsync(ct)) { fila.Status = WmsSapStageStatus.Pendiente; fila.ErrorMsg = null; }
        await _contexto.SaveChangesAsync(ct);
    }
}
