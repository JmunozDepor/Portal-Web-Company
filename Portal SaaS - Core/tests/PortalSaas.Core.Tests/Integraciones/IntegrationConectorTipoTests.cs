using PortalSaas.Data.Entities.Integraciones;
using Xunit;

namespace PortalSaas.Core.Tests.Integraciones;

public class IntegrationConectorTipoTests
{
    // IntegrationSyncHostedService resuelve el IIntegrationConnector real comparando
    // conectores.FirstOrDefault(c => c.Tipo == definicion.ConectorTipo.ToString()) --
    // es decir, contra el NOMBRE del miembro del enum en C# (no el string persistido
    // en BD, que pasa por ConectorTipoAProveedor/DesdeProveedor aparte). El conector
    // real WmsCloudConnector (repo Modulo.Wms) expone Tipo => "WmsCloud" -- este test
    // confirma que el nombre del miembro del enum coincide EXACTO con ese string, sin
    // depender del plugin externo (que no es referenciable desde este repo).
    [Fact]
    public void WmsCloud_ToString_CoincideConElTipoQueExponeWmsCloudConnector()
    {
        Assert.Equal("WmsCloud", IntegrationConectorTipo.WmsCloud.ToString());
    }
}
