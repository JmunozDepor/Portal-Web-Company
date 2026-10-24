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

    /// <summary>Transferencias de SAP (Fase B): por LPN y pendientes por bodega desde una fecha.</summary>
    public List<SapTransferInfo> Transfers { get; } = new();

    /// <summary>Bodegas de SAP; si <see cref="WarehousesError"/> está asignado, la consulta lanza.</summary>
    public List<SapWarehouse> Warehouses { get; } = new();
    public string? WarehousesError { get; set; }

    public Task<SapTransferInfo?> FindTransferByLpnAsync(string lpn, CancellationToken ct = default) =>
        Task.FromResult(Transfers.FirstOrDefault(t => string.Equals(t.Lpn, lpn, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<SapTransferInfo>> ListPendingTransfersAsync(string warehouseCode, DateOnly fromDate, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SapTransferInfo>>(Transfers
            .Where(t => t.WarehouseCode == warehouseCode && t.DocumentDate >= fromDate && !t.IsCanceled).ToList());

    public Task<IReadOnlyList<SapTransferInfo>> ListPendingTransfersAllAsync(DateOnly fromDate, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SapTransferInfo>>(Transfers
            .Where(t => t.DocumentDate >= fromDate && !t.IsCanceled).ToList());

    public Task<IReadOnlyList<SapWarehouse>> ListSapWarehousesAsync(CancellationToken ct = default) =>
        WarehousesError is null
            ? Task.FromResult<IReadOnlyList<SapWarehouse>>(Warehouses)
            : throw new InvalidOperationException(WarehousesError);

    /// <summary>Socios de negocio por RUT normalizado.</summary>
    public Dictionary<string, SapBusinessPartner> Partners { get; } = new();

    public Task<SapBusinessPartner?> FindBusinessPartnerAsync(string taxId, CancellationToken ct = default) =>
        Task.FromResult(Partners.TryGetValue(Modulo.Despachos.Dominio.RutChileno.Normalize(taxId), out var p) ? p : null);

    public Task<QueryValidationResult> ValidateQueryAsync(string sql, long sampleFolio) => throw new NotSupportedException();
    public Task<SapQueryProbeResult> ProbeAsync(string documentKind, string sql, long sampleFolio, string? sampleText = null) => throw new NotSupportedException();
    public Task<IReadOnlyList<SapDocumentQuery>> ListQueriesAsync() => throw new NotSupportedException();
    public Task SaveQueryAsync(string documentKind, string sql, bool isActive, long sampleFolio, string? sampleText = null) => throw new NotSupportedException();
}
