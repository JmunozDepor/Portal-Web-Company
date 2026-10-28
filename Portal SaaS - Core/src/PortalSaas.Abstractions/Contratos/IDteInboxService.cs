using PortalSaas.Abstractions.Modelos.Dte;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Bandeja de documentos electrónicos recibidos de la compañía de la sesión (facturas de proveedor), para módulos.
/// La plataforma trae los documentos del operador DTE, los archiva y responde al SII de forma idempotente; la decisión
/// de aceptar o rechazar es del módulo. Ver la pasarela DTE de plataforma §6.
/// </summary>
public interface IDteInboxService
{
    Task<DteInboxStatus> GetStatusAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DteReceivedDto>> ListAsync(DteReceivedFilter filter, CancellationToken ct = default);

    /// <summary>
    /// Documentos cambiados después del cursor (UpdatedAt, Id), en orden ascendente, como máximo max. El consumidor
    /// guarda el UpdatedAt y el Id del último que procesó y los pasa en la siguiente llamada.
    /// </summary>
    Task<IReadOnlyList<DteReceivedDto>> ListChangedSinceAsync(DateTimeOffset since, long afterId, int max, CancellationToken ct = default);

    Task<DteReceivedDto?> GetAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// El documento completo, leído del XML. Si no está archivado y el operador lo entrega, se pide y se archiva; null si
    /// el operador no lo tiene (ej. documento sin recepción electrónica).
    /// </summary>
    Task<DteReceivedDocument?> GetDocumentAsync(long id, CancellationToken ct = default);

    /// <summary>Líneas de detalle del XML archivado; vacía si no hay XML.</summary>
    Task<IReadOnlyList<DteReceivedLine>> GetLinesAsync(long id, CancellationToken ct = default);

    /// <summary>PDF del documento; null si el proveedor no lo entrega.</summary>
    Task<byte[]?> GetPdfAsync(long id, CancellationToken ct = default);

    /// <summary>Responde ante el SII (idempotente por ResponseKey). Nunca responde dos veces el mismo documento.</summary>
    Task<DteRespondResult> RespondAsync(DteRespondRequest request, CancellationToken ct = default);

    /// <summary>Reemplaza las OC asociadas a mano (las informadas en el DTE se conservan).</summary>
    Task SetPurchaseOrderRefsAsync(long id, IReadOnlyList<int> manualDocNums, Guid userId, CancellationToken ct = default);

    /// <summary>Marca el procesamiento de un módulo posterior (ej. ingreso a SAP).</summary>
    Task MarkProcessingAsync(long id, string moduleCode, string state, string? reference, CancellationToken ct = default);

    /// <summary>Sincroniza ya la compañía de la sesión con el operador ("Leer ahora").</summary>
    Task RequestSyncAsync(CancellationToken ct = default);
}
