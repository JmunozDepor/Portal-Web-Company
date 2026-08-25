# Personalización de menús por organización Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir que el administrador de una organización reordene, oculte y renombre nodos existentes del árbol de menú, sin afectar a otras organizaciones ni al catálogo global de `Menu`.

**Architecture:** Una tabla nueva `organization_menu_overrides` (una fila por `OrganizationId`+`MenuId`) guarda el override; un servicio nuevo (`IOrganizationMenuOverrideService`) hace el CRUD acotado a la organización actual; `MenuNavigationService.GetVisibleMenuAsync` aplica esos overrides (rename/reorder/hide-con-subárbol) en el mismo punto donde ya aplica el filtro de módulos ocultos; una página nueva en `Modulo.Administracion` (`/organizacion/menus`) expone el CRUD en un formulario bulk-save, mismo patrón que `Permissions.cshtml`.

**Tech Stack:** ASP.NET Core Razor Pages (.NET 8), EF Core (motor dual Postgres/SQL Server), xUnit + EF Core InMemory.

## Global Constraints

- Comentarios, mensajes de log y texto de UI en español.
- `src/PortalSaas.Data` nunca importa un paquete de proveedor (Npgsql/SqlServer) — valores por defecto en C#, nunca `HasDefaultValueSql`.
- Toda migración de esquema se genera y aplica para **los dos motores** (PostgreSQL y SQL Server).
- Cualquier `<form method="post">` sin otro atributo `asp-*` lleva `asp-antiforgery="true"` explícito.
- No se modifica la entidad `Menu` ni `MenuSyncService` — el override vive en tabla aparte.
- `dotnet build "Portal SaaS - Core/PortalSaas.sln"` en 0 advertencias/0 errores y `dotnet test` en verde antes de dar cada tarea por terminada.

---

### Task 1: Entidad `OrganizationMenuOverride` + migraciones + DTOs

**Files:**
- Create: `Portal SaaS - Core/src/PortalSaas.Data/Entities/OrganizationMenuOverride.cs`
- Modify: `Portal SaaS - Core/src/PortalSaas.Data/PortalSaasDbContext.cs`
- Create: `Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/MenuOverride.cs`
- Create (generadas por `dotnet ef migrations add`): migración nueva en `Portal SaaS - Core/src/PortalSaas.Data.Migrations.PostgreSql/Migrations/` y en `Portal SaaS - Core/src/PortalSaas.Data.Migrations.SqlServer/Migrations/`

**Interfaces:**
- Produces: entidad `OrganizationMenuOverride` (`OrganizationId: Guid`, `MenuId: long`, `CustomLabel: string?`, `CustomOrder: int?`, `IsHidden: bool`), `DbSet<OrganizationMenuOverride> OrganizationMenuOverrides` en `PortalSaasDbContext`; DTOs `MenuOverrideRowDto` y `MenuOverrideInput` en `PortalSaas.Abstractions.Modelos` — Task 2 los consume.

- [ ] **Step 1: Crear la entidad**

```csharp
namespace PortalSaas.Data.Entities;

/// <summary>
/// Override por organización de nombre/orden/visibilidad de un nodo de `Menu` --
/// capa separada del catálogo global (mismo criterio que OrganizationModuleVisibility
/// para módulos completos, acá a nivel de nodo individual). Sin fila = usa el valor
/// original de Menu (nombre/orden sin cambios, visible).
/// </summary>
public sealed class OrganizationMenuOverride
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public long MenuId { get; set; }
    public Menu Menu { get; set; } = null!;

    /// <summary>Null = usa Menu.Name sin cambios.</summary>
    public string? CustomLabel { get; set; }

    /// <summary>Null = usa Menu.Order sin cambios.</summary>
    public int? CustomOrder { get; set; }

    public bool IsHidden { get; set; }
}
```

Guardar en `Portal SaaS - Core/src/PortalSaas.Data/Entities/OrganizationMenuOverride.cs`.

- [ ] **Step 2: Registrar el `DbSet` y la configuración en `PortalSaasDbContext`**

En `Portal SaaS - Core/src/PortalSaas.Data/PortalSaasDbContext.cs`, agregar la línea del `DbSet` junto a `OrganizationModuleVisibilities` (línea 49 actual):

```csharp
    public DbSet<OrganizationMenuOverride> OrganizationMenuOverrides => Set<OrganizationMenuOverride>();
```

Y agregar la configuración del modelo, inmediatamente después del bloque `modelBuilder.Entity<OrganizationModuleVisibility>(...)` (líneas 407-413 actuales):

