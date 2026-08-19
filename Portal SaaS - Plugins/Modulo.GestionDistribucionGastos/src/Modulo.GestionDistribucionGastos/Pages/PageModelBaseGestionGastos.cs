using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.GestionDistribucionGastos.Pages;

/// <summary>
/// Infra compartida por las páginas de este módulo: mensajes de acción post-redirect y
/// el usuario real de la sesión del portal -- mismo patrón que
/// Modulo.Rendiciones.Pages.RendicionesPageModelBase. [Authorize] explícito (no hay
/// convención global en el Host de esta plataforma) -- mismo bug real ya encontrado y
/// corregido en Modulo.Rendiciones: sin esto, un request anónimo llega directo al
/// handler y crashea con 500 en vez de redirigir a /Account/Login (la resolución de
/// ApplicationDbContext exige ICurrentCompanyAccessor.CompanyId, que no existe sin
/// sesión ni compañía activa).
/// </summary>
[Authorize]
public abstract class PageModelBaseGestionGastos : PageModel
{
    private readonly ICurrentUserContext _usuarioActual;

    protected PageModelBaseGestionGastos(ICurrentUserContext usuarioActual)
    {
        _usuarioActual = usuarioActual;
    }

    protected string NombreUsuarioActual => _usuarioActual.Username;

    // Este módulo cuelga de un único nodo de menú final ("eerr", ver
    // ModuloGestionDistribucionGastos.GetMenu()) -- HasActionAsync exige el código
    // calificado "ModuleCode.Code" (ver ICurrentUserContext), a diferencia del
    // TieneAccionAsync(codigoMenu, accion) del portal viejo que ya recibía el código
    // sin calificar.
    private const string CodigoMenuCalificado = "GestionGastos.eerr";

    protected Task<bool> TieneAccionAsync(string accion) => _usuarioActual.HasActionAsync(CodigoMenuCalificado, accion);

    [TempData]
    public string? MensajeExito { get; set; }

    [TempData]
    public string? MensajeError { get; set; }

    protected static string ObtenerMensajeError(Exception ex) =>
        ex is InvalidOperationException ? ex.Message : $"No se pudo completar la operación: {ex.Message}";
}
