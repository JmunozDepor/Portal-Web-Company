using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Componentes;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Usuarios;

/// <summary>Ver IFormLayoutService -- tabla form_field_layouts (FormFieldLayout).</summary>
public sealed class FormLayoutService : IFormLayoutService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _company;

    public FormLayoutService(PortalSaasDbContext db, ICurrentUserContext currentUser, ICurrentCompanyAccessor company)
    {
        _db = db;
        _currentUser = currentUser;
        _company = company;
    }

    public async Task<FormLayoutDto> GetForCurrentUserAsync(string formKey, CancellationToken ct = default)
    {
        if (!_company.HasCompany || DocumentFormFieldCatalog.Find(formKey) is null)
        {
            return FormLayoutDto.Empty;
        }

        var companyId = _company.CompanyId;
        var userId = _currentUser.UserId;
        var rows = await _db.FormFieldLayouts.AsNoTracking()
            .Where(l => l.CompanyId == companyId && l.FormKey == formKey && (l.UserId == null || l.UserId == userId))
            .ToListAsync(ct);

        var baseHidden = Parse(formKey, rows.FirstOrDefault(r => r.UserId == null)?.HiddenFields);
        var own = rows.FirstOrDefault(r => r.UserId == userId);
        return own is null
            ? new FormLayoutDto(baseHidden, baseHidden, false)
            : new FormLayoutDto(Parse(formKey, own.HiddenFields), baseHidden, true);
    }

    public Task SaveForCurrentUserAsync(string formKey, IReadOnlyCollection<string> hiddenFields, CancellationToken ct = default)
    {
        RequireForm(formKey);
        if (!_company.HasCompany)
        {
            throw new InvalidOperationException("No hay una compañía activa en la sesión.");
        }

        return UpsertAsync(_company.CompanyId, _currentUser.UserId, formKey, hiddenFields, ct);
    }

    public async Task ResetForCurrentUserAsync(string formKey, CancellationToken ct = default)
    {
        if (!_company.HasCompany)
        {
            return;
        }

        var companyId = _company.CompanyId;
        var userId = _currentUser.UserId;
        var rows = await _db.FormFieldLayouts
            .Where(l => l.CompanyId == companyId && l.FormKey == formKey && l.UserId == userId)
            .ToListAsync(ct);
        _db.FormFieldLayouts.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetCompanyBaseAsync(Guid companyId, string formKey, CancellationToken ct = default)
    {
        await RequireAdminOfCompanyAsync(companyId, ct);
        var row = await _db.FormFieldLayouts.AsNoTracking()
            .FirstOrDefaultAsync(l => l.CompanyId == companyId && l.FormKey == formKey && l.UserId == null, ct);
        return Parse(formKey, row?.HiddenFields);
    }

    public async Task SaveCompanyBaseAsync(Guid companyId, string formKey, IReadOnlyCollection<string> hiddenFields, CancellationToken ct = default)
    {
        RequireForm(formKey);
        await RequireAdminOfCompanyAsync(companyId, ct);
        await UpsertAsync(companyId, null, formKey, hiddenFields, ct);
    }

    private async Task UpsertAsync(Guid companyId, Guid? userId, string formKey, IReadOnlyCollection<string> hiddenFields, CancellationToken ct)
    {
        var value = string.Join(",", DocumentFormFieldCatalog.Sanitize(formKey, hiddenFields));
        var rows = await _db.FormFieldLayouts
            .Where(l => l.CompanyId == companyId && l.FormKey == formKey && l.UserId == userId)
            .ToListAsync(ct);

        var row = rows.FirstOrDefault();
        if (row is null)
        {
            row = new FormFieldLayout { CompanyId = companyId, UserId = userId, FormKey = formKey };
            _db.FormFieldLayouts.Add(row);
        }

        // Defensivo: el índice no es único (UserId nullable), así que se limpian duplicados.
        _db.FormFieldLayouts.RemoveRange(rows.Skip(1));
        row.HiddenFields = value;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task RequireAdminOfCompanyAsync(Guid companyId, CancellationToken ct)
    {
        if (!_currentUser.IsAdmin)
        {
            throw new UnauthorizedAccessException("Solo un administrador puede definir el formulario base.");
        }

        var organizationId = _currentUser.OrganizationId;
        if (!await _db.Companies.AnyAsync(c => c.Id == companyId && c.OrganizationId == organizationId, ct))
        {
            throw new InvalidOperationException("La compañía no pertenece a tu organización.");
        }
    }

    private static void RequireForm(string formKey)
    {
        if (DocumentFormFieldCatalog.Find(formKey) is null)
        {
            throw new InvalidOperationException($"Formulario desconocido: {formKey}.");
        }
    }

    private static IReadOnlyList<string> Parse(string formKey, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : DocumentFormFieldCatalog.Sanitize(formKey, value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
