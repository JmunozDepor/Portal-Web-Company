using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages;

/// <summary>
/// Base compartida por RendicionesAdminPageModelBase/RendicionesAprobadorPageModelBase/
/// RendicionesRendidorPageModelBase -- exige AL MENOS UNO de los roles listados en
/// RequiredRoles (ver Models.RendicionesRoles) más allá de tener el módulo habilitado.
/// Mismo patrón que Modulo.Administracion.Pages.AdminPageModelBase (Core, gate
/// ICurrentUserContext.IsAdmin vía OnPageHandlerExecuting), pero acá el chequeo pega
/// contra la base propia del plugin (RendicionesUserRoleService), así que usa la
/// variante async del filtro en vez de la síncrona.
/// </summary>
public abstract class RendicionesRolePageModelBase : RendicionesPageModelBase
{
    private readonly IRendicionesUserRoleService _roles;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;

    protected RendicionesRolePageModelBase(IRendicionesUserRoleService roles, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
    {
        _roles = roles;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    /// <summary>Alcanza con tener UNO de estos roles -- la mayoría de las pantallas piden uno
    /// solo, Informes/Detalle (compartida entre Rendidor y Aprobador) pide dos.</summary>
    protected abstract IReadOnlyList<string> RequiredRoles { get; }

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var hasRole = await _roles.HasAnyRoleAsync(_currentCompany.CompanyId, _currentUser.UserId, RequiredRoles, _currentUser.IsAdmin);
        if (!hasRole)
        {
            // El usuario no forma parte del proceso de Rendiciones (ningún rol para la
            // compañía activa). En vez de un 403 crudo que rebota al login, se lo manda
            // a una página de restricción clara -- ver Pages/SinAcceso.cshtml y
            // Pages/Configuracion/UsuariosRoles para cómo se lo agrega al proceso.
            // SinAcceso solo hereda RendicionesPageModelBase (sin gate de rol), así que
            // no hay bucle de redirección.
            context.Result = new RedirectToPageResult("/SinAcceso");
            return;
        }

        await next();
    }
}
