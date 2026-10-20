using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Documentos;

/// <summary>Ver ICompanyDocumentSettingsService -- tabla company_document_settings.</summary>
public sealed partial class CompanyDocumentSettingsService : ICompanyDocumentSettingsService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public CompanyDocumentSettingsService(PortalSaasDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <summary>Nombre de UDF válido -- se interpola en SQL (nombre de columna), así que la validación es obligatoria.</summary>
    [GeneratedRegex("^U_[A-Za-z0-9_]{1,48}$")]
    internal static partial Regex UdfNamePattern();

    public async Task<int?> GetDraftRetentionDaysAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.CompanyDocumentSettings.AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .Select(s => s.DraftRetentionDays)
            .FirstOrDefaultAsync(ct);

    public async Task SaveDraftRetentionDaysAsync(Guid companyId, int? days, CancellationToken ct = default)
    {
        var settings = await GetOrCreateForAdminAsync(companyId, ct);
        settings.DraftRetentionDays = days is > 0 ? days : null;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetDraftMarkerUdfNameAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.CompanyDocumentSettings.AsNoTracking()
            .Where(s => s.CompanyId == companyId)
            .Select(s => s.DraftMarkerUdfName)
            .FirstOrDefaultAsync(ct);

    public async Task SaveDraftMarkerUdfNameAsync(Guid companyId, string? udfName, CancellationToken ct = default)
    {
        var value = string.IsNullOrWhiteSpace(udfName) ? null : udfName.Trim();
        if (value is not null && !UdfNamePattern().IsMatch(value))
        {
            throw new InvalidOperationException("El UDF debe empezar con \"U_\" y tener solo letras, números o guion bajo (ej. U_PortalDraftId).");
        }

        var settings = await GetOrCreateForAdminAsync(companyId, ct);
        settings.DraftMarkerUdfName = value;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<CompanyDocumentSettings> GetOrCreateForAdminAsync(Guid companyId, CancellationToken ct)
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

        return settings;
    }
}
