using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;

namespace PortalSaas.Host.ViewComponents;

/// <summary>
/// Selector de compañía del topbar (Pages/Shared/_Layout.cshtml, junto al nombre de
/// usuario) -- deja cambiar de compañía activa sin cerrar sesión, publicando a
/// Pages/Account/SwitchCompany.cshtml.cs. Mismo criterio de acceso que SelectCompany
/// (admin bypass / UserMenuGroups / UserMenuProfiles), para no mostrar compañías a las
/// que el usuario no podría entrar igual.
/// </summary>
public sealed class CompanySwitcherViewComponent : ViewComponent
{
    private readonly PortalSaasDbContext _db;

    public CompanySwitcherViewComponent(PortalSaasDbContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var user = HttpContext.User;
        var organizationId = Guid.Parse(user.FindFirstValue("OrganizationId")!);
        var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = bool.Parse(user.FindFirstValue("IsAdmin")!);
        var currentCompanyId = user.FindFirstValue("CompanyId");

        var query = _db.Companies.Where(c => c.OrganizationId == organizationId && c.IsActive);

        // Mismo criterio de acceso que CompanySessionActivator -- no admin, solo las
        // compañías con al menos una fila de UserMenuGroups/UserMenuProfiles.
        if (!isAdmin)
        {
            query = query.Where(c =>
                _db.UserMenuGroups.Any(g => g.UserId == userId && g.CompanyId == c.Id) ||
                _db.UserMenuProfiles.Any(p => p.UserId == userId && p.CompanyId == c.Id));
        }

        var companies = await query
            .OrderBy(c => c.Code)
            .Select(c => new SelectListItem($"{c.Code} — {c.Name}", c.Id.ToString()))
            .ToListAsync();

        // Con 0 compañías visibles no hay nada que mostrar (el usuario entró sin
        // compañía activa -- las funciones que dependen de SAP simplemente no aplican).
        if (companies.Count == 0)
        {
            return View(new CompanySwitcherViewModel([], null));
        }

        // Con exactamente 1 no hay nada para "cambiar", pero SÍ hay que mostrar cuál es
        // -- la vista la renderiza como texto plano (no un <select>), si no el wrapper
        // .sidebar-company queda solo con el ícono de edificio y el usuario no ve en qué
        // compañía está (bug real reportado).
        return View(new CompanySwitcherViewModel(companies, currentCompanyId));
    }
}

public sealed record CompanySwitcherViewModel(List<SelectListItem> Companies, string? CurrentCompanyId);
