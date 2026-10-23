using PdfSharp.Pdf;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.Despachos.Tests.Fakes;

/// <summary>Pasarela DTE falsa: emite en memoria con folios correlativos; se puede simular caída o rechazo.</summary>
public sealed class FakeDteEmissionService : IDteEmissionService
{
    public bool Configured { get; set; } = true;
    public string IssuerTaxId { get; set; } = "76123456-0";
    public long NextFolio { get; set; } = 500;

    /// <summary>Claves que responden "proveedor caído" (quedan pending) mientras estén aquí.</summary>
    public HashSet<Guid> Unavailable { get; } = new();
    /// <summary>Si es true, toda emisión nueva queda pendiente (proveedor caído).</summary>
    public bool Down { get; set; }
    /// <summary>Rechazo de negocio para la próxima emisión.</summary>
    public string? RejectNext { get; set; }

    public List<DteEmissionRequest> Calls { get; } = new();
    public List<Guid> Abandoned { get; } = new();
    public Dictionary<Guid, DteEmissionResult> Emissions { get; } = new();

    public Task<DteConfigurationStatus> GetConfigurationAsync(CancellationToken ct = default) => Task.FromResult(Configured
        ? new DteConfigurationStatus(true, "FAKE", "certification", IssuerTaxId, new[] { 33, 52 }, null)
        : new DteConfigurationStatus(false, null, null, null, Array.Empty<int>(), "La compañía no tiene configurados los documentos electrónicos."));

    public Task<IReadOnlyList<DteValidationError>> ValidateAsync(DteDocument doc, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DteValidationError>>(Array.Empty<DteValidationError>());

    public Task<DtePreviewResult> PreviewAsync(DteDocument doc, CancellationToken ct = default) =>
        Task.FromResult(new DtePreviewResult(Array.Empty<DteValidationError>(), OnePagePdf()));

    public Task<DteEmissionResult> EmitAsync(DteEmissionRequest request, CancellationToken ct = default)
    {
        if (!Configured) throw new InvalidOperationException("La compañía no tiene configurados los documentos electrónicos.");
        Calls.Add(request);
        if (Emissions.TryGetValue(request.EmissionKey, out var existing) && existing.Status == DteEmissionStatuses.Issued)
            return Task.FromResult(existing);
        DteEmissionResult r;
        if (Down || Unavailable.Contains(request.EmissionKey))
            r = new DteEmissionResult(request.EmissionKey, DteEmissionStatuses.Pending, null, "FAKE: proveedor sin conexión.", "FAKE", "certification");
        else if (RejectNext is { } msg)
        {
            RejectNext = null;
            r = new DteEmissionResult(request.EmissionKey, DteEmissionStatuses.Failed, null, msg, "FAKE", "certification");
        }
        else
            r = new DteEmissionResult(request.EmissionKey, DteEmissionStatuses.Issued, NextFolio++, null, "FAKE", "certification");
        Emissions[request.EmissionKey] = r;
        return Task.FromResult(r);
    }

    public Task<DteEmissionResult?> GetAsync(Guid emissionKey, CancellationToken ct = default) =>
        Task.FromResult(Emissions.TryGetValue(emissionKey, out var r) ? r : null);

    public Task<byte[]> GetPdfAsync(Guid emissionKey, bool cedible, int copies, CancellationToken ct = default) =>
        Emissions.TryGetValue(emissionKey, out var r) && r.Status == DteEmissionStatuses.Issued
            ? Task.FromResult(OnePagePdf())
            : throw new InvalidOperationException("El documento todavía no está emitido.");

    public Task AbandonAsync(Guid emissionKey, string reason, CancellationToken ct = default)
    {
        if (Emissions.TryGetValue(emissionKey, out var r) && r.Status == DteEmissionStatuses.Issued)
            throw new InvalidOperationException("Ya emitida.");
        Abandoned.Add(emissionKey);
        return Task.CompletedTask;
    }

    internal static byte[] OnePagePdf()
    {
        using var doc = new PdfDocument();
        doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }
}
