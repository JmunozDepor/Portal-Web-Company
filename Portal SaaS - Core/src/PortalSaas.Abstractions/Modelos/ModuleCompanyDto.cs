namespace PortalSaas.Abstractions.Modelos;

/// <summary>Compañía con una conexión externa activa para un módulo -- ver IExternalDatabaseConnectionService.ListActiveCompanyIdsAsync.</summary>
public sealed record ModuleCompanyDto(Guid CompanyId, Guid OrganizationId);
