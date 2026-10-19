using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using Modulo.AuditoriaInventario.Servicios;

namespace Modulo.AuditoriaInventario.Tests;

public class CongeladoExcelParserTests
{
    private static MemoryStream CrearWorkbook(IEnumerable<(string? Barcode, string? Cantidad, string? Costo)> filas) =>
        CrearWorkbook(filas.Select(f => (f.Barcode, f.Cantidad, f.Costo, (string?)null)));

    private static MemoryStream CrearWorkbook(IEnumerable<(string? Barcode, string? Cantidad, string? Costo, string? ProductCode)> filas)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Congelado");
        ws.Cell(1, 1).Value = "Codigo de Barra";
        ws.Cell(1, 2).Value = "Cantidad";
        ws.Cell(1, 3).Value = "Costo Unitario";
        ws.Cell(1, 4).Value = "Codigo de Producto";

        var fila = 2;
        foreach (var (barcode, cantidad, costo, productCode) in filas)
        {
            ws.Cell(fila, 1).Value = barcode ?? string.Empty;
            ws.Cell(fila, 2).Value = cantidad ?? string.Empty;
            ws.Cell(fila, 3).Value = costo ?? string.Empty;
            ws.Cell(fila, 4).Value = productCode ?? string.Empty;
            fila++;
        }

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Parse_FilaValida_DevuelveUnaFilaSinErrores()
    {
        using var stream = CrearWorkbook(new[] { ("7801234567890", "10", "1500.50") });

        var resultado = CongeladoExcelParser.Parse(stream);

        Assert.Empty(resultado.Errores);
        Assert.Single(resultado.Filas);
        Assert.Equal("7801234567890", resultado.Filas[0].Barcode);
        Assert.Equal(10, resultado.Filas[0].Quantity);
        Assert.Equal(1500.50m, resultado.Filas[0].UnitCost);
    }

    [Fact]
    public void Parse_CeldaDeCostoNumericaBajoCulturaEsCl_NoCorrompeElValor()
    {
        // Reproduce el bug: ExcelDataReader devuelve la celda numérica como double
        // (no string) -- fila[indice].ToString() sin proveedor de formato usa
        // CultureInfo.CurrentCulture. Bajo es-CL (coma decimal, punto de millar),
        // 1500.5 se renderizaría como "1500,5", que decimal.TryParse con
        // NumberStyles.Number e InvariantCulture leería como 15005 (10x).
        var culturaOriginal = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-CL");

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Congelado");
            ws.Cell(1, 1).Value = "Codigo de Barra";
            ws.Cell(1, 2).Value = "Cantidad";
            ws.Cell(1, 3).Value = "Costo Unitario";
            ws.Cell(2, 1).Value = "7801234567890";
            ws.Cell(2, 2).Value = 10;
            ws.Cell(2, 3).Value = 1500.5; // double real, no string -- clave del repro.

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            ms.Position = 0;

            var resultado = CongeladoExcelParser.Parse(ms);

            Assert.Empty(resultado.Errores);
            Assert.Single(resultado.Filas);
            Assert.Equal(1500.5m, resultado.Filas[0].UnitCost);
        }
        finally
        {
            CultureInfo.CurrentCulture = culturaOriginal;
        }
    }

    [Fact]
    public void Parse_CantidadNoNumerica_AcumulaErrorYSigueConElRestoDeLasFilas()
    {
        using var stream = CrearWorkbook(new[]
        {
            ("7801234567890", "no-es-numero", (string?)null),
            ("7809999999999", "5", (string?)null),
        });

        var resultado = CongeladoExcelParser.Parse(stream);

        Assert.Single(resultado.Errores);
        Assert.Contains("Cantidad inválida", resultado.Errores[0].Mensaje);
        Assert.Single(resultado.Filas);
        Assert.Equal("7809999999999", resultado.Filas[0].Barcode);
    }

    [Fact]
    public void Parse_FilaSinBarcodeNiProductCode_AcumulaError()
    {
        using var stream = CrearWorkbook(new[] { ((string?)null, "10", (string?)null) });

        var resultado = CongeladoExcelParser.Parse(stream);

        Assert.Single(resultado.Errores);
        Assert.Empty(resultado.Filas);
    }

    [Fact]
    public void Parse_FilaSoloConCodigoDeProducto_DevuelveFilaSinErrores()
    {
        // Algunos orígenes externos solo traen el código de producto, no el
        // barcode -- Pages/AvanceConteo lo resuelve contra el maestro antes de
        // guardar (el parser no tiene acceso a la base).
        using var stream = CrearWorkbook(new[]
        {
            ((string?)null, "10", (string?)null, (string?)"372860C-102-2X"),
        });

        var resultado = CongeladoExcelParser.Parse(stream);

        Assert.Empty(resultado.Errores);
        Assert.Single(resultado.Filas);
        Assert.Null(resultado.Filas[0].Barcode);
        Assert.Equal("372860C-102-2X", resultado.Filas[0].ProductCode);
        Assert.Equal(10, resultado.Filas[0].Quantity);
    }

    [Fact]
    public void Parse_FilaConBarcodeYProductCode_ConservaAmbos()
    {
        using var stream = CrearWorkbook(new[]
        {
            ("7801234567890", "10", (string?)null, (string?)"372860C-102-2X"),
        });

        var resultado = CongeladoExcelParser.Parse(stream);

        Assert.Empty(resultado.Errores);
        Assert.Equal("7801234567890", resultado.Filas[0].Barcode);
        Assert.Equal("372860C-102-2X", resultado.Filas[0].ProductCode);
    }

    [Fact]
    public void Parse_FilaCompletamenteVacia_SeIgnoraSinError()
    {
        using var stream = CrearWorkbook(new[]
        {
            ("7801234567890", "10", (string?)null),
            ((string?)null, (string?)null, (string?)null),
        });

        var resultado = CongeladoExcelParser.Parse(stream);

        Assert.Empty(resultado.Errores);
        Assert.Single(resultado.Filas);
    }
}
