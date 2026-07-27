namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Perfil (subconjunto de PermissionAction) visible para la organización actual --
/// propio o plantilla global de plataforma (IsGlobalTemplate = true, solo lectura).
/// Ver IOrganizationProfileService.
/// </summary>
public sealed record OrganizationProfileDto(
    long Id,
    string Name,
    string? Description,
    bool IsGlobalTemplate,
    IReadOnlyList<long> ActionIds);
