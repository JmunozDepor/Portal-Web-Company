using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Sap;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>
/// CRUD del catálogo maestro de campos de usuario, acotado a la COMPAÑÍA activa (no la
/// organización) -- regla dura del proyecto: un UDF es una particularidad física de la
/// base SAP de esa Company, ver GenericImportUserField. Portado de
/// CampoUsuarioImportacionGenericaService, pero contra PortalSaasDbContext (base propia
/// de la plataforma) en vez de HANA (ver CLAUDE.md, "Persistencia config" del 26 jul
/// 2026).
/// </summary>
public sealed class GenericImportUserFieldService : IGenericImportUserFieldService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public GenericImportUserFieldService(PortalSaasDbContext db, ICurrentCompanyAccessor currentCompany)
    {
        _db = db;
        _currentCompany = currentCompany;
    }

    public async Task<IReadOnlyList<GenericImportUserFieldDto>> ListAsync(GenericImportModule? module = null, CancellationToken ct = default)
    {
        var query = _db.GenericImportUserFields.Where(f => f.CompanyId == _currentCompany.CompanyId);
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
            .FirstOrDefaultAsync(f => f.Id == id && f.CompanyId == _currentCompany.CompanyId, ct);
        return row is null ? null : Map(row);
    }

    public async Task<int> CreateAsync(GenericImportModule module, GenericImportFieldLevel level, string label,
        string sapFieldName, GenericImportFieldDataType dataType, CancellationToken ct = default)
    {
        sapFieldName = ValidateSapFieldName(label, sapFieldName);

        var entity = new GenericImportUserField
        {
            CompanyId = _currentCompany.CompanyId,
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
        sapFieldName = ValidateSapFieldName(label, sapFieldName);

        var entity = await _db.GenericImportUserFields
            .FirstOrDefaultAsync(f => f.Id == id && f.CompanyId == _currentCompany.CompanyId, ct)
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
            .FirstOrDefaultAsync(f => f.Id == id && f.CompanyId == _currentCompany.CompanyId, ct);
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

    // Nombre de propiedad de Service Layer: letras/dígitos/guion bajo, sin empezar por
    // dígito (UDF "U_Xxx" o nativo "Comments"). Chequeo de FORMA, no de esquema -- la
    // validación contra el esquema real de SAP sigue descartada a propósito (ver
    // NativeFieldCatalog en el plugin). Bug real (2026-10-09): Etiqueta y Nombre SAP
    // cargados al revés ("U_NumAtCard" como etiqueta, "Usuario-Numero Orden " como
    // nombre SAP) -- se guardaba sin queja y SAP rechazaba recién al confirmar la
    // importación ("Property 'Usuario-Numero Orden ' of 'StockTransfer' is invalid").
    private static readonly System.Text.RegularExpressions.Regex SapFieldNamePattern =
        new("^[A-Za-z_][A-Za-z0-9_]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Recorta y valida el nombre SAP; devuelve el valor recortado o lanza InvalidOperationException con un mensaje para el usuario.</summary>
    internal static string ValidateSapFieldName(string label, string sapFieldName)
    {
        var trimmed = (sapFieldName ?? string.Empty).Trim();

        if (!SapFieldNamePattern.IsMatch(trimmed))
        {
            var labelTrimmed = (label ?? string.Empty).Trim();
            var hint = SapFieldNamePattern.IsMatch(labelTrimmed)
                ? $" ¿Quedaron invertidos? \"{labelTrimmed}\" parece el nombre SAP y \"{trimmed}\" la etiqueta."
                : string.Empty;
            throw new InvalidOperationException(
                $"\"{trimmed}\" no es un nombre de campo válido de Service Layer -- solo letras, números y guion bajo, sin espacios, guiones ni tildes (ej. U_NumAtCard, Comments).{hint}");
        }

        if (SapAdditionalFieldsHelper.IsReservedName(trimmed))
        {
            throw new InvalidOperationException($"\"{trimmed}\" es un nombre reservado por el motor -- un campo de usuario no puede pisarlo.");
        }

        return trimmed;
    }
}
