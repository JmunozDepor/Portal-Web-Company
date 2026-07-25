using System.Text.RegularExpressions;

namespace PortalSaas.Core.Sap;

/// <summary>
/// Traduce el SQL HANA que escribe HanaService (SQL en dialecto HANA, ver
/// IHanaService) a T-SQL, para poder ejecutarlo tal cual contra una compañía cuyo SAP B1
/// corre sobre SQL Server. Portado de PortalSAP_v2 (TraductorSqlHanaASqlServer) tal cual
/// -- NO es un traductor general de SQL, cubre exactamente 5 patrones: ":param"->"@param"
/// fuera de literales de texto, "LIMIT n OFFSET m" final -> "OFFSET m ROWS FETCH NEXT n
/// ROWS ONLY", "LIMIT n" sin offset -> "SELECT TOP (n)" insertado tras el único SELECT,
/// "TO_VARCHAR(x)" -> "CAST(x AS NVARCHAR(100))", "||" -> "+". Los identificadores entre
/// comillas dobles y UPPER/LIKE/CASE WHEN no se tocan -- son SQL estándar, válidos en
/// T-SQL sin cambios.
/// </summary>
public static class HanaToSqlServerTranslator
{
    private static readonly Regex ToVarchar = new(@"TO_VARCHAR\(([^()]+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LimitWithOffset =
        new(@"LIMIT\s+(\d+)\s+OFFSET\s+(\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LimitOnly = new(@"LIMIT\s+(\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FirstSelect =
        new(@"^(\s*SELECT\s+(?:DISTINCT\s+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Literal de texto ('...', con '' como comilla escapada) o parámetro con nombre --
    // capturados en una sola pasada para que el reemplazo de :param -> @param nunca toque
    // el contenido de un literal.
    private static readonly Regex LiteralOrParameter =
        new(@"'(?:[^']|'')*'|:([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    public static string Translate(string hanaSql)
    {
        var sql = hanaSql;
        sql = ToVarchar.Replace(sql, "CAST($1 AS NVARCHAR(100))");
        sql = sql.Replace("||", "+");
        sql = TranslatePagination(sql);
        sql = ReplaceParameters(sql);
        return sql;
    }

    /// <summary>
    /// "... ORDER BY ... LIMIT n OFFSET m" (siempre la última cláusula literal) -> "...
    /// ORDER BY ... OFFSET m ROWS FETCH NEXT n ROWS ONLY". "... ORDER BY ... LIMIT n" sin
    /// OFFSET -> se saca del final y se inserta como "SELECT TOP (n) ..." justo después
    /// del (único) SELECT de la query.
    /// </summary>
    private static string TranslatePagination(string sql)
    {
        var matchWithOffset = LimitWithOffset.Match(sql);
        if (matchWithOffset.Success)
        {
            var n = matchWithOffset.Groups[1].Value;
            var m = matchWithOffset.Groups[2].Value;
            return sql[..matchWithOffset.Index] + $"OFFSET {m} ROWS FETCH NEXT {n} ROWS ONLY";
        }

        var matchOnly = LimitOnly.Match(sql);
        if (!matchOnly.Success)
        {
            return sql;
        }

        var limit = matchOnly.Groups[1].Value;
        var withoutLimit = sql[..matchOnly.Index].TrimEnd();
        return FirstSelect.Replace(withoutLimit, $"$1TOP ({limit}) ", 1);
    }

    private static string ReplaceParameters(string sql) =>
        LiteralOrParameter.Replace(sql, m => m.Groups[1].Success ? "@" + m.Groups[1].Value : m.Value);
}
