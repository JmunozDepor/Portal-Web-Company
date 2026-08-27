namespace PortalSaas.Abstractions.Modelos;

/// <summary>Tipo de una conexión externa del catálogo por compañía.</summary>
public static class ExternalConnectionType
{
    public const string DbPostgres = "db_postgres";
    public const string DbSqlServer = "db_sqlserver";
    public const string DbHana = "db_hana";
    public const string HttpApi = "http_api";

    public static readonly IReadOnlyCollection<string> All = [DbPostgres, DbSqlServer, DbHana, HttpApi];

    public static bool IsDatabase(string tipo) => tipo is DbPostgres or DbSqlServer or DbHana;
}
