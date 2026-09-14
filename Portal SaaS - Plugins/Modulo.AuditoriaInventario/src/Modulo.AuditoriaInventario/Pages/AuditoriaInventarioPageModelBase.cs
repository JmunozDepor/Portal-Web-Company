using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Modulo.AuditoriaInventario.Pages;

/// <summary>
/// Infra compartida por las páginas de este módulo -- [Authorize] acá porque no hay
/// convención global de autorización en el Host (ver
/// docs/09-GUIA-DESARROLLO-PLUGINS.md §8 del portal): sin esto, un request anónimo
/// llega directo al handler y crashea con 500 en vez de redirigir a login, porque
/// AuditoriaInventarioDbContext exige una Company activa que no existe sin sesión.
/// Duplicado del mismo patrón que RendicionesPageModelBase -- un plugin solo
/// referencia Abstractions, nunca comparte código con otro plugin.
/// </summary>
[Authorize]
public abstract class AuditoriaInventarioPageModelBase : PageModel
{
    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    protected static string GetErrorMessage(Exception ex) =>
        ex is InvalidOperationException ? ex.Message : $"No se pudo completar la operación: {ex.Message}";
}
