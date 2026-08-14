using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Npgsql;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Wms.Services;

public sealed class ServiceConfigService : IServiceConfigService
{
    private readonly WmsDbContext _db;

    public ServiceConfigService(WmsDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<WmsServiceConfig>> ListAllAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.ServiceConfigs
            .Where(c => c.CompanyId == companyId)
            .OrderBy(c => c.ConfigKey)
            .ToListAsync(ct);

    public async Task<long> CreateAsync(Guid companyId, string configKey, string? configValue, bool isActive, string updatedBy, CancellationToken ct = default)
    {
        if (!WmsServiceConfigKeys.Labels.ContainsKey(configKey))
        {
            throw new InvalidOperationException("Parámetro no reconocido.");
        }

        var yaExiste = await _db.ServiceConfigs.AnyAsync(
            c => c.CompanyId == companyId && c.ConfigKey == configKey, ct);
        if (yaExiste)
        {
            throw new InvalidOperationException("Ya existe una configuración para ese parámetro.");
        }

        var config = new WmsServiceConfig
        {
            CompanyId = companyId,
            ConfigKey = configKey,
            ConfigValue = configValue,
            IsActive = isActive,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = updatedBy,
        };

        _db.ServiceConfigs.Add(config);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Ya existe una configuración para ese parámetro.");
        }

        return config.Id;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
    {
        PostgresException pg => pg.SqlState == "23505",
        SqlException sql => sql.Number is 2601 or 2627,
        _ => false,
    };

    public async Task UpdateAsync(long id, Guid companyId, string? configValue, bool isActive, string updatedBy, CancellationToken ct = default)
    {
        var config = await _db.ServiceConfigs.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("La configuración no existe o no pertenece a esta compañía.");

        if (configValue is not null && configValue.Length > 500)
        {
            throw new InvalidOperationException("El valor no puede superar los 500 caracteres.");
        }

        config.ConfigValue = configValue;
        config.IsActive = isActive;
        config.UpdatedAt = DateTimeOffset.UtcNow;
        config.UpdatedBy = updatedBy;

        await _db.SaveChangesAsync(ct);
    }
}
