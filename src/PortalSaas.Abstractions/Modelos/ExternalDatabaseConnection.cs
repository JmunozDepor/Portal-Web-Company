namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Resultado de resolver la conexión a la base de datos EXTERNA propia de un plugin
/// (ver IExternalDatabaseConnectionService). Motor dual desde el día uno -- el plugin
/// decide con qué proveedor de EF Core registrar su DbContext (UseNpgsql/UseSqlServer)
/// según <see cref="EngineType"/>, nunca asume uno fijo.
/// </summary>
public sealed class ExternalDatabaseConnection
{
    /// <summary>"postgres" | "sqlserver" -- ver ExternalDatabaseEngineType.</summary>
    public required string EngineType { get; init; }

    public required string ConnectionString { get; init; }
}

public static class ExternalDatabaseEngineType
{
    public const string Postgres = "postgres";
    public const string SqlServer = "sqlserver";
}
