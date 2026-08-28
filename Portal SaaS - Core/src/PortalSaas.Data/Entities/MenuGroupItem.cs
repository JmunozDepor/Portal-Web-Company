namespace PortalSaas.Data.Entities;

/// <summary>
/// Qué nodos de Menu pertenecen a cada MenuGroup -- portado de `GRUPO_MENU_DETALLE`.
/// DefaultProfileId (nuevo) -- el Profile que HEREDA cualquier usuario asignado a este
/// grupo para este nodo puntual, sin necesitar una fila individual en UserMenuProfile.
/// Null = el grupo incluye el nodo en la navegación pero no otorga ningún permiso por
/// sí solo (un usuario necesitaría una asignación manual en UserMenuProfile para verlo/
/// usarlo). Un UserMenuProfile explícito para (usuario, nodo, compañía) SIEMPRE gana
/// por sobre este default -- "resetear" esa asignación manual (borrar la fila) hace que
/// el usuario vuelva a heredar este valor, ver CurrentUserContext.HasActionAsync/
/// MenuNavigationService.GetVisibleMenuAsync (misma resolución en los dos lugares).
/// </summary>
public sealed class MenuGroupItem
{
    public long MenuGroupId { get; set; }
    public MenuGroup MenuGroup { get; set; } = null!;

    public long MenuId { get; set; }
    public Menu Menu { get; set; } = null!;

    public long? DefaultProfileId { get; set; }
    public Profile? DefaultProfile { get; set; }
}
