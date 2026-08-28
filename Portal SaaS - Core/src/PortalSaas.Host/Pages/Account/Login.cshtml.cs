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
    private readonly IUserSessionService _sessions;
    private readonly IConfiguration _configuration;

    public LoginModel(
        PortalSaasDbContext db,
        IAuthenticationService authenticationService,
        IOrganizationAccessGateService accessGateService,
        IUserSessionService sessions,
        IConfiguration configuration)
    {
        _db = db;
        _authenticationService = authenticationService;
        _accessGateService = accessGateService;
        _sessions = sessions;
        _configuration = configuration;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    /// <summary>
    /// True cuando Tenant:DefaultOrganizationSlug (appsettings, ver el comentario ahí)
    /// viene configurado -- el campo Organización se muestra precargado y bloqueado, en
    /// vez de pedirle al usuario que lo tipee. Pensado para un perfil OnPremise de una
    /// sola organización real (ej. Comercial Depor) -- mismo criterio que
    /// Tenant:DefaultCompanyCode en SelectCompany.cshtml.cs, un paso más arriba del
    /// mismo flujo (acá bloquea Organización, ahí bloquea Company).
    /// </summary>
    public bool OrganizationLocked { get; private set; }

    public void OnGet(string? returnUrl = null)
    {
        Input.ReturnUrl = returnUrl;
        AplicarOrganizacionBloqueadaSiCorresponde();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Re-aplicar SIEMPRE antes de validar -- el campo bloqueado en el <form> se
        // manda disabled (no viaja en el POST), así que sin esto Input.OrganizationSlug
        // llegaría vacío. Pisa cualquier valor posteado a mano también (mismo criterio
        // de "no confiar en el cliente" que AplicarCompaniaBloqueadaSiCorresponde en
        // SelectCompany.cshtml.cs).
        AplicarOrganizacionBloqueadaSiCorresponde();

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

        // "Clientes conectados" (ver IUserSessionService/Admin/Sessions) -- una fila por
        // login exitoso, con el token en texto plano guardado SOLO como claim de la
        // cookie (nunca en la base, ver UserSession.TokenHash). Permite al admin de
        // plataforma ver quién está logueado y forzar el cierre de una sesión puntual.
        var sessionToken = await _sessions.CreateAsync(
            user.Id, user.OrganizationId,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            userAgent: Request.Headers.UserAgent.ToString());

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
            new("SessionToken", sessionToken),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        // Url.Content("~/...") -- NO un literal "/Home/Index": bajo IIS hosteado como
        // subaplicación (ej. /portalsaas-comercialdepor), un literal absoluto pierde el
        // PathBase y el redirect cae fuera de la app (404 real encontrado en el primer
        // deploy IIS de este runbook, 2026-08-02).
        var returnUrl = Url.IsLocalUrl(Input.ReturnUrl) && Input.ReturnUrl is not null ? Input.ReturnUrl : Url.Content("~/Home/Index");

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

    private void AplicarOrganizacionBloqueadaSiCorresponde()
    {
        var defaultOrganizationSlug = _configuration["Tenant:DefaultOrganizationSlug"];
        if (string.IsNullOrWhiteSpace(defaultOrganizationSlug))
        {
            return;
        }

        Input.OrganizationSlug = defaultOrganizationSlug.Trim().ToLowerInvariant();
        OrganizationLocked = true;

        // El campo viaja "disabled" en el <form> (ver Login.cshtml) -- un input disabled
        // nunca se manda en el POST, así que el binder automático de Razor Pages ya dejó
        // un error [Required] en ModelState para este campo ANTES de que este método
        // corra. Sacarlo a mano, si no ModelState.IsValid da false siempre que la
        // organización está bloqueada.
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.OrganizationSlug)}");
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
