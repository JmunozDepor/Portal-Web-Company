using Microsoft.Extensions.DependencyInjection;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas;

/// <summary>
/// Punto de entrada del primer plugin de negocio real de esta plataforma -- a
/// diferencia de Modulo.Administracion (opera solo contra la base propia de la
/// plataforma), este habla de verdad con el SAP de la organización cliente vía
/// ISalesOrderService/IHanaService/ISapConnectionProvider. Alcance recortado a
/// Órdenes de Venta (digitación directa, solo líneas de Artículo) -- ver CLAUDE.md.
/// </summary>
public sealed class ModuloVentas : IModuloPortal
{
    public string ModuleCode => "Ventas";
    public string Name => "Ventas";
    public string Version => "1.0.0";

    public IEnumerable<MenuItemDefinition> GetMenu()
    {
        yield return new MenuItemDefinition { Code = "raiz", ParentCode = null, Name = "Ventas", Icon = "bi bi-cart", PageRoute = null, Order = 100 };
        yield return new MenuItemDefinition { Code = "ordenes", ParentCode = "raiz", Name = "Órdenes de Venta", Icon = "bi bi-cart-check", PageRoute = "/ventas/ordenes", Order = 1 };
    }

    public void RegisterServices(IServiceCollection services)
    {
        // Los servicios que usan estas páginas (ISalesOrderService, catálogos SAP) ya
        // los registra el Host (son del Core) -- este plugin no trae servicios propios.
    }
}
