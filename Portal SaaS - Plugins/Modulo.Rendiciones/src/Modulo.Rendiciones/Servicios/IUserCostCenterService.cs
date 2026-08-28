using Modulo.Rendiciones.Models;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Servicios;

public interface IUserCostCenterService
{
    Task<IReadOnlyList<UserCostCenter>> ListAssignedAsync(Guid companyId, Guid userId, CancellationToken ct = default);

    Task AssignAsync(Guid companyId, Guid userId, string costCenterCode, string? costCenterName, CancellationToken ct = default);

    Task RemoveAsync(long id, Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Conteo de asignaciones por usuario para TODA la compañía en una sola consulta --
    /// para el listado de Configuracion/CentrosCostoUsuario, que antes hacía una
    /// consulta separada por usuario (N round-trips contra la base externa del plugin,
    /// lento cuando esa base está en un servidor remoto real).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountAssignedByUserAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Centros de costo que el usuario puede elegir al declarar un gasto/fondo: los que
    /// tenga asignados si tiene alguno; si no tiene ninguno todavía (el administrador no
    /// configuró nada), cae al catálogo completo de SAP -- evita bloquear a todo el
    /// mundo hasta que Administración termine de asignar centros de costo uno por uno.
    /// </summary>
    Task<IReadOnlyList<CostCenterDto>> GetAvailableAsync(Guid companyId, Guid userId, CancellationToken ct = default);
}
