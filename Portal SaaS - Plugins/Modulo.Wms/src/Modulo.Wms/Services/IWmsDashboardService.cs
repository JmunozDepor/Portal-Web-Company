namespace Modulo.Wms.Services;

public interface IWmsDashboardService
{
    Task<Models.WmsDashboardResumen> ObtenerResumenAsync(Guid companyId, DateTime desde, CancellationToken cancellationToken);
}
