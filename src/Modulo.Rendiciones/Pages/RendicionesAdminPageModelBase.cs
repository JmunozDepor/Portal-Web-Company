using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages;

/// <summary>Base de las 9 páginas de Pages/Configuracion/* -- exige rol "Administrador" (ver RendicionesRolePageModelBase).</summary>
public abstract class RendicionesAdminPageModelBase : RendicionesRolePageModelBase
{
    protected RendicionesAdminPageModelBase(IRendicionesUserRoleService roles, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
    }

    protected override IReadOnlyList<string> RequiredRoles => new[] { RendicionesRoles.Administrador };
}
