using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using Xunit;

namespace PortalSaas.Core.Tests;

/// <summary>Doble de prueba de IModuloPortal -- sin carga real de plugins/AssemblyLoadContext.</summary>
file sealed class ModuloDePrueba : IModuloPortal
{
    private readonly List<MenuItemDefinition> _menu;

    public ModuloDePrueba(string codigo, params MenuItemDefinition[] menu)
    {
        ModuleCode = codigo;
        _menu = menu.ToList();
    }

    public string ModuleCode { get; }
    public string Name => ModuleCode;
    public string Version => "1.0.0";
    public IEnumerable<MenuItemDefinition> GetMenu() => _menu;
    public void RegisterServices(IServiceCollection services) { }
}

public class MenuSyncServiceTests
{
    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task SyncAsync_CreaNodosRaiz()
    {
        var db = CrearContexto();
        var modulo = new ModuloDePrueba("Ventas",
            new MenuItemDefinition { Code = "Ordenes", Name = "Órdenes", Order = 1 });

        await new MenuSyncService(db).SyncAsync([modulo]);

        var menu = Assert.Single(db.Menus);
        Assert.Equal("Ventas", menu.OriginModule);
        Assert.Equal("Ordenes", menu.Code);
        Assert.Null(menu.ParentMenuId);
        Assert.Equal(0, menu.Level);
        Assert.True(menu.IsActive);
    }

    [Fact]
    public async Task SyncAsync_ResuelvePadreDentroDelMismoModulo()
    {
        var db = CrearContexto();
        var modulo = new ModuloDePrueba("Ventas",
            new MenuItemDefinition { Code = "Raiz", Name = "Ventas", Order = 0 },
            new MenuItemDefinition { Code = "Ordenes", ParentCode = "Raiz", Name = "Órdenes", Order = 1 });

        await new MenuSyncService(db).SyncAsync([modulo]);

        var raiz = db.Menus.Single(m => m.Code == "Raiz");
        var ordenes = db.Menus.Single(m => m.Code == "Ordenes");
        Assert.Equal(raiz.Id, ordenes.ParentMenuId);
        Assert.Equal(1, ordenes.Level);
    }

    [Fact]
    public async Task SyncAsync_ResuelvePadreCalificadoDeOtroModulo()
    {
        var db = CrearContexto();
        var administracion = new ModuloDePrueba("Administracion",
            new MenuItemDefinition { Code = "Raiz", Name = "Administración", Order = 0 });
        var ventas = new ModuloDePrueba("Ventas",
            new MenuItemDefinition { Code = "Ordenes", ParentCode = "Administracion.Raiz", Name = "Órdenes", Order = 1 });

        await new MenuSyncService(db).SyncAsync([administracion, ventas]);

        var raiz = db.Menus.Single(m => m.OriginModule == "Administracion" && m.Code == "Raiz");
        var ordenes = db.Menus.Single(m => m.OriginModule == "Ventas" && m.Code == "Ordenes");
        Assert.Equal(raiz.Id, ordenes.ParentMenuId);
        Assert.Equal(1, ordenes.Level);
    }

    [Fact]
    public async Task SyncAsync_PadreInexistente_DesactivaYDejaEnRaiz()
    {
        var db = CrearContexto();
        var modulo = new ModuloDePrueba("Ventas",
            new MenuItemDefinition { Code = "Ordenes", ParentCode = "NoExiste", Name = "Órdenes", Order = 1 });

        await new MenuSyncService(db).SyncAsync([modulo]);

        var ordenes = Assert.Single(db.Menus);
        Assert.False(ordenes.IsActive);
        Assert.Null(ordenes.ParentMenuId);
    }

    [Fact]
    public async Task SyncAsync_ReSyncActualiza_NoDuplica()
    {
        var db = CrearContexto();
        var modulo = new ModuloDePrueba("Ventas",
            new MenuItemDefinition { Code = "Ordenes", Name = "Órdenes viejo", Order = 1 });
        await new MenuSyncService(db).SyncAsync([modulo]);

        var moduloActualizado = new ModuloDePrueba("Ventas",
            new MenuItemDefinition { Code = "Ordenes", Name = "Órdenes nuevo", Order = 5 });
        await new MenuSyncService(db).SyncAsync([moduloActualizado]);

        var menu = Assert.Single(db.Menus);
        Assert.Equal("Órdenes nuevo", menu.Name);
        Assert.Equal(5, menu.Order);
    }

    [Fact]
    public async Task SyncAsync_ModuloYaNoCargado_DesactivaSusNodos()
    {
        var db = CrearContexto();
        var modulo = new ModuloDePrueba("MigracionVentas",
            new MenuItemDefinition { Code = "Importar", Name = "Importar", Order = 1 });
        await new MenuSyncService(db).SyncAsync([modulo]);

        // Segunda sincronización: el módulo ya no está entre los cargados (plugin retirado).
        await new MenuSyncService(db).SyncAsync([]);

        var menu = Assert.Single(db.Menus);
        Assert.False(menu.IsActive);
    }

    [Fact]
    public async Task SyncAsync_ModuloVuelveACargar_ReactivaSusNodos()
    {
        var db = CrearContexto();
        var modulo = new ModuloDePrueba("Ventas",
            new MenuItemDefinition { Code = "Ordenes", Name = "Órdenes", Order = 1 });
        await new MenuSyncService(db).SyncAsync([modulo]);
        await new MenuSyncService(db).SyncAsync([]); // se desactiva

        await new MenuSyncService(db).SyncAsync([modulo]); // vuelve a cargar

        var menu = Assert.Single(db.Menus);
        Assert.True(menu.IsActive);
    }

    [Fact]
    public async Task SyncAsync_ModuloCargadoDejaDeEmitirUnItem_LoDesactiva()
    {
        var db = CrearContexto();
        var conConfig = new ModuloDePrueba("Wms",
            new MenuItemDefinition { Code = "raiz", Name = "WMS", Order = 0 },
            new MenuItemDefinition { Code = "config", ParentCode = "raiz", Name = "Config", Order = 1 },
            new MenuItemDefinition { Code = "estado", ParentCode = "raiz", Name = "Estado", Order = 2 });
        await new MenuSyncService(db).SyncAsync([conConfig]);

        // El módulo SIGUE cargado pero ya no emite "config" (se borró la página).
        var sinConfig = new ModuloDePrueba("Wms",
            new MenuItemDefinition { Code = "raiz", Name = "WMS", Order = 0 },
            new MenuItemDefinition { Code = "estado", ParentCode = "raiz", Name = "Estado", Order = 2 });
        await new MenuSyncService(db).SyncAsync([sinConfig]);

        var config = db.Menus.Single(m => m.Code == "config");
        Assert.False(config.IsActive);
        Assert.True(db.Menus.Single(m => m.Code == "estado").IsActive);
        Assert.True(db.Menus.Single(m => m.Code == "raiz").IsActive);
    }

    [Fact]
    public async Task SyncAsync_ItemVuelveAEmitirse_SeReactiva()
    {
        var db = CrearContexto();
        var conConfig = new ModuloDePrueba("Wms",
            new MenuItemDefinition { Code = "config", Name = "Config", Order = 1 });
        await new MenuSyncService(db).SyncAsync([conConfig]);
        await new MenuSyncService(db).SyncAsync([new ModuloDePrueba("Wms")]); // deja de emitirlo

        await new MenuSyncService(db).SyncAsync([conConfig]); // lo vuelve a emitir

        Assert.True(db.Menus.Single(m => m.Code == "config").IsActive);
    }
}
