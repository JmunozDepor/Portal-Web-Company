using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Fakes;

/// <summary>Búsqueda SAP de prueba: devuelve documentos de <see cref="Docs"/>; replica el error de folio ambiguo.</summary>
public sealed class FakeSapDocumentLookupService : ISapDocumentLookupService
{
    public List<SapDocumentInfo> Docs { get; } = new();
    public List<(string Kind, long Folio, int? Sii)> Calls { get; } = new();

    public Task<SapDocumentInfo?> FindAsync(string documentKind, long folio, int? siiDocumentType = null, CancellationToken ct = default)
    {
        Calls.Add((documentKind, folio, siiDocumentType));
        var found = Docs.Where(d => d.DocumentKind == documentKind && d.Folio == folio
                                    && (siiDocumentType == null || d.SiiDocumentType == siiDocumentType)).ToList();
        if (found.Select(d => d.SiiDocumentType).Distinct().Count() > 1)
            throw new InvalidOperationException(
                $"El folio {folio} existe con más de un tipo de documento (33, 34); indique el tipo.");
        return Task.FromResult(found.FirstOrDefault());
    }

    /// <summary>Líneas por DocEntry (consulta invoice_lines). Sin entrada = lanza como la consulta sin configurar.</summary>
    public Dictionary<int, SapDocumentLines> Lines { get; } = new();

    public Task<SapDocumentLines> GetLinesAsync(string queryKind, int docEntry, CancellationToken ct = default) =>
        Lines.TryGetValue(docEntry, out var l)
            ? Task.FromResult(l)
            : throw new InvalidOperationException("Configure la consulta SAP de líneas de factura en Despachos > Configuración > Consultas SAP.");

    public Task<QueryValidationResult> ValidateQueryAsync(string sql, long sampleFolio) => throw new NotSupportedException();
    public Task<SapQueryProbeResult> ProbeAsync(string documentKind, string sql, long sampleFolio) => throw new NotSupportedException();
    public Task<IReadOnlyList<SapDocumentQuery>> ListQueriesAsync() => throw new NotSupportedException();
    public Task SaveQueryAsync(string documentKind, string sql, bool isActive, long sampleFolio) => throw new NotSupportedException();
}
