using Microsoft.Extensions.DependencyInjection;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Compras;

/// <summary>
/// Cuarto plugin de negocio real de esta plataforma -- habla con el SAP de la
/// organización cliente vía IPurchaseDocumentService/IHanaService/
/// ISapConnectionProvider. Motor genérico de 4 tipos de documento de compra (ver
/// PurchaseDocumentType/PurchaseDocumentTypeCatalog), sin aprobación; las 2 facturas
/// copian líneas de un Pedido -- ver CLAUDE.md.
/// </summary>
public sealed class ModuloCompras : IModuloPortal
{
    public string ModuleCode => "Compras";
    public string Name => "Compras";
    public string Version => "1.0.0";

    public IEnumerable<MenuItemDefinition> GetMenu()
    {
        yield return new MenuItemDefinition { Code = "raiz", ParentCode = null, Name = "Compras", Icon = "bi bi-bag", PageRoute = null, Order = 150 };
        yield return new MenuItemDefinition { Code = "ofertas", ParentCode = "raiz", Name = "Ofertas de Compra", Icon = "bi bi-file-earmark-text", PageRoute = "/compras/ofertas", Order = 1 };
        yield return new MenuItemDefinition { Code = "pedidos", ParentCode = "raiz", Name = "Pedidos", Icon = "bi bi-bag-check", PageRoute = "/compras/pedidos", Order = 2 };
        yield return new MenuItemDefinition { Code = "facturas", ParentCode = "raiz", Name = "Facturas de Proveedor", Icon = "bi bi-receipt", PageRoute = "/compras/facturas-proveedor", Order = 3 };
        yield return new MenuItemDefinition { Code = "facturasreserva", ParentCode = "raiz", Name = "Facturas Reserva de Proveedor", Icon = "bi bi-receipt-cutoff", PageRoute = "/compras/facturas-reserva", Order = 4 };
    }

    public void RegisterServices(IServiceCollection services)
    {
        // Los servicios que usan estas páginas (IPurchaseDocumentService, catálogos
        // SAP) ya los registra el Host (son del Core) -- este plugin no trae servicios propios.
    }
}
