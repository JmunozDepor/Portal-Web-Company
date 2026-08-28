using PortalSaas.Abstractions.Modelos;
using Xunit;

namespace PortalSaas.Core.Tests.Inventario;

public class InventoryDocumentServiceMapLineTests
{
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