```csharp
        modelBuilder.Entity<OrganizationMenuOverride>(entity =>
        {
            entity.ToTable("organization_menu_overrides");
            entity.HasKey(e => new { e.OrganizationId, e.MenuId });
            entity.HasOne(e => e.Organization).WithMany().HasForeignKey(e => e.OrganizationId);
            entity.HasOne(e => e.Menu).WithMany().HasForeignKey(e => e.MenuId);
            entity.Property(e => e.CustomLabel).HasMaxLength(200);
        });
```

- [ ] **Step 3: Compilar para confirmar que el modelo es válido**

Run: `dotnet build "Portal SaaS - Core/PortalSaas.sln"`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Generar y aplicar la migración en los dos motores**

Desde `Portal SaaS - Core/`:

```bash
dotnet tool run dotnet-ef migrations add AddOrganizationMenuOverrides \
  --project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj \
  --startup-project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj \
  --context PortalSaasDbContext

dotnet tool run dotnet-ef migrations add AddOrganizationMenuOverrides \
  --project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj \
  --startup-project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj \
  --context PortalSaasDbContext
```

Si hay un entorno Postgres/SQL Server local levantado (`docker compose up -d`, ver `CLAUDE.md`), aplicar contra la base real con `dotnet ef database update` (mismos flags `--project`/`--startup-project`, sin `migrations add`). Si no hay entorno disponible en esta sesión, dejarlo documentado en el reporte de la tarea como pendiente — no es bloqueante para las tareas siguientes (usan EF Core InMemory).

- [ ] **Step 5: Crear los DTOs**

```csharp
namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de la pantalla de personalización de menús -- un nodo de Menu con su override actual (si existe) para la organización actual.</summary>
public sealed class MenuOverrideRowDto
{
    public required long MenuId { get; init; }
    public required int Level { get; init; }
    public required string OriginModule { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? CustomLabel { get; init; }
    public int? CustomOrder { get; init; }
    public required bool IsHidden { get; init; }
}

/// <summary>Valor a guardar para un nodo puntual -- CustomLabel/CustomOrder vacíos + IsHidden=false borra el override (vuelve a heredar).</summary>
public sealed record MenuOverrideInput(string? CustomLabel, int? CustomOrder, bool IsHidden);
```

Guardar en `Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/MenuOverride.cs`.

- [ ] **Step 6: Compilar de nuevo**

Run: `dotnet build "Portal SaaS - Core/PortalSaas.sln"`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 7: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Data/Entities/OrganizationMenuOverride.cs" "Portal SaaS - Core/src/PortalSaas.Data/PortalSaasDbContext.cs" "Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/MenuOverride.cs" "Portal SaaS - Core/src/PortalSaas.Data.Migrations.PostgreSql/Migrations/" "Portal SaaS - Core/src/PortalSaas.Data.Migrations.SqlServer/Migrations/"
git commit -m "feat: agregar entidad y migraciones de OrganizationMenuOverride"
```

---

### Task 2: `IOrganizationMenuOverrideService` + implementación + tests

**Files:**
- Create: `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IOrganizationMenuOverrideService.cs`
- Create: `Portal SaaS - Core/src/PortalSaas.Core/Administracion/OrganizationMenuOverrideService.cs`
- Modify: `Portal SaaS - Core/src/PortalSaas.Host/Program.cs`
- Test: `Portal SaaS - Core/tests/PortalSaas.Core.Tests/OrganizationMenuOverrideServiceTests.cs`

**Interfaces:**
- Consumes: `OrganizationMenuOverride` y DTOs de Task 1; `ICurrentUserContext.OrganizationId` (ya existente).
- Produces: `IOrganizationMenuOverrideService.ListAsync(CancellationToken) : Task<IReadOnlyList<MenuOverrideRowDto>>` y `.SaveOverridesAsync(Dictionary<long, MenuOverrideInput>, CancellationToken) : Task` — Task 4 (UI) los consume.

- [ ] **Step 1: Escribir el test que falla primero**

```csharp
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Administracion;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

file sealed class CurrentUserContextFijo : ICurrentUserContext
{
    public required Guid UserId { get; init; }
    public string Username => "usuario.prueba";
    public bool IsAdmin => true;
    public required Guid OrganizationId { get; init; }

    public Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default) => Task.FromResult(true);
}

public class OrganizationMenuOverrideServiceTests
{
    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(PortalSaasDbContext Db, Organization Org, Menu Raiz, Menu Hijo)> CrearOrganizacionConMenuAsync()
    {
        var db = CrearContexto();

        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var raiz = new Menu { OriginModule = "Ventas", Code = "raiz", Name = "Ventas", Order = 0, Level = 0, IsActive = true };
        db.Menus.Add(raiz);
        await db.SaveChangesAsync();

        var hijo = new Menu { OriginModule = "Ventas", Code = "ordenes", Name = "Órdenes", Order = 0, Level = 1, IsActive = true, ParentMenuId = raiz.Id };
        db.Menus.Add(hijo);
        await db.SaveChangesAsync();

        return (db, org, raiz, hijo);
    }

