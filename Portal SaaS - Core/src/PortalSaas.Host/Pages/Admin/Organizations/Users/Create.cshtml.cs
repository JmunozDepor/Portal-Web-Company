using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Users;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class CreateModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly IContractLimitService _contractLimitService;
    private readonly IPasswordResetService _passwordResetService;
    private readonly IEmailSenderService _emailSenderService;
    private readonly ILogger<CreateModel> _logger;

    public CreateModel(
        PortalSaasDbContext db,
        IContractLimitService contractLimitService,
        IPasswordResetService passwordResetService,
        IEmailSenderService emailSenderService,
        ILogger<CreateModel> logger)
    {
        _db = db;
        _contractLimitService = contractLimitService;
        _passwordResetService = passwordResetService;
        _emailSenderService = emailSenderService;
        _logger = logger;
    }

    public Organization Organization { get; private set; } = null!;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Regla dura del proyecto: el límite de plan se hace cumplir en código, no solo
        // se documenta -- si no se puede verificar (sin suscripción activa), se bloquea
        // en vez de dejar pasar en silencio (ver IContractLimitService).
        var limitCheck = await _contractLimitService.CheckUserLimitAsync(organizationId);
        if (!limitCheck.IsAllowed)
        {
            ModelState.AddModelError(string.Empty, limitCheck.Reason!);
            return Page();
        }

        var username = Input.Username.Trim();
        var email = Input.Email.Trim().ToLowerInvariant();

        var usernameEnUso = await _db.Users.AnyAsync(u => u.OrganizationId == organizationId && u.Username.ToLower() == username.ToLower());
        if (usernameEnUso)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Username)}", "Ya existe un usuario con ese nombre en esta organización.");
        }

        var emailEnUso = await _db.Users.AnyAsync(u => u.OrganizationId == organizationId && u.Email.ToLower() == email);
        if (emailEnUso)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Email)}", "Ya existe un usuario con ese correo en esta organización.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Sin campo de contraseña en el alta -- el usuario la elige él mismo vía el
        // correo de invitación (mismo link/token que ForgotPassword). La contraseña
        // aleatoria acá nunca se comunica a nadie, es solo para dejar el hash no-nulo.
        var (hash, salt) = PasswordHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));
        _db.Users.Add(new User
        {
            OrganizationId = organizationId,
            Username = username,
            Email = email,
            PasswordHash = hash,
            PasswordSalt = salt,
            IsAdmin = Input.IsAdmin,
        });

        await _db.SaveChangesAsync();

        Message = await EnviarInvitacionAsync(organizationId, email)
            ? "Usuario creado correctamente. Se envió un correo de invitación para que cree su contraseña."
            : "Usuario creado correctamente, pero no se pudo enviar el correo de invitación (revisa la configuración de correo de la organización).";

        return RedirectToPage("/Admin/Organizations/Users/Index", new { organizationId });
    }

    /// <summary>
    /// Junta IPasswordResetService + IEmailSenderService -- mismo patrón exacto que
    /// ForgotPassword.cshtml.cs (el link de "crear tu contraseña" y el de "recuperarla"
    /// son el mismo mecanismo de token, ver IPasswordResetService). El fallo de envío
    /// nunca bloquea la creación del usuario (falla hacia lo más estricto solo en el
    /// límite de plan, no acá) -- se refleja en el mensaje de la página siguiente.
    /// </summary>
    private async Task<bool> EnviarInvitacionAsync(Guid organizationId, string email)
    {
        var rawToken = await _passwordResetService.RequestResetAsync(organizationId, email);
        if (rawToken is null)
        {
            return false;
        }

        var resetLink = Url.PageLink("/Account/ResetPassword", values: new { token = rawToken });

        try
        {
            await _emailSenderService.SendAsync(organizationId, new EmailMessage(
                email,
                "Creá tu contraseña — Portal SaaS",
                $"""
                <p>Se creó una cuenta para vos en el Portal SaaS.</p>
                <p><a href="{resetLink}">Hacé clic acá para elegir tu contraseña</a> (el link vence en 1 hora).</p>
                """));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló el envío del correo de invitación para el usuario {Email} de la organización {OrganizationId}", email, organizationId);
            return false;
        }
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Ingresa el nombre de usuario.")]
        [Display(Name = "Usuario")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa el correo.")]
        [EmailAddress(ErrorMessage = "Correo inválido.")]
        [Display(Name = "Correo")]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Administrador de la organización")]
        public bool IsAdmin { get; set; }
    }
}
