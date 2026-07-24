using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Home;

[Authorize]
public class PreferencesModel : PageModel
{
    private readonly IUserPreferenceService _preferenceService;

    public PreferencesModel(IUserPreferenceService preferenceService)
    {
        _preferenceService = preferenceService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool Saved { get; private set; }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task OnGetAsync()
    {
        var preferences = await _preferenceService.GetOrCreateDefaultAsync(CurrentUserId);
        Input = new InputModel
        {
            Locale = preferences.Locale,
            Timezone = preferences.Timezone,
            Theme = preferences.Theme,
            EmailNotificationsEnabled = preferences.EmailNotificationsEnabled,
        };
    }

    public async Task OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return;
        }

        await _preferenceService.UpdateAsync(CurrentUserId, new UserPreferenceDto(
            Input.Locale, Input.Timezone, Input.Theme, Input.EmailNotificationsEnabled));

        Saved = true;
    }

    public sealed class InputModel
    {
        [Required]
        [Display(Name = "Idioma")]
        public string Locale { get; set; } = "es-CL";

        [Required]
        [Display(Name = "Zona horaria")]
        public string Timezone { get; set; } = "America/Santiago";

        [Required]
        [Display(Name = "Tema")]
        public string Theme { get; set; } = UserThemePreference.System;

        [Display(Name = "Recibir notificaciones por correo")]
        public bool EmailNotificationsEnabled { get; set; } = true;
    }
}
