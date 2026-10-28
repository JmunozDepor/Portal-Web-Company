using PortalSaas.Abstractions.Contratos;

namespace Modulo.FacturasProveedor.Tests.Fakes;

/// <summary>
/// SAP de prueba: <see cref="Results"/> mapea el SQL exacto a las filas que devuelve
/// QueryDynamicAsync; <see cref="LastParameters"/> guarda los parámetros de la última
/// consulta. Un SQL sin resultado configurado devuelve lista vacía.
/// </summary>
public sealed class FakeHanaService : IHanaService
{
    public Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> Results { get; } = new();
    public object? LastParameters { get; private set; }
    public List<string> ExecutedSql { get; } = new();

    /// <summary>Si se asigna, QueryDynamicAsync lanza esta excepción (simula un error de SAP).</summary>
    public Exception? ThrowOnQuery { get; set; }

    /// <summary>Motor que informa GetPlatformEngineTypeAsync ("hana" para SAP HANA).</summary>
    public string EngineType { get; set; } = "PostgreSQL";

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryDynamicAsync(
        string sqlParametrizado, object? parametros = null, CancellationToken ct = default)
    {
        ExecutedSql.Add(sqlParametrizado);
        LastParameters = parametros;
        if (ThrowOnQuery is not null) throw ThrowOnQuery;
        // El servicio ejecuta las consultas sin comentarios: los resultados se buscan ignorándolos.
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
            Results.TryGetValue(sqlParametrizado, out var r) ? r
            : Results.FirstOrDefault(x => WithoutComments(x.Key) == WithoutComments(sqlParametrizado)).Value
              ?? Array.Empty<IReadOnlyDictionary<string, object?>>();
        return Task.FromResult(rows);
    }

    private static string WithoutComments(string sql) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(sql, @"--[^\n]*|/\*.*?\*/", " ", System.Text.RegularExpressions.RegexOptions.Singleline),
            @"\s+", " ").Trim().TrimEnd(';').Trim();

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sqlParametrizado, object? parametros = null, CancellationToken ct = default)
    {
        ExecutedSql.Add(sqlParametrizado);
        LastParameters = parametros;
        return Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());
    }

    public Task<int> ExecuteAsync(string sqlParametrizado, object? parametros = null, CancellationToken ct = default)
    {
        ExecutedSql.Add(sqlParametrizado);
        LastParameters = parametros;
        return Task.FromResult(0);
    }

    public Task<string> GetPlatformEngineTypeAsync(CancellationToken ct = default) => Task.FromResult(EngineType);
}
