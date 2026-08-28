namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Una entrada de menú declarada por un plugin en IModuloPortal.ObtenerMenu().
/// Portado de PortalSAP_v2 tal cual -- el host futuro la sincronizará (upsert) contra
/// la tabla de menú equivalente a PORTALWEB.MENU una vez que esa tabla núcleo se porte
/// (ver CLAUDE.md "Todavía no existe").
/// </summary>
public sealed class MenuItemDefinition
{
    /// <summary>Código de este nodo, único DENTRO del módulo que lo declara.</summary>
    public required string Code { get; init; }

    /// <summary>
    /// Código del nodo padre. Puede pertenecer a OTRO módulo (ej. colgarse bajo un
    /// nodo "Ventas" que declaró otro plugin) -- en ese caso usar el código completo
    /// calificado como "ModuleCode.ParentCode". Null = nodo raíz.
    /// </summary>
    public string? ParentCode { get; init; }

    public required string Name { get; init; }

    /// <summary>Clase de ícono (ej. FontAwesome). Si es null, el layout usa un default.</summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Ruta de la página destino. NULL significa que este nodo es puramente de
    /// agrupación (una "carpeta" del árbol) y no es navegable ni puede recibir un perfil.
    /// </summary>
    public string? PageRoute { get; init; }

    public int Order { get; init; }
}
