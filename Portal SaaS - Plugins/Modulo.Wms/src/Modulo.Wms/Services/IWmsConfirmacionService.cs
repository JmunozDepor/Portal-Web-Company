namespace Modulo.Wms.Services;

public interface IWmsConfirmacionService
{
    Task<Models.WmsPagedResult<Models.WmsConfirmacionRow>> BuscarAsync(Guid companyId, Models.WmsConfirmacionFiltro filtro, CancellationToken cancellationToken);
    Task ResetearAsync(Guid companyId, Models.WmsTipoTransaccion tipo, string documento, CancellationToken cancellationToken);
}
