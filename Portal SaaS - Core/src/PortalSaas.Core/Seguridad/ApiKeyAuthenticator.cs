using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;

namespace PortalSaas.Core.Seguridad;

public class ApiKeyAuthenticator : IApiKeyAuthenticator
{
    private readonly PortalSaasDbContext _contexto;

    public ApiKeyAuthenticator(PortalSaasDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<ApiKeyAuthenticationResult> AuthenticateAsync(string rawApiKey, CancellationToken cancellationToken)
    {
        var hash = ApiKeyGenerator.Hash(rawApiKey);

        var credencial = await _contexto.ApiClientCredentials
            .FirstOrDefaultAsync(c => c.ApiKeyHash == hash && c.Activo, cancellationToken);

        if (credencial is null)
        {
            return ApiKeyAuthenticationResult.Failure();
        }

        var company = await _contexto.Companies
            .FirstOrDefaultAsync(c => c.Id == credencial.CompanyId && c.IsActive, cancellationToken);

        if (company is null)
        {
            return ApiKeyAuthenticationResult.Failure();
        }

        var ahora = DateTimeOffset.UtcNow;
        if (credencial.LastUsedAt is null || ahora - credencial.LastUsedAt.Value > TimeSpan.FromMinutes(1))
        {
            credencial.LastUsedAt = ahora;
            await _contexto.SaveChangesAsync(cancellationToken);
        }

        return ApiKeyAuthenticationResult.Ok(
            company.Id,
            company.Code,
            company.DatabaseName,
            company.ServiceLayerUrl,
            company.Country);
    }
}
