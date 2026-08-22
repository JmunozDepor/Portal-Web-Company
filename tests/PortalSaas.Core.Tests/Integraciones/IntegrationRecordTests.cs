using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace PortalSaas.Core.Tests.Integraciones;

public class IntegrationRecordTests
{
    [Fact]
    public void Fields_ExponeTodasLasClavesDelRegistro()
    {
        var registro = new IntegrationRecord(new Dictionary<string, object?> { ["A"] = 1, ["B"] = "x" });

        Assert.Equal(2, registro.Fields.Count);
        Assert.Equal(1, registro.Fields["A"]);
        Assert.Equal("x", registro.Fields["B"]);
    }

    [Fact]
    public void Indexador_DevuelveNullParaClaveInexistente()
    {
        var registro = new IntegrationRecord(new Dictionary<string, object?> { ["A"] = 1 });

        Assert.Null(registro["NoExiste"]);
        Assert.Equal(1, registro["A"]);
    }
}
