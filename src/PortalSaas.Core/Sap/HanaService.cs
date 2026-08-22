using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Sap.Data.Hana;

namespace PortalSaas.Core.Sap;

/// <summary>
/// Scoped -- NUNCA Singleton. Debe resolver su cadena de conexión en cada request a
/// partir de la compañía activa (ICurrentCompanyAccessor). Un singleton congelaría la
/// conexión a la primera compañía resuelta, rompiendo a todas las demás (bug real que
/// tuvo PortalSAP_v2 con el servicio equivalente, ver su CLAUDE.md).
///
/// Todo call site sigue escribiendo SQL en dialecto HANA (ver el doc-comment de
/// IHanaService) -- si el motor de la instancia de la compañía activa es SqlServer, este
/// servicio traduce el SQL con HanaToSqlServerTranslator y ejecuta contra SqlConnection en
/// vez de HanaConnection, de forma transparente. Ningún otro servicio del Core necesita
/// saber en qué motor corre la compañía activa.
///
/// A diferencia de PortalSAP_v2 (que resolvía Company/Instance con SQL crudo contra
/// PORTALWEB.EMPRESA/INSTANCIA en HANA vía EmpresaRepositorio), acá esas dos tablas viven
/// en la base propia de la plataforma (Postgres/SQL Server, ver Entities/Company.cs,
/// Entities/Instance.cs) -- se resuelven con una consulta EF Core directa, sin repositorio
/// intermedio.
/// </summary>
public sealed class HanaService : IHanaService
{
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ISecretoCifradoService _secrets;
    private readonly PortalSaasDbContext _db;
    private (string EngineType, string ConnectionString)? _connection;

    public HanaService(ICurrentCompanyAccessor currentCompany, ISecretoCifradoService secrets, PortalSaasDbContext db)
    {
        _currentCompany = currentCompany;
        _secrets = secrets;
        _db = db;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sqlParametrizado, object? parametros = null, CancellationToken ct = default)
    {
        var connection = await ResolveConnectionAsync(ct);

        // Reflection de T (GetProperties + el diccionario por nombre de columna) se
        // calcula UNA vez por consulta, no una vez por fila -- ver el doc-comment de
        // RowReflectionMapper para el costo real que evita en catálogos grandes.
        var (underlyingType, isSimpleType, properties) = RowReflectionMapper.PrepareType(typeof(T));
        var result = new List<T>();

        if (connection.EngineType == SapEngineType.SqlServer)
        {
            var translatedSql = HanaToSqlServerTranslator.Translate(sqlParametrizado);
            using var conn = new SqlConnection(connection.ConnectionString);
            using var cmd = new SqlCommand(translatedSql, conn);
            AddParametersSqlServer(cmd, parametros);

            await conn.OpenAsync(ct);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(RowReflectionMapper.MapRow<T>(reader, underlyingType, isSimpleType, properties));
            }

            return result;
        }

        using var connHana = new HanaConnection(connection.ConnectionString);
        using var cmdHana = new HanaCommand(sqlParametrizado, connHana);
        AddParametersHana(cmdHana, parametros);

        await connHana.OpenAsync(ct);
        using var readerHana = (HanaDataReader)await cmdHana.ExecuteReaderAsync(ct);
        while (await readerHana.ReadAsync(ct))
        {
            result.Add(RowReflectionMapper.MapRow<T>(readerHana, underlyingType, isSimpleType, properties));
        }

        return result;
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryDynamicAsync(
        string sqlParametrizado, object? parametros = null, CancellationToken ct = default)
    {
        var connection = await ResolveConnectionAsync(ct);
        var result = new List<IReadOnlyDictionary<string, object?>>();

        if (connection.EngineType == SapEngineType.SqlServer)
        {
            var translatedSql = HanaToSqlServerTranslator.Translate(sqlParametrizado);
            using var conn = new SqlConnection(connection.ConnectionString);
            using var cmd = new SqlCommand(translatedSql, conn);
            AddParametersSqlServer(cmd, parametros);

            await conn.OpenAsync(ct);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(MapRowToDictionary(reader));
            }
            return result;
        }

