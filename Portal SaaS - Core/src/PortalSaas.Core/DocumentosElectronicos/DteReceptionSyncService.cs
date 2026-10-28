using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Modelos.Dte;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.DocumentosElectronicos;

public sealed record DteReceptionSyncResult(int New, int Updated, int MarkedExternal, string? Error);

/// <summary>
/// Trae a dte_received lo recibido por el operador DTE de una compañía. Ventana móvil por fecha de emisión (la primera
/// vez, ReceptionInitialDays hacia atrás; después, ReceptionWindowDays) y upsert por (compañía, RUT emisor, tipo,
/// folio). Detecta respuestas hechas fuera del portal (DteReceivedRecord.RespondedAction) y confirma las propias que
/// quedaron pendientes. Un error del proveedor queda en company_dte_settings.reception_last_error, sin credenciales.
/// </summary>
public sealed class DteReceptionSyncService
{
    private readonly PortalSaasDbContext _db;
    private readonly DteSettingsService _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<DteReceptionSyncService> _logger;

    public DteReceptionSyncService(PortalSaasDbContext db, DteSettingsService settings, TimeProvider clock, ILogger<DteReceptionSyncService> logger)
    {
        _db = db;
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    public async Task<DteReceptionSyncResult> SyncCompanyAsync(Guid companyId, CancellationToken ct)
    {
        var resolved = await _settings.ResolveReceptionAsync(companyId);
        var row = await _db.CompanyDteSettings.FirstOrDefaultAsync(x => x.CompanyId == companyId, ct);
        if (resolved is null || row is null)
            return new DteReceptionSyncResult(0, 0, 0, "La recepción de documentos no está habilitada para esta compañía.");

        var (ctx, provider) = resolved.Value;
        var now = _clock.GetUtcNow();
        var today = ChileTime.Today(now);
        var initial = row.ReceptionInitialLoadDoneAt is null;
        var from = today.AddDays(-(initial ? row.ReceptionInitialDays : row.ReceptionWindowDays));

        int created = 0, updated = 0, external = 0;
        try
        {
            var records = await provider.ListReceivedAsync(ctx, from, today, ct);
            var existing = await _db.DteReceived
                .Where(x => x.CompanyId == companyId && x.IssueDate >= from && x.IssueDate <= today)
                .ToListAsync(ct);
            var byKey = existing.ToDictionary(x => (x.IssuerTaxId, x.DteType, x.Folio));

            foreach (var record in records)
            {
                if (byKey.TryGetValue((record.IssuerTaxId, record.DteType, record.Folio), out var doc))
                {
                    var changed = ApplyProviderData(doc, record);
                    var wasExternal = doc.ResponseStatus == DteResponseStatuses.External;
                    changed |= ApplyOperatorResponse(doc, record.RespondedAction, now);
                    if (!wasExternal && doc.ResponseStatus == DteResponseStatuses.External) external++;
                    if (changed)
                    {
                        doc.UpdatedAt = now;
                        updated++;
                    }
                    continue;
                }

                var refs = await provider.GetReferencesAsync(ctx, record.IssuerTaxId, record.DteType, record.Folio, ct);
                doc = new DteReceived
                {
                    CompanyId = companyId,
                    ProviderCode = provider.Code,
                    Environment = ctx.Environment,
                    IssuerTaxId = record.IssuerTaxId,
                    DteType = record.DteType,
                    Folio = record.Folio,
                    IssueDate = record.IssueDate,
                    ReferencesJson = DteReceivedJson.References(refs),
                    PurchaseOrderRefsJson = DteReceivedJson.PurchaseOrderRefs(DteReceivedJson.PurchaseOrdersFromReferences(refs, now)),
                    Xml = provider.Capabilities.SupportsDocumentDownload ? await provider.GetXmlAsync(ctx, record, ct) : null,
                    FirstSeenAt = now,
                    UpdatedAt = now,
                };
                ApplyProviderData(doc, record);
                ApplyOperatorResponse(doc, record.RespondedAction, now);
                if (doc.ResponseStatus == DteResponseStatuses.External) external++;
                _db.DteReceived.Add(doc);
                byKey[(doc.IssuerTaxId, doc.DteType, doc.Folio)] = doc;
                created++;
            }

            row.ReceptionLastSyncAt = now;
            row.ReceptionLastError = null;
            if (initial) row.ReceptionInitialLoadDoneAt = now;
            await _db.SaveChangesAsync(ct);
            return new DteReceptionSyncResult(created, updated, external, null);
        }
        catch (DteProviderUnavailableException ex)
        {
            // Lo leído hasta acá se descarta: el próximo ciclo vuelve a leer la misma ventana.
            foreach (var entry in _db.ChangeTracker.Entries<DteReceived>().ToList()) entry.State = EntityState.Detached;
            var message = Sanitize(ex.Message, ctx);
            row.ReceptionLastSyncAt = now;
            row.ReceptionLastError = message.Length > 500 ? message[..500] : message;
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("Recepción DTE (compañía {CompanyId}): {Error}", companyId, message);
            return new DteReceptionSyncResult(0, 0, 0, message);
        }
    }

    /// <summary>Copia los datos del proveedor; true si algo cambió.</summary>
    private static bool ApplyProviderData(DteReceived doc, DteReceivedRecord r)
    {
        var siiReceivedAt = r.SiiReceivedAt?.ToUniversalTime();
        var changed = doc.IssuerName != r.IssuerName || doc.SiiReceivedAt != siiReceivedAt || doc.NetAmount != r.Net
                      || doc.ExemptAmount != r.Exempt || doc.VatAmount != r.Vat || doc.TotalAmount != r.Total
                      || doc.OtherTaxesJson != r.OtherTaxesJson || doc.SiiStatus != r.SiiStatus
                      || doc.CommercialStatus != r.CommercialStatus || doc.ClaimStatus != r.ClaimStatus
                      || doc.IsAssigned != r.IsAssigned || doc.IsVoided != r.IsVoided || doc.ProviderUrl != r.Url
                      || doc.NotRespondableReason != r.NotRespondableReason;
        doc.IssuerName = r.IssuerName;
        doc.SiiReceivedAt = siiReceivedAt;
        doc.NetAmount = r.Net;
        doc.ExemptAmount = r.Exempt;
        doc.VatAmount = r.Vat;
        doc.TotalAmount = r.Total;
        doc.OtherTaxesJson = r.OtherTaxesJson;
        doc.SiiStatus = r.SiiStatus;
        doc.CommercialStatus = r.CommercialStatus;
        doc.ClaimStatus = r.ClaimStatus;
        doc.IsAssigned = r.IsAssigned;
        doc.IsVoided = r.IsVoided;
        doc.ProviderUrl = r.Url;
        doc.NotRespondableReason = r.NotRespondableReason;
        return changed;
    }

    /// <summary>
    /// El operador ya muestra una respuesta final: si el portal no había respondido (o falló) es externa; si el portal
    /// tenía una pendiente, se confirma cuando coincide y pasa a externa cuando no.
    /// </summary>
    private static bool ApplyOperatorResponse(DteReceived doc, string? respondedAction, DateTimeOffset now)
    {
        if (respondedAction is null) return false;
        switch (doc.ResponseStatus)
        {
            case DteResponseStatuses.None or DteResponseStatuses.Failed:
                doc.ResponseStatus = DteResponseStatuses.External;
                doc.ResponseAction = respondedAction;
                return true;
            case DteResponseStatuses.Pending when doc.ResponseAction == respondedAction:
                doc.ResponseStatus = DteResponseStatuses.Sent;
                doc.RespondedAt = now;
                doc.LastError = null;
                return true;
            case DteResponseStatuses.Pending:
                doc.ResponseStatus = DteResponseStatuses.External;
                doc.ResponseAction = respondedAction;
                return true;
            default:
                return false;
        }
    }

    private static string Sanitize(string message, DteReceptionContext ctx)
    {
        foreach (var secret in new[] { ctx.Password, ctx.Username })
        {
            if (!string.IsNullOrEmpty(secret)) message = message.Replace(secret, "***", StringComparison.Ordinal);
        }
        return message;
    }
}
