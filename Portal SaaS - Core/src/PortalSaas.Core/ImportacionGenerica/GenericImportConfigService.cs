using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>
/// CRUD de la configuración de importación genérica, acotado a la COMPAÑÍA activa (no
/// la organización) -- regla dura del proyecto: el layout de columnas del Excel es
/// propio de la Company (cada Company puede tener su propio SAP con UDFs/series
/// distintas aunque compartan Organization), ver GenericImportConfig. Portado de
/// ConfiguracionImportacionGenericaService, pero contra PortalSaasDbContext en vez de
/// HANA (ver CLAUDE.md, "Persistencia config" del 26 jul 2026). El detalle de campos se
/// reemplaza por completo en cada Create/Update (borra y vuelve a crear, mismo criterio
/// que ItemCrossReferenceService.SyncAsync).
/// </summary>
public sealed class GenericImportConfigService : IGenericImportConfigService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public GenericImportConfigService(PortalSaasDbContext db, ICurrentCompanyAccessor currentCompany)
    {
        _db = db;
        _currentCompany = currentCompany;
    }

    public async Task<IReadOnlyList<GenericImportConfigDto>> ListAsync(GenericImportModule? module = null, CancellationToken ct = default)
    {
        var query = _db.GenericImportConfigs.Include(c => c.Fields).Include(c => c.ValidationRules)
            .Where(c => c.CompanyId == _currentCompany.CompanyId);
        if (module is { } m)
        {
            query = query.Where(c => c.Module == m.ToString());
        }

        var rows = await query.OrderBy(c => c.Alias).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<GenericImportConfigDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var row = await _db.GenericImportConfigs.Include(c => c.Fields).Include(c => c.ValidationRules)
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == _currentCompany.CompanyId, ct);
        return row is null ? null : Map(row);
    }

    public async Task<GenericImportConfigDto?> ResolveAsync(GenericImportModule module, string documentType,
        GenericImportLineType lineType, string? businessPartnerCardCode, int? configId = null, CancellationToken ct = default)
    {
        var cardCode = string.IsNullOrWhiteSpace(businessPartnerCardCode) ? null : businessPartnerCardCode.Trim();
        var candidates = ActiveForCombination(module.ToString(), documentType, lineType.ToString());

        if (configId is { } id)
        {
            // Formato elegido en el wizard -- mismo filtro de combinación + "del socio o
            // estándar", así un id manipulado no puede traer el formato de otro socio.
            var chosen = await candidates
                .FirstOrDefaultAsync(c => c.Id == id && (c.BusinessPartnerCardCode == null || c.BusinessPartnerCardCode == cardCode), ct);
            return chosen is null ? null : Map(chosen);
        }

        if (cardCode is not null)
        {
            var exception = await candidates
                .Where(c => c.BusinessPartnerCardCode == cardCode)
                .OrderBy(c => c.Alias)
                .FirstOrDefaultAsync(ct);
            if (exception is not null)
            {
                return Map(exception);
            }
        }

        var standard = await candidates
            .Where(c => c.BusinessPartnerCardCode == null)
            .OrderBy(c => c.Alias)
            .FirstOrDefaultAsync(ct);

        return standard is null ? null : Map(standard);
    }

    public async Task<IReadOnlyList<GenericImportConfigDto>> ListAvailableAsync(GenericImportModule module, string documentType,
        GenericImportLineType lineType, string? businessPartnerCardCode, CancellationToken ct = default)
    {
        var cardCode = string.IsNullOrWhiteSpace(businessPartnerCardCode) ? null : businessPartnerCardCode.Trim();
        var rows = await ActiveForCombination(module.ToString(), documentType, lineType.ToString())
            .Where(c => c.BusinessPartnerCardCode == null || c.BusinessPartnerCardCode == cardCode)
            .ToListAsync(ct);

        // Los del socio primero (son la razón de elegirlo), después los estándar.
        return rows
            .OrderBy(c => c.BusinessPartnerCardCode == null ? 1 : 0)
            .ThenBy(c => c.Alias, StringComparer.CurrentCultureIgnoreCase)
            .Select(Map)
            .ToList();
    }

    private IQueryable<GenericImportConfig> ActiveForCombination(string module, string documentType, string lineType)
    {
        var companyId = _currentCompany.CompanyId;
        return _db.GenericImportConfigs.Include(c => c.Fields).Include(c => c.ValidationRules)
            .Where(c => c.CompanyId == companyId && c.Module == module
                && c.DocumentType == documentType && c.LineType == lineType && c.IsActive);
    }

    /// <summary>
    /// Alias único dentro de la misma combinación Módulo+Tipo de documento+Tipo de línea+
    /// Socio -- es lo que distingue un formato de otro en el selector del wizard (y el
    /// índice único de la base, ver PortalSaasDbContext). Mensaje claro en vez de la
    /// excepción de índice de la base.
    /// </summary>
    private async Task EnsureAliasIsUniqueAsync(string module, string documentType, string lineType,
        string? businessPartnerCardCode, string alias, int? excludeId, CancellationToken ct)
    {
        var companyId = _currentCompany.CompanyId;
        var exists = await _db.GenericImportConfigs.AnyAsync(c => c.CompanyId == companyId && c.Module == module
            && c.DocumentType == documentType && c.LineType == lineType
            && c.BusinessPartnerCardCode == businessPartnerCardCode && c.Alias == alias
            && (excludeId == null || c.Id != excludeId), ct);
        if (exists)
        {
            throw new InvalidOperationException(
                $"Ya existe un formato llamado \"{alias}\" para esta combinación de módulo, tipo de documento, tipo de línea y socio -- usá otro alias.");
        }
    }

    public async Task<int> CreateAsync(GenericImportModule module, string documentType, GenericImportLineType lineType,
        string? businessPartnerCardCode, string? groupingColumn, GenericImportItemIdentifier itemIdentifier, string alias,
        IReadOnlyList<GenericImportConfigFieldDto> fields,
        GenericImportPriceSource priceSource = GenericImportPriceSource.BusinessPartner, int? systemPriceListCode = null,
        bool businessPartnerFromFile = false,
        CancellationToken ct = default)
    {
        alias = (alias ?? string.Empty).Trim();
        businessPartnerCardCode = string.IsNullOrWhiteSpace(businessPartnerCardCode) ? null : businessPartnerCardCode.Trim();
        await EnsureAliasIsUniqueAsync(module.ToString(), documentType, lineType.ToString(), businessPartnerCardCode, alias, null, ct);

        var entity = new GenericImportConfig
        {
            CompanyId = _currentCompany.CompanyId,
            Module = module.ToString(),
            DocumentType = documentType,
            LineType = lineType.ToString(),
            BusinessPartnerCardCode = string.IsNullOrWhiteSpace(businessPartnerCardCode) ? null : businessPartnerCardCode,
            GroupingColumn = GroupingColumns.Normalize(groupingColumn),
            ItemIdentifier = itemIdentifier.ToString(),
            Alias = alias,
            IsActive = true,
            PriceSource = priceSource.ToString(),
            SystemPriceListCode = systemPriceListCode,
            BusinessPartnerFromFile = businessPartnerFromFile,
            Fields = fields.Select(MapField).ToList(),
        };

        _db.GenericImportConfigs.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task UpdateAsync(int id, string? groupingColumn, GenericImportItemIdentifier itemIdentifier, string alias, bool isActive,
        IReadOnlyList<GenericImportConfigFieldDto> fields,
        GenericImportPriceSource priceSource = GenericImportPriceSource.BusinessPartner, int? systemPriceListCode = null,
        bool businessPartnerFromFile = false,
        CancellationToken ct = default)
    {
        var entity = await _db.GenericImportConfigs.Include(c => c.Fields).Include(c => c.ValidationRules)
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == _currentCompany.CompanyId, ct)
            ?? throw new InvalidOperationException("Configuración no encontrada.");

        alias = (alias ?? string.Empty).Trim();
        await EnsureAliasIsUniqueAsync(entity.Module, entity.DocumentType, entity.LineType, entity.BusinessPartnerCardCode, alias, entity.Id, ct);

        entity.GroupingColumn = GroupingColumns.Normalize(groupingColumn);
        entity.ItemIdentifier = itemIdentifier.ToString();
        entity.Alias = alias;
        entity.IsActive = isActive;
        entity.PriceSource = priceSource.ToString();
        entity.SystemPriceListCode = systemPriceListCode;
        entity.BusinessPartnerFromFile = businessPartnerFromFile;

        _db.GenericImportConfigFields.RemoveRange(entity.Fields);
        entity.Fields = fields.Select(MapField).ToList();

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.GenericImportConfigs
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == _currentCompany.CompanyId, ct);
        if (entity is null)
        {
            return;
        }

        _db.GenericImportConfigs.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }

    private static readonly IReadOnlyList<GenericImportValidationRuleType> PriceRuleTypes =
        [GenericImportValidationRuleType.PriceVsFixedList, GenericImportValidationRuleType.PriceVsCustomerList];

    // Nombre de UDF de OCRD: se interpola en SQL (no se puede parametrizar un nombre de
    // columna), así que se exige el formato estricto de un campo de usuario de SAP.
    private static readonly System.Text.RegularExpressions.Regex UserFieldNamePattern =
        new("^U_[A-Za-z0-9_]{1,46}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    internal static bool IsValidUserFieldName(string? name) => name is not null && UserFieldNamePattern.IsMatch(name);

    public async Task SaveDocumentOptionsAsync(int configId, GenericImportDocumentOptionsDto options, CancellationToken ct = default)
    {
        var entity = await _db.GenericImportConfigs
            .FirstOrDefaultAsync(c => c.Id == configId && c.CompanyId == _currentCompany.CompanyId, ct)
            ?? throw new InvalidOperationException("Configuración no encontrada.");

        var userField = string.IsNullOrWhiteSpace(options.WarehouseUserField) ? null : options.WarehouseUserField.Trim();
        var usesUserField = options.WarehouseSource is GenericImportWarehouseSource.BusinessPartnerField or GenericImportWarehouseSource.Screen;
        if (usesUserField)
        {
            userField ??= GenericImportDocumentOptionsDto.DefaultWarehouseUserField;
            if (!IsValidUserFieldName(userField))
            {
                throw new InvalidOperationException($"\"{userField}\" no es un campo de usuario válido para el almacén del cliente -- debe empezar con U_ y tener solo letras, números y guion bajo (ej. U_NX_WhsCode).");
            }
        }

        if (options.SalesPersonMode == GenericImportSalesPersonMode.Fixed && options.SalesPersonCode is null)
        {
            throw new InvalidOperationException("Elegí el vendedor fijo del formato.");
        }

        entity.WarehouseSource = options.WarehouseSource.ToString();
        entity.WarehouseUserField = usesUserField ? userField : null;
        entity.BranchSource = options.BranchSource.ToString();
        entity.SalesPersonMode = options.SalesPersonMode.ToString();
        entity.SalesPersonCode = options.SalesPersonMode == GenericImportSalesPersonMode.Fixed ? options.SalesPersonCode : null;
        entity.AnalyzeStock = options.AnalyzeStock;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SaveValidationRulesAsync(int configId, IReadOnlyList<GenericImportValidationRuleAssignmentDto> rules, CancellationToken ct = default)
    {
        var activePriceRules = rules.Where(r => r.IsActive && PriceRuleTypes.Contains(r.RuleType)).ToList();
        if (activePriceRules.Count > 1)
        {
            throw new InvalidOperationException(
                "Solo se puede activar una regla de precio a la vez (\"Precio vs. lista fija\" o \"Precio vs. lista del cliente\").");
        }

        var entity = await _db.GenericImportConfigs.Include(c => c.ValidationRules)
            .FirstOrDefaultAsync(c => c.Id == configId && c.CompanyId == _currentCompany.CompanyId, ct)
            ?? throw new InvalidOperationException("Configuración no encontrada.");

        _db.GenericImportValidationRuleAssignments.RemoveRange(entity.ValidationRules);
        entity.ValidationRules = rules.Select(MapRuleAssignment).ToList();

        await _db.SaveChangesAsync(ct);
    }

    private static GenericImportValidationRuleAssignment MapRuleAssignment(GenericImportValidationRuleAssignmentDto dto) => new()
    {
        RuleType = dto.RuleType.ToString(),
        Severity = dto.Severity.ToString(),
        IsActive = dto.IsActive,
        ParametersJson = dto.Parameters.Count > 0 ? JsonSerializer.Serialize(dto.Parameters) : null,
    };

    private static GenericImportValidationRuleAssignmentDto MapRuleAssignmentDto(GenericImportValidationRuleAssignment row) => new(
        row.Id,
        Enum.Parse<GenericImportValidationRuleType>(row.RuleType),
        Enum.Parse<GenericImportValidationSeverity>(row.Severity),
        row.IsActive,
        ParseParameters(row.ParametersJson));

    /// <summary>
    /// Deserializa ParametersJson a primitivos CLR (long/decimal/string/bool), NO a
    /// JsonElement -- las reglas hacen Convert.ToInt32/ToDecimal sobre estos valores y
    /// JsonElement no implementa IConvertible (tiraba InvalidCastException al procesar un
    /// archivo con una regla parametrizada activa, ej. StockAvailable).
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ParseParameters(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new Dictionary<string, object?>();
        }

        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
        return raw.ToDictionary(kv => kv.Key, kv => (object?)(kv.Value.ValueKind switch
        {
            JsonValueKind.Number => kv.Value.TryGetInt64(out var l) ? l : kv.Value.GetDecimal(),
            JsonValueKind.String => kv.Value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => kv.Value.GetRawText(),
        }));
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
        row.CompanyId,
        Enum.Parse<GenericImportModule>(row.Module),
        row.DocumentType,
        Enum.Parse<GenericImportLineType>(row.LineType),
        row.BusinessPartnerCardCode,
        row.GroupingColumn,
        Enum.TryParse<GenericImportItemIdentifier>(row.ItemIdentifier, out var itemIdentifier) ? itemIdentifier : GenericImportItemIdentifier.ItemCode,
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
        row.SystemPriceListCode,
        row.BusinessPartnerFromFile,
        row.ValidationRules.Select(MapRuleAssignmentDto).ToList())
    {
        DocumentOptions = new GenericImportDocumentOptionsDto(
            Enum.TryParse<GenericImportWarehouseSource>(row.WarehouseSource, out var warehouseSource) ? warehouseSource : GenericImportWarehouseSource.Mapping,
            row.WarehouseUserField,
            Enum.TryParse<GenericImportSalesPersonMode>(row.SalesPersonMode, out var salesPersonMode) ? salesPersonMode : GenericImportSalesPersonMode.BusinessPartner,
            row.SalesPersonCode,
            row.AnalyzeStock,
            Enum.TryParse<GenericImportBranchSource>(row.BranchSource, out var branchSource) ? branchSource : GenericImportBranchSource.Mapping),
    };
}
