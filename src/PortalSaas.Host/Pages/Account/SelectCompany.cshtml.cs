using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Account;

/// <summary>
/// Segundo paso del login de tenant -- fija la compañía SAP activa de la sesión (ver
/// ICurrentCompanyAccessor), solo cuando la organización tiene al menos una. Una vez
/// fijada, no se puede volver a elegir sin logout (ver Login.cshtml.cs) -- si el usuario
/// ya tiene el claim "CompanyId", esta página redirige directo, nunca deja re-elegir.
/// </summary>
[Authorize]
public class SelectCompanyModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public SelectCompanyModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> Companies { get; private set; } = [];

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        if (User.FindFirst("CompanyId") is not null)
        {
            return LocalRedirect(Url.IsLocalUrl(returnUrl) && returnUrl is not null ? returnUrl : "/Home/Index");
        }

        Input.ReturnUrl = returnUrl;
        await CargarCompaniasAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.FindFirst("CompanyId") is not null)
        {
            return LocalRedirect(Url.IsLocalUrl(Input.ReturnUrl) && Input.ReturnUrl is not null ? Input.ReturnUrl : "/Home/Index");
        }

        await CargarCompaniasAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var organizationId = Guid.Parse(User.FindFirstValue("OrganizationId")!);
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = bool.Parse(User.FindFirstValue("IsAdmin")!);

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == Input.CompanyId && c.OrganizationId == organizationId && c.IsActive);
        if (company is null)
        {
            ErrorMessage = "Compañía inválida.";
            return Page();
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
                ErrorMessage = "No tienes acceso a esa compañía.";
                return Page();
            }
        }

        var claims = User.Claims.ToList();
        claims.Add(new Claim("CompanyId", company.Id.ToString()));
        claims.Add(new Claim("CompanyCode", company.Code));
        claims.Add(new Claim("CompanyDatabase", company.DatabaseName));
        claims.Add(new Claim("CompanyServiceLayerUrl", company.ServiceLayerUrl));
        claims.Add(new Claim("CompanyCountry", company.Country));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return LocalRedirect(Url.IsLocalUrl(Input.ReturnUrl) && Input.ReturnUrl is not null ? Input.ReturnUrl : "/Home/Index");
    }

    private async Task CargarCompaniasAsync()
    {
        var organizationId = Guid.Parse(User.FindFirstValue("OrganizationId")!);
        Companies = await _db.Companies
            .Where(c => c.OrganizationId == organizationId && c.IsActive)
            .OrderBy(c => c.Code)
            .Select(c => new SelectListItem($"{c.Code} — {c.Name}", c.Id.ToString()))
            .ToListAsync();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Selecciona una compañía.")]
        [Display(Name = "Compañía")]
        public Guid CompanyId { get; set; }

        public string? ReturnUrl { get; set; }
    }
}
