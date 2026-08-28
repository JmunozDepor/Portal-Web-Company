namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila del catálogo fijo de acciones (Ver/Crear/Editar/Eliminar/Aprobar/Exportar) -- ver IOrganizationProfileService.ListAllActionsAsync.</summary>
public sealed record PermissionActionDto(long Id, string Code, string Name);
