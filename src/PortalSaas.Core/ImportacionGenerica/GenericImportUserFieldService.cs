using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Sap;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>
/// CRUD del catálogo maestro de campos de usuario, acotado a la organización actual
/// (ver ICurrentUserContext.OrganizationId) -- portado de
/// CampoUsuarioImportacionGenericaService, pero contra PortalSaasDbContext (organización
/// propia) en vez de HANA (ver CLAUDE.md, "Persistencia config" del 26 jul 2026).
/// </summary>
public sealed class GenericImportUserFieldService : IGenericImportUserFieldService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public GenericImportUserFieldService(PortalSaasDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<GenericImportUserFieldDto>> ListAsync(GenericImportModule? module = null, CancellationToken ct = default)
    {
        var query = _db.GenericImportUserFields.Where(f => f.OrganizationId == _currentUser.OrganizationId);
        if (module is { } m)
        {
            query = query.Where(f => f.Module == m.ToString());
        }

        var rows = await query.OrderBy(f => f.Label).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<GenericImportUserFieldDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var row = await _db.GenericImportUserFields
            .FirstOrDefaultAsync(f => f.Id == id && f.OrganizationId == _currentUser.OrganizationId, ct);
        return row is null ? null : Map(row);
    }

    public async Task<int> CreateAsync(GenericImportModule module, GenericImportFieldLevel level, string label,
        string sapFieldName, GenericImportFieldDataType dataType, CancellationToken ct = default)
    {
        if (SapAdditionalFieldsHelper.IsReservedName(sapFieldName))
        {
            throw new InvalidOperationException($"\"{sapFieldName}\" es un nombre reservado por el motor -- un campo de usuario no puede pisarlo.");
        }

        var entity = new GenericImportUserField
        {
            OrganizationId = _currentUser.OrganizationId,
            Module = module.ToString(),
            Level = level.ToString(),
            Label = label,
            SapFieldName = sapFieldName,
            DataType = dataType.ToString(),
            IsActive = true,
        };

        _db.GenericImportUserFields.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task UpdateAsync(int id, string label, string sapFieldName, GenericImportFieldDataType dataType,
        bool isActive, CancellationToken ct = default)
    {
        if (SapAdditionalFieldsHelper.IsReservedName(sapFieldName))
        {
            throw new InvalidOperationException($"\"{sapFieldName}\" es un nombre reservado por el motor -- un campo de usuario no puede pisarlo.");
        }

        var entity = await _db.GenericImportUserFields
            .FirstOrDefaultAsync(f => f.Id == id && f.OrganizationId == _currentUser.OrganizationId, ct)
            ?? throw new InvalidOperationException("Campo de usuario no encontrado.");

        entity.Label = label;
        entity.SapFieldName = sapFieldName;
        entity.DataType = dataType.ToString();
        entity.IsActive = isActive;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.GenericImportUserFields
            .FirstOrDefaultAsync(f => f.Id == id && f.OrganizationId == _currentUser.OrganizationId, ct);
        if (entity is null)
        {
            return;
        }

        _db.GenericImportUserFields.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }

    private static GenericImportUserFieldDto Map(GenericImportUserField row) => new(
        row.Id,
        Enum.Parse<GenericImportModule>(row.Module),
        Enum.Parse<GenericImportFieldLevel>(row.Level),
        row.Label,
        row.SapFieldName,
        Enum.Parse<GenericImportFieldDataType>(row.DataType),
        row.IsActive);
}
