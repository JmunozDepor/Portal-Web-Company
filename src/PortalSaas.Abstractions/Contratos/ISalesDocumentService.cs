using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Motor genérico de documentos de venta (SAP ORDR/ORIN/OINV/ORRR/ORDN, ver
/// SalesDocumentType) de la compañía activa -- digitación directa, sin aprobación ni
/// Copy-From (a diferencia de Compras). Generaliza el antiguo ISalesOrderService (una
/// Orden de Venta es hoy solo SalesDocumentType.SalesOrder, ver CLAUDE.md) -- portado de
/// IGenericoVentaService en referencia-original/PortalSAP_v2: el plugin nunca conoce
/// tabla/recurso SAP de cada tipo, solo pasa el SalesDocumentType (ver
/// SalesDocumentTypeCatalog en PortalSaas.Core).
/// </summary>
public interface ISalesDocumentService
{
    /// <summary>
    /// Default fijo por tipo (ver SalesDocumentTypeCatalog) -- todavía no existe el
    /// toggle runtime por organización (equivalente a IPermiteCrearDocumentoService de
    /// la referencia), queda documentado como pendiente en CLAUDE.md.
    /// </summary>
    Task<bool> CanCreateAsync(SalesDocumentType type, CancellationToken ct = default);

    /// <summary>
    /// portalUsername se graba en el UDF de trazabilidad U_PortalUser -- prerrequisito
    /// manual en SAP (crear ese UDF alfanumérico en la tabla de cabecera del tipo)
    /// antes de que esto funcione contra un ambiente real.
    /// </summary>
    Task<int> CreateAsync(SalesDocumentType type, string portalUsername, SalesDocumentDto document, CancellationToken ct = default);

    Task<SalesDocumentDto?> GetAsync(SalesDocumentType type, int docEntry, CancellationToken ct = default);

    Task<SalesDocumentListResult> ListAsync(SalesDocumentType type, SalesDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default);
}
