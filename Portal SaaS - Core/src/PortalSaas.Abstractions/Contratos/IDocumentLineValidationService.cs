namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Restricciones de las líneas de los formularios genéricos de documento (Venta/Compra/
/// Inventario) -- mismo criterio que las reglas de Importación Genérica
/// (StockAvailableRule / PriceVs*ListRule), pero SIEMPRE bloqueantes (decisión del dueño
/// del proyecto, 2026-10-20: "como restricción, al igual que SAP"). Se corre al importar
/// el Excel de líneas (marca cada línea) y otra vez al crear el documento (servidor).
///
/// - Stock: demanda agregada de TODAS las líneas por (artículo, bodega de origen) vs.
///   disponible real (OnHand - IsCommited). Solo en documentos que sacan mercadería.
/// - Precio: el precio de la línea vs. el que corresponde (precio especial del socio
///   vigente a la fecha; si no tiene, la lista elegida). Sin precio de referencia o con
///   monedas distintas, la línea no se valida (igual que Importación Genérica).
/// </summary>
public interface IDocumentLineValidationService
{
    Task<IReadOnlyList<DocumentLineIssue>> ValidateAsync(DocumentLineValidationRequest request, CancellationToken ct = default);
}

/// <param name="Key">Identificador de la línea que devuelve cada problema (índice de la fila).</param>
/// <param name="Warehouse">Bodega de la que sale el stock (Almacén en Venta, Almacén origen en Inventario).</param>
/// <param name="Currency">Moneda en que está expresado UnitPrice (null = moneda del documento).</param>
public sealed record DocumentLineCheck(int Key, string ItemCode, string? Warehouse, decimal Quantity, decimal? UnitPrice, string? Currency = null);

public sealed record DocumentLineValidationRequest(
    IReadOnlyList<DocumentLineCheck> Lines,
    bool ValidateStock,
    bool ValidatePrice,
    string? CardCode = null,
    int? PriceList = null,
    DateOnly? Date = null);

/// <param name="Field">"Quantity" | "UnitPrice" -- campo de la línea a marcar.</param>
public sealed record DocumentLineIssue(int Key, string Field, string Message);
