using Servicios.Common.Configuracion;
using Xunit;

namespace Servicios.Common.Tests;

public class WorkerOptionsTests
{
    [Fact]
    public void CicloIntervalo_ConValorPorDefecto_EsCincoMinutos()
    {
        var options = new WorkerOptions();

        Assert.Equal(TimeSpan.FromMinutes(5), options.CicloIntervalo);
    }

    [Fact]
    public void CicloIntervalo_ReflejaCicloIntervaloSegundosConfigurado()
    {
        var options = new WorkerOptions { CicloIntervaloSegundos = 90 };

        Assert.Equal(TimeSpan.FromSeconds(90), options.CicloIntervalo);
    }
}
