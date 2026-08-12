using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;

namespace PortalSaas.Core.Seguridad;

/// <summary>
/// Lee ICurrentUserContext.OrganizationId para el filtro global de
/// PortalSaasDbContext -- ver docs/superpowers/plans. En páginas del backoffice de
/// plataforma (esquema de cookie "PlatformAdmin", sin claim OrganizationId) devuelve
/// null a propósito -- el admin de plataforma opera sobre todas las organizaciones.
/// </summary>
public sealed class OrganizationScopeProvider(ICurrentUserContext currentUserContext) : IOrganizationScopeProvider
{
    public Guid? CurrentOrganizationId
    {
        get
        {
            try
            {
                return currentUserContext.OrganizationId;
            }
            catch
            {
                // Sin claim OrganizationId (sesión de PlatformAdmin, o sin sesión
                // todavía) -- sin filtro, mismo criterio que null explícito.
                return null;
            }
        }
    }
}
