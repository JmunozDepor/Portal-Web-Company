namespace Modulo.Rendiciones.Pages;

/// <summary>
/// Página de restricción del módulo. Se muestra cuando un usuario que NO forma parte
/// del proceso de Rendiciones de la compañía activa (ninguna fila en
/// rendiciones_user_roles) intenta abrir cualquier pantalla del módulo -- ver
/// RendicionesRolePageModelBase, que redirige acá en vez de devolver un 403.
///
/// Solo hereda RendicionesPageModelBase ([Authorize] + TempData), SIN gate de rol, para
/// no entrar en un bucle de redirección.
/// </summary>
public sealed class SinAccesoModel : RendicionesPageModelBase
{
}
