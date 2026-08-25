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
    /// Solo los 2 idiomas que la app realmente traduce (SharedResources.es/en.resx,
    /// ver PortalSaas.Host/Resources) -- antes había 6 opciones regionales de español
    /// (MX/CO/AR/PE) hardcodeadas acá, pero SharedResources solo tiene contenido "es"/
    /// "en", así que esas 4 elegían un valor que igual caía al mismo recurso "es" por
    /// herencia de CultureInfo -- una elección sin efecto real, confusa para el
    /// usuario. Si el día de mañana se agrega contenido específico por país (formato
    /// de fecha/moneda distinto, no solo idioma), recién ahí vuelve a tener sentido
    /// separarlos -- hasta entonces, elegir entre 6 variantes que producen el mismo
    /// resultado es peor que elegir entre 2 que sí hacen algo.
    /// </summary>
    private static readonly (string Value, string Label)[] LocaleChoices =
    [
        ("es-CL", "Español"),
        ("en-US", "English"),
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

        // CONSOLIDACIÓN (2026-08-19, "dejar amarrado" idioma/tema acá): no hace
        // falta escribir ninguna cookie de cultura -- UserProfileCultureProvider
        // (PortalSaas.Core.Infraestructura, primer proveedor de la cadena en
        // Program.cs) YA resuelve el idioma leyendo IUserPreferenceService.Locale
        // en cada request para cualquier usuario autenticado, con prioridad sobre
        // la cookie. Guardar acá ya alcanza -- el cambio se ve desde el próximo
        // request, sin re-login. El selector rápido ES/EN que vivía en el topbar
        // (SetLanguage.cshtml, eliminado en este mismo cambio) escribía esa
        // cookie porque él SÍ corría fuera de esta página; ya no hace falta.
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
        public string Theme { get; set; } = UserThemePreference.Claro;

        [Display(Name = "Recibir notificaciones por correo")]
        public bool EmailNotificationsEnabled { get; set; } = true;
    }
}
