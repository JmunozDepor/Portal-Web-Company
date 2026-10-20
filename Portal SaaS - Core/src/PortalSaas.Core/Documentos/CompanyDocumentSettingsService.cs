using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Documentos;

/// <summary>Ver ICompanyDocumentSettingsService -- tabla company_document_settings.</summary>
public sealed class CompanyDocumentSettingsService : ICompanyDocumentSettingsService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public CompanyDocumentSettingsService(PortalSaasDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<int?> GetDraftRetentionDaysAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.CompanyDocumentSettings.AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .Select(s => s.DraftRetentionDays)
            .FirstOrDefaultAsync(ct);

    public async Task SaveDraftRetentionDaysAsync(Guid companyId, int? days, CancellationToken ct = default)
    {
        if (!_currentUser.IsAdmin)
        {
            throw new UnauthorizedAccessException("Solo un administrador puede cambiar los parámetros de documentos.");
        }

        var organizationId = _currentUser.OrganizationId;
        if (!await _db.Companies.AnyAsync(c => c.Id == companyId && c.OrganizationId == organizationId, ct))
        {
            throw new InvalidOperationException("La compañía no pertenece a tu organización.");
        }

        var settings = await _db.CompanyDocumentSettings.FirstOrDefaultAsync(s => s.CompanyId == companyId, ct);
        if (settings is null)
        {
            settings = new CompanyDocumentSettings { CompanyId = companyId };
            _db.CompanyDocumentSettings.Add(settings);
        }

        settings.DraftRetentionDays = days is > 0 ? days : null;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
