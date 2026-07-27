namespace PortalSaas.Core.Catalogos;

/// <summary>
/// Arma el WHERE de búsqueda (LIKE case-insensitive, OR entre 1+ columnas) y el LIMIT
/// opcionales que comparten TODOS los *CatalogService de este namespace -- portado del
/// mismo criterio de CatalogoSqlHelper en referencia-original/PortalSAP_v2. Un catálogo
/// SIN searchText sigue sirviendo su listado completo (dropdowns chicos: Almacén/
/// Vendedor/Moneda/etc., unos pocos a unos cientos de filas) -- el LIMIT solo se aplica
/// cuando el caller busca texto (buscador en vivo tipo Artículo/Cliente, catálogos que
/// pueden tener cientos de miles de filas), y ahí SIEMPRE se aplica un tope aunque el
/// caller no pase uno explícito -- nunca en silencio "buscar sin límite".
/// </summary>
internal static class CatalogSqlHelper
{
    /// <summary>Tope por defecto de un buscador en vivo sin límite explícito -- mismo valor ya usado por ItemCatalogService.SearchAsync.</summary>
    public const int DefaultSearchLimit = 30;

    /// <summary>
    /// searchColumns ya deben venir con comillas dobles (ej. "CardCode") -- este helper
    /// arma el SQL, no conoce el motor de destino. extraParameters -- parámetros de un
    /// :placeholder ya presente en fixedWhere (ej. ":dimCode" de CostCenterCatalogService),
    /// se devuelven mezclados en el diccionario final.
    /// </summary>
    public static (string WhereSql, string LimitSql, IReadOnlyDictionary<string, object?> Parameters) BuildSearchFilter(
        string? searchText, string[] searchColumns, int? limit = null, string fixedWhere = "1 = 1",
        IReadOnlyDictionary<string, object?>? extraParameters = null)
    {
        var parameters = extraParameters is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(extraParameters);
        var whereSql = fixedWhere;
        var trimmed = searchText?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return (whereSql, "", parameters);
        }

        var value = $"%{trimmed}%";
        var clauses = new List<string>();
        for (var i = 0; i < searchColumns.Length; i++)
        {
            // Un parámetro con nombre DISTINTO por columna (search0, search1, ...) --
            // mismo motivo que CatalogoSqlHelper del original: reusar el mismo nombre en
            // más de una posición del OR no bindea todas las apariciones en HANA.
            var paramName = $"search{i}";
            clauses.Add($"UPPER({searchColumns[i]}) LIKE UPPER(:{paramName})");
            parameters[paramName] = value;
        }

        whereSql += " AND (" + string.Join(" OR ", clauses) + ")";

        // Buscando texto SIEMPRE cap -- si el caller no pidió un límite explícito, se
        // aplica el default, nunca "sin límite" (serían cientos de miles de filas en
        // Artículo/Cliente). LIMIT sin bind (HANA no lo permite parametrizado) -- clamp
        // explícito, nunca un valor externo directo.
        var clampedLimit = Math.Clamp(limit ?? DefaultSearchLimit, 1, 100);
        return (whereSql, $"LIMIT {clampedLimit}", parameters);
    }
}
