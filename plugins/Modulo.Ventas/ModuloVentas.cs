using Microsoft.Extensions.DependencyInjection;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Ventas;

/// <summary>
/// Punto de entrada del primer plugin de negocio real de esta plataforma -- a
/// diferencia de Modulo.Administracion (opera solo contra la base propia de la
/// plataforma), este habla de verdad con el SAP de la organización cliente vía
/// ISalesDocumentService/IHanaService/ISapConnectionProvider. Motor genérico de 7
/// tipos de documento de venta (ver SalesDocumentType/SalesDocumentTypeCatalog),
/// solo líneas de Artículo todavía -- ver CLAUDE.md.
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
        yield return new MenuItemDefinition { Code = "notascredito", ParentCode = "raiz", Name = "Notas de Crédito", Icon = "bi bi-file-earmark-minus", PageRoute = "/ventas/notas-credito", Order = 2 };
        yield return new MenuItemDefinition { Code = "facturas", ParentCode = "raiz", Name = "Facturas", Icon = "bi bi-receipt", PageRoute = "/ventas/facturas", Order = 3 };
        yield return new MenuItemDefinition { Code = "facturasreserva", ParentCode = "raiz", Name = "Facturas de Reserva", Icon = "bi bi-receipt-cutoff", PageRoute = "/ventas/facturas-reserva", Order = 4 };
        yield return new MenuItemDefinition { Code = "solicitudesdevolucion", ParentCode = "raiz", Name = "Solicitudes de Devolución", Icon = "bi bi-arrow-counterclockwise", PageRoute = "/ventas/solicitudes-devolucion", Order = 5 };
        yield return new MenuItemDefinition { Code = "devoluciones", ParentCode = "raiz", Name = "Devoluciones", Icon = "bi bi-arrow-return-left", PageRoute = "/ventas/devoluciones", Order = 6 };
        yield return new MenuItemDefinition { Code = "boletas", ParentCode = "raiz", Name = "Boletas", Icon = "bi bi-receipt", PageRoute = "/ventas/boletas", Order = 7 };
    }

    public void RegisterServices(IServiceCollection services)
    {
        // Los servicios que usan estas páginas (ISalesOrderService, catálogos SAP) ya
        // los registra el Host (son del Core) -- este plugin no trae servicios propios.
    }
}
