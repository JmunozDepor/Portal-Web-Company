namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Wrapper de consultas SQL directas a la base SAP B1 de la compañía activa (ver
/// ICurrentCompanyAccessor), agnóstico del cliente ADO.NET concreto -- usar siempre SQL
/// parametrizado, nunca concatenado. El SQL se escribe SIEMPRE en dialecto HANA (comillas
/// dobles, :param, LIMIT/OFFSET, TO_VARCHAR, etc.) -- si la compañía activa corre sobre
/// SQL Server, la implementación lo traduce internamente (ver HanaService/
/// HanaToSqlServerTranslator). Los call sites nunca necesitan saber en qué motor corre la
/// compañía activa. Portado de PortalSAP_v2 (IHanaService), tal cual.
/// </summary>
public interface IHanaService
{
    Task<IReadOnlyList<T>> QueryAsync<T>(string sqlParametrizado, object? parametros = null, CancellationToken ct = default);

    /// <summary>
    /// Igual que QueryAsync&lt;T&gt; pero sin DTO fijo -- cada fila se devuelve como
    /// diccionario columna→valor, para conectores que no conocen de antemano el set de
    /// columnas (ver SqlDirectConnector, Ronda de ingesta SQL directa a staging).
    /// </summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryDynamicAsync(
        string sqlParametrizado, object? parametros = null, CancellationToken ct = default);

    Task<int> ExecuteAsync(string sqlParametrizado, object? parametros = null, CancellationToken ct = default);

    /// <summary>
    /// Motor de base de datos de la compañía activa ("hana" | "sqlserver", ver
    /// SapEngineType) -- lo necesita código que no puede confiar en la traducción
    /// automática de QueryAsync/ExecuteAsync (SQL arbitrario fuera del set cerrado de
    /// patrones que el traductor soporta).
    /// </summary>
    Task<string> GetPlatformEngineTypeAsync(CancellationToken ct = default);
}
