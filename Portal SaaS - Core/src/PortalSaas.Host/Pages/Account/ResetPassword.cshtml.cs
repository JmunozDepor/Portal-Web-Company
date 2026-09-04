using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Seguridad;

namespace PortalSaas.Host.Pages.Account;

public class ResetPasswordModel : PageModel
{
    private readonly IPasswordResetService _passwordResetService;

    public ResetPasswordModel(IPasswordResetService passwordResetService)
    {
        _passwordResetService = passwordResetService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool? Succeeded { get; private set; }

    public void OnGet(string? token)
    {
        Input.Token = token ?? string.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Input.NewPassword != Input.ConfirmPassword)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ConfirmPassword)}", "Las contraseñas no coinciden.");
            return Page();
        }

        foreach (var error in PasswordPolicy.Validate(Input.NewPassword))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.NewPassword)}", error);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        Succeeded = await _passwordResetService.ResetPasswordAsync(Input.Token, Input.NewPassword);
        return Page();
    }

    public sealed class InputModel
    {
        public string Token { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa tu nueva contraseña.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña nueva")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirma tu nueva contraseña.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirmar contraseña")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
