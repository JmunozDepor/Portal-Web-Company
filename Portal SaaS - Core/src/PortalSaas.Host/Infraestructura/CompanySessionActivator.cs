using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Infraestructura;

/// <summary>
/// Valida acceso a una compañía y fija los claims Company* en la sesión (cookie +
/// IUserSessionService) -- lógica compartida entre Pages/Account/SelectCompany.cshtml.cs
/// (primer login) y Pages/Account/SwitchCompany.cshtml.cs (cambiar de compañía sin
/// cerrar sesión, desde el selector del topbar). Antes vivía duplicada en
/// SelectCompanyModel; se extrajo acá para que la regla de acceso (admin bypass /
/// UserMenuGroups / UserMenuProfiles) tenga un solo lugar de verdad.
/// </summary>
public interface ICompanySessionActivator
{
    Task<Company?> TryActivateAsync(HttpContext httpContext, Guid companyId);
}

public sealed class CompanySessionActivator : ICompanySessionActivator
{
    private readonly PortalSaasDbContext _db;
    private readonly IUserSessionService _sessions;

    public CompanySessionActivator(PortalSaasDbContext db, IUserSessionService sessions)
    {
        _db = db;
        _sessions = sessions;
    }

    public async Task<Company?> TryActivateAsync(HttpContext httpContext, Guid companyId)
    {
        var user = httpContext.User;
        var organizationId = Guid.Parse(user.FindFirstValue("OrganizationId")!);
        var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = bool.Parse(user.FindFirstValue("IsAdmin")!);

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId && c.OrganizationId == organizationId && c.IsActive);
        if (company is null)
        {
            return null;
        }

        // Mismo criterio que PortalSAP_v2 (ES_ADMINISTRADOR tiene acceso total, bypassea
        // grupos y perfiles en cualquier compañía). Si no es admin, exige al menos una
        // fila de acceso real a esta compañía puntual.
        if (!isAdmin)
        {
            var tieneAcceso = await _db.UserMenuGroups.AnyAsync(g => g.UserId == userId && g.CompanyId == company.Id)
                || await _db.UserMenuProfiles.AnyAsync(p => p.UserId == userId && p.CompanyId == company.Id);
            if (!tieneAcceso)
            {
                return null;
            }
        }

        var sessionToken = user.FindFirstValue("SessionToken");
        if (sessionToken is not null)
        {
            await _sessions.SetCompanyAsync(sessionToken, company.Id);
        }

        var claims = user.Claims.Where(c => c.Type is not ("CompanyId" or "CompanyCode" or "CompanyName" or "CompanyDatabase" or "CompanyServiceLayerUrl" or "CompanyCountry")).ToList();
        claims.Add(new Claim("CompanyId", company.Id.ToString()));
        claims.Add(new Claim("CompanyCode", company.Code));
        claims.Add(new Claim("CompanyName", company.Name));
        claims.Add(new Claim("CompanyDatabase", company.DatabaseName));
        claims.Add(new Claim("CompanyServiceLayerUrl", company.ServiceLayerUrl));
        claims.Add(new Claim("CompanyCountry", company.Country));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return company;
    }
}
