using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public class WmsArchivoService : IWmsArchivoService
{
    private readonly WmsDbContext _contexto;

    public WmsArchivoService(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<WmsPagedResult<WmsOracleInboundStage>> ListarAsync(Guid companyId, string? tipoDoc, string? estado, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _contexto.WmsOracleInboundStages.Where(x => x.CompanyId == companyId);

        if (!string.IsNullOrWhiteSpace(tipoDoc))
        {
            query = query.Where(x => x.TipoDoc == tipoDoc);
        }
        if (!string.IsNullOrWhiteSpace(estado) && Enum.TryParse<WmsInboundEstado>(estado, out var estadoEnum))
        {
            query = query.Where(x => x.Estado == estadoEnum);
        }

        var total = await query.CountAsync(cancellationToken);
        if (pageSize <= 0)
        {
            pageSize = 25;
        }
        var totalPages = (int)Math.Ceiling((double)total / pageSize);
        page = Math.Clamp(page, 1, Math.Max(totalPages, 1));

        var items = await query
            .OrderByDescending(x => x.InsertedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new WmsPagedResult<WmsOracleInboundStage> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    public async Task ReintentarAsync(Guid companyId, long id, CancellationToken cancellationToken)
    {
        var fila = await _contexto.WmsOracleInboundStages.SingleAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);
        fila.Estado = WmsInboundEstado.Pendiente;
        fila.MensajeError = null;
        await _contexto.SaveChangesAsync(cancellationToken);
    }
}
