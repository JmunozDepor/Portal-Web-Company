using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Árbol de `menus` ya filtrado y anidado para el usuario logueado en el request
/// actual -- lo consume el sidebar del shell (Pages/Shared/_Layout.cshtml, vía
/// SidebarMenuViewComponent). Sin parámetros: se resuelve todo de
/// ICurrentUserContext/ICurrentCompanyAccessor, mismo criterio de auto-scope que
/// ITenantUserAdminService -- nunca confía en un id recibido desde afuera.
///
/// Administradores ven el árbol activo completo (mismo bypass que
/// ICurrentUserContext.HasActionAsync); el resto solo los nodos cuyo MenuGroup tenga
/// asignado vía UserMenuGroup en la compañía activa, más sus carpetas ancestro (para
/// que la carpeta contenedora aparezca aunque no esté asignada ella misma). Sin
/// compañía activa (organización sin Companies, o todavía no seleccionada), un usuario
/// no-admin no puede tener ninguna fila de UserMenuGroup (siempre lleva CompanyId) --
/// el árbol devuelto queda vacío.
/// </summary>
public interface IMenuNavigationService
{
    Task<IReadOnlyList<MenuNodeDto>> GetVisibleMenuAsync(CancellationToken ct = default);
}
