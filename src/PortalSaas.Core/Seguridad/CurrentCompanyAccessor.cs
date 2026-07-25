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

    public CurrentCompanyAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid CompanyId => Guid.Parse(GetClaim("CompanyId"));
    public string Code => GetClaim("CompanyCode");
    public string Database => GetClaim("CompanyDatabase");
    public string ServiceLayerUrl => GetClaim("CompanyServiceLayerUrl");
    public string Country => GetClaim("CompanyCountry");

    public bool HasCompany => _httpContextAccessor.HttpContext?.User.FindFirst("CompanyId") is not null;

    private string GetClaim(string type)
    {
        var value = _httpContextAccessor.HttpContext?.User.FindFirst(type)?.Value;
        return value ?? throw new InvalidOperationException($"No hay claim '{type}' en la sesión actual.");
    }
}
