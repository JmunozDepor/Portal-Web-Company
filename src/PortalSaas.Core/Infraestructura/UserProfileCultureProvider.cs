using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Primer RequestCultureProvider de la cadena (ver Program.cs) -- resuelve el idioma
/// del usuario AUTENTICADO desde IUserPreferenceService.Locale (la misma preferencia
/// que ya usa /Home/Preferences, no un claim nuevo: los claims del login son
/// estáticos por sesión, ver Login.cshtml.cs, y cambiarían recién con un re-login --
/// la preferencia de idioma debe poder cambiar en caliente sin cerrar sesión).
///
/// Sin sesión, o sin fila de preferencias todavía, devuelve null -- el
/// RequestLocalizationMiddleware sigue probando el resto de la cadena
/// (CookieRequestCultureProvider, después el DefaultRequestCulture "es"), nunca
/// fuerza "es" acá para no pisar la cookie de un usuario anónimo que ya eligió "en"
/// antes de loguearse.
/// </summary>
public sealed class UserProfileCultureProvider : IRequestCultureProvider
{
    public async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        if (httpContext.User.Identity is not { IsAuthenticated: true })
            return null;

        var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
            return null;

        var preferences = httpContext.RequestServices.GetRequiredService<IUserPreferenceService>();

        try
        {
            var dto = await preferences.GetOrCreateDefaultAsync(userId, httpContext.RequestAborted);
            return string.IsNullOrWhiteSpace(dto.Locale) ? null : new ProviderCultureResult(dto.Locale);
        }
        catch
        {
            // La base de preferencias no debe tumbar TODO el pipeline de localización si
            // falla (ej. compañía sin sesión activa todavía, error transitorio de conexión)
            // -- se deja caer al resto de la cadena (cookie/default), mismo criterio de
            // "un catálogo caído no tumba la página" ya usado en RendicionesPageModelBase.
            return null;
        }
    }
}
