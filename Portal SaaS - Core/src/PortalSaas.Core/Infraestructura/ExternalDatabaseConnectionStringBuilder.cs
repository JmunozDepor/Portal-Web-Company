using Microsoft.Data.SqlClient;
using Npgsql;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Arma la connection string de la base externa de un módulo a partir de los datos de la
/// Instance + la base + la credencial efectiva. Único lugar con este armado: lo usan el
/// resolver de runtime (ExternalDatabaseConnectionService) y el botón "Probar"
/// (CompanyExternalConnectionService.TestBindingAsync).
/// </summary>
internal static class ExternalDatabaseConnectionStringBuilder
{
    public static string Build(
        string engineType,
        string host,
        int port,
        string databaseName,
        string username,
        string password,
        int? timeoutSeconds = null)
        => engineType switch
        {
            InstanceEngineType.Postgres => new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = port,
                Database = databaseName,
                Username = username,
                Password = password,
                Timeout = timeoutSeconds ?? 15,
            }.ConnectionString,
            InstanceEngineType.SqlServer => new SqlConnectionStringBuilder
            {
                DataSource = $"{host},{port}",
                InitialCatalog = databaseName,
                UserID = username,
                Password = password,
                // Mismo motivo que Sap/SapConnectionStringFactory: instalaciones con
                // certificado propio (self-signed/CA interna) rechazan el login sin esto.
                TrustServerCertificate = true,
                ConnectTimeout = timeoutSeconds ?? 15,
            }.ConnectionString,
            InstanceEngineType.Hana =>
                $"Server={host}:{port};UserID={username};Password={password};Current Schema={databaseName}",
            _ => throw new InvalidOperationException($"Motor de base de datos no soportado: '{engineType}'."),
        };
}
