using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Modulo.Wms.Pages;

/// <summary>
/// Infra compartida por las páginas de este módulo: mensajes de acción
/// post-redirect y normalización de errores. Duplicado del mismo patrón que
/// Modulo.Rendiciones (RendicionesPageModelBase) -- un plugin nunca comparte
/// código con otro plugin. [Authorize] acá: sin sesión, la resolución de
/// WmsDbContext exige ICurrentCompanyAccessor.HasCompany, que no existe sin
/// login -- sin este atributo, un request anónimo llegaría al handler y
/// crashearía con 500 en vez de redirigir a /Account/Login.
/// </summary>
[Authorize]
public abstract class WmsPageModelBase : PageModel
{
    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    protected static string GetErrorMessage(Exception ex) =>
        ex is InvalidOperationException ? ex.Message : $"No se pudo completar la operación: {ex.Message}";
}
