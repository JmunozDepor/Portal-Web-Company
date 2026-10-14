using System.Data;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica;

/// <summary>Agrupación de filas en documentos por una o varias columnas ("A,B").</summary>
public sealed class GenericImportGroupingTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("b", "B")]
    [InlineData(" a , b ", "A,B")]
    [InlineData("A;B;A", "A,B")]
    public void Normaliza_las_columnas_de_agrupacion(string? input, string? expected)
    {
        Assert.Equal(expected, GroupingColumns.Normalize(input));
    }

    private static GenericImportConfigDto Config(string? groupingColumn) =>
        new(1, Guid.NewGuid(), GenericImportModule.Sales, "SalesOrder", GenericImportLineType.Item, "C1", groupingColumn,
            GenericImportItemIdentifier.ItemCode, "Prueba", true,
            [new GenericImportConfigFieldDto(0, GenericImportLogicalField.ItemCode, "C", true, null, null)]);

    private static DataTable Table(params (string Order, string Branch, string Item)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("A");
        table.Columns.Add("B");
        table.Columns.Add("C");
        foreach (var (order, branch, item) in rows)
        {
            table.Rows.Add(order, branch, item);
        }
        return table;
    }

    [Fact]
    public void Dos_columnas_generan_un_grupo_por_combinacion()
    {
        var table = Table(("OC-1", "1202", "X"), ("OC-1", "1202", "Y"), ("OC-1", "0609", "Z"), ("OC-2", "1202", "W"));

        var keys = GenericImportService.ReadRawRows(table, Config("A,B")).Select(r => r.RawGroupingKey).ToList();

        Assert.Equal(["OC-1 · 1202", "OC-1 · 1202", "OC-1 · 0609", "OC-2 · 1202"], keys);
    }

    [Fact]
    public void Una_columna_sigue_funcionando_igual_que_antes()
    {
        var table = Table(("OC-1", "1202", "X"), ("OC-2", "1202", "Y"));

        var keys = GenericImportService.ReadRawRows(table, Config("A")).Select(r => r.RawGroupingKey).ToList();

        Assert.Equal(["OC-1", "OC-2"], keys);
    }
}
