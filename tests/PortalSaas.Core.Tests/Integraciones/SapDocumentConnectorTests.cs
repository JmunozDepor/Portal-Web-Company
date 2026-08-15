using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Integrations.Connectors;
using Xunit;

namespace PortalSaas.Core.Tests.Integraciones;

public class SapDocumentConnectorTests
{
    [Fact]
    public void Tipo_EsSap()
    {
        var conector = new SapDocumentConnector();

        Assert.Equal("Sap", conector.Tipo);
    }

    [Fact]
    public async Task PushAsync_SinImplementacionReal_LanzaNotSupportedException()
    {
        var conector = new SapDocumentConnector();
        var registros = new List<IntegrationRecord> { new(new Dictionary<string, object?>()) };

        await Assert.ThrowsAsync<NotSupportedException>(
            () => conector.PushAsync("{}", registros, CancellationToken.None));
    }
}
