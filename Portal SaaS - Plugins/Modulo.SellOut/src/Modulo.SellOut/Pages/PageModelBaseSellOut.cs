using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.SellOut.Pages;

/// <summary>
/// Infra compartida por las paginas de este modulo: mensajes de accion post-redirect,
/// normalizacion de errores y el gate de permisos por CodigoMenu (View/Create/Edit/
/// Delete via HasActionAsync, mismo motor que cualquier otro modulo -- pese a que
/// CLSELLOUT no tiene EMPRESA_CODIGO, los permisos SI son por compañía activa, asi que un
/// administrador de Sell Out puede depender del perfil que tenga en la compañía con la
/// que inicio sesion). [Authorize] obligatorio (ver checklist de
/// docs/09-GUIA-DESARROLLO-PLUGINS.md §9) -- sin esto un request anonimo llega directo
/// al handler y crashea con 500 en vez de redirigir a login, porque la resolucion de
/// SellOutDbContext exige una Company activa en sesion.
/// </summary>
[Authorize]
public abstract class PageModelBaseSellOut : PageModel
{
    private readonly ICurrentUserContext _usuarioActual;

    protected PageModelBaseSellOut(ICurrentUserContext usuarioActual)
    {
        _usuarioActual = usuarioActual;
    }

    /// <summary>Codigo de menu para HasActionAsync -- distinto por pantalla (hojas de menu separadas, con permisos propios).</summary>
    protected abstract string CodigoMenu { get; }

    protected Task<bool> TieneAccionAsync(string accion) => _usuarioActual.HasActionAsync(CodigoMenu, accion);

    protected Task<bool> PuedeVerAsync() => TieneAccionAsync(PortalActions.View);
    protected Task<bool> PuedeCrearAsync() => TieneAccionAsync(PortalActions.Create);
    protected Task<bool> PuedeEditarAsync() => TieneAccionAsync(PortalActions.Edit);
    protected Task<bool> PuedeEliminarAsync() => TieneAccionAsync(PortalActions.Delete);

    [TempData]
    public string? MensajeExito { get; set; }

    [TempData]
    public string? MensajeError { get; set; }

    protected static string ObtenerMensajeError(Exception ex) =>
        ex is InvalidOperationException ? ex.Message : $"No se pudo completar la operación: {ex.Message}";

    /// <summary>
    /// Paginas con mas de un formulario independiente (todas las de este modulo, siempre
    /// tienen alta + edicion en la misma pagina) reciben binding+validacion de TODOS los
    /// [BindProperty] en cualquier POST -- hay que limpiar ModelState y validar solo el
    /// prefijo del formulario que realmente se envio. Mismo patron que AdminPageModelBase/
    /// ComprasPageModelBase.
    /// </summary>
    protected bool ValidarSoloEsteFormulario<T>(T modelo, string prefijo) where T : class
    {
        ModelState.Clear();
        return TryValidateModel(modelo, prefijo);
    }
}
