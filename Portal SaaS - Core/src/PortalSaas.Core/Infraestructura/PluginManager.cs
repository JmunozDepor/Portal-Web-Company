using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Razor.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Descubre, carga y registra los plugins publicados en
/// `artifacts/plugins/{Modulo}/{Version}/`. Portado de PortalSAP_v2
/// (`PluginManager`), con una diferencia deliberada: **todavía no sincroniza menú**
/// -- la tabla equivalente a `PORTALWEB.MENU` y su `MenuSyncService` son parte del
/// "núcleo heredado" que sigue pendiente de portar (ver CLAUDE.md "Todavía no
/// existe"). `ModulosCargados` queda disponible igual para cuando esa pieza exista;
/// no se inventó una abstracción de sincronización de menú antes de tener la tabla
/// real detrás.
///
/// Bug conocido y corregido acá respecto al original: el ordenamiento de carpetas de
/// versión por string puro (`OrderByDescending(v => v)`) hace que "1.9.0" > "1.10.0"
/// alfabéticamente -- documentado como bug pendiente en `PortalSAP_v2/PLUGIN_DEVELOPMENT.md`
/// §6, corregido acá de una con `Version.TryParse`.
/// </summary>
public sealed class PluginManager
{
    private readonly ILogger<PluginManager> _logger;
    private readonly List<IModuloPortal> _loadedModules = new();

    // Solo se necesita el Assembly de los módulos con ServesOwnWwwRoot = true (el host
    // futuro lo usará para servir su wwwroot embebido) -- no vale la pena guardarlo para todos.
    private readonly Dictionary<IModuloPortal, Assembly> _assembliesWithOwnWwwRoot = new();

    // PluginLoadContext es "isCollectible: true" -- mantener vivo el IModuloPortal (vía
    // _loadedModules) NO alcanza para mantener vivo el AssemblyLoadContext en sí mismo,
    // que es un objeto separado. Sin esta lista, un plugin cuya dependencia se resuelve
    // recién en el primer request que la necesita de verdad (no al cargar el plugin)
    // puede toparse con el ALC ya recolectado por el GC -- bug real de PortalSAP_v2,
    // documentado ahí ("AssemblyLoadContext is unloading or was already unloaded").
    private readonly List<PluginLoadContext> _loadedContexts = new();

    // Ruta Razor compilada ("/Pages/X/Index.cshtml") -> módulo que la registró primero.
    // Ver RegistrarRutasRazor.
    private readonly Dictionary<string, string> _razorPathOwners = new(StringComparer.OrdinalIgnoreCase);

