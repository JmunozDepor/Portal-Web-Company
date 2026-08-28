namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Grupo de menú visible para la organización actual -- propio (creado por su admin)
/// o plantilla global de plataforma (IsGlobalTemplate = true, solo lectura). Ver
/// IOrganizationMenuGroupService.
/// </summary>
public sealed record OrganizationMenuGroupDto(
    long Id,
    string Name,
    string? Description,
    bool IsActive,
    Guid? CompanyId,
    bool IsGlobalTemplate,
    IReadOnlyList<long> MenuIds,
    IReadOnlyDictionary<long, long?> DefaultProfileByMenu);
