namespace Modulo.Despachos.Servicios;

/// <summary>
/// Escritura de campos de cabecera en una transferencia de stock de SAP (Service Layer, recurso StockTransfers):
/// el folio de la guía de traslado (FolioNumber / FolioPrefixString) y los campos de usuario configurados (Fase B).
/// </summary>
public interface ISapTransferWriter
{
    /// <summary>PATCH de los campos en la transferencia <paramref name="docEntry"/>. Lanza con el mensaje de SAP si la rechaza.</summary>
    Task WriteAsync(int docEntry, IReadOnlyDictionary<string, object?> fields, CancellationToken ct = default);

    /// <summary>Valor actual de los campos pedidos (null si SAP no los informa).</summary>
    Task<IReadOnlyDictionary<string, object?>> ReadAsync(int docEntry, IReadOnlyList<string> fields, CancellationToken ct = default);

    /// <summary>PATCH de los campos en la Entrega <paramref name="docEntry"/> (DeliveryNotes, Entregas con guía previa).</summary>
    Task WriteDeliveryAsync(int docEntry, IReadOnlyDictionary<string, object?> fields, CancellationToken ct = default);

    /// <summary>Valor actual de los campos pedidos de la Entrega (null si SAP no los informa).</summary>
    Task<IReadOnlyDictionary<string, object?>> ReadDeliveryAsync(int docEntry, IReadOnlyList<string> fields, CancellationToken ct = default);
}
