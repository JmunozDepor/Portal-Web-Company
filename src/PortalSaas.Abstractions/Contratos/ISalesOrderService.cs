using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Órdenes de Venta (SAP ORDR/Orders) de la compañía activa -- digitación directa, sin
/// aprobación ni Copy-From (a diferencia de Compras). Primer plugin de negocio real
/// que habla con el SAP de una organización, ver CLAUDE.md para el alcance recortado
/// de esta primera entrega.
/// </summary>
public interface ISalesOrderService
{
    /// <summary>
    /// Hardcodeado true por ahora -- todavía no existe el toggle runtime por compañía
    /// (equivalente a IPermiteCrearDocumentoService de la referencia), se agrega cuando
    /// haya una pantalla de administración que lo gobierne.
    /// </summary>
    Task<bool> CanCreateAsync(CancellationToken ct = default);

    /// <summary>
    /// portalUsername se graba en el UDF de trazabilidad U_PortalUser -- prerrequisito
    /// manual en SAP (crear ese UDF alfanumérico en ORDR/RDR1) antes de que esto
    /// funcione contra un ambiente real.
    /// </summary>
    Task<int> CreateAsync(string portalUsername, SalesOrderDto document, CancellationToken ct = default);

    Task<SalesOrderDto?> GetAsync(int docEntry, CancellationToken ct = default);

    Task<SalesOrderListResult> ListAsync(SalesOrderFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default);
}
