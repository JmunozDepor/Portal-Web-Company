using System.Text;
using System.Text.RegularExpressions;

namespace Modulo.FacturasProveedor.Servicios;

/// <summary>
/// Validación de las consultas SAP configurables (solo lectura): una sola sentencia SELECT/WITH, solo los parámetros
/// permitidos de su tipo y sin palabras de escritura. Copia de la lógica de Despachos (SapDocumentLookupService): los
/// plugins no comparten código.
/// </summary>
internal static class SapSqlGuard
{
    private static readonly Regex ParameterRegex = new(@":(\w+)", RegexOptions.Compiled);

    // Palabras de escritura/ejecución rechazadas sobre el SQL ya sin comentarios, literales ni
    // identificadores entre comillas. REPLACE solo se rechaza si no es la función REPLACE( ... ).
    private static readonly Regex ForbiddenRegex = new(
        @"\b(INSERT|UPDATE|DELETE|MERGE|INTO|EXEC|EXECUTE|DROP|ALTER|CREATE|TRUNCATE|GRANT|REVOKE|CALL|UPSERT|DECLARE|OPENROWSET|OPENQUERY|OPENDATASOURCE)\b|\bREPLACE\b(?!\s*\()|\bxp_\w*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Error legible si la consulta no es válida; null si está bien.</summary>
    public static string? Check(string sql, (string? Required, string[] Allowed) parameters)
    {
        if (string.IsNullOrWhiteSpace(sql)) return "La consulta está vacía.";
        var clean = Sanitize(sql).Trim();
        if (clean.EndsWith(';')) clean = clean[..^1].TrimEnd();
        if (clean.Length == 0) return "La consulta está vacía.";
        if (clean.Contains(';')) return "Solo se permite una sentencia (sin ';' intermedios).";

        var firstWord = Regex.Match(clean, @"^\w+").Value;
        if (!firstWord.Equals("SELECT", StringComparison.OrdinalIgnoreCase) &&
            !firstWord.Equals("WITH", StringComparison.OrdinalIgnoreCase))
            return "La consulta debe empezar con SELECT o WITH (solo lectura).";

        var names = ParameterRegex.Matches(clean).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var extra = names.Where(n => !parameters.Allowed.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        if (extra.Count > 0)
            return parameters.Allowed.Length == 0
                ? $"Parámetro no permitido: :{extra[0]}. Esta consulta no usa parámetros."
                : $"Parámetro no permitido: :{extra[0]}. Solo se admiten {string.Join(" y ", parameters.Allowed.Select(a => ":" + a))}.";
        if (parameters.Required is not null && !names.Contains(parameters.Required, StringComparer.OrdinalIgnoreCase))
            return $"La consulta debe filtrar por el parámetro :{parameters.Required}.";

        var forbidden = ForbiddenRegex.Match(clean);
        if (forbidden.Success) return $"La consulta contiene una palabra no permitida (solo lectura): {forbidden.Value}.";
        return null;
    }

    /// <summary>
    /// Devuelve el SQL con comentarios y contenido de literales/identificadores entre comillas
    /// reemplazados por espacios, conservando el largo (así los índices coinciden con el original).
    /// </summary>
    private static string Sanitize(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') { sb.Append(' '); i++; }
                if (i < sql.Length) sb.Append('\n');
            }
            else if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = end < 0 ? sql.Length : end + 2;
                for (; i < stop; i++) sb.Append(sql[i] == '\n' ? '\n' : ' ');
                i--;
            }
            else if (c == '\'' || c == '"' || c == '[')
            {
                var close = c == '[' ? ']' : c;
                sb.Append(c);
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == close)
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == close) { sb.Append("  "); i += 2; continue; }
                        break;
                    }
                    sb.Append(' ');
                    i++;
                }
                if (i < sql.Length) sb.Append(close);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>SQL a ejecutar: el original sin el ';' final (si lo hay).</summary>
    /// <remarks>
    /// Se ejecuta sin comentarios: el servicio HANA enlaza los parámetros por posición de cada ":nombre" en el
    /// texto, incluidos los de un comentario ("-- filtra por :warehouse"), lo que corría los valores (el filtro de
    /// fecha recibía el código de bodega).
    /// </remarks>
    public static string Executable(string sql)
    {
        var text = StripComments(sql).TrimEnd();
        var clean = Sanitize(text);
        return clean.EndsWith(';') ? text[..^1].TrimEnd() : text.Trim();
    }

    /// <summary>Quita los comentarios -- y /* */ conservando intacto el texto entre comillas simples o dobles.</summary>
    private static string StripComments(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                if (i < sql.Length) sb.Append('\n');
            }
            else if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? sql.Length : end + 1;
                sb.Append(' ');
            }
            else if (c is '\'' or '"')
            {
                sb.Append(c);
                for (i++; i < sql.Length; i++)
                {
                    sb.Append(sql[i]);
                    if (sql[i] != c) continue;
                    if (i + 1 < sql.Length && sql[i + 1] == c) { sb.Append(c); i++; continue; }
                    break;
                }
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
