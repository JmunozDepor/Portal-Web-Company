using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Motor genérico de documentos de inventario (SAP OWTQ/OWTR, ver
/// InventoryDocumentType) de la compañía activa -- digitación directa, sin aprobación
/// ni Copy-From. Mismo patrón que ISalesDocumentService (el plugin nunca conoce tabla/
/// recurso SAP de cada tipo, solo pasa el InventoryDocumentType, ver
/// InventoryDocumentTypeCatalog en PortalSaas.Core) -- portado de
/// IGenericoInventarioService en referencia-original/PortalSAP_v2.
/// </summary>
public interface IInventoryDocumentService
{
    Task<bool> CanCreateAsync(InventoryDocumentType type, CancellationToken ct = default);

    /// <summary>
    /// portalUsername se graba en el UDF de trazabilidad U_PortalUser -- prerrequisito
    /// manual en SAP (crear ese UDF alfanumérico en la tabla de cabecera del tipo)
    /// antes de que esto funcione contra un ambiente real.
    /// </summary>
    Task<int> CreateAsync(InventoryDocumentType type, string portalUsername, InventoryDocumentDto document, CancellationToken ct = default);

    /// <summary>Ver ISalesDocumentService.AddLinesAsync -- mismo patrón (releer + patchear el arreglo completo), pensado para lotes de importación masiva.</summary>
    Task AddLinesAsync(InventoryDocumentType type, int docEntry, IReadOnlyList<InventoryDocumentLineDto> newLines, CancellationToken ct = default);

    Task<InventoryDocumentDto?> GetAsync(InventoryDocumentType type, int docEntry, CancellationToken ct = default);

    Task<InventoryDocumentListResult> ListAsync(InventoryDocumentType type, InventoryDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default);

    /// <summary>Código de objeto SAP (NNM1.ObjectCode) del tipo de documento -- ver ISalesDocumentService.GetSapObjectCode.</summary>
    int GetSapObjectCode(InventoryDocumentType type);
}
