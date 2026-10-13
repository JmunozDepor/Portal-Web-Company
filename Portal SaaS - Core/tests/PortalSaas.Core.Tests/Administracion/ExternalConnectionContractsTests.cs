using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using Xunit;

namespace PortalSaas.Core.Tests.Administracion;

public sealed class ExternalConnectionContractsTests
{
    [Fact]
    public void BindingInput_PorDefectoUsaLasCredencialesDeLaInstancia()
    {
        var m = new ModuleDatabaseBindingInput();
        Assert.False(m.UseOwnCredentials);
        Assert.Null(m.TechnicalUsername);
        Assert.Null(m.TechnicalSecretKey);
    }

    [Fact]
    public void ModuloQueNoLoDeclara_NoUsaBaseExterna()
    {
        IModuloPortal modulo = new ModuloDummy();
        Assert.False(modulo.RequiresExternalDatabase);
    }

    private sealed class ModuloDummy : IModuloPortal
    {
        public string ModuleCode => "Dummy";
        public string Name => "Dummy";
        public string Version => "1.0.0";
        public IEnumerable<MenuItemDefinition> GetMenu() => [];
        public void RegisterServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services) { }
    }
}
