using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Users;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class CreateModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly IContractLimitService _contractLimitService;

    public CreateModel(PortalSaasDbContext db, IContractLimitService contractLimitService)
    {
        _db = db;
        _contractLimitService = contractLimitService;
    }

    public Organization Organization { get; private set; } = null!;

    [BindProperty]
    public InputModel Input { get; set; } = new();

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

        var (hash, salt) = PasswordHasher.Hash(Input.Password);
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

        return RedirectToPage("/Admin/Organizations/Users/Index", new { organizationId });
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

        [Required(ErrorMessage = "Ingresa la contraseña.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirma la contraseña.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirmar contraseña")]
        [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Display(Name = "Administrador de la organización")]
        public bool IsAdmin { get; set; }
    }
}
