using Microsoft.Extensions.DependencyInjection;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Contrato que debe implementar el punto de entrada de todo plugin -- portado tal
/// cual de PortalSAP_v2 (`IModuloPortal`), es la pieza de arquitectura más valiosa a
/// reutilizar (ver ARCHITECTURE.md §2). El host futuro descubre una implementación de
/// esta interfaz por cada assembly cargado desde `artifacts/plugins/{Modulo}/{Version}/`
/// y la usa para sincronizar menú y registrar servicios propios del módulo.
/// </summary>
public interface IModuloPortal
{
    /// <summary>
    /// Código único y ESTABLE del módulo (no cambiar entre versiones). Es la clave que
    /// usa el host para hacer upsert de menú por módulo de origen.
    /// </summary>
    string ModuleCode { get; }

    /// <summary>Nombre visible del módulo (para pantallas de administración).</summary>
    string Name { get; }

    /// <summary>Versión del módulo, informativa (se puede loguear/auditar al cargar).</summary>
    string Version { get; }

    /// <summary>
    /// Entradas de menú que este módulo provee. El host las sincroniza usando
    /// (ModuleCode, Code) como clave de upsert.
    /// </summary>
    IEnumerable<MenuItemDefinition> GetMenu();

    /// <summary>
    /// Punto donde el módulo registra SUS PROPIOS servicios (los que no son del Core).
    /// No registrar acá servicios del Core -- esos ya los registra el host.
    /// </summary>
    void RegisterServices(IServiceCollection services);

    /// <summary>
    /// Default false: el módulo NO trae su propio wwwroot embebido. Poner en true si
    /// el módulo trae wwwroot propio -- ver PortalSAP_v2 (`SirveWwwRootPropio`) para el
    /// mecanismo completo, portado igual el día que haga falta.
    /// </summary>
    bool ServesOwnWwwRoot => false;

    /// <summary>
    /// Conexiones externas que el módulo necesita configurar. Vacío = un único
    /// requisito implícito Purpose="Default", Kind=Database, Required=true.
    /// </summary>
    IReadOnlyList<ExternalConnectionRequirement> ExternalConnectionRequirements => Array.Empty<ExternalConnectionRequirement>();
}
