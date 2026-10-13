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
            InstanceEngineType.Hana => BuildHana(host, port, databaseName, username, password),
            _ => throw new InvalidOperationException($"Motor de base de datos no soportado: '{engineType}'."),
        };

    /// <summary>
    /// La connection string de HANA es texto plano (sin builder tipado): un ';' o '=' en un
    /// valor permitiría inyectar parámetros (p. ej. otro Server). Se rechaza en vez de escapar
    /// porque las reglas de comillado del cliente HANA no están garantizadas.
    /// </summary>
    private static string BuildHana(string host, int port, string databaseName, string username, string password)
    {
        RechazarSeparadores("host", host, "=;");
        RechazarSeparadores("base de datos", databaseName, "=;");
        RechazarSeparadores("usuario", username, "=;");
        RechazarSeparadores("clave", password, ";");

        var cs = $"Server={host}:{port};UserID={username};Password={password}";
        return string.IsNullOrEmpty(databaseName) ? cs : $"{cs};Current Schema={databaseName}";
    }

    private static void RechazarSeparadores(string campo, string? valor, string prohibidos)
    {
        if (valor is not null && valor.IndexOfAny(prohibidos.ToCharArray()) >= 0)
        {
            throw new InvalidOperationException(
                $"El valor de {campo} para HANA contiene caracteres no permitidos ({string.Join(" ", prohibidos.ToCharArray())}).");
        }
    }
}
