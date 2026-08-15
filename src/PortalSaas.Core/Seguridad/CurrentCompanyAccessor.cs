using Microsoft.AspNetCore.Http;
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Core.Seguridad;

/// <summary>
/// Implementación scoped de ICurrentCompanyAccessor. La compañía se fija en el login
/// (ver Pages/Account/Login.cshtml.cs) y vive en los claims de la cookie de identidad
/// durante toda la sesión (no cambia sin logout). Portado de PortalSAP_v2
/// (CurrentEmpresaAccessor), renombrado "Empresa" -> "Company".
/// </summary>
public sealed class CurrentCompanyAccessor : ICurrentCompanyAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICurrentCompanyOverride _override;

    public CurrentCompanyAccessor(IHttpContextAccessor httpContextAccessor, ICurrentCompanyOverride @override)
    {
        _httpContextAccessor = httpContextAccessor;
        _override = @override;
    }

    // El override ambiente (fijado explícitamente, ej. por IntegrationSyncHostedService
    // para un ciclo de integración de una compañía puntual) tiene prioridad sobre el
    // claim de sesión -- es el único mecanismo disponible para un BackgroundService, que
    // no tiene HttpContext. Code/Database/ServiceLayerUrl/Country siguen dependiendo solo
    // de claims: nadie los necesita todavía fuera de un HttpContext real.
    public Guid CompanyId => _override.CompanyId ?? Guid.Parse(GetClaim("CompanyId"));
    public string Code => GetClaim("CompanyCode");
    public string Database => GetClaim("CompanyDatabase");
    public string ServiceLayerUrl => GetClaim("CompanyServiceLayerUrl");
    public string Country => GetClaim("CompanyCountry");

    public bool HasCompany => _override.CompanyId is not null || _httpContextAccessor.HttpContext?.User.FindFirst("CompanyId") is not null;

    private string GetClaim(string type)
    {
        var value = _httpContextAccessor.HttpContext?.User.FindFirst(type)?.Value;
        return value ?? throw new InvalidOperationException($"No hay claim '{type}' en la sesión actual.");
    }
}
