namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>
/// Columna(s) de agrupación de un formato -- una o varias letras de columna Excel
/// separadas por coma (ej. "A,B": un documento por cada combinación distinta de N° orden
/// + Sucursal). Guardado normalizado en mayúsculas sin espacios ("A,B").
/// </summary>
internal static class GroupingColumns
{
    /// <summary>Separador entre los valores de cada columna en la clave del grupo (lo que se ve como "Grupo" en la vista previa).</summary>
    internal const string KeySeparator = " · ";

    internal static IReadOnlyList<string> Parse(string? groupingColumn) =>
        string.IsNullOrWhiteSpace(groupingColumn)
            ? []
            : groupingColumn.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.ToUpperInvariant())
                .Distinct()
                .ToList();

    internal static string? Normalize(string? groupingColumn)
    {
        var columns = Parse(groupingColumn);
        return columns.Count == 0 ? null : string.Join(",", columns);
    }
}
