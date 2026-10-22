using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.ImportacionGenerica.Pages;

/// <summary>
/// Base de toda página de este plugin -- esquema de cookie default (tenant) + gate por
/// perfil: ICurrentUserContext.HasActionAsync(MenuCode, VIEW), mismo criterio que los
/// motores genéricos de Venta/Compra/Inventario (un admin de la organización bypasea).
///
/// Antes (hasta 2026-10-21) exigía ICurrentUserContext.IsAdmin e ignoraba los perfiles:
/// un usuario no-admin con el módulo asignado veía las opciones en el menú, pero al
/// abrirlas recibía Forbid() -- bug real (usuario areyes, Comercial Depor): "me deja
/// entrar y me saca".
/// </summary>
[Authorize]
public abstract class PageModelBaseAdmin : PageModel
{
    protected PageModelBaseAdmin(ICurrentUserContext currentUser)
    {
        CurrentUser = currentUser;
    }

    protected ICurrentUserContext CurrentUser { get; }

    /// <summary>Nodo de menú calificado ("ImportacionGenerica.&lt;code&gt;", ver ModuloImportacionGenerica.GetMenu) cuyo perfil gobierna la página.</summary>
    protected abstract string MenuCode { get; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    protected static string GetErrorMessage(Exception ex) =>
        ex is InvalidOperationException ? ex.Message : $"No se pudo completar la operación: {ex.Message}";

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (!await CurrentUser.HasActionAsync(MenuCode, PortalActions.View, HttpContext.RequestAborted))
        {
            context.Result = Forbid();
            return;
        }

        await next();
    }
}
