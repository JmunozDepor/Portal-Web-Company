using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public interface IWmsArchivoService
{
    Task<List<WmsOracleInboundStage>> ListarAsync(Guid companyId, string? tipoDoc, string? estado, CancellationToken cancellationToken);
    Task ReintentarAsync(Guid companyId, long id, CancellationToken cancellationToken);
}
