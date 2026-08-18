using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages;

/// <summary>Base de Pages/Aprobaciones/* -- exige rol "Aprobador" (ver RendicionesRolePageModelBase).</summary>
public abstract class RendicionesAprobadorPageModelBase : RendicionesRolePageModelBase
{
    protected RendicionesAprobadorPageModelBase(IRendicionesUserRoleService roles, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
    }

    protected override IReadOnlyList<string> RequiredRoles => new[] { RendicionesRoles.Aprobador };
}
