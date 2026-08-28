using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages;

/// <summary>Base de Pages/Informes/Detalle -- la única pantalla que sirve a las dos audiencias
/// a la vez (el rendidor dueño del informe Y el aprobador que debe resolverlo), ver el
/// comentario de IndexModel de Aprobaciones ("reutiliza Informes/Detalle para Aprobar/
/// Rechazar"). Rol Administrador ya pasa por el bypass IsAdmin de la base, no hace
/// falta listarlo acá.</summary>
public abstract class RendicionesRendidorOAprobadorPageModelBase : RendicionesRolePageModelBase
{
    protected RendicionesRendidorOAprobadorPageModelBase(IRendicionesUserRoleService roles, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
    }

    protected override IReadOnlyList<string> RequiredRoles => new[] { RendicionesRoles.Rendidor, RendicionesRoles.Aprobador };
}
