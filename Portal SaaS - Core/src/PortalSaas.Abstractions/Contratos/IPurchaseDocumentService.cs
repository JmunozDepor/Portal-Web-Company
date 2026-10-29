using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Motor genérico de documentos de compra (SAP OPQT/OPOR/OPCH, ver PurchaseDocumentType)
/// de la compañía activa -- sin aprobación; las facturas copian líneas de un Pedido (ver
/// el doc-comment de PurchaseDocumentDto). Mismo patrón que
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

    /// <summary>
    /// Mismo payload que CreateAsync, enviado en modo SIMULADO (ver
    /// ISapSession.SimulatePostAsync): SAP lo valida completo y lo revierte, sin crear el
    /// documento. Usado por "Probar formato" de Importación Genérica.
    /// </summary>
    Task<SapSimulationResult> SimulateCreateAsync(PurchaseDocumentType type, string portalUsername, PurchaseDocumentDto document, CancellationToken ct = default) =>
        throw new NotSupportedException("Este servicio no soporta envío simulado.");

    /// <summary>Ver ISalesDocumentService.AddLinesAsync -- mismo patrón (releer + patchear el arreglo completo), pensado para lotes de importación masiva.</summary>
    Task AddLinesAsync(PurchaseDocumentType type, int docEntry, IReadOnlyList<PurchaseDocumentLineDto> newLines, CancellationToken ct = default);

    Task<PurchaseDocumentDto?> GetAsync(PurchaseDocumentType type, int docEntry, CancellationToken ct = default);

    Task<PurchaseDocumentListResult> ListAsync(PurchaseDocumentType type, PurchaseDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default);

    /// <summary>Ver ISalesDocumentService.CloseAsync -- mismo patrón (POST {recurso}(docEntry)/Close).</summary>
    Task CloseAsync(PurchaseDocumentType type, int docEntry, CancellationToken ct = default);

    /// <summary>Ver ISalesDocumentService.CancelAsync -- mismo patrón (POST {recurso}(docEntry)/Cancel).</summary>
    Task CancelAsync(PurchaseDocumentType type, int docEntry, CancellationToken ct = default);

    /// <summary>Ver ISalesDocumentService.SupportsCancel.</summary>
    bool SupportsCancel(PurchaseDocumentType type);

    /// <summary>Código de objeto SAP (NNM1.ObjectCode) del tipo de documento -- ver ISalesDocumentService.GetSapObjectCode.</summary>
    int GetSapObjectCode(PurchaseDocumentType type);

    /// <summary>Particularidades del tipo que lee el formulario base -- ver PurchaseDocumentTypeInfo.</summary>
    PurchaseDocumentTypeInfo Describe(PurchaseDocumentType type);

    /// <summary>
    /// "Copiar de Pedido": Pedidos abiertos del proveedor con al menos una línea pendiente.
    /// Vive en Core (no en Modulo.Compras) para que la Etapa 2 de Facturas de Proveedor
    /// arme la factura aprobada con el detalle de la Orden de compra sin depender del plugin.
    /// </summary>
    Task<IReadOnlyList<PurchaseOpenOrderDto>> ListOpenOrdersAsync(string supplierCardCode, CancellationToken ct = default);

    /// <summary>Líneas pendientes de esos Pedidos (vacío sin consultar si no viene ninguno).</summary>
    Task<IReadOnlyList<PurchaseOpenOrderLineDto>> GetOpenOrderLinesAsync(IReadOnlyCollection<int> docEntries, CancellationToken ct = default);

    /// <summary>DocNum de la factura no cancelada del proveedor que ya usa ese folio, o null -- evita registrar dos veces la misma factura.</summary>
    Task<int?> FindTaxFolioAsync(string supplierCardCode, string folioPrefix, int folioNumber, CancellationToken ct = default);
}
