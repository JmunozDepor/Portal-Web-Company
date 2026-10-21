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

/// <summary>Resultado de "Probar": la validación y, si fue válida, el documento mapeado.</summary>
public sealed record SapQueryProbeResult(QueryValidationResult Validation, SapDocumentInfo? Document);

/// <summary>
/// Búsqueda de facturas / guías de traslado por folio con las consultas SAP configuradas
/// por compañía (sap_document_queries), y mantenimiento de esas consultas.
/// </summary>
public interface ISapDocumentLookupService
{
    /// <summary>
    /// Busca el documento por tipo y folio. Null si SAP no lo tiene; lanza
    /// InvalidOperationException si el tipo no tiene consulta configurada.
    /// </summary>
    Task<SapDocumentInfo?> FindAsync(string documentKind, long folio, CancellationToken ct);

    /// <summary>
    /// Valida la consulta (una sola sentencia SELECT/WITH, solo :folio) y la ejecuta con el
    /// folio de prueba comprobando el contrato de columnas. No lanza por errores de SAP.
    /// </summary>
    Task<QueryValidationResult> ValidateQueryAsync(string sql, long sampleFolio);

    /// <summary>Como ValidateQueryAsync pero devuelve además el documento mapeado (pantalla "Probar").</summary>
    Task<SapQueryProbeResult> ProbeAsync(string documentKind, string sql, long sampleFolio);

    /// <summary>Consultas de la compañía (siembra las de HANA si faltan).</summary>
    Task<IReadOnlyList<SapDocumentQuery>> ListQueriesAsync();

    /// <summary>Guarda la consulta del tipo solo si valida con el folio de prueba (fija validated_at).</summary>
    Task SaveQueryAsync(string documentKind, string sql, bool isActive, long sampleFolio);
}
