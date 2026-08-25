using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Self-service para que el admin de la organización actual (ICurrentUserContext.OrganizationId)
/// personalice nombre/orden/visibilidad de un nodo de Menu -- capa separada del catálogo
/// global (mismo criterio que IOrganizationModuleVisibilityService, a nivel de nodo).
/// </summary>
public interface IOrganizationMenuOverrideService
{
    /// <summary>Todos los nodos de Menu activos, con su override actual (si existe) para la organización actual.</summary>
    Task<IReadOnlyList<MenuOverrideRowDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Upsert/delete por cada MenuId presente en el diccionario -- un input en sus 3 valores por defecto (CustomLabel null/vacío, CustomOrder null, IsHidden false) borra el override existente en vez de guardar uno redundante.</summary>
    Task SaveOverridesAsync(Dictionary<long, MenuOverrideInput> overridesByMenuId, CancellationToken ct = default);
}
