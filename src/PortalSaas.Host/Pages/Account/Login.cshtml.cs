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
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return LocalRedirect(Url.IsLocalUrl(Input.ReturnUrl) && Input.ReturnUrl is not null ? Input.ReturnUrl : "/Home/Index");
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
