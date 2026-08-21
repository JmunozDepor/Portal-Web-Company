namespace Modulo.Wms.Services;

public interface IWmsTransaccionService
{
    Task<Models.WmsPagedResult<Models.WmsTransaccionRow>> BuscarAsync(Guid companyId, Models.WmsTransaccionFiltro filtro, CancellationToken cancellationToken);
    Task ResetearAsync(Guid companyId, Models.WmsTipoTransaccion tipo, IReadOnlyList<long> lineIds, CancellationToken cancellationToken);
}
