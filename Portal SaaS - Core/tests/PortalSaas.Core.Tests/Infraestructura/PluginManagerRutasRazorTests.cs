using PortalSaas.Core.Infraestructura;
using Xunit;

namespace PortalSaas.Core.Tests.Infraestructura;

public class PluginManagerRutasRazorTests
{
    [Fact]
    public void Misma_carpeta_de_pagina_en_dos_plugins_se_reporta_como_colision()
    {
        var duenos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        PluginManager.RegistrarRutasRazor(duenos, "Modulo.AuditoriaInventario",
            new[] { "/Pages/Sucursales/Index.cshtml" });

        var colisiones = PluginManager.RegistrarRutasRazor(duenos, "Modulo.SellOut",
            new[] { "/Pages/Sucursales/Index.cshtml", "/Pages/Clientes/Index.cshtml" });

        var colision = Assert.Single(colisiones);
        Assert.Equal("/Pages/Sucursales/Index.cshtml", colision.Ruta);
        Assert.Equal("Modulo.AuditoriaInventario", colision.DuenoPrevio);
    }

    [Fact]
    public void ViewStart_y_ViewImports_compartidos_no_son_colision()
    {
        var duenos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        PluginManager.RegistrarRutasRazor(duenos, "Modulo.Wms",
            new[] { "/_ViewStart.cshtml", "/Pages/_ViewImports.cshtml" });

        var colisiones = PluginManager.RegistrarRutasRazor(duenos, "Modulo.SellOut",
            new[] { "/_ViewStart.cshtml", "/Pages/_ViewImports.cshtml" });

        Assert.Empty(colisiones);
    }

    [Fact]
    public void Paginas_bajo_carpeta_propia_del_modulo_no_colisionan()
    {
        var duenos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        PluginManager.RegistrarRutasRazor(duenos, "Modulo.AuditoriaInventario",
            new[] { "/Pages/Sucursales/Index.cshtml" });

        var colisiones = PluginManager.RegistrarRutasRazor(duenos, "Modulo.SellOut",
            new[] { "/Pages/SellOut/Sucursales/Index.cshtml" });

        Assert.Empty(colisiones);
    }
}
