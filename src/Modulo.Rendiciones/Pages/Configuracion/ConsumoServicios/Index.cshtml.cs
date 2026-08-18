using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.ConsumoServicios;

/// <summary>
/// Solo lectura -- consumo del mes actual de cada proveedor configurado (Azure Maps/
/// Azure Document Intelligence, ver Configuracion/Proveedores). El bloqueo real ya
/// corre solo en AzureMapsRoutingService/AzureDocumentIntelligenceExtractorService vía
/// IExternalServiceProviderSelector -- esta pantalla es para poder ver el número sin
/// tener que ir a mirar la tabla en la base.
/// </summary>
public sealed class IndexModel : RendicionesAdminPageModelBase
{
    private readonly IExternalServiceProviderService _providers;
    private readonly IExternalServiceUsageService _usage;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IExternalServiceProviderService providers, IExternalServiceUsageService usage,
        IRendicionesUserRoleService roles, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
        _providers = providers;
        _usage = usage;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<UsageDto> Usages { get; private set; } = Array.Empty<UsageDto>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var providers = await _providers.ListAsync(_currentCompany.CompanyId, ct);

        var usages = new List<UsageDto>();
        foreach (var provider in providers)
        {
            var used = await _usage.GetCurrentMonthUsageAsync(provider.Id, ct);
            usages.Add(new UsageDto(provider.Id, provider.ServiceType, provider.Name, provider.IsActive, used, provider.MonthlyLimit));
        }

        Usages = usages;
    }

    public string DisplayName(string serviceType) => serviceType switch
    {
        ExternalServiceType.AzureMaps => "Azure Maps (cálculo de kilometraje)",
        ExternalServiceType.AzureDocumentIntelligence => "Azure Document Intelligence (OCR de comprobantes)",
        _ => serviceType,
    };

    public string DisplayUnit(string serviceType) => serviceType switch
    {
        ExternalServiceType.AzureMaps => "transacciones",
        ExternalServiceType.AzureDocumentIntelligence => "páginas",
        _ => "unidades",
    };

    public sealed record UsageDto(long ProviderId, string ServiceType, string ProviderName, bool IsActive, int Used, int MonthlyLimit)
    {
        public bool LimitReached => Used >= MonthlyLimit;

        public double PercentageUsed => MonthlyLimit <= 0 ? 0 : Math.Min(100.0, Used * 100.0 / MonthlyLimit);
    }
}
