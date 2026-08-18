using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Home;

[Authorize]
public class PreferencesModel : PageModel
{
    private readonly IUserPreferenceService _preferenceService;

    /// <summary>
    /// Acotado a los países reales de este proyecto (Chile/México, ver CLAUDE.md) más
    /// los vecinos típicos de una instalación SAP Business One LatAm -- no un selector
    /// exhaustivo de idiomas del mundo, mismo criterio YAGNI del resto del proyecto.
    /// "Punto de partida, no cerrado" (docs/06-AUTENTICACION-Y-PREFERENCIAS.md §4) --
    /// agregar acá si una organización real necesita otro.
    /// </summary>
    private static readonly (string Value, string Label)[] LocaleChoices =
    [
        ("es-CL", "Español (Chile)"),
        ("es-MX", "Español (México)"),
        ("es-CO", "Español (Colombia)"),
        ("es-AR", "Español (Argentina)"),
        ("es-PE", "Español (Perú)"),
        ("en-US", "English (US)"),
    ];

    /// <summary>Mismo criterio de acotado que LocaleChoices -- zonas IANA reales, no una lista mundial.</summary>
    private static readonly (string Value, string Label)[] TimezoneChoices =
    [
        ("America/Santiago", "Santiago (Chile)"),
        ("America/Mexico_City", "Ciudad de México (México)"),
        ("America/Bogota", "Bogotá (Colombia)"),
        ("America/Argentina/Buenos_Aires", "Buenos Aires (Argentina)"),
        ("America/Lima", "Lima (Perú)"),
        ("America/New_York", "Nueva York (EE.UU., este)"),
    ];

    public PreferencesModel(IUserPreferenceService preferenceService)
    {
        _preferenceService = preferenceService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool Saved { get; private set; }

    public List<SelectListItem> LocaleOptions { get; private set; } = [];

    public List<SelectListItem> TimezoneOptions { get; private set; } = [];

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
        BuildOptions();
    }

    public async Task OnPostAsync()
    {
        // Reconstruir las opciones ANTES de validar -- si ModelState.IsValid es false
        // la página se vuelve a renderizar con Input tal cual vino del POST, y sin
        // esto el <select> quedaría sin ninguna opción (BuildOptions también corre en
        // OnGetAsync, pero un POST no pasa por ahí).
        BuildOptions();

        if (!ModelState.IsValid)
        {
            return;
        }

        await _preferenceService.UpdateAsync(CurrentUserId, new UserPreferenceDto(
            Input.Locale, Input.Timezone, Input.Theme, Input.EmailNotificationsEnabled));

        Saved = true;
    }

    /// <summary>
    /// Si el valor guardado hoy (o el que trae el POST) no está en la lista acotada --
    /// dato viejo, o cargado a mano en la base -- se agrega igual como opción extra en
    /// vez de forzarlo silenciosamente a otro valor al guardar. Nunca perder un dato
    /// real del usuario porque no está en la lista corta.
    /// </summary>
    private void BuildOptions()
    {
        LocaleOptions = BuildSelectList(LocaleChoices, Input.Locale);
        TimezoneOptions = BuildSelectList(TimezoneChoices, Input.Timezone);
    }

    private static List<SelectListItem> BuildSelectList((string Value, string Label)[] choices, string currentValue)
    {
        var options = choices
            .Select(c => new SelectListItem(c.Label, c.Value, c.Value == currentValue))
            .ToList();

        if (!string.IsNullOrWhiteSpace(currentValue) && choices.All(c => c.Value != currentValue))
        {
            options.Insert(0, new SelectListItem($"{currentValue} (valor actual, fuera de la lista)", currentValue, selected: true));
        }

        return options;
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
