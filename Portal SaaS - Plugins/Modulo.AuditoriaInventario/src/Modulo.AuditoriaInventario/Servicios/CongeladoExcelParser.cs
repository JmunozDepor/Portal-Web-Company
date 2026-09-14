using System.Data;
using System.Globalization;
using ExcelDataReader;

namespace Modulo.AuditoriaInventario.Servicios;

public sealed record CongeladoFilaParseada(string Barcode, string? ProductCode, int Quantity, decimal? UnitCost);

public sealed record CongeladoFilaError(int NumeroFila, string Mensaje);

public sealed record CongeladoParseResult(IReadOnlyList<CongeladoFilaParseada> Filas, IReadOnlyList<CongeladoFilaError> Errores);

/// <summary>
/// Parser del Excel de congelado exportado desde el punto de venta. Mapeo por
/// POSICIÓN de columna (no por nombre de encabezado) -- mismo criterio que
/// GenericImportService de PortalSaas.Core, porque el archivo lo genera un sistema
/// externo con formato fijo: A = código de barra, B = cantidad, C = costo unitario
/// (opcional). Fila 1 = encabezado, se ignora. Una fila inválida se acumula como
/// error y NO aborta el resto del archivo (mismo criterio que ImportacionGenerica).
/// </summary>
public static class CongeladoExcelParser
{
    static CongeladoExcelParser()
    {
        // ExcelReaderConfiguration hace Encoding.GetEncoding(1252) incondicionalmente
        // (fallback de .xls legacy) -- sin este provider registrado, .NET Core/8 tira
        // NotSupportedException incluso al leer un .xlsx. No mencionado en el brief;
        // detectado al correr CongeladoExcelParserTests contra el paquete real.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public static CongeladoParseResult Parse(Stream excelStream)
    {
        using var reader = ExcelReaderFactory.CreateReader(excelStream);
        // Nombres completamente calificados para evitar depender de un using adicional.
        // Verificado por reflexión contra el paquete ExcelDataReader.DataSet 3.7.0
        // realmente restaurado: ExcelDataSetConfiguration/ExcelDataTableConfiguration
        // viven en el namespace ExcelDataReader (NO en un namespace anidado
        // ExcelDataReader.Configuration, a diferencia de versiones más nuevas del
        // paquete) -- si se sube la versión de ExcelDataReader.DataSet, revisar de
        // nuevo si el namespace cambió.
        var dataSet = reader.AsDataSet(new ExcelDataReader.ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataReader.ExcelDataTableConfiguration { UseHeaderRow = false },
        });
        var table = dataSet.Tables[0];

        var filas = new List<CongeladoFilaParseada>();
        var errores = new List<CongeladoFilaError>();

        string? Celda(DataRow fila, int indice) => indice < table.Columns.Count ? fila[indice]?.ToString()?.Trim() : null;

        for (var i = 1; i < table.Rows.Count; i++)
        {
            var fila = table.Rows[i];
            var numeroFila = i + 1; // 1-based, tal como lo ve el usuario en Excel.

            var barcode = Celda(fila, 0);
            var cantidadTexto = Celda(fila, 1);
            var costoTexto = Celda(fila, 2);

            var filaVacia = string.IsNullOrWhiteSpace(barcode) && string.IsNullOrWhiteSpace(cantidadTexto) && string.IsNullOrWhiteSpace(costoTexto);
            if (filaVacia)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(barcode))
            {
                errores.Add(new CongeladoFilaError(numeroFila, "Falta el código de barra (columna A)."));
                continue;
            }

            if (!int.TryParse(cantidadTexto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cantidad))
            {
                errores.Add(new CongeladoFilaError(numeroFila, $"Cantidad inválida en columna B: '{cantidadTexto}'."));
                continue;
            }

            decimal? costoUnitario = null;
            if (!string.IsNullOrWhiteSpace(costoTexto))
            {
                if (!decimal.TryParse(costoTexto, NumberStyles.Number, CultureInfo.InvariantCulture, out var costo))
                {
                    errores.Add(new CongeladoFilaError(numeroFila, $"Costo unitario inválido en columna C: '{costoTexto}'."));
                    continue;
                }
                costoUnitario = costo;
            }

            filas.Add(new CongeladoFilaParseada(barcode, null, cantidad, costoUnitario));
        }

        return new CongeladoParseResult(filas, errores);
    }
}
