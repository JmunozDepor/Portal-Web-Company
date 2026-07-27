using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>
/// CRUD de la configuración de importación genérica, acotado a la organización actual
/// -- portado de ConfiguracionImportacionGenericaService, pero contra
/// PortalSaasDbContext en vez de HANA (ver CLAUDE.md, "Persistencia config" del 26 jul
/// 2026). El detalle de campos se reemplaza por completo en cada Create/Update (borra y
/// vuelve a crear, mismo criterio que ItemCrossReferenceService.SyncAsync).
/// </summary>
public sealed class GenericImportConfigService : IGenericImportConfigService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public GenericImportConfigService(PortalSaasDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<GenericImportConfigDto>> ListAsync(GenericImportModule? module = null, CancellationToken ct = default)
    {
        var query = _db.GenericImportConfigs.Include(c => c.Fields)
            .Where(c => c.OrganizationId == _currentUser.OrganizationId);
        if (module is { } m)
        {
            query = query.Where(c => c.Module == m.ToString());
        }

        var rows = await query.OrderBy(c => c.Alias).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<GenericImportConfigDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var row = await _db.GenericImportConfigs.Include(c => c.Fields)
            .FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == _currentUser.OrganizationId, ct);
        return row is null ? null : Map(row);
    }

    public async Task<GenericImportConfigDto?> ResolveAsync(GenericImportModule module, string documentType,
        GenericImportLineType lineType, string? businessPartnerCardCode, CancellationToken ct = default)
    {
        var moduleStr = module.ToString();
        var lineTypeStr = lineType.ToString();

        if (!string.IsNullOrWhiteSpace(businessPartnerCardCode))
        {
            var exception = await _db.GenericImportConfigs.Include(c => c.Fields)
                .FirstOrDefaultAsync(c => c.OrganizationId == _currentUser.OrganizationId && c.Module == moduleStr
                    && c.DocumentType == documentType && c.LineType == lineTypeStr
                    && c.BusinessPartnerCardCode == businessPartnerCardCode && c.IsActive, ct);
            if (exception is not null)
            {
                return Map(exception);
            }
        }

        var standard = await _db.GenericImportConfigs.Include(c => c.Fields)
            .FirstOrDefaultAsync(c => c.OrganizationId == _currentUser.OrganizationId && c.Module == moduleStr
                && c.DocumentType == documentType && c.LineType == lineTypeStr
                && c.BusinessPartnerCardCode == null && c.IsActive, ct);

        return standard is null ? null : Map(standard);
    }

    public async Task<int> CreateAsync(GenericImportModule module, string documentType, GenericImportLineType lineType,
        string? businessPartnerCardCode, string? groupingColumn, bool skuIsCustomerOwn, string alias,
        IReadOnlyList<GenericImportConfigFieldDto> fields,
        GenericImportPriceSource priceSource = GenericImportPriceSource.BusinessPartner, int? systemPriceListCode = null,
        CancellationToken ct = default)
    {
        var entity = new GenericImportConfig
        {
            OrganizationId = _currentUser.OrganizationId,
            Module = module.ToString(),
            DocumentType = documentType,
            LineType = lineType.ToString(),
            BusinessPartnerCardCode = string.IsNullOrWhiteSpace(businessPartnerCardCode) ? null : businessPartnerCardCode,
            GroupingColumn = string.IsNullOrWhiteSpace(groupingColumn) ? null : groupingColumn,
            SkuIsCustomerOwn = skuIsCustomerOwn,
            Alias = alias,
            IsActive = true,
            PriceSource = priceSource.ToString(),
            SystemPriceListCode = systemPriceListCode,
            Fields = fields.Select(MapField).ToList(),
        };

        _db.GenericImportConfigs.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task UpdateAsync(int id, string? groupingColumn, bool skuIsCustomerOwn, string alias, bool isActive,
        IReadOnlyList<GenericImportConfigFieldDto> fields,
        GenericImportPriceSource priceSource = GenericImportPriceSource.BusinessPartner, int? systemPriceListCode = null,
        CancellationToken ct = default)
    {
        var entity = await _db.GenericImportConfigs.Include(c => c.Fields)
            .FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == _currentUser.OrganizationId, ct)
            ?? throw new InvalidOperationException("Configuración no encontrada.");

        entity.GroupingColumn = string.IsNullOrWhiteSpace(groupingColumn) ? null : groupingColumn;
        entity.SkuIsCustomerOwn = skuIsCustomerOwn;
        entity.Alias = alias;
        entity.IsActive = isActive;
        entity.PriceSource = priceSource.ToString();
        entity.SystemPriceListCode = systemPriceListCode;

        _db.GenericImportConfigFields.RemoveRange(entity.Fields);
        entity.Fields = fields.Select(MapField).ToList();

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.GenericImportConfigs
            .FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == _currentUser.OrganizationId, ct);
        if (entity is null)
        {
            return;
        }

        _db.GenericImportConfigs.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }

    private static GenericImportConfigField MapField(GenericImportConfigFieldDto dto) => new()
    {
        LogicalField = dto.LogicalField.ToString(),
        ExcelColumn = dto.ExcelColumn,
        IsRequired = dto.IsRequired,
        FixedValue = dto.FixedValue,
        UserFieldId = dto.UserFieldId,
    };

    private static GenericImportConfigDto Map(GenericImportConfig row) => new(
        row.Id,
        row.OrganizationId,
        Enum.Parse<GenericImportModule>(row.Module),
        row.DocumentType,
        Enum.Parse<GenericImportLineType>(row.LineType),
        row.BusinessPartnerCardCode,
        row.GroupingColumn,
        row.SkuIsCustomerOwn,
        row.Alias,
        row.IsActive,
        row.Fields.Select(f => new GenericImportConfigFieldDto(
            f.Id,
            Enum.Parse<GenericImportLogicalField>(f.LogicalField),
            f.ExcelColumn,
            f.IsRequired,
            f.FixedValue,
            f.UserFieldId)).ToList(),
        Enum.Parse<GenericImportPriceSource>(row.PriceSource),
        row.SystemPriceListCode);
}
