namespace PortalSaas.Abstractions.Contratos;

public interface IApiKeyAuthenticator
{
    Task<ApiKeyAuthenticationResult> AuthenticateAsync(string rawApiKey, CancellationToken cancellationToken);
}

public sealed class ApiKeyAuthenticationResult
{
    public required bool Success { get; init; }
    public Guid? CompanyId { get; init; }
    public string? CompanyCode { get; init; }
    public string? CompanyDatabase { get; init; }
    public string? CompanyServiceLayerUrl { get; init; }
    public string? CompanyCountry { get; init; }

    public static ApiKeyAuthenticationResult Failure() => new() { Success = false };

    public static ApiKeyAuthenticationResult Ok(Guid companyId, string companyCode, string companyDatabase, string companyServiceLayerUrl, string companyCountry) => new()
    {
        Success = true,
        CompanyId = companyId,
        CompanyCode = companyCode,
        CompanyDatabase = companyDatabase,
        CompanyServiceLayerUrl = companyServiceLayerUrl,
        CompanyCountry = companyCountry,
    };
}
