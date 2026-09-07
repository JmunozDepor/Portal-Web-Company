using System.Text.Json;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Inventario;
using Xunit;

namespace PortalSaas.Core.Tests.Inventario;

public class InventoryDocumentServiceMapLineTests
{
    [Theory]
    [InlineData("\"1250000001\"", 1250000001)] // Service Layer devuelve BaseType como string en un Traslado por Copy-From
    [InlineData("1250000001", 1250000001)]
    [InlineData("null", null)]
    [InlineData("\"\"", null)]
    [InlineData("\"InventoryTransferRequest\"", null)]
    public void SapInventoryDocumentLine_DeserializaBaseType_NumeroOString(string baseTypeJson, int? esperado)
    {
        var json = $$"""
        { "ItemCode": "ITEM-001", "Quantity": 5, "BaseType": {{baseTypeJson}}, "BaseEntry": "42", "BaseLine": 0 }
        """;

        var line = JsonSerializer.Deserialize<SapInventoryDocumentLine>(json);

        Assert.NotNull(line);
        Assert.Equal(esperado, line!.BaseType);
        Assert.Equal(42, line.BaseEntry);
        Assert.Equal(0, line.BaseLine);
    }

    [Fact]
    public void InventoryDocumentLineDto_AceptaCamposDeCopyFrom()
    {
        var linea = new InventoryDocumentLineDto(
            ItemCode: "ITEM-001",
            Description: null,
            Quantity: 10,
            FromWarehouseCode: null,
            ToWarehouseCode: null,
            BaseType: 1250000001,
            BaseEntry: 42,
            BaseLine: 0);

        Assert.Equal(1250000001, linea.BaseType);
        Assert.Equal(42, linea.BaseEntry);
        Assert.Equal(0, linea.BaseLine);
        Assert.Null(linea.FromWarehouseCode);
        Assert.Null(linea.ToWarehouseCode);
    }
}
