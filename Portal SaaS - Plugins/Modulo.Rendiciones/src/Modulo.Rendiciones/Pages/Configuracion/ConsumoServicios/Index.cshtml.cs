using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.ConsumoServicios;

/// <summary>
/// Solo lectura -- consumo del período de cuota vigente de cada proveedor configurado
/// (Azure Maps / Azure Document Intelligence miden por MES; Google Gemini por DÍA, ver
/// Models.QuotaPeriods). El bloqueo real ya corre solo en AzureMapsRoutingService/
/// los extractores vía IExternalServiceProviderSelector -- esta pantalla es para ver el
/// número sin ir a mirar la tabla en la base.
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
            var quotaPeriod = QuotaPeriods.ForServiceType(provider.ServiceType);
            var used = await _usage.GetCurrentUsageAsync(provider.Id, quotaPeriod, ct);
            usages.Add(new UsageDto(provider.Id, provider.ServiceType, provider.Name, provider.IsActive, used, provider.MonthlyLimit, quotaPeriod));
        }

        Usages = usages;
    }

    public string DisplayName(string serviceType) => serviceType switch
    {
        ExternalServiceType.AzureMaps => "Azure Maps (cálculo de kilometraje)",
        ExternalServiceType.AzureDocumentIntelligence => "Azure Document Intelligence (OCR de comprobantes)",
        ExternalServiceType.GoogleGeminiVision => "Google Gemini (OCR de comprobantes)",
        _ => serviceType,
    };

    public string DisplayUnit(string serviceType) => serviceType switch
    {
        ExternalServiceType.AzureMaps => "transacciones",
        ExternalServiceType.AzureDocumentIntelligence => "páginas",
        ExternalServiceType.GoogleGeminiVision => "solicitudes",
        _ => "unidades",
    };

    public sealed record UsageDto(long ProviderId, string ServiceType, string ProviderName, bool IsActive, int Used, int Limit, string QuotaPeriod)
    {
        public bool LimitReached => Used >= Limit;

        public double PercentageUsed => Limit <= 0 ? 0 : Math.Min(100.0, Used * 100.0 / Limit);

        /// <summary>"día" (Gemini) o "mes" (Azure) -- para los textos de la pantalla.</summary>
        public string PeriodLabel => QuotaPeriod == QuotaPeriods.Daily ? "día" : "mes";
    }
}
