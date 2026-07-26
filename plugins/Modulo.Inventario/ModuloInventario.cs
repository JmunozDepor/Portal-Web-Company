using Microsoft.Extensions.DependencyInjection;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Inventario;

/// <summary>
/// Tercer plugin de negocio real de esta plataforma -- habla con el SAP de la
/// organización cliente vía IInventoryDocumentService/IHanaService/
/// ISapConnectionProvider. Motor genérico de 2 tipos de documento de inventario (ver
/// InventoryDocumentType/InventoryDocumentTypeCatalog) -- traslado de mercadería entre
/// almacenes, sin cliente/proveedor. Ver CLAUDE.md.
/// </summary>
public sealed class ModuloInventario : IModuloPortal
{
    public string ModuleCode => "Inventario";
    public string Name => "Inventario";
    public string Version => "1.0.0";

    public IEnumerable<MenuItemDefinition> GetMenu()
    {
        yield return new MenuItemDefinition { Code = "raiz", ParentCode = null, Name = "Inventario", Icon = "bi bi-box-seam", PageRoute = null, Order = 200 };
        yield return new MenuItemDefinition { Code = "solicitudestraslado", ParentCode = "raiz", Name = "Solicitudes de Traslado", Icon = "bi bi-signpost-split", PageRoute = "/inventario/solicitudes-traslado", Order = 1 };
        yield return new MenuItemDefinition { Code = "traslados", ParentCode = "raiz", Name = "Traslados", Icon = "bi bi-arrow-left-right", PageRoute = "/inventario/traslados", Order = 2 };
    }

    public void RegisterServices(IServiceCollection services)
    {
        // Los servicios que usan estas páginas (IInventoryDocumentService, catálogos
        // SAP) ya los registra el Host (son del Core) -- este plugin no trae servicios propios.
    }
}
