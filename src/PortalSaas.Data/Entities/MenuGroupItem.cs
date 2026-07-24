namespace PortalSaas.Data.Entities;

/// <summary>Qué nodos de Menu pertenecen a cada MenuGroup -- portado de `GRUPO_MENU_DETALLE`.</summary>
public sealed class MenuGroupItem
{
    public long MenuGroupId { get; set; }
    public MenuGroup MenuGroup { get; set; } = null!;

    public long MenuId { get; set; }
    public Menu Menu { get; set; } = null!;
}
