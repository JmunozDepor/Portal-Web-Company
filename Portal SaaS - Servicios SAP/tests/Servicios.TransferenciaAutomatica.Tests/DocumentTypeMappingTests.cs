using Servicios.TransferenciaAutomatica.Sap;
using Xunit;

namespace Servicios.TransferenciaAutomatica.Tests;

public class DocumentTypeMappingTests
{
    [Theory]
    [InlineData("17", "rot_SalesOrder", "RDR1", "ORDR")]
    [InlineData("13", "rot_SalesInvoice", "INV1", "OINV")]
    [InlineData("1250000001", "rot_InventoryTransferRequest", "WTQ1", "OWTQ")]
    public void TryResolver_resuelve_los_3_ObjType_soportados(
        string objType, string refObjType, string tablaDetalle, string tablaCabecera)
    {
        var resuelto = DocumentTypeMapping.TryResolver(objType, out var tipoDocumento);

        Assert.True(resuelto);
        Assert.Equal(refObjType, tipoDocumento.ServiceLayerRefObjType);
        Assert.Equal(tablaDetalle, tipoDocumento.TablaDetalle);
        Assert.Equal(tablaCabecera, tipoDocumento.TablaCabecera);
    }

    [Theory]
    [InlineData("")]
    [InlineData("99")]
    [InlineData("1250000002")]
    public void TryResolver_ObjType_no_soportado_devuelve_false(string objType)
    {
        var resuelto = DocumentTypeMapping.TryResolver(objType, out var tipoDocumento);

        Assert.False(resuelto);
        Assert.Null(tipoDocumento);
    }
}
