using PortalSaas.Core.Seguridad;
using Xunit;

namespace PortalSaas.Core.Tests.Seguridad;

public class RandomPasswordGeneratorTests
{
    [Fact]
    public void Generate_SiempreCumpleLaPoliticaDeContrasena()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = RandomPasswordGenerator.Generate();
            var errores = PasswordPolicy.Validate(password);

            Assert.Empty(errores);
        }
    }

    [Fact]
    public void Generate_DosLlamadasConsecutivas_ProducenValoresDistintos()
    {
        var primera = RandomPasswordGenerator.Generate();
        var segunda = RandomPasswordGenerator.Generate();

        Assert.NotEqual(primera, segunda);
    }

    [Fact]
    public void Generate_RespetaLaLongitudSolicitada()
    {
        var password = RandomPasswordGenerator.Generate(length: 20);

        Assert.Equal(20, password.Length);
    }

    [Fact]
    public void Generate_NoContieneCaracteresAmbiguos()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = RandomPasswordGenerator.Generate();

            Assert.DoesNotContain(password, c => "0O1lI".Contains(c));
        }
    }
}
