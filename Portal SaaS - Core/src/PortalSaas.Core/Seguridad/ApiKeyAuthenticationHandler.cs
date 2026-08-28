using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Core.Seguridad;

public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
}

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>
{
    private const string HeaderName = "X-Api-Key";

    private readonly IApiKeyAuthenticator _authenticator;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyAuthenticator authenticator)
        : base(options, logger, encoder)
    {
        _authenticator = authenticator;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var apiKeyValues) || apiKeyValues.Count == 0)
        {
            return AuthenticateResult.Fail($"Falta el header '{HeaderName}'.");
        }

        var rawApiKey = apiKeyValues[0];
        if (string.IsNullOrWhiteSpace(rawApiKey))
        {
            return AuthenticateResult.Fail($"El header '{HeaderName}' está vacío.");
        }

        var resultado = await _authenticator.AuthenticateAsync(rawApiKey, Context.RequestAborted);
        if (!resultado.Success)
        {
            return AuthenticateResult.Fail("API key inválida o revocada.");
        }

        var claims = new[]
        {
            new Claim("CompanyId", resultado.CompanyId!.Value.ToString()),
            new Claim("CompanyCode", resultado.CompanyCode!),
            new Claim("CompanyDatabase", resultado.CompanyDatabase!),
            new Claim("CompanyServiceLayerUrl", resultado.CompanyServiceLayerUrl!),
            new Claim("CompanyCountry", resultado.CompanyCountry!),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
