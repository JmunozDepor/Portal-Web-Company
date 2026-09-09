using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Npgsql;

namespace Modulo.Wms.Services;

public sealed class ValidationFieldService : IValidationFieldService
{
    private readonly WmsDbContext _db;

    public ValidationFieldService(WmsDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<WmsValidationField>> ListAllAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.ValidationFields
            .Where(v => v.CompanyId == companyId)
            .OrderBy(v => v.TipoEntidad)
            .ThenBy(v => v.FieldName)
            .ToListAsync(ct);

    public async Task<long> CreateAsync(Guid companyId, string tipoEntidad, string fieldName, bool isActive, CancellationToken ct = default)
    {
        tipoEntidad = (tipoEntidad ?? string.Empty).Trim();
        fieldName = (fieldName ?? string.Empty).Trim();

        if (!IValidationFieldService.TiposEntidad.Contains(tipoEntidad, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Tipo de entidad no soportado.");
        }
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new InvalidOperationException("El nombre del campo es requerido.");
        }
        if (fieldName.Length > 100)
        {
            throw new InvalidOperationException("El nombre del campo no puede superar los 100 caracteres.");
        }

        // El comparador del Writer (extra_fields JSON, case-sensitive) exige que el
        // nombre calce EXACTO con el alias de la Query de Bajada -- que por convención
        // es minúscula (name, city, zip...). Se normaliza acá para que no dependa de
        // cómo lo tipeó el usuario.
        fieldName = fieldName.ToLowerInvariant();

        var yaExiste = await _db.ValidationFields.AnyAsync(
            v => v.CompanyId == companyId && v.TipoEntidad == tipoEntidad && v.FieldName == fieldName, ct);
        if (yaExiste)
        {
            throw new InvalidOperationException("Ya existe ese campo de validación para ese tipo de entidad.");
        }

        var fila = new WmsValidationField
        {
            CompanyId = companyId,
            TipoEntidad = tipoEntidad,
            FieldName = fieldName,
            IsActive = isActive,
        };
        _db.ValidationFields.Add(fila);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Ya existe ese campo de validación para ese tipo de entidad.");
        }

        return fila.Id;
    }

    public async Task SetActiveAsync(long id, Guid companyId, bool isActive, CancellationToken ct = default)
    {
        var fila = await _db.ValidationFields.FirstOrDefaultAsync(v => v.Id == id && v.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("El campo de validación no existe o no pertenece a esta compañía.");

        fila.IsActive = isActive;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default)
    {
        var fila = await _db.ValidationFields.FirstOrDefaultAsync(v => v.Id == id && v.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("El campo de validación no existe o no pertenece a esta compañía.");

        _db.ValidationFields.Remove(fila);
        await _db.SaveChangesAsync(ct);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
    {
        PostgresException pg => pg.SqlState == "23505",
        SqlException sql => sql.Number is 2601 or 2627,
        _ => false,
    };
}
