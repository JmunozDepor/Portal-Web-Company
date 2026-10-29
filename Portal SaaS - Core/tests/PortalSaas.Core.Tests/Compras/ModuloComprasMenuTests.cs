using Modulo.Compras;
using Xunit;

namespace PortalSaas.Core.Tests.Compras;

public sealed class ModuloComprasMenuTests
{
    [Fact]
    public void Menu_FacturasConIconoYRuta()
    {
        var menu = new ModuloCompras().GetMenu().ToList();

        var facturas = Assert.Single(menu, m => m.Code == "facturas");
        Assert.Equal("/compras/facturas-proveedor", facturas.PageRoute);
        Assert.Equal("bi bi-receipt", facturas.Icon);

        var reserva = Assert.Single(menu, m => m.Code == "facturasreserva");
        Assert.Equal("/compras/facturas-reserva", reserva.PageRoute);
        Assert.Equal("bi bi-receipt-cutoff", reserva.Icon);

        Assert.All(menu, m => Assert.False(string.IsNullOrWhiteSpace(m.Icon)));
    }
}
