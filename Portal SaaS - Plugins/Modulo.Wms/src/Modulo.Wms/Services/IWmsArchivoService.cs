using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public interface IWmsArchivoService
{
    Task<WmsPagedResult<WmsOracleInboundStage>> ListarAsync(Guid companyId, string? tipoDoc, string? estado, int page, int pageSize, CancellationToken cancellationToken);
    Task ReintentarAsync(Guid companyId, long id, CancellationToken cancellationToken);
}
