using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Documentos;

/// <summary>Ver IDocumentDraftService -- tabla document_drafts (DocumentDraft).</summary>
public sealed class DocumentDraftService : IDocumentDraftService
{
    /// <summary>Un "sending" sin cambios por más de esto se considera interrumpido (servidor caído a mitad del envío).</summary>
    internal static readonly TimeSpan StaleSending = TimeSpan.FromMinutes(10);

    private readonly PortalSaasDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _company;
    private readonly INotificationService _notifications;

    public DocumentDraftService(PortalSaasDbContext db, ICurrentUserContext currentUser, ICurrentCompanyAccessor company,
        INotificationService notifications)
    {
        _db = db;
        _currentUser = currentUser;
        _company = company;
        _notifications = notifications;
    }

    public async Task<Guid> SaveAsync(DocumentDraftSaveRequest request, CancellationToken ct = default)
    {
        var companyId = RequireCompany();
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new InvalidOperationException("Indicá el motivo por el que el documento queda en borrador.");
        }

        DocumentDraft draft;
        if (request.Id is { } id)
        {
            draft = await _db.DocumentDrafts.FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId, ct)
                ?? throw new InvalidOperationException("El borrador ya no existe -- puede haber sido eliminado.");
            if (!DocumentDraftStatus.IsEditable(draft.Status))
            {
                throw new InvalidOperationException(draft.Status == DocumentDraftStatus.Sent
                    ? "Este borrador ya fue enviado a SAP."
                    : "Este borrador se está enviando a SAP en este momento.");
            }
        }
        else
        {
            draft = new DocumentDraft
            {
                CompanyId = companyId,
                Engine = request.Engine,
                DocumentType = request.DocumentType,
                CreatedByUserId = _currentUser.UserId,
                CreatedByUsername = _currentUser.Username,
            };
            _db.DocumentDrafts.Add(draft);
        }

        draft.Reason = Truncate(request.Reason.Trim(), 500)!;
        draft.PayloadJson = request.PayloadJson;
        draft.PartnerCode = Truncate(request.PartnerCode?.Trim(), 50);
        draft.Reference = Truncate(request.Reference?.Trim(), 100);
        draft.DocDate = request.DocDate;
        draft.LineCount = request.LineCount;
        draft.Total = request.Total;
        draft.UpdatedByUsername = _currentUser.Username;
        draft.UpdatedAt = DateTimeOffset.UtcNow;
        draft.Version++;

        await _db.SaveChangesAsync(ct);
        return draft.Id;
    }

    public async Task<DocumentDraftDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        if (!_company.HasCompany)
        {
            return null;
        }

        var companyId = _company.CompanyId;
        var draft = await _db.DocumentDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId, ct);
        return draft is null ? null : Map(draft);
    }

    public async Task<DocumentDraftListResult> ListAsync(DocumentDraftQuery query, CancellationToken ct = default)
    {
        if (!_company.HasCompany)
        {
            return new DocumentDraftListResult([], 0);
        }

        var companyId = _company.CompanyId;
        var drafts = _db.DocumentDrafts.AsNoTracking()
            .Where(d => d.CompanyId == companyId && d.Engine == query.Engine && d.DocumentType == query.DocumentType);

        if (query.OnlyMine)
        {
            var userId = _currentUser.UserId;
            drafts = drafts.Where(d => d.CreatedByUserId == userId);
        }

        drafts = query.StatusGroup switch
        {
            "enviados" => drafts.Where(d => d.Status == DocumentDraftStatus.Sent),
            "todos" => drafts,
            _ => drafts.Where(d => d.Status != DocumentDraftStatus.Sent),
        };

        var total = await drafts.CountAsync(ct);
        var pageSize = query.PageSize is 50 or 100 ? query.PageSize : 25;
        var items = await drafts
            .OrderByDescending(d => d.UpdatedAt)
            .Skip(Math.Max(0, query.Page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new DocumentDraftListResult(items.Select(Map).ToList(), total);
    }

    public async Task DeleteAsync(Guid id, string? notifyUrl = null, CancellationToken ct = default)
    {
        var companyId = RequireCompany();
        var draft = await _db.DocumentDrafts.FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId, ct);
        if (draft is null)
        {
            return;
        }

        if (draft.Status == DocumentDraftStatus.Sending && !IsStale(draft))
        {
            throw new InvalidOperationException("Este borrador se está enviando a SAP en este momento -- esperá a que termine.");
        }

        _db.DocumentDrafts.Remove(draft);
        await _db.SaveChangesAsync(ct);

        if (draft.CreatedByUserId != _currentUser.UserId && draft.Status != DocumentDraftStatus.Sent)
        {
            await _notifications.TryEnqueueAsync(draft.CreatedByUserId, "Borrador eliminado",
                $"{_currentUser.Username} eliminó tu borrador ({Describe(draft)}). Motivo del borrador: {draft.Reason}",
                NotificationSeverity.Warning, notifyUrl, ct);
        }
    }

    public async Task<bool> TryBeginSendAsync(Guid id, CancellationToken ct = default)
    {
        var companyId = RequireCompany();
        var draft = await _db.DocumentDrafts.FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId, ct);
        if (draft is null)
        {
            return false;
        }

        var canTake = DocumentDraftStatus.IsEditable(draft.Status) || (draft.Status == DocumentDraftStatus.Sending && IsStale(draft));
        if (!canTake)
        {
            return false;
        }

        draft.Status = DocumentDraftStatus.Sending;
        draft.ErrorMessage = null;
        draft.UpdatedByUsername = _currentUser.Username;
        draft.UpdatedAt = DateTimeOffset.UtcNow;
        draft.Version++;
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Otro usuario/pestaña lo tomó entre la lectura y el guardado.
            _db.Entry(draft).State = EntityState.Detached;
            return false;
        }
    }

    public async Task MarkSentAsync(Guid id, int docEntry, string? notifyUrl = null, CancellationToken ct = default)
    {
        var companyId = RequireCompany();
        var draft = await _db.DocumentDrafts.FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId, ct);
        if (draft is null)
        {
            return;
        }

        draft.Status = DocumentDraftStatus.Sent;
        draft.SapDocEntry = docEntry;
        draft.ErrorMessage = null;
        draft.SentAt = DateTimeOffset.UtcNow;
        draft.UpdatedByUsername = _currentUser.Username;
        draft.UpdatedAt = DateTimeOffset.UtcNow;
        draft.Version++;
        await _db.SaveChangesAsync(ct);

        if (draft.CreatedByUserId != _currentUser.UserId)
        {
            await _notifications.TryEnqueueAsync(draft.CreatedByUserId, "Borrador enviado a SAP",
                $"{_currentUser.Username} envió tu borrador ({Describe(draft)}) -- DocEntry {docEntry}.",
                NotificationSeverity.Success, notifyUrl, ct);
        }
    }

    public async Task MarkErrorAsync(Guid id, string errorMessage, CancellationToken ct = default)
    {
        var companyId = RequireCompany();
        var draft = await _db.DocumentDrafts.FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId, ct);
        if (draft is null)
        {
            return;
        }

        draft.Status = DocumentDraftStatus.Error;
        draft.ErrorMessage = Truncate(errorMessage, 2000);
        draft.UpdatedByUsername = _currentUser.Username;
        draft.UpdatedAt = DateTimeOffset.UtcNow;
        draft.Version++;
        await _db.SaveChangesAsync(ct);
    }

    private Guid RequireCompany() => _company.HasCompany
        ? _company.CompanyId
        : throw new InvalidOperationException("No hay una compañía activa en la sesión.");

    private static bool IsStale(DocumentDraft draft) => DateTimeOffset.UtcNow - draft.UpdatedAt > StaleSending;

    private static string Describe(DocumentDraft draft) =>
        string.Join(" · ", new[] { draft.PartnerCode, draft.Reference, $"{draft.LineCount} línea(s)" }.Where(s => !string.IsNullOrWhiteSpace(s)));

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];

    private static DocumentDraftDto Map(DocumentDraft d) => new(
        d.Id, d.Engine, d.DocumentType, d.Status, d.Reason, d.PayloadJson, d.PartnerCode, d.Reference, d.DocDate,
        d.LineCount, d.Total, d.CreatedByUserId, d.CreatedByUsername, d.UpdatedByUsername, d.SapDocEntry, d.ErrorMessage,
        d.CreatedAt, d.UpdatedAt, d.SentAt);
}
