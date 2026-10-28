using PortalSaas.Core.DocumentosElectronicos;
using Xunit;

namespace PortalSaas.Core.Tests.DocumentosElectronicos;

public class DteReceivedXmlReaderTests
{
    [Fact]
    public async Task ReadLines_EjemploSii()
    {
        var xml = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "DocumentosElectronicos", "Fixtures", "factura33-recibida.xml"));

        var lines = DteReceivedXmlReader.ReadLines(xml);

        Assert.Equal(2, lines.Count);
        Assert.Equal(1, lines[0].LineNumber);
        Assert.Equal("POL-001", lines[0].ItemCode);
        Assert.Equal("Polera algodón niño", lines[0].Name);
        Assert.Equal(100m, lines[0].Quantity);
        Assert.Equal(12500.50m, lines[0].UnitPrice);
        Assert.Equal(1250050, lines[0].Amount);
        Assert.Null(lines[1].ItemCode);
        Assert.Null(lines[1].UnitPrice);
        Assert.Equal(749950, lines[1].Amount);
    }

    [Fact]
    public void ReadLines_XmlInvalido_Vacio() => Assert.Empty(DteReceivedXmlReader.ReadLines("<no-es-xml"));
}
