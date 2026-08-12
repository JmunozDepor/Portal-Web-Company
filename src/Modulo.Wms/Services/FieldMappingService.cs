using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Wms.Services;

public sealed class FieldMappingService : IFieldMappingService
{
    private readonly WmsDbContext _db;

    public FieldMappingService(WmsDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<WmsFieldMapping>> ListAllAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.FieldMappings
            .Where(m => m.CompanyId == companyId)
            .OrderBy(m => m.MapperKey)
            .ThenBy(m => m.FieldName)
            .ToListAsync(ct);

    public async Task<long> CreateAsync(Guid companyId, string mapperKey, string fieldName, string valueTemplate, bool isActive, string updatedBy, CancellationToken ct = default)
    {
        if (!WmsFieldMapperKeys.Labels.ContainsKey(mapperKey))
        {
            throw new InvalidOperationException("Documento no reconocido.");
        }

        var yaExiste = await _db.FieldMappings.AnyAsync(
            m => m.CompanyId == companyId && m.MapperKey == mapperKey && m.FieldName == fieldName, ct);
        if (yaExiste)
        {
            throw new InvalidOperationException("Ya existe un mapeo para ese documento y campo UDF.");
        }

        var mapping = new WmsFieldMapping
        {
            CompanyId = companyId,
            MapperKey = mapperKey,
            FieldName = fieldName,
            ValueTemplate = valueTemplate,
            IsActive = isActive,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = updatedBy,
        };

        _db.FieldMappings.Add(mapping);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException("Ya existe un mapeo para ese documento y campo UDF.");
        }

        return mapping.Id;
    }

    public async Task UpdateAsync(long id, Guid companyId, string valueTemplate, bool isActive, string updatedBy, CancellationToken ct = default)
    {
        var mapping = await _db.FieldMappings.FirstOrDefaultAsync(m => m.Id == id && m.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("El mapeo no existe o no pertenece a esta compañía.");

        mapping.ValueTemplate = valueTemplate;
        mapping.IsActive = isActive;
        mapping.UpdatedAt = DateTimeOffset.UtcNow;
        mapping.UpdatedBy = updatedBy;

        await _db.SaveChangesAsync(ct);
    }
}
