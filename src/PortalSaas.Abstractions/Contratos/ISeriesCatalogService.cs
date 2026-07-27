using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Catálogo de series de numeración (NNM1) de la compañía SAP activa -- filtrado por
/// el código de objeto SAP del tipo de documento (ej. "17" Órdenes de Venta, "22"
/// Pedidos de Compra), porque una serie solo aplica a un tipo de documento puntual.
/// El código de objeto de cada tipo vive en el catálogo estático del motor genérico
/// correspondiente (SalesDocumentTypeCatalog/PurchaseDocumentTypeCatalog/
/// InventoryDocumentTypeCatalog), no acá -- este servicio no conoce esa relación.
/// </summary>
public interface ISeriesCatalogService
{
    Task<IReadOnlyList<SeriesDto>> ListAsync(string objectCode, string? searchText = null, int? limit = null, CancellationToken ct = default);
}
