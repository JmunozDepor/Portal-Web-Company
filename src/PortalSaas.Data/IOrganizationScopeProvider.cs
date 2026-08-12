namespace PortalSaas.Data;

/// <summary>
/// Resuelve la organización actual para el filtro global de EF Core
/// (HasQueryFilter en PortalSaasDbContext) -- null significa "sin filtro",
/// usado por el backoffice de plataforma (/Admin/*, sin sesión de tenant).
/// Implementado en PortalSaas.Core (lee ICurrentUserContext) -- este contrato
/// vive en Data para no invertir la dirección de dependencias del proyecto
/// (Data nunca referencia Core).
/// </summary>
public interface IOrganizationScopeProvider
{
    Guid? CurrentOrganizationId { get; }
}
