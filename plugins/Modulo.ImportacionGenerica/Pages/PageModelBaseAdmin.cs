using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.ImportacionGenerica.Pages;

/// <summary>
/// Base de toda página de este plugin -- esquema de cookie default (tenant) + gate
/// ICurrentUserContext.IsAdmin. Copia local del mismo gate de
/// Modulo.Administracion.AdminPageModelBase -- un plugin no puede referenciar la clase
/// de otro (cada uno vive en su propio AssemblyLoadContext).
/// </summary>
[Authorize]
public abstract class PageModelBaseAdmin : PageModel
{
    protected PageModelBaseAdmin(ICurrentUserContext currentUser)
    {
        CurrentUser = currentUser;
    }

    protected ICurrentUserContext CurrentUser { get; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    protected static string GetErrorMessage(Exception ex) =>
        ex is InvalidOperationException ? ex.Message : $"No se pudo completar la operación: {ex.Message}";

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        if (!CurrentUser.IsAdmin)
        {
            context.Result = Forbid();
        }
    }
}
