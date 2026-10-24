using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace Modulo.Despachos.Dominio.Guias;

/// <summary>Líneas importadas y los errores por fila (con errores no se aplica nada).</summary>
public sealed record ImportedLinesResult(IReadOnlyList<ManualGuideLineInput> Lines, IReadOnlyList<string> Errors);

/// <summary>
/// Detalle de la guía manual en bloque: filas pegadas desde Excel (tabulador o ';') o planilla .xlsx. Columnas en orden:
/// Código, Descripción, Cantidad, Unidad, Precio unitario. La primera fila se omite si es encabezado (Cantidad no
/// numérica); las filas vacías se omiten; precio vacío = 0.
/// </summary>
public static partial class ManualGuideLineImporter
{
    public static readonly IReadOnlyList<string> Headers = new[] { "Código", "Descripción", "Cantidad", "Unidad", "Precio unitario" };

    public static ImportedLinesResult FromText(string text)
    {
        var rows = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(line => (IReadOnlyList<string>)line.Split(line.Contains('\t') ? '\t' : ';'))
            .ToList();
        return Parse(rows);
    }

    public static ImportedLinesResult FromXlsx(Stream xlsx)
    {
        try
        {
            using var wb = new XLWorkbook(xlsx);
            var ws = wb.Worksheet(1);
            var last = ws.LastRowUsed()?.RowNumber() ?? 0;
            var rows = new List<IReadOnlyList<string>>();
            for (var r = 1; r <= last; r++)
                rows.Add(Enumerable.Range(1, 5).Select(c => CellText(ws.Cell(r, c))).ToList());
            return Parse(rows);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new ImportedLinesResult(Array.Empty<ManualGuideLineInput>(), new[] { "No se pudo leer la planilla: use un archivo .xlsx." });
        }
    }

    public static byte[] TemplateXlsx()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Detalle");
        for (var i = 0; i < Headers.Count; i++) ws.Cell(1, i + 1).Value = Headers[i];
        ws.Row(1).Style.Font.Bold = true;
        ws.Columns(1, 5).AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string CellText(IXLCell cell) =>
        cell.DataType == XLDataType.Number
            ? cell.GetDouble().ToString(CultureInfo.InvariantCulture)
            : cell.GetString();

    private static ImportedLinesResult Parse(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var lines = new List<ManualGuideLineInput>();
        var errors = new List<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var cells = rows[i].Select(c => c.Trim()).ToList();
            if (cells.All(c => c.Length == 0)) continue;
            string At(int k) => k < cells.Count ? cells[k] : "";
            var n = i + 1;
            if (i == 0 && !TryNumber(At(2), out _)) continue; // encabezado

            var description = At(1);
            var ok = true;
            if (description.Length == 0) { errors.Add($"Fila {n}: falta la descripción."); ok = false; }
            if (!TryNumber(At(2), out var qty) || qty <= 0) { errors.Add($"Fila {n}: la cantidad debe ser un número mayor que cero."); ok = false; }
            var price = 0m;
            if (At(4).Length > 0 && (!TryNumber(At(4), out price) || price < 0))
            {
                errors.Add($"Fila {n}: el precio unitario debe ser un número mayor o igual a cero.");
                ok = false;
            }
            if (ok)
                lines.Add(new ManualGuideLineInput(At(0).Length == 0 ? null : At(0), description, qty,
                    At(3).Length == 0 ? null : At(3), price));
        }
        return new ImportedLinesResult(lines, errors);
    }

    /// <summary>
    /// Número escrito como en Chile o como en Excel: con coma = formato es-CL (punto de miles); solo con puntos, un punto
    /// seguido de exactamente 3 dígitos es separador de miles ("1.500"); si no, punto decimal ("2.5").
    /// </summary>
    private static bool TryNumber(string raw, out decimal value)
    {
        value = 0;
        var s = raw.Replace(" ", "").Replace("$", "");
        if (s.Length == 0) return false;
        if (s.Contains(','))
            return decimal.TryParse(s, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CL"), out value);
        if (ThousandsRegex().IsMatch(s)) s = s.Replace(".", "");
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    [GeneratedRegex(@"^-?\d{1,3}(\.\d{3})+$")]
    private static partial Regex ThousandsRegex();
}
