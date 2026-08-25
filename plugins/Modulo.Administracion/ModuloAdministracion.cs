using Microsoft.Extensions.DependencyInjection;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Administracion;

/// <summary>
/// Punto de entrada del primer plugin real de esta plataforma -- ver
/// referencia-original/PortalSAP_v2/plugins/Modulo.Administracion/ModuloAdministracion.cs
/// para el original completo (8 pantallas, mono-tenant). Acá el alcance se redujo
/// deliberadamente a Usuarios (self-service por organización) -- Grupos/Perfiles/
/// Instancias/Empresas siguen siendo exclusivos de /Admin/* (operador de plataforma),
/// porque esas tablas son globales a la plataforma o llevan credenciales SAP.
/// </summary>
public sealed class ModuloAdministracion : IModuloPortal
{
    public string ModuleCode => "Administracion";
    public string Name => "Administración";
    public string Version => "1.0.0";

    public IEnumerable<MenuItemDefinition> GetMenu()
    {
        // bi-gear-fill (no bi-gear) -- mismo criterio que Inicio: el glifo outline se
        // ve más chico que cart/bag/box-seam/truck en la misma caja de 46x46px, la
        // variante rellena empareja el peso visual (ver "Densidad visual estándar").
        yield return new MenuItemDefinition { Code = "raiz", ParentCode = null, Name = "Administración", Icon = "bi bi-gear-fill", PageRoute = null, Order = 900 };
        yield return new MenuItemDefinition { Code = "usuarios", ParentCode = "raiz", Name = "Usuarios", Icon = "bi bi-people", PageRoute = "/organizacion/usuarios", Order = 1 };
        yield return new MenuItemDefinition { Code = "modulos", ParentCode = "raiz", Name = "Módulos", Icon = "bi bi-puzzle", PageRoute = "/organizacion/modulos", Order = 2 };
        yield return new MenuItemDefinition { Code = "menus", ParentCode = "raiz", Name = "Menús", Icon = "bi bi-list-nested", PageRoute = "/organizacion/menus", Order = 3 };
        yield return new MenuItemDefinition { Code = "grupos-menu", ParentCode = "raiz", Name = "Grupos de menú", Icon = "bi bi-diagram-3", PageRoute = "/organizacion/grupos-menu", Order = 4 };
        yield return new MenuItemDefinition { Code = "perfiles", ParentCode = "raiz", Name = "Perfiles", Icon = "bi bi-person-badge", PageRoute = "/organizacion/perfiles", Order = 5 };
    }

    public void RegisterServices(IServiceCollection services)
    {
        // ITenantUserAdminService ya lo registra el Host (es del Core) -- este plugin
        // no trae servicios propios.
    }
}
