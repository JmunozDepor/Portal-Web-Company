using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Administracion.Pages;

/// <summary>
/// Base de toda página de este plugin -- esquema de cookie default (tenant, NO
/// "PlatformAdmin") + gate ICurrentUserContext.IsAdmin. El gate evita el problema de
/// huevo-y-gallina de necesitar ser admin para poder darse permiso de admin.
/// </summary>
[Authorize]
public abstract class AdminPageModelBase : PageModel
{
    protected AdminPageModelBase(ICurrentUserContext currentUser)
    {
        CurrentUser = currentUser;
    }

    protected ICurrentUserContext CurrentUser { get; }

    [TempData]
    public string? MensajeExito { get; set; }

    [TempData]
    public string? MensajeError { get; set; }

    protected static string ObtenerMensajeError(Exception ex) =>
        ex is InvalidOperationException ? ex.Message : $"No se pudo completar la operación: {ex.Message}";

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        if (!CurrentUser.IsAdmin)
        {
            context.Result = Forbid();
        }
    }
}
