using Microsoft.EntityFrameworkCore;
using Npgsql;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Implementación real de IExternalDatabaseConnectionService -- resuelve contra
/// PortalSaasDbContext.ModuleExternalConnections (base propia de la plataforma, motor
/// dual), nunca contra HANA/SAP. Sin caché por ahora (primer consumidor real,
/// Modulo.Rendiciones, YAGNI hasta que el volumen de resoluciones lo justifique --
/// a diferencia del SqlServerService de PortalSAP_v2, que sí cacheaba 10 minutos).
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

    public async Task<ExternalDatabaseConnection> ResolveConnectionAsync(
        string moduleCode,
        Guid organizationId,
        Guid? companyId,
        CancellationToken ct = default)
    {
        var connection = (companyId is not null
                ? await BuscarAsync(moduleCode, organizationId, companyId, ct)
                : null)
            ?? await BuscarAsync(moduleCode, organizationId, null, ct)
            ?? throw new InvalidOperationException(
                $"No hay una base de datos externa asociada al módulo '{moduleCode}' para esta organización -- " +
                "configurarla desde Administración.");

        var password = _secretos.Decrypt(connection.TechnicalSecretKey);

        var connectionString = connection.EngineType switch
        {
            ModuleExternalConnectionEngineType.Postgres => new NpgsqlConnectionStringBuilder
            {
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.DatabaseName,
                Username = connection.TechnicalUsername,
                Password = password,
            }.ConnectionString,
            ModuleExternalConnectionEngineType.SqlServer => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = $"{connection.Host},{connection.Port}",
                InitialCatalog = connection.DatabaseName,
                UserID = connection.TechnicalUsername,
                Password = password,
                // Mismo motivo que Sap/SapConnectionStringFactory: instalaciones con
                // certificado propio (self-signed/CA interna) rechazan el login sin
                // esto, Microsoft.Data.SqlClient exige Encrypt por default.
                TrustServerCertificate = true,
            }.ConnectionString,
            _ => throw new InvalidOperationException($"Motor de base de datos externa no soportado: '{connection.EngineType}'."),
        };

        return new ExternalDatabaseConnection
        {
            EngineType = connection.EngineType,
            ConnectionString = connectionString,
        };
    }

    private Task<ModuleExternalConnection?> BuscarAsync(string moduleCode, Guid organizationId, Guid? companyId, CancellationToken ct)
    {
        return _db.ModuleExternalConnections
            .AsNoTracking()
            .Where(x => x.ModuleCode == moduleCode && x.OrganizationId == organizationId && x.IsActive)
            .Where(x => x.CompanyId == companyId)
            .FirstOrDefaultAsync(ct);
    }
}
