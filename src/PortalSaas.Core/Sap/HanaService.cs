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
        AddParametersHana(cmdHana, sqlParametrizado, parametros);

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
        AddParametersHana(cmdHana, sqlParametrizado, parametros);

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
            fila[reader.GetName(i)] = reader.IsDBNull(i) ? null : NormalizarValorHana(reader.GetValue(i));
        }
        return fila;
    }

    /// <summary>
    /// Sap.Data.Hana.HanaCommand devuelve columnas DECIMAL como Sap.Data.Hana.HanaDecimal
    /// (struct propio del driver, no System.Decimal) -- System.Text.Json.JsonSerializer no
    /// sabe serializarlo (no tiene propiedades públicas), produce "{}" en vez del valor
    /// numérico. Confirmado 22 ago 2026: una fila real con extra_fields guardó
    /// "unit_length": {} en vez de "1.000000" hasta este fix. Se convierte acá, en el único
    /// punto de entrada de filas dinámicas, para que QueryDynamicAsync nunca devuelva un
    /// tipo específico del driver que un consumidor genérico (ej. JsonSerializer en
    /// WmsSapStageItemWriter) no pueda serializar.
    /// </summary>
    private static object NormalizarValorHana(object valor) =>
        valor is HanaDecimal hanaDecimal ? hanaDecimal.ToDecimal() : valor;

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
        AddParametersHana(cmdHana, sqlParametrizado, parametros);

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
    /// <summary>
    /// A diferencia de SqlClient/Npgsql, Sap.Data.Hana NO reutiliza un parámetro con
    /// nombre repetido -- exige un HanaParameter distinto por cada OCURRENCIA del
    /// placeholder en el texto SQL, no uno por nombre único (confirmado 22 ago 2026
    /// contra HANA real: "WHERE :cursor IS NULL OR x >= :cursor" con un solo
    /// HanaParameter agregado tira "Parameter/Column (2) not bound."). Por eso este
    /// método necesita el SQL, a diferencia de AddParametersSqlServer.
    /// </summary>
    private static void AddParametersHana(HanaCommand cmd, string sql, object? parametros)
    {
        if (parametros is null)
        {
            return;
        }

        Dictionary<string, object?> valoresPorNombre;
        if (parametros is IReadOnlyDictionary<string, object?> dictionary)
        {
            valoresPorNombre = new Dictionary<string, object?>(dictionary, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            valoresPorNombre = parametros.GetType().GetProperties()
                .ToDictionary(p => p.Name, p => p.GetValue(parametros), StringComparer.OrdinalIgnoreCase);
        }

        // Se recorre el SQL de izquierda a derecha agregando un HanaParameter por cada
        // ocurrencia de ":nombre" EN EL ORDEN TEXTUAL en que aparece -- necesario porque
        // Sap.Data.Hana bindea posicionalmente por orden de aparición, no por nombre
        // único (ver doc-comment de arriba). Agrupar por clave del diccionario en vez de
        // por posición real en el texto rompería el binding si el SQL intercala más de
        // un parámetro distinto (ej. ":a ... :b ... :a").
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(sql, @":(\w+)"))
        {
            var nombre = match.Groups[1].Value;
            var valor = valoresPorNombre.TryGetValue(nombre, out var v) ? v : null;
            cmd.Parameters.Add(new HanaParameter($":{nombre}", valor ?? DBNull.Value));
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
