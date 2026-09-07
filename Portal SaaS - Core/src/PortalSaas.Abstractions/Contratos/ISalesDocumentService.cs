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

    /// <summary>
    /// Relee el documento tal cual quedó en SAP (con los LineNum reales que SAP asignó) y
    /// patchea el arreglo de líneas COMPLETO (existentes + nuevas) -- Service Layer trata
    /// "DocumentLines" como el estado final del arreglo, así que no alcanza con mandar solo
    /// las líneas nuevas. Pensado para importaciones masivas con miles de filas: crear con
    /// el primer lote y agregar el resto en lotes sucesivos evita un único POST gigante que
    /// puede superar el timeout configurado de Service Layer.
    /// </summary>
    Task AddLinesAsync(SalesDocumentType type, int docEntry, IReadOnlyList<SalesDocumentLineDto> newLines, CancellationToken ct = default);

    Task<SalesDocumentDto?> GetAsync(SalesDocumentType type, int docEntry, CancellationToken ct = default);

    Task<SalesDocumentListResult> ListAsync(SalesDocumentType type, SalesDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default);

    /// <summary>
    /// Cierra el documento en SAP (POST {recurso}(docEntry)/Close) -- acción de negocio
    /// normal, a diferencia de un borrado. El documento sigue existiendo, sin acciones
    /// pendientes. Portado de IGenericoVentaService.CerrarAsync (referencia-original/
    /// PortalSAP_v2).
    /// </summary>
    Task CloseAsync(SalesDocumentType type, int docEntry, CancellationToken ct = default);

    /// <summary>
    /// Cancela el documento en SAP (POST {recurso}(docEntry)/Cancel) -- a diferencia de
    /// CloseAsync, deja rastro de auditoría (movimiento de anulación), pensado para
    /// revertir un documento creado por error. Portado de
    /// IGenericoVentaService.CancelarAsync.
    /// </summary>
    Task CancelAsync(SalesDocumentType type, int docEntry, CancellationToken ct = default);

    /// <summary>
    /// Ver el doc-comment de SupportsCancel en SalesDocumentTypeCatalog.Entry
    /// (PortalSaas.Core) -- documentos de intención (Orden/Solicitud) no soportan
    /// "/Cancel" en Service Layer, solo Close.
    /// </summary>
    bool SupportsCancel(SalesDocumentType type);

    /// <summary>
    /// Código de objeto SAP (NNM1.ObjectCode) del tipo de documento -- lo necesita el
    /// plugin para pedirle a ISeriesCatalogService la lista de series aplicables, sin
    /// que el plugin tenga que conocer SalesDocumentTypeCatalog (vive en
    /// PortalSaas.Core, un plugin nunca lo referencia directo).
    /// </summary>
    int GetSapObjectCode(SalesDocumentType type);
}
