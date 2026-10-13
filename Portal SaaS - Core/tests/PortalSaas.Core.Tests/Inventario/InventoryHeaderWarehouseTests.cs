using PortalSaas.Core.Inventario;
using Xunit;

namespace PortalSaas.Core.Tests.Inventario;

/// <summary>Almacén de cabecera (FromWarehouse/ToWarehouse) derivado de las líneas al crear un traslado/solicitud.</summary>
public sealed class InventoryHeaderWarehouseTests
{
    [Fact]
    public void Todas_las_lineas_iguales_devuelve_ese_almacen()
    {
        Assert.Equal("C_RETAIL", InventoryDocumentService.HeaderWarehouse(["C_RETAIL", "C_RETAIL", " C_RETAIL "]));
    }

    [Fact]
    public void Lineas_mezcladas_devuelve_el_mas_frecuente()
    {
        Assert.Equal("9999", InventoryDocumentService.HeaderWarehouse(["9999", "BO02", "9999"]));
    }

    [Fact]
    public void Sin_almacen_en_ninguna_linea_devuelve_null()
    {
        Assert.Null(InventoryDocumentService.HeaderWarehouse([null, "", "  "]));
    }
}
