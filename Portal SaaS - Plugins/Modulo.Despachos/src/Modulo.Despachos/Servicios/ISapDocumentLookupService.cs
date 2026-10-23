using Modulo.Despachos.Models;

namespace Modulo.Despachos.Servicios;

/// <summary>Documento SAP encontrado por folio, ya mapeado al contrato fijo de columnas.</summary>
public sealed record SapDocumentInfo(
    string DocumentKind, int SiiDocumentType, long Folio, int DocEntry, int DocNum, DateOnly DocumentDate,
    string CustomerCode, string CustomerName, string? CustomerTaxId, string? DestinationAddress,
    string? DestinationCommune, string? CustomerPo, int? BaseOrderEntry, decimal Units, int Packages,
    decimal TotalAmount, bool IsCanceled, decimal? WeightKg, decimal? VolumeM3);

/// <summary>Resultado de validar una consulta SAP (estática + ejecución de prueba).</summary>
public sealed record QueryValidationResult(bool IsValid, IReadOnlyList<string> MissingColumns, string? Error);

/// <summary>Línea de un documento SAP (consulta "invoice_lines"). WeightKg null = el artículo no tiene peso en SAP.</summary>
/// <summary>
/// Línea de factura. Columnas opcionales: "model_code"/"model_name" (código y nombre sin talla) y "line_amount"
/// (monto neto de la línea, para valorizar la guía).
/// </summary>
public sealed record SapDocumentLine(int LineNum, string ItemCode, string ItemName, decimal Quantity, string? Unit, decimal? WeightKg,
    string? ModelCode = null, string? ModelName = null, decimal? Amount = null);

/// <summary>Líneas de un documento + datos del receptor que solo trae esa consulta (giro y ciudad de despacho).</summary>
public sealed record SapDocumentLines(string? CustomerActivity, string? DestinationCity, IReadOnlyList<SapDocumentLine> Lines);

/// <summary>Resultado de "Probar": la validación y, si fue válida, el documento (o las líneas) mapeado.</summary>
public sealed record SapQueryProbeResult(QueryValidationResult Validation, SapDocumentInfo? Document, SapDocumentLines? Lines = null);

/// <summary>
/// Búsqueda de facturas / guías de traslado por folio con las consultas SAP configuradas
/// por compañía (sap_document_queries), y mantenimiento de esas consultas.
/// </summary>
public interface ISapDocumentLookupService
{
    /// <summary>
    /// Busca el documento por tipo y folio. Null si SAP no lo tiene; lanza
    /// InvalidOperationException si el tipo no tiene consulta configurada o si el folio existe
    /// con más de un tipo SII y no se indicó <paramref name="siiDocumentType"/> (33 vs 34).
    /// Siempre se enlazan :folio y :sii_document_type (0 = sin filtro).
    /// </summary>
    Task<SapDocumentInfo?> FindAsync(string documentKind, long folio, int? siiDocumentType = null, CancellationToken ct = default);

    /// <summary>
    /// Valida la consulta (una sola sentencia SELECT/WITH, solo :folio) y la ejecuta con el
    /// folio de prueba comprobando el contrato de columnas. No lanza por errores de SAP.
    /// </summary>
    Task<QueryValidationResult> ValidateQueryAsync(string sql, long sampleFolio);

    /// <summary>Como ValidateQueryAsync pero devuelve además el documento mapeado (pantalla "Probar").</summary>
    Task<SapQueryProbeResult> ProbeAsync(string documentKind, string sql, long sampleFolio);

    /// <summary>
    /// Líneas del documento SAP <paramref name="docEntry"/> con la consulta <paramref name="queryKind"/>
    /// (hoy solo <see cref="SapQueryKinds.InvoiceLines"/>, parámetro :doc_entry). Lanza InvalidOperationException
    /// si la consulta no está configurada o no cumple el contrato de columnas.
    /// </summary>
    Task<SapDocumentLines> GetLinesAsync(string queryKind, int docEntry, CancellationToken ct = default);

    /// <summary>Consultas de la compañía (siembra las de HANA si faltan).</summary>
    Task<IReadOnlyList<SapDocumentQuery>> ListQueriesAsync();

    /// <summary>Guarda la consulta del tipo solo si valida con el folio de prueba (fija validated_at).</summary>
    Task SaveQueryAsync(string documentKind, string sql, bool isActive, long sampleFolio);
}
