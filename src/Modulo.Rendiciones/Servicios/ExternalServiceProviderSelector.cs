using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Servicios;

public sealed class ExternalServiceProviderSelector : IExternalServiceProviderSelector
{
    private readonly IExternalServiceProviderService _providers;
    private readonly IExternalServiceUsageService _usage;
    private readonly ISecretoCifradoService _secretos;

    public ExternalServiceProviderSelector(IExternalServiceProviderService providers, IExternalServiceUsageService usage,
        ISecretoCifradoService secretos)
    {
        _providers = providers;
        _usage = usage;
        _secretos = secretos;
    }

    public async Task<SelectedProvider?> SelectForReservationAsync(Guid companyId, string serviceType, int quantity, CancellationToken ct = default)
    {
        var candidates = await _providers.ListByServiceAsync(companyId, serviceType, ct);
        foreach (var provider in candidates)
        {
            if (await _usage.TryReserveAsync(provider.Id, quantity, provider.MonthlyLimit, ct))
                return new SelectedProvider(provider.Id, provider.Endpoint, _secretos.Decrypt(provider.ApiKeyEncrypted));
        }

        return null;
    }

    public async Task<SelectedProvider?> SelectAvailableAsync(Guid companyId, string serviceType, CancellationToken ct = default)
    {
        var candidates = await _providers.ListByServiceAsync(companyId, serviceType, ct);
        foreach (var provider in candidates)
        {
            var used = await _usage.GetCurrentMonthUsageAsync(provider.Id, ct);
            if (used < provider.MonthlyLimit)
                return new SelectedProvider(provider.Id, provider.Endpoint, _secretos.Decrypt(provider.ApiKeyEncrypted));
        }

        return null;
    }
}
