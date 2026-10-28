using Modulo.FacturasProveedor.Pages;

namespace Modulo.FacturasProveedor.Tests;

public class ModuloFacturasProveedorTests
{
    [Fact]
    public void ModuleCode_EsFacturasProveedor()
    {
        var m = new ModuloFacturasProveedor();
        Assert.Equal("FacturasProveedor", m.ModuleCode);
        Assert.Equal("Facturas de Proveedor", m.Name);
        Assert.True(m.RequiresExternalDatabase);
        Assert.True(m.ServesOwnWwwRoot);
    }

    [Fact]
    public void Menu_TodoItemTieneIconoYCodigoUnico()
    {
        var items = new ModuloFacturasProveedor().GetMenu().ToList();
        Assert.All(items, i => Assert.StartsWith("bi-", i.Icon));
        Assert.Equal(items.Count, items.Select(i => i.Code).Distinct().Count());
        Assert.All(items.Where(i => i.PageRoute != null), i => Assert.StartsWith("/facturas-proveedor/", i.PageRoute));
        var leaves = items.Where(i => i.PageRoute != null).Select(i => i.Code).ToHashSet();
        Assert.Equal(new[] { "aprobadores", "bandeja", "consultas-sap", "mis-aprobaciones", "proveedores", "reglas" }, leaves.OrderBy(c => c));
    }

    [Theory]
    [InlineData("in_review", "Por decidir", "badge-status--warning")]
    [InlineData("accepted", "Aceptada", "badge-status--success")]
    [InlineData("rejected", "Rechazada", "badge-status--danger")]
    [InlineData("expired", "Vencida (aceptada por ley)", "badge-status--danger")]
    [InlineData("external", "Respondida fuera del portal", "badge-status--neutral")]
    public void Ui_EtiquetaYBadgePorEstado(string status, string label, string badge)
    {
        Assert.Equal(label, FacturasProveedorUi.StatusLabel(status));
        Assert.Equal(badge, FacturasProveedorUi.StatusBadgeClass(status));
    }
}
