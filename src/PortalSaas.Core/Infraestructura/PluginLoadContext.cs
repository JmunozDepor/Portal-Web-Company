using System.Reflection;
using System.Runtime.Loader;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// AssemblyLoadContext aislado y colectable, uno por plugin. Portado tal cual de
/// PortalSAP_v2 (`PluginLoadContext`) -- es la pieza de arquitectura más valiosa a
/// reutilizar (ver ARCHITECTURE.md §2), ya verificada en producción ahí.
///
/// Por qué esto y no Assembly.LoadFrom:
///   - Un conflicto de versión de dependencia en un plugin no puede romper a otro
///     plugin ni al host, porque cada uno resuelve sus propias dependencias privadas
///     en su propio contexto.
///   - Al ser "collectible: true", en el futuro se podría descargar un plugin sin
///     reiniciar el proceso completo (no implementado todavía, pero el diseño lo permite).
///
/// Regla crítica: PortalSaas.Abstractions NUNCA se debe cargar dentro de este contexto.
/// Si un plugin lo trae como dependencia (porque tiene ProjectReference a él, cosa
/// esperada), hay que resolverlo SIEMPRE contra el AssemblyLoadContext.Default del host
/// -- si no, el tipo IModuloPortal del plugin no va a ser "el mismo tipo" que el que
/// espera el host, y el casteo/reflexión va a fallar silenciosamente.
/// </summary>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    /// <summary>Nombres de ensamblados que SIEMPRE se resuelven contra el contexto del host.</summary>
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "PortalSaas.Abstractions"
    };

    public PluginLoadContext(string moduleName, string mainAssemblyPath)
        : base(name: $"Plugin.{moduleName}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Contratos compartidos: resolver siempre desde el host, nunca duplicar.
        if (assemblyName.Name is not null && SharedAssemblies.Contains(assemblyName.Name))
        {
            return null; // null le indica al runtime que siga buscando en el Default context
        }

        // Cualquier ensamblado que el Default context (Host/framework compartido) ya
        // tenga cargado se resuelve ahí, nunca desde una copia propia del plugin --
        // bug real encontrado con Modulo.Rendiciones (primer plugin con dependencias
        // NuGet propias que arrastran ensamblados del framework compartido, ej.
        // Microsoft.Extensions.DependencyInjection.Abstractions vía los providers de
        // EF Core con CopyLocalLockFileAssemblies=true): sin este chequeo, el plugin
        // cargaba su PROPIA copia de ese ensamblado con una versión distinta a la que
        // ya usa el Host, y el runtime rechazaba RegisterServices(IServiceCollection)
        // con TypeLoadException ("does not have an implementation") porque
        // IServiceCollection del plugin y el del Host dejaban de ser "el mismo tipo".
        // Mismo criterio que ya aplica a PortalSaas.Abstractions arriba, generalizado.
        var alreadyLoaded = AssemblyLoadContext.Default.Assemblies
            .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
        if (alreadyLoaded is not null)
        {
            return null;
        }

        var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return resolvedPath is not null ? LoadFromAssemblyPath(resolvedPath) : null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var resolvedPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return resolvedPath is not null ? LoadUnmanagedDllFromPath(resolvedPath) : nint.Zero;
    }
}
