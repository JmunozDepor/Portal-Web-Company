using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Motor genérico de documentos de compra (SAP OPQT/OPOR, ver PurchaseDocumentType) de
/// la compañía activa -- digitación directa, sin aprobación ni Copy-From en esta
/// entrega (ver el doc-comment de PurchaseDocumentDto). Mismo patrón que
/// ISalesDocumentService -- portado de IGenericoCompraService en
/// referencia-original/PortalSAP_v2.
/// </summary>
public interface IPurchaseDocumentService
{
    Task<bool> CanCreateAsync(PurchaseDocumentType type, CancellationToken ct = default);

    /// <summary>
    /// portalUsername se graba en el UDF de trazabilidad U_PortalUser -- prerrequisito
    /// manual en SAP (crear ese UDF alfanumérico en la tabla de cabecera del tipo)
    /// antes de que esto funcione contra un ambiente real.
    /// </summary>
    Task<int> CreateAsync(PurchaseDocumentType type, string portalUsername, PurchaseDocumentDto document, CancellationToken ct = default);

    Task<PurchaseDocumentDto?> GetAsync(PurchaseDocumentType type, int docEntry, CancellationToken ct = default);

    Task<PurchaseDocumentListResult> ListAsync(PurchaseDocumentType type, PurchaseDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default);
}
