using Modulo.Despachos;

namespace Modulo.Despachos.Tests;

public class ModuloDespachosTests
{
    [Fact] public void ModuleCode_EsDespachos() => Assert.Equal("Despachos", new ModuloDespachos().ModuleCode);

    [Fact]
    public void Menu_TodoItemTieneIconoYCodigoUnico()
    {
        var items = new ModuloDespachos().GetMenu().ToList();
        Assert.All(items, i => Assert.StartsWith("bi-", i.Icon));
        Assert.Equal(items.Count, items.Select(i => i.Code).Distinct().Count());
        Assert.All(items.Where(i => i.PageRoute != null), i => Assert.StartsWith("/despachos/", i.PageRoute));
    }
}
