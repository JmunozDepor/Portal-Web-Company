namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Abstrae la sesión de Service Layer de la compañía activa del usuario actual (ver
/// ICurrentCompanyAccessor). La implementación real (en PortalSaas.Core) resuelve el
/// usuario TÉCNICO/de integración de la compañía (nunca la contraseña del usuario del
/// portal) y cachea la sesión. Scoped por request. Portado de PortalSAP_v2
/// (ISapConnectionProvider), tal cual.
/// </summary>
public interface ISapConnectionProvider
{
    /// <summary>Devuelve una sesión SL ya autenticada para la compañía activa.</summary>
    Task<ISapSession> GetConnectionAsync(CancellationToken ct = default);
}

/// <summary>
/// Operaciones mínimas contra el Service Layer, agnósticas de la librería concreta que
/// las implementa (B1SLayer u otra), para que Abstractions no dependa de ella.
/// </summary>
public interface ISapSession
{
    Task<T?> GetAsync<T>(string recurso, string? filtroOData = null, string? expandOData = null, CancellationToken ct = default);

    /// <summary>
    /// Trae TODAS las filas de un recurso, siguiendo la paginación de Service Layer
    /// (odata.nextLink) internamente hasta agotarla -- Service Layer solo devuelve
    /// ~20 filas por default en un GetAsync&lt;List&lt;T&gt;&gt; simple, así que cualquier
    /// consulta que pueda traer más de una página (ej. PullAsync de un conector de
    /// integración) debe usar este método, nunca GetAsync&lt;List&lt;T&gt;&gt;.
    /// </summary>
    Task<IReadOnlyList<T>> GetAllAsync<T>(string recurso, string? filtroOData = null, string? expandOData = null, CancellationToken ct = default);

    Task<T?> PostAsync<T>(string recurso, object cuerpo, CancellationToken ct = default);

    /// <summary>
    /// clave se pasa tal cual (nunca .ToString()) -- la implementación decide si la
    /// envuelve en comillas simples según el tipo en tiempo de ejecución. Un DocEntry es
    /// numérico en Service Layer; pasarlo como string produce "Recurso('123')" en vez de
    /// "Recurso(123)" y SAP lo rechaza con "Bad Request - Error in query syntax".
    /// </summary>
    Task PatchAsync(string recurso, object clave, object cuerpo, CancellationToken ct = default);

    /// <summary>
    /// recurso incluye la clave ya formateada (ej. "AlternateCatNum(ItemCode='X',CardCode='Y')")
    /// -- a diferencia de PatchAsync, la clave de borrado puede ser compuesta, así que el
    /// llamador arma el string completo en vez de pasar una clave simple.
    /// </summary>
    Task DeleteAsync(string recurso, CancellationToken ct = default);
}
