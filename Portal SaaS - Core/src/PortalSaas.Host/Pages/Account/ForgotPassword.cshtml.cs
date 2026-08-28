using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;

namespace PortalSaas.Host.Pages.Account;

/// <summary>
/// Junta IPasswordResetService + IEmailSenderService -- el flujo real de recuperación
/// de contraseña, ver docs/06-AUTENTICACION-Y-PREFERENCIAS.md §3 y §7.
/// </summary>
public class ForgotPasswordModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly IPasswordResetService _passwordResetService;
    private readonly IEmailSenderService _emailSenderService;
    private readonly ILogger<ForgotPasswordModel> _logger;

    public ForgotPasswordModel(
        PortalSaasDbContext db,
        IPasswordResetService passwordResetService,
        IEmailSenderService emailSenderService,
        ILogger<ForgotPasswordModel> logger)
    {
        _db = db;
        _passwordResetService = passwordResetService;
        _emailSenderService = emailSenderService;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool RequestSubmitted { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Anti-enumeración deliberado: SIEMPRE se muestra el mismo mensaje al
        // navegador, sin importar si la organización/correo existen de verdad --
        // ver el doc-comment de IPasswordResetService.RequestResetAsync.
        RequestSubmitted = true;

        var organizationSlug = Input.OrganizationSlug.Trim().ToLowerInvariant();
        var organization = await _db.Organizations.FirstOrDefaultAsync(o => o.Slug == organizationSlug);

        if (organization is null)
        {
            return Page();
        }

        var rawToken = await _passwordResetService.RequestResetAsync(organization.Id, Input.Email);
        if (rawToken is null)
        {
            return Page();
        }

        var resetLink = Url.PageLink("/Account/ResetPassword", values: new { token = rawToken });

        try
        {
            await _emailSenderService.SendAsync(organization.Id, new EmailMessage(
                Input.Email,
                "Recupera tu contraseña — Portal SaaS",
                $"""
                <p>Recibimos una solicitud para restablecer tu contraseña.</p>
                <p><a href="{resetLink}">Haz clic acá para elegir una contraseña nueva</a> (el link vence en 1 hora).</p>
                <p>Si no fuiste tú, ignora este correo -- tu contraseña actual sigue funcionando.</p>
                """));
        }
        catch (Exception ex)
        {
            // El fallo de envío NUNCA se revela al navegador (mismo mensaje genérico
            // de siempre) -- pero sí queda en el log del servidor, es lo único que
            // permite detectar un email_settings mal configurado.
            _logger.LogError(ex, "Falló el envío del correo de recuperación de contraseña para la organización {OrganizationId}", organization.Id);
        }

        return Page();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Ingresa el código de tu organización.")]
        [Display(Name = "Organización")]
        public string OrganizationSlug { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa tu correo.")]
        [EmailAddress(ErrorMessage = "Ingresa un correo válido.")]
        [Display(Name = "Correo")]
        public string Email { get; set; } = string.Empty;
    }
}
