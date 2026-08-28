using Microsoft.EntityFrameworkCore;
using Npgsql;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Implementación real de IExternalDatabaseConnectionService -- resuelve el runtime
/// contra el binding PortalSaasDbContext.CompanyModuleConnections (slot lógico por
/// módulo + compañía) y el catálogo CompanyExternalConnections (base propia de la
/// plataforma, motor dual), nunca contra HANA/SAP. Sin caché por ahora (primer
/// consumidor real, Modulo.Rendiciones, YAGNI hasta que el volumen de resoluciones lo
/// justifique -- a diferencia del SqlServerService de PortalSAP_v2, que sí cacheaba 10
/// minutos).
/// </summary>
public sealed class ExternalDatabaseConnectionService : IExternalDatabaseConnectionService
{
    private readonly PortalSaasDbContext _db;
    private readonly ISecretoCifradoService _secretos;

    public ExternalDatabaseConnectionService(PortalSaasDbContext db, ISecretoCifradoService secretos)
    {
        _db = db;
        _secretos = secretos;
    }

    public Task<ExternalDatabaseConnection> ResolveConnectionAsync(
        string moduleCode,
        Guid companyId,
        CancellationToken ct = default)
        => ResolveConnectionAsync(moduleCode, companyId, "Default", ct);

    public async Task<ExternalDatabaseConnection> ResolveConnectionAsync(
        string moduleCode,
        Guid companyId,
        string purpose,
        CancellationToken ct = default)
    {
        var binding = await _db.CompanyModuleConnections
                .AsNoTracking()
                .Include(b => b.Connection)
                .FirstOrDefaultAsync(b => b.ModuleCode == moduleCode
                                          && b.CompanyId == companyId
                                          && b.Purpose == purpose
                                          && b.Connection.IsActive, ct)
            ?? throw new InvalidOperationException(
                $"No hay una base de datos externa asociada al módulo '{moduleCode}' " +
                $"(propósito '{purpose}') para esta compañía -- configurarla desde Administración.");

        var c = binding.Connection;
        var password = string.IsNullOrEmpty(c.TechnicalSecretKey) ? "" : _secretos.Decrypt(c.TechnicalSecretKey);

        (string engine, string connectionString) = c.Tipo switch
        {
            ExternalConnectionType.DbPostgres => (ExternalDatabaseEngineType.Postgres, new NpgsqlConnectionStringBuilder
            {
                Host = c.Host,
                Port = c.Port ?? 5432,
                Database = c.DatabaseName,
                Username = c.TechnicalUsername,
                Password = password,
            }.ConnectionString),
            ExternalConnectionType.DbSqlServer => (ExternalDatabaseEngineType.SqlServer, new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = $"{c.Host},{c.Port}",
                InitialCatalog = c.DatabaseName,
                UserID = c.TechnicalUsername,
                Password = password,
                // Mismo motivo que Sap/SapConnectionStringFactory: instalaciones con
                // certificado propio (self-signed/CA interna) rechazan el login sin
                // esto, Microsoft.Data.SqlClient exige Encrypt por default.
                TrustServerCertificate = true,
            }.ConnectionString),
            ExternalConnectionType.DbHana => (ExternalDatabaseEngineType.Hana,
                $"Server={c.Host}:{c.Port};UID={c.TechnicalUsername};PWD={password}"),
            _ => throw new InvalidOperationException(
                $"El módulo '{moduleCode}' requiere una conexión de base de datos, " +
                $"pero '{c.Nombre}' es de tipo '{c.Tipo}'."),
        };

        return new ExternalDatabaseConnection
        {
            EngineType = engine,
            ConnectionString = connectionString,
        };
    }

    public async Task<IReadOnlyList<ModuleCompanyDto>> ListActiveCompanyIdsAsync(
        string moduleCode,
        CancellationToken ct = default)
    {
        return await _db.CompanyModuleConnections
            .AsNoTracking()
            .Where(b => b.ModuleCode == moduleCode && b.Purpose == "Default" && b.Connection.IsActive)
            .Select(b => new ModuleCompanyDto(b.CompanyId, b.Company.OrganizationId))
            .Distinct()
            .ToListAsync(ct);
    }
}
