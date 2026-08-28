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
        // El filtro de Estado se aplica ANTES del Select, comparando el enum directamente
        // (x.Status == estado) -- así Npgsql lo traduce como "status = 'Enviado'" usando el
        // HasConversion<string> de la columna. Si en cambio se compara después de proyectar
        // a WmsTransaccionRow.Status (string), EF necesita traducir un x.Status.ToString()
        // explícito para poder filtrar, y esa traducción no existe: "Translation of method
        // 'object.ToString' failed" (confirmado en producción al filtrar por "Enviado").
        WmsSapStageStatus? estado = !string.IsNullOrWhiteSpace(filtro.Estado) && Enum.TryParse<WmsSapStageStatus>(filtro.Estado, out var parsed)
            ? parsed
            : null;

        var query = filtro.Tipo switch
        {
            WmsTipoTransaccion.EnvioProducto => _contexto.WmsSapStageItems.Where(x => x.CompanyId == companyId && (estado == null || x.Status == estado))
                .Select(x => new WmsTransaccionRow { LineId = x.LineId, Documento = x.ItemCode, Status = x.Status.ToString(), RetryCount = x.RetryCount, ErrorMsg = x.ErrorMsg, CreatedAt = x.CreatedAt, SyncedAt = x.SyncedAt, ItemName = x.ItemName, BarCode = x.BarCode }),
            WmsTipoTransaccion.EnvioSucursal => _contexto.WmsSapStageStores.Where(x => x.CompanyId == companyId && (estado == null || x.Status == estado))
                .Select(x => new WmsTransaccionRow { LineId = x.LineId, Documento = x.Pk, Status = x.Status.ToString(), RetryCount = x.RetryCount, ErrorMsg = x.ErrorMsg, CreatedAt = x.CreatedAt, SyncedAt = x.SyncedAt }),
            WmsTipoTransaccion.EnvioOrdenes => _contexto.WmsSapStageOrderHdrs.Where(x => x.CompanyId == companyId && (estado == null || x.Status == estado))
                .Select(x => new WmsTransaccionRow { LineId = x.LineId, Documento = x.OrderNbr, Status = x.Status.ToString(), RetryCount = x.RetryCount, ErrorMsg = x.ErrorMsg, CreatedAt = x.CreatedAt, SyncedAt = x.SyncedAt }),
            WmsTipoTransaccion.EnvioIngresoAsn => _contexto.WmsSapStageInboundHdrs.Where(x => x.CompanyId == companyId && (estado == null || x.Status == estado))
                .Select(x => new WmsTransaccionRow { LineId = x.LineId, Documento = x.SapDocEntry.ToString(), Status = x.Status.ToString(), RetryCount = x.RetryCount, ErrorMsg = x.ErrorMsg, CreatedAt = x.CreatedAt, SyncedAt = x.SyncedAt }),
            _ => throw new NotSupportedException($"WmsTransaccionService no soporta el tipo '{filtro.Tipo}' (usar WmsConfirmacionService para confirmaciones)."),
        };

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

        if (filtro.Tipo == WmsTipoTransaccion.EnvioProducto && items.Count > 0)
        {
            await PoblarMarcaAsync(companyId, items, cancellationToken);
        }

        return new WmsPagedResult<WmsTransaccionRow> { Items = items, TotalCount = total, Page = filtro.Page, PageSize = filtro.PageSize };
    }

    /// <summary>
    /// La marca (brand_code) vive dentro de extra_fields (jsonb, campos dinámicos de la Query SQL --
    /// ver WmsSapStageItemWriter). No se puede proyectar en la misma consulta LINQ de arriba sin forzar
    /// a Postgres a traer y parsear el jsonb completo de TODA la tabla antes de paginar, así que se
    /// resuelve en una segunda consulta acotada solo a las filas de la página actual (25-100 líneas).
    /// </summary>
    private async Task PoblarMarcaAsync(Guid companyId, List<WmsTransaccionRow> items, CancellationToken cancellationToken)
    {
        var lineIds = items.Select(r => r.LineId).ToList();
        var extras = await _contexto.WmsSapStageItems
            .Where(x => x.CompanyId == companyId && lineIds.Contains(x.LineId))
            .Select(x => new { x.LineId, x.ExtraFieldsJson })
            .ToListAsync(cancellationToken);

        var marcaPorLineId = new Dictionary<long, string?>();
        foreach (var extra in extras)
        {
            if (string.IsNullOrEmpty(extra.ExtraFieldsJson)) continue;
            var campos = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(extra.ExtraFieldsJson)!;
            if (campos.TryGetValue("brand_code", out var valor))
            {
                marcaPorLineId[extra.LineId] = valor.ValueKind == System.Text.Json.JsonValueKind.String ? valor.GetString() : valor.ToString();
            }
        }

        foreach (var row in items)
        {
            row.Marca = marcaPorLineId.GetValueOrDefault(row.LineId);
        }
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
