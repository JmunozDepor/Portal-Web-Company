using ClosedXML.Excel;
using Modulo.Despachos.Dominio.Guias;

namespace Modulo.Despachos.Tests.Dominio.Guias;

/// <summary>Detalle de la guía manual pegado desde Excel o subido en planilla.</summary>
public class ManualGuideLineImporterTests
{
    [Fact]
    public void Texto_Tabulado_ConEncabezado()
    {
        var r = ManualGuideLineImporter.FromText("Código\tDescripción\tCantidad\tUnidad\tPrecio\nITEM-0\tCAJAS 550x500x345\t30\tUN\t608");
        Assert.Empty(r.Errors);
        Assert.Equal(new ManualGuideLineInput("ITEM-0", "CAJAS 550x500x345", 30, "UN", 608), Assert.Single(r.Lines));
    }

    [Fact]
    public void Texto_PuntoYComa_ComaDecimal()
    {
        var r = ManualGuideLineImporter.FromText(";Pallet;1,5;;1.500");
        Assert.Empty(r.Errors);
        var l = Assert.Single(r.Lines);
        Assert.Equal((null, 1.5m, null, 1500m), (l.ItemCode, l.Quantity, l.Unit, l.UnitPrice));
    }

    [Fact]
    public void Texto_PrecioVacio_Cero_YFilasVaciasSeOmiten()
    {
        var r = ManualGuideLineImporter.FromText("\tBolsas\t10\n\n\tCintas\t2.5\t\t\r\n");
        Assert.Empty(r.Errors);
        Assert.Equal(new[] { 0m, 0m }, r.Lines.Select(l => l.UnitPrice));
        Assert.Equal(2.5m, r.Lines[1].Quantity);
    }

    [Fact]
    public void Texto_SinDescripcion_ErrorConFila()
    {
        var r = ManualGuideLineImporter.FromText("A\tCaja\t1\nB\t\t2");
        Assert.Contains(r.Errors, e => e.StartsWith("Fila 2"));
    }

    [Fact]
    public void Texto_CantidadNegativa_Error() =>
        Assert.Contains(ManualGuideLineImporter.FromText("A\tCaja\t-3").Errors, e => e.Contains("Fila 1") && e.Contains("cantidad"));

    [Fact]
    public void Xlsx_DesdePlantilla()
    {
        using var wb = new XLWorkbook(new MemoryStream(ManualGuideLineImporter.TemplateXlsx()));
        var ws = wb.Worksheet(1);
        ws.Cell(2, 1).Value = "ITEM-0"; ws.Cell(2, 2).Value = "CAJAS"; ws.Cell(2, 3).Value = 30; ws.Cell(2, 5).Value = 608;
        ws.Cell(3, 2).Value = "Mueble"; ws.Cell(3, 3).Value = 1; ws.Cell(3, 5).Value = 500000;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var r = ManualGuideLineImporter.FromXlsx(ms);
        Assert.Empty(r.Errors);
        Assert.Equal(new[] { "CAJAS", "Mueble" }, r.Lines.Select(l => l.Description));
        Assert.Equal(500000m, r.Lines[1].UnitPrice);
    }

    [Fact]
    public void Plantilla_TieneEncabezados()
    {
        using var wb = new XLWorkbook(new MemoryStream(ManualGuideLineImporter.TemplateXlsx()));
        var ws = wb.Worksheet(1);
        Assert.Equal("Código", ws.Cell(1, 1).GetString());
        Assert.Equal("Precio unitario", ws.Cell(1, 5).GetString());
    }
}