    [Fact]
    public async Task SaveOverridesAsync_ConValores_CreaLaFila()
    {
        var (db, org, raiz, _) = await CrearOrganizacionConMenuAsync();
        var servicio = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org.Id });

        await servicio.SaveOverridesAsync(new Dictionary<long, MenuOverrideInput>
        {
            [raiz.Id] = new MenuOverrideInput("Mis Ventas", 5, false),
        });

        var filas = await servicio.ListAsync();
        var fila = Assert.Single(filas, f => f.MenuId == raiz.Id);
        Assert.Equal("Mis Ventas", fila.CustomLabel);
        Assert.Equal(5, fila.CustomOrder);
        Assert.False(fila.IsHidden);
    }

    [Fact]
    public async Task SaveOverridesAsync_ValoresPorDefecto_BorraLaFilaExistente()
    {
        var (db, org, raiz, _) = await CrearOrganizacionConMenuAsync();
        var servicio = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org.Id });

        await servicio.SaveOverridesAsync(new Dictionary<long, MenuOverrideInput> { [raiz.Id] = new MenuOverrideInput("Custom", 1, false) });
        Assert.Equal(1, await db.OrganizationMenuOverrides.CountAsync());

        await servicio.SaveOverridesAsync(new Dictionary<long, MenuOverrideInput> { [raiz.Id] = new MenuOverrideInput(null, null, false) });

        Assert.Equal(0, await db.OrganizationMenuOverrides.CountAsync());
    }

    [Fact]
    public async Task ListAsync_AislaEntreOrganizaciones()
    {
        var (db, org1, raiz, _) = await CrearOrganizacionConMenuAsync();
        var org2 = new Organization { LegalName = "Otro cliente", Slug = "otro-cliente", Country = "CL" };
        db.Organizations.Add(org2);
        await db.SaveChangesAsync();

        var servicioOrg1 = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org1.Id });
        var servicioOrg2 = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org2.Id });

        await servicioOrg1.SaveOverridesAsync(new Dictionary<long, MenuOverrideInput> { [raiz.Id] = new MenuOverrideInput("Solo Org1", null, false) });

        var filasOrg2 = await servicioOrg2.ListAsync();
        var filaOrg2 = Assert.Single(filasOrg2, f => f.MenuId == raiz.Id);
        Assert.Null(filaOrg2.CustomLabel);

        var filasOrg1 = await servicioOrg1.ListAsync();
        var filaOrg1 = Assert.Single(filasOrg1, f => f.MenuId == raiz.Id);
        Assert.Equal("Solo Org1", filaOrg1.CustomLabel);
    }

    [Fact]
    public async Task ListAsync_SinOverrides_DevuelveTodosLosNodosConValoresPorDefecto()
    {
        var (db, org, raiz, hijo) = await CrearOrganizacionConMenuAsync();
        var servicio = new OrganizationMenuOverrideService(db, new CurrentUserContextFijo { UserId = Guid.NewGuid(), OrganizationId = org.Id });

        var filas = await servicio.ListAsync();

        Assert.Equal(2, filas.Count);
        Assert.All(filas, f => Assert.Null(f.CustomLabel));
        Assert.All(filas, f => Assert.False(f.IsHidden));
        Assert.Contains(filas, f => f.MenuId == raiz.Id && f.Level == 0);
        Assert.Contains(filas, f => f.MenuId == hijo.Id && f.Level == 1);
    }
}
```

Guardar en `Portal SaaS - Core/tests/PortalSaas.Core.Tests/OrganizationMenuOverrideServiceTests.cs`.

- [ ] **Step 2: Correr los tests y verificar que fallan (el servicio todavía no existe)**

Run: `dotnet test "Portal SaaS - Core/PortalSaas.sln" --filter OrganizationMenuOverrideServiceTests`
Expected: FAIL con error de compilación (`OrganizationMenuOverrideService` no existe).

- [ ] **Step 3: Crear la interfaz**

```csharp
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Self-service para que el admin de la organización actual (ICurrentUserContext.OrganizationId)
/// personalice nombre/orden/visibilidad de un nodo de Menu -- capa separada del catálogo
/// global (mismo criterio que IOrganizationModuleVisibilityService, a nivel de nodo).
/// </summary>
public interface IOrganizationMenuOverrideService
{
    /// <summary>Todos los nodos de Menu activos, con su override actual (si existe) para la organización actual.</summary>
    Task<IReadOnlyList<MenuOverrideRowDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Upsert/delete por cada MenuId presente en el diccionario -- un input en sus 3 valores por defecto (CustomLabel null/vacío, CustomOrder null, IsHidden false) borra el override existente en vez de guardar uno redundante.</summary>
    Task SaveOverridesAsync(Dictionary<long, MenuOverrideInput> overridesByMenuId, CancellationToken ct = default);
}
```

Guardar en `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IOrganizationMenuOverrideService.cs`.

- [ ] **Step 4: Implementar el servicio**

```csharp
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Administracion;

