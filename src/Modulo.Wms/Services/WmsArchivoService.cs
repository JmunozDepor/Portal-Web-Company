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

    public async Task<List<WmsOracleInboundStage>> ListarAsync(Guid companyId, string? tipoDoc, string? estado, CancellationToken cancellationToken)
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

        return await query.OrderByDescending(x => x.InsertedAt).Take(200).ToListAsync(cancellationToken);
    }

    public async Task ReintentarAsync(Guid companyId, long id, CancellationToken cancellationToken)
    {
        var fila = await _contexto.WmsOracleInboundStages.SingleAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);
        fila.Estado = WmsInboundEstado.Pendiente;
        fila.MensajeError = null;
        await _contexto.SaveChangesAsync(cancellationToken);
    }
}
