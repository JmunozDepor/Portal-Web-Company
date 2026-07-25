namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Vista de un nodo de `menus`, ya filtrado y anidado en árbol real (Children) para el
/// usuario del request actual -- ver IMenuNavigationService. A diferencia del original
/// de PortalSAP_v2 (lista plana DFS + Nivel para indentar sin recursión), acá el árbol
/// se anida de verdad porque el sidebar usa el componente `collapse` nativo de
/// Bootstrap 5 (cada `<div class="collapse">` tiene que envolver exactamente a sus
/// hijos) -- Razor recorre `Children` recursivamente (ver
/// Pages/Shared/Components/SidebarMenu/_MenuNode.cshtml).
/// </summary>
public sealed class MenuNodeDto
{
    public required long Id { get; init; }

    public long? ParentMenuId { get; init; }

    /// <summary>IModuloPortal.ModuleCode del plugin dueño de este nodo.</summary>
    public required string OriginModule { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public string? Icon { get; init; }

    /// <summary>Null = nodo de agrupación (carpeta), no navegable.</summary>
    public string? PagePath { get; init; }

    public required int Order { get; init; }

    public List<MenuNodeDto> Children { get; } = [];
}
