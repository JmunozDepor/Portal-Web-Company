using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
// Alias necesario: Microsoft.AspNetCore.Authentication ya trae su propio
// IAuthenticationService (SignInAsync/SignOutAsync) -- distinto del nuestro
// (validación de credenciales), el nombre choca sin este alias.
using IAuthenticationService = PortalSaas.Abstractions.Contratos.IAuthenticationService;

namespace PortalSaas.Host.Pages.Account;

public class LoginModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly IAuthenticationService _authenticationService;
    private readonly IOrganizationAccessGateService _accessGateService;

    public LoginModel(
        PortalSaasDbContext db,
        IAuthenticationService authenticationService,
        IOrganizationAccessGateService accessGateService)
    {
        _db = db;
        _authenticationService = authenticationService;
        _accessGateService = accessGateService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public void OnGet(string? returnUrl = null)
    {
        Input.ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Mensaje genérico a propósito -- no distinguir "organización no existe" de
        // "credenciales incorrectas" (mismo criterio anti-enumeración que
        // IAuthenticationService, ver docs/06-...md).
        const string credencialesInvalidas = "Organización, usuario o contraseña incorrectos.";

        var organizationSlug = Input.OrganizationSlug.Trim().ToLowerInvariant();
        var organization = await _db.Organizations.FirstOrDefaultAsync(o => o.Slug == organizationSlug);

        if (organization is null)
        {
            ErrorMessage = credencialesInvalidas;
            return Page();
        }

        var result = await _authenticationService.AuthenticateAsync(organization.Id, Input.EmailOrUsername, Input.Password);

        if (!result.IsSuccess)
        {
            ErrorMessage = result.Reason;
            return Page();
        }

        // Gate comercial -- ver docs/03-...md §5. Se evalúa DESPUÉS de validar la
        // contraseña (nunca antes) para no revelar el estado comercial de la
        // organización a alguien sin credenciales válidas.
        var access = await _accessGateService.CheckAccessAsync(organization.Id);
        if (!access.IsAllowed)
        {
            ErrorMessage = access.Reason;
            return Page();
        }

        var user = await _db.Users.FirstAsync(u => u.Id == result.UserId);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new("OrganizationId", user.OrganizationId.ToString()),
            new("OrganizationSlug", organization.Slug),
            // Necesario para ICurrentUserContext.IsAdmin (ver PortalSaas.Core.Seguridad) --
            // no depende de que haya compañía seleccionada.
            new("IsAdmin", user.IsAdmin.ToString()),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        var returnUrl = Url.IsLocalUrl(Input.ReturnUrl) && Input.ReturnUrl is not null ? Input.ReturnUrl : "/Home/Index";

        // La compañía SAP activa (ver ICurrentCompanyAccessor) se fija en el login y no
        // cambia sin logout -- si la organización tiene compañías, un segundo paso la
        // pide antes de dejar entrar al portal (mismo criterio que PortalSAP_v2, ahora en
        // dos requests porque acá la organización recién se conoce después de validar
        // credenciales, a diferencia del selector único del proyecto original). Si la
        // organización no tiene ninguna compañía todavía, no hay nada que elegir -- se
        // entra directo, sin claim de compañía (las funciones que dependen de SAP
        // simplemente no están disponibles).
        var tieneCompanias = await _db.Companies.AnyAsync(c => c.OrganizationId == organization.Id && c.IsActive);
        if (tieneCompanias)
        {
            return RedirectToPage("/Account/SelectCompany", new { returnUrl });
        }

        return LocalRedirect(returnUrl);
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Ingresa el código de tu organización.")]
        [Display(Name = "Organización")]
        public string OrganizationSlug { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa tu correo o usuario.")]
        [Display(Name = "Correo o usuario")]
        public string EmailOrUsername { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa tu contraseña.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }
    }
}
