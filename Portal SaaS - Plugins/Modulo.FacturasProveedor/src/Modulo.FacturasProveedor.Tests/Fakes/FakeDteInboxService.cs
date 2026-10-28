using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.FacturasProveedor.Tests.Fakes;

/// <summary>
/// Bandeja de plataforma en memoria. Items se cargan a mano; RespondAsync registra el pedido y responde
/// NextRespondOutcome (Sent por defecto), marcando el documento como lo haría la plataforma.
/// </summary>
public sealed class FakeDteInboxService : IDteInboxService
{
    public List<DteReceivedDto> Items { get; } = new();
    public List<DteRespondRequest> Responses { get; } = new();
    public List<(long Id, IReadOnlyList<int> DocNums, Guid UserId)> ManualRefs { get; } = new();
    public Dictionary<long, IReadOnlyList<DteReceivedLine>> Lines { get; } = new();
    public int SyncRequests { get; private set; }

    public DteInboxStatus Status { get; set; } = new(
        true, "FAKE", new[] { 33, 34, 43 }, DteResponseActions.All, false, false, 60, 30, null, null);

    /// <summary>Resultado de la próxima respuesta (después vuelve a Sent).</summary>
    public DteRespondOutcome? NextRespondOutcome { get; set; }

    private long _nextId = 1;

    /// <summary>Agrega un recibido con valores razonables; devuelve el DTO guardado.</summary>
    public DteReceivedDto Add(
        string issuerTaxId = "76412553-1", int dteType = 33, long folio = 18452, long total = 2380000,
        DateTimeOffset? siiReceivedAt = null, DateOnly? issueDate = null, IReadOnlyList<int>? purchaseOrders = null,
        string responseStatus = DteResponseStatuses.None, string? responseAction = null, string? claimStatus = null,
        string issuerName = "Comercial Textil Andes SpA", DateTimeOffset? updatedAt = null, string? notRespondableReason = null,
        string? paymentForm = null, DateOnly? dueDate = null, bool hasPdf = false, IReadOnlyList<DteReceivedReference>? references = null)
    {
        var received = siiReceivedAt ?? new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var dto = new DteReceivedDto
        {
            Id = _nextId++, ProviderCode = "FAKE", IssuerTaxId = issuerTaxId, IssuerName = issuerName, DteType = dteType,
            Folio = folio, IssueDate = issueDate ?? DateOnly.FromDateTime(received.UtcDateTime.AddDays(-1)),
            SiiReceivedAt = received, TotalAmount = total, NetAmount = total, ClaimStatus = claimStatus,
            PurchaseOrderRefs = (purchaseOrders ?? Array.Empty<int>())
                .Select(n => new DtePurchaseOrderRef(n, DtePurchaseOrderRefSources.Dte, null, received)).ToList(),
            ResponseStatus = responseStatus, ResponseAction = responseAction, NotRespondableReason = notRespondableReason,
            PaymentForm = paymentForm, DueDate = dueDate, HasPdf = hasPdf, References = references ?? Array.Empty<DteReceivedReference>(),
            UpdatedAt = updatedAt ?? received.AddMinutes(_nextId),
        };
        Items.Add(dto);
        return dto;
    }

    public void Replace(DteReceivedDto updated) => Items[Items.FindIndex(i => i.Id == updated.Id)] = updated;

    public Task<DteInboxStatus> GetStatusAsync(CancellationToken ct = default) => Task.FromResult(Status);

    public Task<IReadOnlyList<DteReceivedDto>> ListAsync(DteReceivedFilter filter, CancellationToken ct = default)
    {
        IEnumerable<DteReceivedDto> q = Items;
        if (filter.ReceivedFrom is { } from) q = q.Where(i => i.SiiReceivedAt is null || DateOnly.FromDateTime(i.SiiReceivedAt.Value.UtcDateTime) >= from);
        if (filter.ReceivedTo is { } to) q = q.Where(i => i.SiiReceivedAt is null || DateOnly.FromDateTime(i.SiiReceivedAt.Value.UtcDateTime) <= to);
        return Task.FromResult<IReadOnlyList<DteReceivedDto>>(q.ToList());
    }

    /// <summary>Demora de la lectura por cursor (para simular dos ciclos en paralelo).</summary>
    public TimeSpan ListDelay { get; set; }

