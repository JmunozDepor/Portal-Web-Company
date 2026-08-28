using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using Xunit;

namespace PortalSaas.Core.Tests.Administracion;

public sealed class ExternalConnectionContractsTests
{
    [Fact]
    public void EditModelTieneDefaultsRazonables()
    {
        var m = new ExternalConnectionEditModel();
        Assert.Equal(ExternalConnectionType.DbSqlServer, m.Tipo);
        Assert.True(m.IsActive);
    }

    [Fact]
    public void ModuloSinRequisitosDevuelveListaVacia()
    {
        IModuloPortal modulo = new ModuloDummy();
        Assert.Empty(modulo.ExternalConnectionRequirements);
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
