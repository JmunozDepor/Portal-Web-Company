using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsFieldTemplateResolverTests
{
    [Fact]
    public void Resolve_SustituyeUnPlaceholder()
    {
        var registro = new IntegrationRecord(new Dictionary<string, object?> { ["ItemCode"] = "ITM001" });

        var resultado = WmsFieldTemplateResolver.Resolve("{ItemCode}", registro);

        Assert.Equal("ITM001", resultado);
    }

    [Fact]
    public void Resolve_ConTextoLiteralAlrededor_LoConserva()
    {
        var registro = new IntegrationRecord(new Dictionary<string, object?> { ["ItemCode"] = "ITM001" });

        var resultado = WmsFieldTemplateResolver.Resolve("PREFIX-{ItemCode}-SUFFIX", registro);

        Assert.Equal("PREFIX-ITM001-SUFFIX", resultado);
    }

    [Fact]
    public void Resolve_SinPlaceholders_EsUnLiteralPuro()
    {
        var registro = new IntegrationRecord(new Dictionary<string, object?>());

        var resultado = WmsFieldTemplateResolver.Resolve("DEPOR", registro);

        Assert.Equal("DEPOR", resultado);
    }

    [Fact]
    public void Resolve_ConcatenaDosCampos()
    {
        var registro = new IntegrationRecord(new Dictionary<string, object?> { ["A"] = "uno", ["B"] = "dos" });

        var resultado = WmsFieldTemplateResolver.Resolve("{A} ; {B}", registro);

        Assert.Equal("uno ; dos", resultado);
    }

    [Fact]
    public void Resolve_CampoAusente_SustituyePorVacio()
    {
        var registro = new IntegrationRecord(new Dictionary<string, object?>());

        var resultado = WmsFieldTemplateResolver.Resolve("[{NoExiste}]", registro);

        Assert.Equal("[]", resultado);
    }

    [Fact]
    public void Resolve_CampoNull_SustituyePorVacio()
    {
        var registro = new IntegrationRecord(new Dictionary<string, object?> { ["Campo"] = null });

        var resultado = WmsFieldTemplateResolver.Resolve("[{Campo}]", registro);

        Assert.Equal("[]", resultado);
    }
}
