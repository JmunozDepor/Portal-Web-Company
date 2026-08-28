using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages;

/// <summary>Base de Pages/Gastos/Index|Detalle|Importar, Pages/Informes/Index y Pages/FondosPorRendir/Index --
/// exige rol "Rendidor" (ver RendicionesRolePageModelBase). NO se usa para
/// Pages/Gastos/Comprobante|ComprobanteArchivo (tienen su propio control de acceso vía
/// ComprobanteAccesoBase -- dueño, admin o cualquier aprobador del informe, no solo
/// Rendidor) ni para Pages/Informes/Detalle (compartida con Aprobador, ver
/// RendicionesRendidorOAprobadorPageModelBase).</summary>
public abstract class RendicionesRendidorPageModelBase : RendicionesRolePageModelBase
{
    protected RendicionesRendidorPageModelBase(IRendicionesUserRoleService roles, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
    }

    protected override IReadOnlyList<string> RequiredRoles => new[] { RendicionesRoles.Rendidor };
}