        using var connHana = new HanaConnection(connection.ConnectionString);
        using var cmdHana = new HanaCommand(sqlParametrizado, connHana);
        AddParametersHana(cmdHana, parametros);

        await connHana.OpenAsync(ct);
        using var readerHana = (HanaDataReader)await cmdHana.ExecuteReaderAsync(ct);
        while (await readerHana.ReadAsync(ct))
        {
            result.Add(MapRowToDictionary(readerHana));
        }
        return result;
    }

    private static IReadOnlyDictionary<string, object?> MapRowToDictionary(System.Data.Common.DbDataReader reader)
    {
        var fila = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            fila[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }
        return fila;
    }

    public async Task<int> ExecuteAsync(string sqlParametrizado, object? parametros = null, CancellationToken ct = default)
    {
        var connection = await ResolveConnectionAsync(ct);

        if (connection.EngineType == SapEngineType.SqlServer)
        {
            var translatedSql = HanaToSqlServerTranslator.Translate(sqlParametrizado);
            using var conn = new SqlConnection(connection.ConnectionString);
            using var cmd = new SqlCommand(translatedSql, conn);
            AddParametersSqlServer(cmd, parametros);

            await conn.OpenAsync(ct);
            return await cmd.ExecuteNonQueryAsync(ct);
        }

        using var connHana = new HanaConnection(connection.ConnectionString);
        using var cmdHana = new HanaCommand(sqlParametrizado, connHana);
        AddParametersHana(cmdHana, parametros);

        await connHana.OpenAsync(ct);
        return await cmdHana.ExecuteNonQueryAsync(ct);
    }

    public async Task<string> GetPlatformEngineTypeAsync(CancellationToken ct = default)
    {
        var connection = await ResolveConnectionAsync(ct);
        return connection.EngineType;
    }

    private async Task<(string EngineType, string ConnectionString)> ResolveConnectionAsync(CancellationToken ct)
    {
        if (_connection is { } cached)
        {
            return cached;
        }

        var company = await _db.Companies
            .Include(c => c.Instance)
            .FirstOrDefaultAsync(c => c.Id == _currentCompany.CompanyId && c.IsActive, ct)
            ?? throw new InvalidOperationException($"Compañía '{_currentCompany.CompanyId}' no encontrada/activa.");

        _connection = SapConnectionStringFactory.Build(company, _secrets);
        return _connection.Value;
    }

    /// <summary>
    /// Acepta tanto un objeto anónimo de forma fija (uso típico) como un
    /// IReadOnlyDictionary&lt;string, object?&gt; cuando la cantidad de parámetros es
    /// dinámica -- por ejemplo un IN (...) armado con :codigo0, :codigo1, etc.
    /// </summary>
    private static void AddParametersHana(HanaCommand cmd, object? parametros)
    {
        if (parametros is null)
        {
            return;
        }

        if (parametros is IReadOnlyDictionary<string, object?> dictionary)
        {
            foreach (var (name, value) in dictionary)
            {
                cmd.Parameters.Add(new HanaParameter($":{name}", value ?? DBNull.Value));
            }
            return;
        }

        foreach (var property in parametros.GetType().GetProperties())
        {
            cmd.Parameters.Add(new HanaParameter($":{property.Name}", property.GetValue(parametros) ?? DBNull.Value));
        }
    }

    /// <summary>Mismo shape que AddParametersHana, prefijo "@" en vez de ":".</summary>
    private static void AddParametersSqlServer(SqlCommand cmd, object? parametros)
    {
        if (parametros is null)
        {
            return;
        }

        if (parametros is IReadOnlyDictionary<string, object?> dictionary)
        {
            foreach (var (name, value) in dictionary)
            {
                cmd.Parameters.AddWithValue($"@{name}", value ?? DBNull.Value);
            }
            return;
        }

        foreach (var property in parametros.GetType().GetProperties())
        {
            cmd.Parameters.AddWithValue($"@{property.Name}", property.GetValue(parametros) ?? DBNull.Value);
        }
    }
}