/// <summary>Implementación real de IOrganizationMenuOverrideService, acotada a ICurrentUserContext.OrganizationId.</summary>
public sealed class OrganizationMenuOverrideService : IOrganizationMenuOverrideService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public OrganizationMenuOverrideService(PortalSaasDbContext db, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<MenuOverrideRowDto>> ListAsync(CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;

        var menus = await _db.Menus
            .Where(m => m.IsActive)
            .OrderBy(m => m.OriginModule).ThenBy(m => m.Level).ThenBy(m => m.Order)
            .ToListAsync(ct);

        var overrides = await _db.OrganizationMenuOverrides
            .Where(o => o.OrganizationId == organizationId)
            .ToDictionaryAsync(o => o.MenuId, ct);

        return menus
            .Select(m =>
            {
                var ov = overrides.GetValueOrDefault(m.Id);
                return new MenuOverrideRowDto
                {
                    MenuId = m.Id,
                    Level = m.Level,
                    OriginModule = m.OriginModule,
                    Code = m.Code,
                    Name = m.Name,
                    CustomLabel = ov?.CustomLabel,
                    CustomOrder = ov?.CustomOrder,
                    IsHidden = ov?.IsHidden ?? false,
                };
            })
            .ToList();
    }

    public async Task SaveOverridesAsync(Dictionary<long, MenuOverrideInput> overridesByMenuId, CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;

        var validMenuIds = (await _db.Menus.Where(m => m.IsActive).Select(m => m.Id).ToListAsync(ct)).ToHashSet();

        var existing = await _db.OrganizationMenuOverrides
            .Where(o => o.OrganizationId == organizationId)
            .ToDictionaryAsync(o => o.MenuId, ct);

        foreach (var (menuId, input) in overridesByMenuId)
        {
            if (!validMenuIds.Contains(menuId))
            {
                continue;
            }

            var customLabel = string.IsNullOrWhiteSpace(input.CustomLabel) ? null : input.CustomLabel;
            var esValorPorDefecto = customLabel is null && input.CustomOrder is null && !input.IsHidden;
            var actual = existing.GetValueOrDefault(menuId);

            if (esValorPorDefecto)
            {
                if (actual is not null)
                {
                    _db.OrganizationMenuOverrides.Remove(actual);
                }

                continue;
            }

            if (actual is null)
            {
                _db.OrganizationMenuOverrides.Add(new OrganizationMenuOverride
                {
                    OrganizationId = organizationId,
                    MenuId = menuId,
                    CustomLabel = customLabel,
                    CustomOrder = input.CustomOrder,
                    IsHidden = input.IsHidden,
                });
            }
            else
            {
                actual.CustomLabel = customLabel;
                actual.CustomOrder = input.CustomOrder;
                actual.IsHidden = input.IsHidden;
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
```

Guardar en `Portal SaaS - Core/src/PortalSaas.Core/Administracion/OrganizationMenuOverrideService.cs`.

- [ ] **Step 5: Registrar el servicio en DI**

En `Portal SaaS - Core/src/PortalSaas.Host/Program.cs`, agregar junto a la línea existente de `IOrganizationModuleVisibilityService` (línea 159 actual):

```csharp
builder.Services.AddScoped<IOrganizationMenuOverrideService, OrganizationMenuOverrideService>();
```

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test "Portal SaaS - Core/PortalSaas.sln" --filter OrganizationMenuOverrideServiceTests`
Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4`

- [ ] **Step 7: Compilar todo el proyecto**

Run: `dotnet build "Portal SaaS - Core/PortalSaas.sln"`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 8: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IOrganizationMenuOverrideService.cs" "Portal SaaS - Core/src/PortalSaas.Core/Administracion/OrganizationMenuOverrideService.cs" "Portal SaaS - Core/src/PortalSaas.Host/Program.cs" "Portal SaaS - Core/tests/PortalSaas.Core.Tests/OrganizationMenuOverrideServiceTests.cs"
git commit -m "feat: agregar IOrganizationMenuOverrideService con tests"
```

---

### Task 3: `MenuNavigationService` aplica los overrides

**Files:**
- Modify: `Portal SaaS - Core/src/PortalSaas.Core/Infraestructura/MenuNavigationService.cs`
- Test: `Portal SaaS - Core/tests/PortalSaas.Core.Tests/MenuNavigationServiceTests.cs`

**Interfaces:**
- Consumes: `OrganizationMenuOverride` (Task 1); `IModuleAccessService` (ya existente, real, no fake — con `PlatformModules` vacío no filtra nada).
- Produces: `MenuNavigationService.GetVisibleMenuAsync` ahora aplica rename/reorder/hide-con-subárbol antes del bypass de admin — ningún consumidor externo cambia de firma.

- [ ] **Step 1: Escribir el test que falla primero**

```csharp
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Comercial;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

file sealed class CurrentUserContextNoAdmin : ICurrentUserContext
{
    public required Guid UserId { get; init; }
    public string Username => "usuario.prueba";
    public bool IsAdmin => false;
    public required Guid OrganizationId { get; init; }

    public Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default) => Task.FromResult(true);
}

file sealed class CurrentUserContextAdmin : ICurrentUserContext
{
    public required Guid UserId { get; init; }
    public string Username => "admin.prueba";
    public bool IsAdmin => true;
    public required Guid OrganizationId { get; init; }

    public Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default) => Task.FromResult(true);
}

file sealed class CurrentCompanyAccessorFijo : ICurrentCompanyAccessor
{
    public required Guid CompanyId { get; init; }
    public string Code => "TEST";
    public string Database => "TEST";
    public string ServiceLayerUrl => "http://test";
    public string Country => "CL";
    public bool HasCompany => true;
}

public class MenuNavigationServiceTests
{
    private static PortalSaasDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static MenuNavigationService CrearServicio(PortalSaasDbContext db, Guid organizationId, bool isAdmin = true) =>
        new(db, isAdmin
                ? new CurrentUserContextAdmin { UserId = Guid.NewGuid(), OrganizationId = organizationId }
                : new CurrentUserContextNoAdmin { UserId = Guid.NewGuid(), OrganizationId = organizationId },
            new CurrentCompanyAccessorFijo { CompanyId = Guid.NewGuid() },
            new ModuleAccessService(db));

    private static async Task<(PortalSaasDbContext Db, Organization Org, Menu Raiz, Menu Hijo)> CrearOrganizacionConMenuAsync()
    {
        var db = CrearContexto();

        var org = new Organization { LegalName = "Cliente de prueba", Slug = "cliente-prueba", Country = "CL" };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var raiz = new Menu { OriginModule = "Ventas", Code = "raiz", Name = "Ventas", Order = 0, Level = 0, IsActive = true };
        db.Menus.Add(raiz);
        await db.SaveChangesAsync();

        var hijo = new Menu { OriginModule = "Ventas", Code = "ordenes", Name = "Órdenes", Order = 0, Level = 1, IsActive = true, ParentMenuId = raiz.Id };
        db.Menus.Add(hijo);
        await db.SaveChangesAsync();

        return (db, org, raiz, hijo);
    }

    [Fact]
    public async Task GetVisibleMenuAsync_SinOverrides_ComportamientoIgualQueHoy()
    {
        var (db, org, raiz, hijo) = await CrearOrganizacionConMenuAsync();
        var servicio = CrearServicio(db, org.Id);

        var arbol = await servicio.GetVisibleMenuAsync();

        var raizVisible = Assert.Single(arbol);
        Assert.Equal("Ventas", raizVisible.Name);
        var hijoVisible = Assert.Single(raizVisible.Children);
        Assert.Equal("Órdenes", hijoVisible.Name);
    }

    [Fact]
    public async Task GetVisibleMenuAsync_ConCustomLabel_UsaElNombrePersonalizado()
    {
        var (db, org, raiz, _) = await CrearOrganizacionConMenuAsync();
        db.OrganizationMenuOverrides.Add(new OrganizationMenuOverride { OrganizationId = org.Id, MenuId = raiz.Id, CustomLabel = "Mis Ventas" });
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db, org.Id);
        var arbol = await servicio.GetVisibleMenuAsync();

        var raizVisible = Assert.Single(arbol);
        Assert.Equal("Mis Ventas", raizVisible.Name);
    }

    [Fact]
    public async Task GetVisibleMenuAsync_NodoOculto_DesaparaceConSuSubarbol()
    {
        var (db, org, raiz, hijo) = await CrearOrganizacionConMenuAsync();
        db.OrganizationMenuOverrides.Add(new OrganizationMenuOverride { OrganizationId = org.Id, MenuId = raiz.Id, IsHidden = true });
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db, org.Id);
        var arbol = await servicio.GetVisibleMenuAsync();

        Assert.Empty(arbol);
    }

    [Fact]
    public async Task GetVisibleMenuAsync_OverrideAplicaTambienAUnAdmin()
    {
        var (db, org, raiz, _) = await CrearOrganizacionConMenuAsync();
        db.OrganizationMenuOverrides.Add(new OrganizationMenuOverride { OrganizationId = org.Id, MenuId = raiz.Id, IsHidden = true });
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db, org.Id, isAdmin: true);
        var arbol = await servicio.GetVisibleMenuAsync();

        Assert.Empty(arbol);
    }

    [Fact]
    public async Task GetVisibleMenuAsync_OverrideDeOtraOrganizacion_NoAfecta()
    {
        var (db, org1, raiz, _) = await CrearOrganizacionConMenuAsync();
        var org2 = new Organization { LegalName = "Otro cliente", Slug = "otro-cliente", Country = "CL" };
        db.Organizations.Add(org2);
        await db.SaveChangesAsync();
        db.OrganizationMenuOverrides.Add(new OrganizationMenuOverride { OrganizationId = org2.Id, MenuId = raiz.Id, IsHidden = true });
        await db.SaveChangesAsync();

        var servicio = CrearServicio(db, org1.Id);
        var arbol = await servicio.GetVisibleMenuAsync();

        Assert.Single(arbol);
    }
}
```

Guardar en `Portal SaaS - Core/tests/PortalSaas.Core.Tests/MenuNavigationServiceTests.cs`.

- [ ] **Step 2: Correr los tests y verificar cuáles fallan**

Run: `dotnet test "Portal SaaS - Core/PortalSaas.sln" --filter MenuNavigationServiceTests`
Expected: `GetVisibleMenuAsync_SinOverrides_ComportamientoIgualQueHoy` PASA (comportamiento ya existente); los otros 4 FALLAN (los overrides todavía no se aplican).

- [ ] **Step 3: Aplicar los overrides en `MenuNavigationService.GetVisibleMenuAsync`**

En `Portal SaaS - Core/src/PortalSaas.Core/Infraestructura/MenuNavigationService.cs`, agregar este bloque inmediatamente después del filtro de `hiddenModules` (después de la línea 85 actual, `}` que cierra el `if (hiddenModules.Count > 0)`) y **antes** de `if (_currentUser.IsAdmin)` (línea 87 actual):

```csharp
        // Personalización por organización (rename/reorder/hide de un nodo puntual) --
        // igual que el filtro de módulos ocultos de arriba, corre ANTES del bypass de
        // administrador: es preferencia de la organización sobre qué se ve, no permiso
        // individual, así que también alcanza a los admins de esa organización.
        var overrides = await _db.OrganizationMenuOverrides
            .Where(o => o.OrganizationId == _currentUser.OrganizationId)
            .ToDictionaryAsync(o => o.MenuId, ct);
        if (overrides.Count > 0)
        {
            activeMenus = ApplyOverrides(activeMenus, overrides);
        }

```

Y agregar estos dos métodos privados nuevos, junto a `ExpandWithAncestors` (después de su cierre, línea 154 actual):

```csharp
    /// <summary>
    /// Aplica rename/reorder de OrganizationMenuOverride y remueve los nodos ocultos
    /// junto con TODO su subárbol (para no dejar hijos huérfanos visibles sin su
    /// carpeta contenedora) -- todo en memoria sobre `nodes` ya cargado, sin consultas
    /// adicionales. MenuNodeDto tiene propiedades `init`, así que un nodo con override
    /// de nombre/orden se reconstruye entero en vez de mutarse.
    /// </summary>
    private static List<MenuNodeDto> ApplyOverrides(List<MenuNodeDto> nodes, Dictionary<long, Data.Entities.OrganizationMenuOverride> overridesByMenuId)
    {
        var hiddenRootIds = overridesByMenuId.Where(kv => kv.Value.IsHidden).Select(kv => kv.Key).ToHashSet();
        var hiddenWithDescendants = hiddenRootIds.Count == 0 ? hiddenRootIds : ExpandWithDescendants(nodes, hiddenRootIds);

        return nodes
            .Where(n => !hiddenWithDescendants.Contains(n.Id))
            .Select(n =>
            {
                if (!overridesByMenuId.TryGetValue(n.Id, out var ov))
                {
                    return n;
                }

                return new MenuNodeDto
                {
                    Id = n.Id,
                    ParentMenuId = n.ParentMenuId,
                    OriginModule = n.OriginModule,
                    Code = n.Code,
                    Name = ov.CustomLabel ?? n.Name,
                    Icon = n.Icon,
                    PagePath = n.PagePath,
                    Order = ov.CustomOrder ?? n.Order,
                };
            })
            .ToList();
    }

    /// <summary>Baja por Children (vía ParentMenuId) desde cada id raíz hasta las hojas -- mismo criterio sin-N+1 que ExpandWithAncestors, pero en la dirección opuesta.</summary>
    private static HashSet<long> ExpandWithDescendants(List<MenuNodeDto> allNodes, HashSet<long> rootIds)
    {
        var childrenByParent = allNodes
            .Where(n => n.ParentMenuId is not null)
            .GroupBy(n => n.ParentMenuId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList());

        var result = new HashSet<long>(rootIds);
        var queue = new Queue<long>(rootIds);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!childrenByParent.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var childId in children)
            {
                if (result.Add(childId))
                {
                    queue.Enqueue(childId);
                }
            }
        }

        return result;
    }
```

- [ ] **Step 4: Correr los tests y verificar que todos pasan**

Run: `dotnet test "Portal SaaS - Core/PortalSaas.sln" --filter MenuNavigationServiceTests`
Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5`

- [ ] **Step 5: Correr toda la suite (regresión cero sobre lo existente)**

Run: `dotnet test "Portal SaaS - Core/PortalSaas.sln"`
Expected: todos los tests en verde, ninguno roto.

- [ ] **Step 6: Compilar**

Run: `dotnet build "Portal SaaS - Core/PortalSaas.sln"`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 7: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Core/Infraestructura/MenuNavigationService.cs" "Portal SaaS - Core/tests/PortalSaas.Core.Tests/MenuNavigationServiceTests.cs"
git commit -m "feat: aplicar OrganizationMenuOverride en MenuNavigationService"
```

---

### Task 4: Pantalla `/organizacion/menus`

**Files:**
- Create: `Portal SaaS - Core/plugins/Modulo.Administracion/Pages/Menus/Index.cshtml.cs`
- Create: `Portal SaaS - Core/plugins/Modulo.Administracion/Pages/Menus/Index.cshtml`

**Interfaces:**
- Consumes: `IOrganizationMenuOverrideService` (Task 2), `AdminPageModelBase` (ya existente, gate `ICurrentUserContext.IsAdmin`).
- Produces: página Razor en `/organizacion/menus` — no la consume ninguna tarea posterior (última tarea del plan).

- [ ] **Step 1: Crear el `PageModel`**

```csharp
using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Administracion.Pages.Menus;

/// <summary>
/// Self-service: reordenar, ocultar y renombrar nodos existentes del árbol de menú
/// para la organización actual -- ver IOrganizationMenuOverrideService. Nunca crea
/// nodos nuevos, solo personaliza los que ya sincronizaron los plugins.
/// </summary>
public sealed class IndexModel : AdminPageModelBase
{
    private readonly IOrganizationMenuOverrideService _overrides;

    public IndexModel(IOrganizationMenuOverrideService overrides, ICurrentUserContext currentUser) : base(currentUser)
    {
        _overrides = overrides;
    }

    public IReadOnlyList<MenuOverrideRowDto> Rows { get; private set; } = [];

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        Rows = await _overrides.ListAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var hiddenSet = (Input.HiddenMenuIds ?? []).ToHashSet();
        var todosLosIds = Input.CustomLabel.Keys.Union(Input.CustomOrder.Keys).Union(hiddenSet);

        var overridesByMenuId = todosLosIds.ToDictionary(
            menuId => menuId,
            menuId => new MenuOverrideInput(
                Input.CustomLabel.GetValueOrDefault(menuId),
                Input.CustomOrder.GetValueOrDefault(menuId),
                hiddenSet.Contains(menuId)));

        await _overrides.SaveOverridesAsync(overridesByMenuId);

        MensajeExito = "Menús actualizados correctamente.";
        return RedirectToPage();
    }

    public sealed class InputModel
    {
        public Dictionary<long, string?> CustomLabel { get; set; } = [];
        public Dictionary<long, int?> CustomOrder { get; set; } = [];
        public List<long> HiddenMenuIds { get; set; } = [];
    }
}
```

Guardar en `Portal SaaS - Core/plugins/Modulo.Administracion/Pages/Menus/Index.cshtml.cs`.

- [ ] **Step 2: Crear la vista**

```cshtml
@page "/organizacion/menus"
@model Modulo.Administracion.Pages.Menus.IndexModel
@{
    ViewData["Title"] = "Menús";
}

<div class="card-surface">
    <h2 class="mb-1">Menús de tu organización</h2>
    <p class="text-muted mb-3">
        Personalizá el orden, el nombre y la visibilidad de cada nodo del menú para tu
        organización. Dejar "Nombre personalizado" y "Orden" en blanco usa el valor
        original del módulo.
    </p>

    @if (Model.MensajeExito is not null)
    {
        <div class="alert alert-success">@Model.MensajeExito</div>
    }
    @if (Model.MensajeError is not null)
    {
        <div class="alert alert-danger">@Model.MensajeError</div>
    }

    @if (Model.Rows.Count == 0)
    {
        <p class="text-muted">Todavía no hay nodos de menú (no hay plugins cargados).</p>
    }
    else
    {
        <form method="post" asp-antiforgery="true">
            <div class="document-list-table-wrapper">
                <table class="table document-list-table mb-0">
                    <thead>
                        <tr>
                            <th>Menú</th>
                            <th style="width:220px;">Nombre personalizado</th>
                            <th style="width:110px;">Orden</th>
                            <th style="width:70px;">Ocultar</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var row in Model.Rows)
                        {
                            <tr>
                                <td style="padding-left: @(row.Level * 20 + 8)px;">
                                    @row.Name <span class="text-muted" style="font-size:11px;">(@($"{row.OriginModule}.{row.Code}"))</span>
                                </td>
                                <td>
                                    <input type="text" class="form-control form-control-sm" name="Input.CustomLabel[@row.MenuId]" value="@row.CustomLabel" placeholder="@row.Name" />
                                </td>
                                <td>
                                    <input type="number" class="form-control form-control-sm" name="Input.CustomOrder[@row.MenuId]" value="@row.CustomOrder" />
                                </td>
                                <td class="text-center">
                                    <input type="checkbox" name="Input.HiddenMenuIds" value="@row.MenuId" checked="@row.IsHidden" />
                                </td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>

            <button type="submit" class="btn-primary mt-3">Guardar</button>
        </form>
    }
</div>
```

Guardar en `Portal SaaS - Core/plugins/Modulo.Administracion/Pages/Menus/Index.cshtml`.

- [ ] **Step 3: Compilar todo el proyecto**

Run: `dotnet build "Portal SaaS - Core/PortalSaas.sln"`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Correr toda la suite de tests**

Run: `dotnet test "Portal SaaS - Core/PortalSaas.sln"`
Expected: todos los tests en verde (sin tests nuevos en esta tarea — es una pantalla CRUD sin lógica de negocio propia, mismo criterio ya usado en el resto del proyecto para pantallas de administración equivalentes, ej. `Modulos/Index.cshtml`).

- [ ] **Step 5: Agregar el link "Menús" al punto de entrada del plugin, junto a "Módulos"**

En `Portal SaaS - Core/plugins/Modulo.Administracion/ModuloAdministracion.cs`, método
`GetMenu()`, agregar una línea nueva inmediatamente después de la entrada `"modulos"`
(línea 28 actual) y antes de `"grupos-menu"`:

```csharp
        yield return new MenuItemDefinition { Code = "menus", ParentCode = "raiz", Name = "Menús", Icon = "bi bi-list-nested", PageRoute = "/organizacion/menus", Order = 3 };
```

Y renumerar el `Order` de las dos entradas siguientes (`"grupos-menu"` pasa de `3` a
`4`, `"perfiles"` pasa de `4` a `5`), para que el orden visual en el sidebar sea
Usuarios → Módulos → Menús → Grupos de menú → Perfiles:

```csharp
        yield return new MenuItemDefinition { Code = "grupos-menu", ParentCode = "raiz", Name = "Grupos de menú", Icon = "bi bi-diagram-3", PageRoute = "/organizacion/grupos-menu", Order = 4 };
        yield return new MenuItemDefinition { Code = "perfiles", ParentCode = "raiz", Name = "Perfiles", Icon = "bi bi-person-badge", PageRoute = "/organizacion/perfiles", Order = 5 };
```

El árbol de `menus` se resincroniza solo al reiniciar el Host (`MenuSyncService`, ver
`CLAUDE.md`) — no hace falta ninguna migración ni script manual para que este nodo
nuevo aparezca.

- [ ] **Step 6: Verificación manual E2E (documentar en el reporte si no hay entorno disponible en esta sesión)**

Con el Host corriendo (`dotnet run --project "Portal SaaS - Core/src/PortalSaas.Host"`) y un usuario admin de organización real:
1. Entrar a `/organizacion/menus` — debe listar el árbol completo indentado por nivel.
2. Escribir un nombre personalizado en un nodo, guardar, recargar la página — el nombre debe persistir en el input y también reflejarse en el sidebar.
3. Marcar "Ocultar" en un nodo padre con hijos, guardar — el nodo y sus hijos deben desaparecer del sidebar (propio y de otros usuarios de la misma organización), sin afectar el sidebar de otra organización.
4. Limpiar todos los campos de una fila que tenía override y guardar — el nodo debe volver a su nombre/orden original.

- [ ] **Step 7: Commit**

```bash
git add "Portal SaaS - Core/plugins/Modulo.Administracion/Pages/Menus/" "Portal SaaS - Core/plugins/Modulo.Administracion/ModuloAdministracion.cs"
git commit -m "feat: agregar pantalla /organizacion/menus para personalizar el arbol de menu"
```

---

## Nota de alcance (no es una tarea, es contexto para quien ejecute este plan)

Crear carpetas/páginas manuales nuevas dentro del árbol de menú queda fuera de este
plan (alcance "completo" descartado explícitamente por el dueño del proyecto). El
punto 4 original ("crear usuarios") ya existía antes de este plan (`/organizacion/usuarios`).
