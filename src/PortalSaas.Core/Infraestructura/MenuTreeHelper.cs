using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Anida una lista plana de MenuNodeDto (ya filtrada) en árbol real, vía ParentMenuId
/// -- necesario para que el sidebar renderice con el componente `collapse` nativo de
/// Bootstrap 5, donde cada contenedor debe envolver exactamente a sus hijos (a
/// diferencia de PortalSAP_v2, que solo necesitaba una lista plana en orden DFS con
/// Nivel para indentar, ver MenuArbolHelper.OrdenarDfs en la referencia).
/// </summary>
internal static class MenuTreeHelper
{
    // Dictionary<TKey,...> vía ToDictionary exige TKey : notnull -- long? no aplica.
    // Mismo criterio que MenuArbolHelper de PortalSAP_v2 (SinPadre = -1): un centinela
    // fuera del rango real de Id (siempre > 0) representa "sin padre".
    private const long NoParent = -1;

    public static IReadOnlyList<MenuNodeDto> BuildTree(IReadOnlyList<MenuNodeDto> flatNodes)
    {
        // ThenBy(Id) es el desempate para filas con el mismo Order (ej. "0" por
        // defecto en formularios que no lo cambian) -- sin esto el orden entre esos
        // empates queda librado a como el motor de base devuelva las filas, no es
        // estable entre una carga de página y otra.
        var childrenByParent = flatNodes
            .GroupBy(n => n.ParentMenuId ?? NoParent)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.Order).ThenBy(n => n.Id).ToList());

        foreach (var node in flatNodes)
        {
            if (childrenByParent.TryGetValue(node.Id, out var children))
            {
                node.Children.AddRange(children);
            }
        }

        return childrenByParent.TryGetValue(NoParent, out var roots) ? roots : [];
    }
}
