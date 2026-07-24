using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Host.Pages.Admin;

public class LoginModel : PageModel
{
    private readonly IPlatformAdminAuthenticationService _authenticationService;

    public LoginModel(IPlatformAdminAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
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

        var result = await _authenticationService.AuthenticateAsync(Input.Email, Input.Password);

        if (!result.IsSuccess)
        {
            ErrorMessage = result.Reason;
            return Page();
        }

        var normalizedEmail = Input.Email.Trim().ToLowerInvariant();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.UserId!.Value.ToString()),
            new(ClaimTypes.Name, normalizedEmail),
            new(ClaimTypes.Email, normalizedEmail),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "PlatformAdmin"));
        await HttpContext.SignInAsync("PlatformAdmin", principal);

        return LocalRedirect(Url.IsLocalUrl(Input.ReturnUrl) && Input.ReturnUrl is not null
            ? Input.ReturnUrl
            : "/Admin/Organizations/Index");
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Ingresa tu correo.")]
        [Display(Name = "Correo")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa tu contraseña.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }
    }
}