    public async Task<IReadOnlyList<DteReceivedDto>> ListChangedSinceAsync(DateTimeOffset since, long afterId, int max, CancellationToken ct = default)
    {
        if (ListDelay > TimeSpan.Zero) await Task.Delay(ListDelay, ct);
        return Items
            .Where(i => i.UpdatedAt > since || (i.UpdatedAt == since && i.Id > afterId))
            .OrderBy(i => i.UpdatedAt).ThenBy(i => i.Id).Take(max).ToList();
    }

    public Task<DteReceivedDto?> GetAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(i => i.Id == id));

    public Task<IReadOnlyList<DteReceivedLine>> GetLinesAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Lines.TryGetValue(id, out var lines) ? lines : (IReadOnlyList<DteReceivedLine>)Array.Empty<DteReceivedLine>());

    /// <summary>Documento leído del XML por id de la bandeja (lo que entregaría el operador).</summary>
    public Dictionary<long, DteReceivedDocument> Documents { get; } = new();

    public Task<DteReceivedDocument?> GetDocumentAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Documents.TryGetValue(id, out var document) ? document : null);

    /// <summary>Un documento de ejemplo (factura exenta con transporte y dos líneas).</summary>
    public static DteReceivedDocument SampleDocument(DteReceivedDto dto) => new(
        dto.DteType, dto.Folio, dto.IssueDate, new DateOnly(2026, 12, 12), 2, "45 DIAS", null, null, null, false,
        new DteParty(dto.IssuerTaxId, dto.IssuerName, "AGENCIA DE CARGA", "AV. PROVIDENCIA 1000", "PROVIDENCIA", "SANTIAGO"),
        new DteParty("76030680-0", "COMERCIAL DEPOR LIMITADA", "VENTA AL POR MAYOR", "AV. PRUEBA 1500", "LO BARNECHEA", null),
        new DteReceivedTransport(null, null, null, null, "BODEGA CENTRAL", "PUDAHUEL", "SANTIAGO"),
        new[]
        {
            new DteReceivedLine(1, null, "PICK UP", 1, 82752, 82752, "PICK UP (=> USD 84 X 985.14)"),
            new DteReceivedLine(2, "FUEL", "FUEL SURCHARGE", 2, 56040, 100872, null, "UN", 10, 11208),
        },
        Array.Empty<DteReceivedReference>(), 0, dto.TotalAmount, null, 0, Array.Empty<DteOtherTax>(), dto.TotalAmount);

    /// <summary>PDF por id de la bandeja (lo que entregaría el operador).</summary>
    public Dictionary<long, byte[]> Pdfs { get; } = new();

    public Task<byte[]?> GetPdfAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Pdfs.TryGetValue(id, out var pdf) ? pdf : null);

    public Task<DteRespondResult> RespondAsync(DteRespondRequest request, CancellationToken ct = default)
    {
        var item = Items.FirstOrDefault(i => i.Id == request.ReceivedId)
            ?? throw new InvalidOperationException("No se encontró el documento recibido en la compañía activa.");
        Responses.Add(request);
        if (item.NotRespondableReason is { } reason) return Task.FromResult(new DteRespondResult(DteRespondOutcome.NotRespondable, reason));
        var outcome = NextRespondOutcome ?? DteRespondOutcome.Sent;
        NextRespondOutcome = null;
        if (outcome == DteRespondOutcome.Sent)
            Replace(item with { ResponseStatus = DteResponseStatuses.Sent, ResponseAction = request.Action, ResponseActor = request.Actor });
        else if (outcome == DteRespondOutcome.AlreadyResponded)
            Replace(item with { ResponseStatus = DteResponseStatuses.External });
        return Task.FromResult(new DteRespondResult(outcome, outcome == DteRespondOutcome.Failed ? "FAKE: sin conexión." : null));
    }

    public Task SetPurchaseOrderRefsAsync(long id, IReadOnlyList<int> manualDocNums, Guid userId, CancellationToken ct = default)
    {
        ManualRefs.Add((id, manualDocNums, userId));
        var item = Items.First(i => i.Id == id);
        var refs = item.PurchaseOrderRefs.Where(r => r.Source == DtePurchaseOrderRefSources.Dte)
            .Concat(manualDocNums.Select(n => new DtePurchaseOrderRef(n, DtePurchaseOrderRefSources.Manual, userId, DateTimeOffset.UtcNow)))
            .ToList();
        Replace(item with { PurchaseOrderRefs = refs, UpdatedAt = item.UpdatedAt.AddSeconds(1) });
        return Task.CompletedTask;
    }

    public Task MarkProcessingAsync(long id, string moduleCode, string state, string? reference, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task RequestSyncAsync(CancellationToken ct = default)
    {
        SyncRequests++;
        return Task.CompletedTask;
    }
}