    public PluginManager(ILogger<PluginManager> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<IModuloPortal> ModulosCargados => _loadedModules;

    /// <summary>Assemblies de los módulos que declararon ServesOwnWwwRoot = true.</summary>
    public IReadOnlyDictionary<IModuloPortal, Assembly> AssembliesConWwwRootPropio => _assembliesWithOwnWwwRoot;

    /// <summary>
    /// Escanea la carpeta de artefactos, carga cada plugin en su propio
    /// PluginLoadContext, y registra sus páginas Razor como ApplicationPart.
    /// </summary>
    public void DiscoverAndLoad(string artifactsFolder, ApplicationPartManager partManager, IServiceCollection services)
    {
        if (!Directory.Exists(artifactsFolder))
        {
            _logger.LogWarning("Carpeta de plugins {Carpeta} no existe, no se carga ningún módulo", artifactsFolder);
            return;
        }

        // Estructura esperada: artifacts/plugins/{NombreModulo}/{Version}/*.dll
        foreach (var moduleFolder in Directory.GetDirectories(artifactsFolder))
        {
            var latestVersion = Directory.GetDirectories(moduleFolder)
                .Select(path => (Path: path, Version: Version.TryParse(new DirectoryInfo(path).Name, out var v) ? v : null))
                .Where(x => x.Version is not null)
                .OrderByDescending(x => x.Version)
                .Select(x => x.Path)
                .FirstOrDefault();

            if (latestVersion is null)
            {
                continue;
            }

            LoadModule(latestVersion, partManager, services);
        }
    }

    private void LoadModule(string versionFolder, ApplicationPartManager partManager, IServiceCollection services)
    {
        var folderName = new DirectoryInfo(versionFolder).Parent?.Name ?? "Desconocido";

        // Se asume convención: el DLL principal se llama igual que la carpeta del
        // módulo, ej. plugins/Modulo.Ventas/1.2.0/Modulo.Ventas.dll
        var mainDll = Path.Combine(versionFolder, $"{folderName}.dll");
        if (!File.Exists(mainDll))
        {
            _logger.LogWarning("No se encontró {Dll} para el módulo {Modulo}, se omite", mainDll, folderName);
            return;
        }

        var context = new PluginLoadContext(folderName, mainDll);
        _loadedContexts.Add(context);
        var assembly = context.LoadFromAssemblyPath(mainDll);

        // Assemblies de vistas compañeras (*.Views.dll) se registran pero no se buscan
        // implementaciones de IModuloPortal en ellos.
        if (assembly.GetName().Name?.EndsWith(".Views", StringComparison.OrdinalIgnoreCase) == true)
        {
            partManager.ApplicationParts.Add(new CompiledRazorAssemblyPart(assembly));
            return;
        }

        foreach (var (ruta, duenoPrevio) in RegistrarRutasRazor(
                     _razorPathOwners, folderName,
                     new RazorCompiledItemLoader().LoadItems(assembly).Select(item => item.Identifier)))
        {
            _logger.LogError(
                "Colisión de ruta Razor {Ruta}: la declaran {ModuloPrevio} y {Modulo}. ASP.NET Core registra UNA " +
                "sola por ruta y descarta la otra EN SILENCIO (su @page responde 404). Mover las páginas de " +
                "{Modulo} bajo Pages/{{NombreModulo}}/ -- ver docs/09-GUIA-DESARROLLO-PLUGINS.md §4",
                ruta, duenoPrevio, folderName);
        }

        partManager.ApplicationParts.Add(new AssemblyPart(assembly));
        partManager.ApplicationParts.Add(new CompiledRazorAssemblyPart(assembly));

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // GetTypes() en un assembly con un tipo que no resuelve (dependencia
            // faltante en el ALC del plugin, normalmente) tira esta excepción sin
            // mostrar la causa real -- está en LoaderExceptions, no en Message. Sin
            // este log es indepurable.
            foreach (var loaderEx in ex.LoaderExceptions)
            {
                _logger.LogError(loaderEx, "Falló al cargar un tipo del assembly {Assembly} ({Modulo})",
                    assembly.GetName().Name, folderName);
            }

            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }

        var moduleType = types
            .FirstOrDefault(t => typeof(IModuloPortal).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        if (moduleType is null)
        {
            _logger.LogWarning("El assembly {Assembly} no implementa IModuloPortal, se carga solo como vistas", assembly.GetName().Name);
            return;
        }

        if (Activator.CreateInstance(moduleType) is not IModuloPortal module)
        {
            _logger.LogError("No se pudo instanciar IModuloPortal en {Tipo}", moduleType.FullName);
            return;
        }

        _logger.LogInformation("Módulo {Codigo} v{Version} cargado ({CantidadMenus} entradas de menú)",
            module.ModuleCode, module.Version, module.GetMenu().Count());

        module.RegisterServices(services);
        _loadedModules.Add(module);

        if (module.ServesOwnWwwRoot)
        {
            _assembliesWithOwnWwwRoot[module] = assembly;
        }
    }

    /// <summary>
    /// Registra las rutas Razor compiladas de un módulo y devuelve las que ya declaraba
    /// otro módulo cargado antes. Bug real recurrente (2026-09-29: /sellout/sucursales
    /// 404 porque Modulo.AuditoriaInventario también tenía Pages/Sucursales/Index.cshtml):
    /// las páginas se indexan por ruta relativa ("/Pages/Sucursales/Index.cshtml"), NO por
    /// su @page, así que dos plugins con la misma carpeta se pisan aunque sus URLs sean
    /// distintas -- gana el primero en cargar y el otro desaparece sin error.
    /// _ViewStart/_ViewImports se excluyen: todos los plugins tienen uno en la raíz por
    /// diseño.
    /// </summary>
    public static IReadOnlyList<(string Ruta, string DuenoPrevio)> RegistrarRutasRazor(
        Dictionary<string, string> duenosPorRuta, string modulo, IEnumerable<string> rutas)
    {
        var colisiones = new List<(string, string)>();
        foreach (var ruta in rutas)
        {
            var archivo = Path.GetFileName(ruta);
            if (archivo.Equals("_ViewStart.cshtml", StringComparison.OrdinalIgnoreCase) ||
                archivo.Equals("_ViewImports.cshtml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (duenosPorRuta.TryGetValue(ruta, out var duenoPrevio))
            {
                if (!duenoPrevio.Equals(modulo, StringComparison.OrdinalIgnoreCase))
                {
                    colisiones.Add((ruta, duenoPrevio));
                }
            }
            else
            {
                duenosPorRuta[ruta] = modulo;
            }
        }

        return colisiones;
    }
}
