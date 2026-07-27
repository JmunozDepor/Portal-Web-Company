namespace PortalSaas.Abstractions.Modelos;

/// <summary>Un módulo contratado por la organización actual, con su estado de visibilidad -- ver IOrganizationModuleVisibilityService.</summary>
public sealed record ModuleVisibilityDto(long ModuleId, string Code, string Name, bool IsCore, bool IsHidden);
