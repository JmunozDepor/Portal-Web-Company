using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Sincroniza (upsert) el árbol de `menus` desde `IModuloPortal.GetMenu()` de cada
/// plugin cargado -- portado de PortalSAP_v2 (`MenuSyncService`), invocado por
/// `PluginManager` una vez que terminó de cargar todos los plugins (ver Program.cs).
/// Incluye la fase que desactiva (`IsActive = false`) los nodos de un `OriginModule`
/// que ya no corresponde a ningún plugin cargado -- para que no queden huérfanos
/// activos para siempre si se retira un plugin (bug real encontrado y corregido en
/// PortalSAP_v2 al eliminar `Modulo.MigracionVentas`, ver su CLAUDE.md).
/// </summary>
public sealed class MenuSyncService
{
    private readonly PortalSaasDbContext _db;

    public MenuSyncService(PortalSaasDbContext db)
    {
        _db = db;
    }

    public async Task SyncAsync(IEnumerable<IModuloPortal> loadedModules, CancellationToken ct = default)
    {
        var modules = loadedModules.ToList();
        var loadedModuleCodes = modules.Select(m => m.ModuleCode).ToHashSet();

        var existing = await _db.Menus.ToListAsync(ct);
        var byKey = existing.ToDictionary(m => (m.OriginModule, m.Code));

        // Claves realmente emitidas por GetMenu() en este ciclo -- lo que un módulo
        // cargado dejó de emitir (ej. se borró una página) se desactiva más abajo.
        var seenKeys = new HashSet<(string, string)>();

        // Paso 1: upsert de cada nodo, SIN resolver padre todavía -- un padre puede
        // estar declarado en otro módulo que todavía no se procesó en este loop.
        foreach (var module in modules)
        {
            foreach (var item in module.GetMenu())
            {
                var key = (module.ModuleCode, item.Code);
                seenKeys.Add(key);
                if (byKey.TryGetValue(key, out var menu))
                {
                    menu.Name = item.Name;
                    menu.Icon = item.Icon;
                    menu.PagePath = item.PageRoute;
                    menu.Order = item.Order;
                    menu.IsActive = true;
                }
                else
                {
                    menu = new Menu
                    {
                        OriginModule = module.ModuleCode,
                        Code = item.Code,
                        Name = item.Name,
                        Icon = item.Icon,
                        PagePath = item.PageRoute,
                        Order = item.Order,
                        IsActive = true,
                    };
                    _db.Menus.Add(menu);
                    byKey[key] = menu;
                }
            }
        }

        // SaveChanges intermedio -- asegura que todo nodo nuevo ya tenga Id asignado
        // antes de resolver referencias de padre en el paso 2.
        await _db.SaveChangesAsync(ct);

        // Paso 2: resolver ParentMenuId + Level, ahora que todos los nodos existen.
        foreach (var module in modules)
        {
            foreach (var item in module.GetMenu())
            {
                var menu = byKey[(module.ModuleCode, item.Code)];

                if (item.ParentCode is null)
                {
                    menu.ParentMenuId = null;
                    menu.Level = 0;
                    continue;
                }

                var parentKey = ResolveParentKey(module.ModuleCode, item.ParentCode);

                // Padre declarado pero inexistente (typo, o el módulo dueño no está
                // cargado) -- falla hacia lo más seguro: se desactiva en vez de
                // mostrarse mal ubicado en la raíz del árbol.
                if (!byKey.TryGetValue(parentKey, out var parent))
                {
                    menu.IsActive = false;
                    menu.ParentMenuId = null;
                    menu.Level = 0;
                    continue;
                }

                menu.ParentMenuId = parent.Id;
                menu.Level = parent.Level + 1;
            }
        }

        // Fase de desactivación:
        //  (a) nodos de un módulo que ya no corresponde a ningún plugin cargado
        //      (plugin retirado del todo);
        //  (b) nodos de un módulo que SÍ está cargado pero que ya no emite esa clave
        //      en GetMenu() -- ej. se borró la página "Configuración del Servicio" de
        //      Modulo.Wms (2026-09-08) y su fila menus quedaba activa para siempre
        //      porque este servicio nunca revisaba "clave no vista este ciclo".
        // Soft-delete siempre (IsActive=false), nunca DELETE: si la clave reaparece en
        // un ciclo futuro, el Paso 1 la vuelve a activar sin perder su Id/hijos.
        foreach (var menu in existing)
        {
            if (!loadedModuleCodes.Contains(menu.OriginModule)
                || !seenKeys.Contains((menu.OriginModule, menu.Code)))
            {
                menu.IsActive = false;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// ParentCode sin punto = mismo módulo que el nodo hijo. Con punto = calificado
    /// como "ModuleCode.ParentCode", el padre pertenece a OTRO módulo (ver
    /// MenuItemDefinition.ParentCode).
    /// </summary>
    private static (string Module, string Code) ResolveParentKey(string currentModule, string parentCode)
    {
        var dotIndex = parentCode.IndexOf('.');
        return dotIndex < 0
            ? (currentModule, parentCode)
            : (parentCode[..dotIndex], parentCode[(dotIndex + 1)..]);
    }
}
