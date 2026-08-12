using PortalSaas.Data;

namespace PortalSaas.Core.Seguridad;

/// <summary>
/// Implementación fija de IOrganizationScopeProvider sin organización actual --
/// CurrentOrganizationId siempre null, mismo criterio que el backoffice de
/// plataforma (sin filtro). Usada por herramientas de consola/scripts que crean
/// PortalSaasDbContext a mano, fuera de DI (ej. PortalSaas.Tools.EmailSmokeTest).
/// </summary>
public sealed class NullOrganizationScopeProvider : IOrganizationScopeProvider
{
    public Guid? CurrentOrganizationId => null;
}
