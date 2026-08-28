# Escalabilidad Horizontal (Web Farm) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminar los bloqueadores reales encontrados en `Portal SaaS - Core` para poder correr más de una instancia de `PortalSaas.Host` detrás de un balanceador (IIS ARR / web farm), sin perder progreso de importaciones, sin cuellos de botella de sesión SAP, y con una red de seguridad automática contra fuga de datos entre organizaciones.

**Architecture:** El modelo de datos ya es multi-tenant (`Organization → Company → User`, FK obligatoria en casi todas las tablas). Los cambios de este plan no tocan ese modelo — atacan puntualmente los hallazgos concretos de la auditoría: (1) `GenericImportProgressStore` singleton en memoria → store persistido en `PortalSaasDbContext` (misma base que ya usa todo el proyecto, sin infraestructura nueva); (2) ausencia de `HasQueryFilter` global de EF Core → filtros globales por `OrganizationId` como red de seguridad además del filtrado manual existente; (3) `SapSessionCache` con un único semáforo global → semáforo por compañía; (4) verificación explícita de que no queda ningún otro estado de proceso no compartido; (5) indexado real contra queries que ya se ejecutan hoy (sidebar, auditoría, usuarios activos); (6) estrategia de caching por instancia (`IMemoryCache` + invalidación activa, sin Redis — no hace falta con una sola instancia hoy) para catálogos de lectura frecuente/escritura rara; (7) la importación genérica pasa de bloquear el hilo de request HTTP a procesarse en background (`Channel<T>` + `BackgroundService`, mismo patrón ya usado por `LicenseActivatorBackgroundService`).

**Tech Stack:** .NET 8, EF Core (Postgres/SQL Server motor dual), xUnit + EF Core InMemory.

## Global Constraints

- Nunca agregar paquete de proveedor (`Npgsql`/`SqlServer`) a `src/PortalSaas.Data` — motor dual (ver `CLAUDE.md`, regla dura).
- Toda tabla nueva lleva `organization_id` (directo o vía `company_id`) desde el primer modelo.
- Cualquier cambio en `PortalSaasDbContext`/`Entities` exige regenerar migraciones para **los dos** motores (Postgres y SQL Server) y aplicarlas contra las bases de desarrollo reales, no solo generarlas — "generar la migración no prueba nada" (lección ya documentada en `CLAUDE.md`).
- Ningún módulo comercial se declara terminado sin tests — este plan agrega tests xUnit reales para cada pieza con lógica de negocio (no para CRUD puro).
- Comentarios/logs/texto de UI en español, mismo criterio que el resto del proyecto.
- No modificar `PortalSAP_v2` (fuera de alcance — es el sistema legado en producción, no se toca).

---

### Task 1: Store de progreso de importación persistido (elimina el singleton en memoria)

**Contexto:** `GenericImportProgressStore` (`src/PortalSaas.Core/ImportacionGenerica/GenericImportProgressStore.cs`) es un `Singleton` con `ConcurrentDictionary<string, GenericImportProgressDto>` en memoria de proceso, registrado en `Program.cs:188`. En un web farm de 2+ instancias sin sticky sessions, si el upload cae en la instancia A y la consulta de progreso cae en la instancia B, el progreso no existe ahí — bloquea escalar horizontalmente. La solución más simple dado el stack ya existente (sin agregar Redis, que no está en el proyecto hoy) es persistir el progreso en `PortalSaasDbContext`, la misma base que ya comparten todas las instancias.

**Files:**
- Modify: `src/PortalSaas.Data/Entities/` — Create: `src/PortalSaas.Data/Entities/GenericImportJobProgress.cs`
- Modify: `src/PortalSaas.Data/PortalSaasDbContext.cs`
- Modify: `src/PortalSaas.Core/ImportacionGenerica/GenericImportProgressStore.cs`
- Create: `src/PortalSaas.Data.Migrations.PostgreSql/Migrations/<timestamp>_AddGenericImportJobProgress.cs` (generada, no escrita a mano)
- Create: `src/PortalSaas.Data.Migrations.SqlServer/Migrations/<timestamp>_AddGenericImportJobProgress.cs` (generada, no escrita a mano)
- Test: `tests/PortalSaas.Core.Tests/ImportacionGenerica/GenericImportProgressStoreTests.cs`

**Interfaces:**
- Consumes: `PortalSaasDbContext` (ya existente), `IGenericImportProgressStore` (contrato ya existente en `PortalSaas.Abstractions.Contratos`, sin cambios en la firma — `Update(string jobId, GenericImportProgressDto progress)` / `GenericImportProgressDto? Get(string jobId)`).
- Produces: `GenericImportProgressStore` sigue siendo la única implementación registrada — los consumidores actuales (`GenericImportService`) no cambian ninguna llamada.

- [ ] **Step 1: Escribir el test que exige persistencia real (no en memoria de proceso)**

```csharp
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica;
using PortalSaas.Data;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica;

public class GenericImportProgressStoreTests
{
    private static PortalSaasDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new PortalSaasDbContext(options);
    }

    [Fact]
    public async Task Update_persiste_en_la_base_no_en_memoria_de_proceso()
    {
        var dbName = Guid.NewGuid().ToString();
        var progress = new GenericImportProgressDto
        {
            TotalRows = 100,
            ProcessedRows = 40,
            Status = "En progreso",
        };

        // Escribe con una instancia del store, sobre un DbContext propio --
        // simula una instancia de la app distinta a la que va a leer.
        await using (var writerContext = CreateContext(dbName))
        {
            var writerStore = new GenericImportProgressStore(writerContext);
            await writerStore.UpdateAsync("job-1", progress);
        }

        // Lee con OTRA instancia del store y OTRO DbContext contra la misma base --
        // si el progreso viviera solo en memoria de proceso (ConcurrentDictionary),
        // esto devolvería null.
        await using var readerContext = CreateContext(dbName);
        var readerStore = new GenericImportProgressStore(readerContext);
        var result = await readerStore.GetAsync("job-1");

        Assert.NotNull(result);
        Assert.Equal(100, result!.TotalRows);
        Assert.Equal(40, result.ProcessedRows);
        Assert.Equal("En progreso", result.Status);
    }

    [Fact]
    public async Task GetAsync_con_jobId_inexistente_devuelve_null()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var store = new GenericImportProgressStore(context);

        var result = await store.GetAsync("no-existe");

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_sobre_el_mismo_jobId_actualiza_en_vez_de_duplicar()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateContext(dbName);
        var store = new GenericImportProgressStore(context);

        await store.UpdateAsync("job-2", new GenericImportProgressDto { TotalRows = 10, ProcessedRows = 1, Status = "Inicio" });
        await store.UpdateAsync("job-2", new GenericImportProgressDto { TotalRows = 10, ProcessedRows = 10, Status = "Completado" });

        var result = await store.GetAsync("job-2");

        Assert.NotNull(result);
        Assert.Equal(10, result!.ProcessedRows);
        Assert.Equal("Completado", result.Status);
        Assert.Single(context.Set<Data.Entities.GenericImportJobProgress>());
    }
}
```

- [ ] **Step 2: Ejecutar el test para confirmar que falla (la clase todavía no compila así)**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter GenericImportProgressStoreTests`
Expected: FAIL (no compila — `GenericImportProgressStore` no tiene constructor con `PortalSaasDbContext`, ni métodos `UpdateAsync`/`GetAsync`, ni existe `GenericImportJobProgress`)

- [ ] **Step 3: Crear la entidad `GenericImportJobProgress`**

```csharp
namespace PortalSaas.Data.Entities;

/// <summary>
/// Progreso de un job de importación genérica -- reemplaza el ConcurrentDictionary
/// en memoria (rompía en web farm sin sticky sessions, ver docs/superpowers/plans).
/// Fila efímera: se sobrescribe en cada Update, no es historial.
/// </summary>
public sealed class GenericImportJobProgress
{
    public string JobId { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int ProcessedRows { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 4: Registrar la entidad en `PortalSaasDbContext`**

En `src/PortalSaas.Data/PortalSaasDbContext.cs`, agregar el `DbSet` junto a los demás (línea ~57, después de `UserHomeShortcuts`):

```csharp
    public DbSet<GenericImportJobProgress> GenericImportJobProgresses => Set<GenericImportJobProgress>();
```

Y el mapeo en `OnModelCreating` (después del bloque de `AuditLog`, línea ~490):

```csharp
        modelBuilder.Entity<GenericImportJobProgress>(entity =>
        {
            entity.ToTable("generic_import_job_progress");
            entity.HasKey(e => e.JobId);
            entity.Property(e => e.JobId).HasMaxLength(100);
            entity.Property(e => e.Status).HasMaxLength(50);
        });
```

- [ ] **Step 5: Reescribir `GenericImportProgressStore` contra `PortalSaasDbContext`**

```csharp
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>
/// Store del progreso de trabajos de IGenericImportService, persistido en la base
/// propia de la plataforma -- ANTES vivía en memoria de proceso (Singleton +
/// ConcurrentDictionary), lo que rompía apenas hubiera más de una instancia de
/// PortalSaas.Host detrás de un balanceador sin sticky sessions (el upload podía
/// caer en una instancia y la consulta de progreso en otra). Registrado como Scoped
/// (ver Program.cs), consistente con el ciclo de vida de PortalSaasDbContext.
/// </summary>
public sealed class GenericImportProgressStore(PortalSaasDbContext db) : IGenericImportProgressStore
{
    public async Task UpdateAsync(string jobId, GenericImportProgressDto progress)
    {
        var existing = await db.GenericImportJobProgresses.FindAsync(jobId);
        if (existing is null)
        {
            db.GenericImportJobProgresses.Add(new GenericImportJobProgress
            {
                JobId = jobId,
                TotalRows = progress.TotalRows,
                ProcessedRows = progress.ProcessedRows,
                Status = progress.Status,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.TotalRows = progress.TotalRows;
            existing.ProcessedRows = progress.ProcessedRows;
            existing.Status = progress.Status;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    public async Task<GenericImportProgressDto?> GetAsync(string jobId)
    {
        var entity = await db.GenericImportJobProgresses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.JobId == jobId);

        return entity is null
            ? null
            : new GenericImportProgressDto
            {
                TotalRows = entity.TotalRows,
                ProcessedRows = entity.ProcessedRows,
                Status = entity.Status,
            };
    }
}
```

- [ ] **Step 6: Actualizar el contrato `IGenericImportProgressStore` a async**

En `src/PortalSaas.Abstractions/Contratos/IGenericImportProgressStore.cs`, cambiar la firma síncrona por async (mismo patrón que el resto de `Abstractions`):

```csharp
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

public interface IGenericImportProgressStore
{
    Task UpdateAsync(string jobId, GenericImportProgressDto progress);
    Task<GenericImportProgressDto?> GetAsync(string jobId);
}
```

- [ ] **Step 7: Actualizar el único consumidor real (`GenericImportService`)**

En `src/PortalSaas.Core/ImportacionGenerica/GenericImportService.cs`, cambiar toda llamada `_progressStore.Update(...)`/`.Get(...)` a `await _progressStore.UpdateAsync(...)`/`await _progressStore.GetAsync(...)` (localizar con grep antes de tocar, ver Step de verificación abajo).

- [ ] **Step 8: Cambiar el registro de DI de Singleton a Scoped**

En `src/PortalSaas.Host/Program.cs:188`, reemplazar:

```csharp
builder.Services.AddSingleton<IGenericImportProgressStore, GenericImportProgressStore>();
```

por:

```csharp
// Scoped, no Singleton -- ahora depende de PortalSaasDbContext (también Scoped).
// El progreso ya no vive en memoria de proceso, ver GenericImportProgressStore.
builder.Services.AddScoped<IGenericImportProgressStore, GenericImportProgressStore>();
```

- [ ] **Step 9: Generar y aplicar las migraciones en los dos motores**

Run:
```bash
dotnet tool run dotnet-ef migrations add AddGenericImportJobProgress \
  --project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj \
  --startup-project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj \
  --context PortalSaasDbContext

dotnet tool run dotnet-ef migrations add AddGenericImportJobProgress \
  --project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj \
  --startup-project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj \
  --context PortalSaasDbContext

dotnet ef database update \
  --project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj \
  --startup-project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj
```

Expected: dos migraciones generadas (una por motor), la de Postgres aplicada con éxito contra la base de desarrollo (Docker). Aplicar también contra SQL Server local si está disponible en el entorno; si no, dejarlo documentado como pendiente explícito en el reporte de la tarea (mismo criterio que el resto del proyecto — no fingir que se verificó lo que no se verificó).

- [ ] **Step 10: Ejecutar el test completo y confirmar que pasa**

Run: `dotnet build PortalSaas.sln && dotnet test tests/PortalSaas.Core.Tests --filter GenericImportProgressStoreTests`
Expected: PASS, 0 advertencias/0 errores de build.

- [ ] **Step 11: Ejecutar la suite completa de tests para confirmar cero regresión**

Run: `dotnet test tests/PortalSaas.Core.Tests`
Expected: todos los tests existentes siguen en verde (109 + 3 nuevos = 112).

- [ ] **Step 12: Commit**

```bash
git add src/PortalSaas.Data/Entities/GenericImportJobProgress.cs \
        src/PortalSaas.Data/PortalSaasDbContext.cs \
        src/PortalSaas.Core/ImportacionGenerica/GenericImportProgressStore.cs \
        src/PortalSaas.Core/ImportacionGenerica/GenericImportService.cs \
        src/PortalSaas.Abstractions/Contratos/IGenericImportProgressStore.cs \
        src/PortalSaas.Host/Program.cs \
        src/PortalSaas.Data.Migrations.PostgreSql/Migrations/ \
        src/PortalSaas.Data.Migrations.SqlServer/Migrations/ \
        tests/PortalSaas.Core.Tests/ImportacionGenerica/GenericImportProgressStoreTests.cs
git commit -m "fix(escalabilidad): persistir progreso de importación en BD, no en memoria de proceso"
```

---

### Task 2: `HasQueryFilter` global por `OrganizationId` (red de seguridad automática multi-tenant)

**Contexto:** Hoy el aislamiento entre organizaciones depende 100% de que cada desarrollador agregue manualmente `.Where(x => x.OrganizationId == ...)` en cada query nueva (confirmado: cero `HasQueryFilter` en `PortalSaasDbContext.OnModelCreating`). Es el riesgo #1 de fuga de datos entre tenants. Este task agrega un filtro global de EF Core como red de seguridad, **sin quitar** el filtrado manual existente (defensa en profundidad, no reemplazo). Se aplica solo a las entidades con `OrganizationId` directo (no las que solo tienen `CompanyId`, que resuelven indirectamente — ver Global Constraints del `CLAUDE.md`: "todo plugin ... lo hace por CompanyId, NUNCA por OrganizationId directo").

**Alcance deliberadamente acotado:** `PortalSaasDbContext` no tiene hoy ningún mecanismo para saber "cuál es la organización actual" — eso vive en `ICurrentUserContext` (`PortalSaas.Core.Seguridad`), que es `PortalSaas.Core`, una capa por encima de `PortalSaas.Data` (que no puede referenciar `Core` sin invertir la dirección de dependencias, regla dura del `CLAUDE.md`). La solución: un `IOrganizationScopeProvider` chico definido en `PortalSaas.Data` (sin dependencia de HTTP/claims), implementado en `PortalSaas.Core.Seguridad` leyendo `ICurrentUserContext`, e inyectado al `DbContext`.

**Files:**
- Create: `src/PortalSaas.Data/IOrganizationScopeProvider.cs`
- Modify: `src/PortalSaas.Data/PortalSaasDbContext.cs`
- Create: `src/PortalSaas.Core/Seguridad/OrganizationScopeProvider.cs`
- Modify: `src/PortalSaas.Host/Program.cs`
- Test: `tests/PortalSaas.Core.Tests/Data/OrganizationQueryFilterTests.cs`

**Interfaces:**
- Consumes: nada nuevo de tasks anteriores.
- Produces: `IOrganizationScopeProvider.CurrentOrganizationId` (`Guid?`, `null` = sin filtro, usado por el backoffice de plataforma que opera sobre todas las organizaciones) — cualquier código futuro que necesite bypasear el filtro explícitamente usa `IgnoreQueryFilters()` de EF Core, nunca lo hace implícito.

- [ ] **Step 1: Escribir el test que exige el filtro global**

```csharp
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Data;

public class OrganizationQueryFilterTests
{
    private sealed class FixedOrganizationScopeProvider(Guid? organizationId) : IOrganizationScopeProvider
    {
        public Guid? CurrentOrganizationId { get; } = organizationId;
    }

    private static async Task<(Guid org1, Guid org2)> SeedTwoOrganizationsAsync(string dbName)
    {
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var seedContext = new PortalSaasDbContext(options, new FixedOrganizationScopeProvider(null));

        var org1 = new Organization { Id = Guid.NewGuid(), LegalName = "Org 1", Slug = "org-1" };
        var org2 = new Organization { Id = Guid.NewGuid(), LegalName = "Org 2", Slug = "org-2" };
        seedContext.Organizations.AddRange(org1, org2);

        seedContext.Profiles.Add(new Profile { Id = Guid.NewGuid(), Name = "Perfil Org 1", OrganizationId = org1.Id });
        seedContext.Profiles.Add(new Profile { Id = Guid.NewGuid(), Name = "Perfil Org 2", OrganizationId = org2.Id });
        await seedContext.SaveChangesAsync();

        return (org1.Id, org2.Id);
    }

    [Fact]
    public async Task Con_organizacion_actual_fijada_solo_ve_sus_propias_filas()
    {
        var dbName = Guid.NewGuid().ToString();
        var (org1, org2) = await SeedTwoOrganizationsAsync(dbName);

        var options = new DbContextOptionsBuilder<PortalSaasDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var scopedContext = new PortalSaasDbContext(options, new FixedOrganizationScopeProvider(org1));

        var profiles = await scopedContext.Profiles.ToListAsync();

        Assert.Single(profiles);
        Assert.Equal(org1, profiles[0].OrganizationId);
    }

    [Fact]
    public async Task Sin_organizacion_actual_ve_todas_las_filas_bypass_explicito_de_plataforma()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedTwoOrganizationsAsync(dbName);

        var options = new DbContextOptionsBuilder<PortalSaasDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var platformContext = new PortalSaasDbContext(options, new FixedOrganizationScopeProvider(null));

        var profiles = await platformContext.Profiles.ToListAsync();

        Assert.Equal(2, profiles.Count);
    }

    [Fact]
    public async Task IgnoreQueryFilters_permite_bypass_explicito_con_organizacion_fijada()
    {
        var dbName = Guid.NewGuid().ToString();
        var (org1, _) = await SeedTwoOrganizationsAsync(dbName);

        var options = new DbContextOptionsBuilder<PortalSaasDbContext>().UseInMemoryDatabase(dbName).Options;
        await using var scopedContext = new PortalSaasDbContext(options, new FixedOrganizationScopeProvider(org1));

        var allProfiles = await scopedContext.Profiles.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, allProfiles.Count);
    }
}
```

- [ ] **Step 2: Ejecutar el test para confirmar que falla**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter OrganizationQueryFilterTests`
Expected: FAIL (no compila — `PortalSaasDbContext` no tiene ese constructor, `IOrganizationScopeProvider` no existe)

- [ ] **Step 3: Crear `IOrganizationScopeProvider` en `PortalSaas.Data`**

```csharp
namespace PortalSaas.Data;

/// <summary>
/// Resuelve la organización actual para el filtro global de EF Core
/// (HasQueryFilter en PortalSaasDbContext) -- null significa "sin filtro",
/// usado por el backoffice de plataforma (/Admin/*, sin sesión de tenant).
/// Implementado en PortalSaas.Core (lee ICurrentUserContext) -- este contrato
/// vive en Data para no invertir la dirección de dependencias del proyecto
/// (Data nunca referencia Core).
/// </summary>
public interface IOrganizationScopeProvider
{
    Guid? CurrentOrganizationId { get; }
}
```

- [ ] **Step 4: Inyectar el provider en `PortalSaasDbContext` y agregar `HasQueryFilter`**

En `src/PortalSaas.Data/PortalSaasDbContext.cs`, cambiar el constructor:

```csharp
public sealed class PortalSaasDbContext : DbContext
{
    private readonly IOrganizationScopeProvider _scopeProvider;

    public PortalSaasDbContext(DbContextOptions<PortalSaasDbContext> options, IOrganizationScopeProvider scopeProvider)
        : base(options)
    {
        _scopeProvider = scopeProvider;
    }
```

Y agregar, al final de `OnModelCreating` (después del bloque de `AuditLog`, antes de la llave de cierre del método), el filtro global sobre **cada entidad con `OrganizationId` directo** — red de seguridad, no reemplazo del filtrado manual ya existente en `CurrentUserContext`/servicios:

```csharp
        // ---------------------------------------------------------------------------
        // Filtro global por organización -- red de seguridad automática, ADEMÁS del
        // filtrado manual que ya hace cada servicio (CurrentUserContext, etc.), no en
        // vez de. _scopeProvider.CurrentOrganizationId en null (backoffice de
        // plataforma, /Admin/*) deja pasar todo -- ver docs/superpowers/plans para el
        // razonamiento completo. Un bypass explícito puntual usa
        // .IgnoreQueryFilters(), nunca se desactiva esto a nivel de DbContext.
        // ---------------------------------------------------------------------------
        modelBuilder.Entity<Company>().HasQueryFilter(e => _scopeProvider.CurrentOrganizationId == null || e.OrganizationId == _scopeProvider.CurrentOrganizationId);
        modelBuilder.Entity<OrganizationDocumentPermission>().HasQueryFilter(e => _scopeProvider.CurrentOrganizationId == null || e.OrganizationId == _scopeProvider.CurrentOrganizationId);
        modelBuilder.Entity<Subscription>().HasQueryFilter(e => _scopeProvider.CurrentOrganizationId == null || e.OrganizationId == _scopeProvider.CurrentOrganizationId);
        modelBuilder.Entity<OnPremiseLicense>().HasQueryFilter(e => _scopeProvider.CurrentOrganizationId == null || e.OrganizationId == _scopeProvider.CurrentOrganizationId);
        modelBuilder.Entity<Instance>().HasQueryFilter(e => _scopeProvider.CurrentOrganizationId == null || e.OrganizationId == _scopeProvider.CurrentOrganizationId);
        modelBuilder.Entity<User>().HasQueryFilter(e => _scopeProvider.CurrentOrganizationId == null || e.OrganizationId == _scopeProvider.CurrentOrganizationId);
        modelBuilder.Entity<UserSession>().HasQueryFilter(e => _scopeProvider.CurrentOrganizationId == null || e.OrganizationId == _scopeProvider.CurrentOrganizationId);
        modelBuilder.Entity<EmailSettings>().HasQueryFilter(e => _scopeProvider.CurrentOrganizationId == null || e.OrganizationId == _scopeProvider.CurrentOrganizationId);
        modelBuilder.Entity<UsageMetric>().HasQueryFilter(e => _scopeProvider.CurrentOrganizationId == null || e.OrganizationId == _scopeProvider.CurrentOrganizationId);
        modelBuilder.Entity<MenuGroup>().HasQueryFilter(e => _scopeProvider.CurrentOrganizationId == null || e.OrganizationId == _scopeProvider.CurrentOrganizationId);
        modelBuilder.Entity<OrganizationModuleVisibility>().HasQueryFilter(e => _scopeProvider.CurrentOrganizationId == null || e.OrganizationId == _scopeProvider.CurrentOrganizationId);
        modelBuilder.Entity<Profile>().HasQueryFilter(e => _scopeProvider.CurrentOrganizationId == null || e.OrganizationId == _scopeProvider.CurrentOrganizationId);
```

**Nota para quien ejecute esta tarea:** `OrganizationModule`/`PasswordResetToken`/`Menu`/`ProfileAction`/`MenuGroupItem`/`UserMenuGroup`/`UserMenuProfile`/`UserHomeShortcut`/`AuditLog`/`UserPreference`/`GenericImportUserField`/`GenericImportConfig`/`ModuleExternalConnection` quedan **fuera** de este filtro directo a propósito: no tienen `OrganizationId` propio (llevan `CompanyId` o resuelven vía una relación de un solo salto — `OrganizationId` en esos casos requeriría un filtro con subquery, que EF Core soporta pero es un cambio de mayor riesgo/rendimiento; queda documentado como Task futura, no se improvisa acá). `Organization`/`Plan`/`PlatformModule`/`PlanModule`/`PlatformAdmin`/`PermissionAction`/`GenericImportConfigField`/`OnPremiseLicenseConflict` tampoco llevan filtro: son catálogos globales de plataforma o ya cuelgan de una entidad que si filtra (ej. `OnPremiseLicenseConflict` vía `OnPremiseLicense`, filtrar el padre ya lo protege en la práctica de las pantallas reales, aunque no a nivel de EF Core directo).

- [ ] **Step 5: Implementar `IOrganizationScopeProvider` en `PortalSaas.Core`**

```csharp
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;

namespace PortalSaas.Core.Seguridad;

/// <summary>
/// Lee ICurrentUserContext.OrganizationId para el filtro global de
/// PortalSaasDbContext -- ver docs/superpowers/plans. En páginas del backoffice de
/// plataforma (esquema de cookie "PlatformAdmin", sin claim OrganizationId) devuelve
/// null a propósito -- el admin de plataforma opera sobre todas las organizaciones.
/// </summary>
public sealed class OrganizationScopeProvider(ICurrentUserContext currentUserContext) : IOrganizationScopeProvider
{
    public Guid? CurrentOrganizationId
    {
        get
        {
            try
            {
                return currentUserContext.OrganizationId;
            }
            catch
            {
                // Sin claim OrganizationId (sesión de PlatformAdmin, o sin sesión
                // todavía) -- sin filtro, mismo criterio que null explícito.
                return null;
            }
        }
    }
}
```

- [ ] **Step 6: Registrar el provider en DI antes de `AddDbContext`**

En `src/PortalSaas.Host/Program.cs`, agregar antes de la línea 51 (`builder.Services.AddDbContext<PortalSaasDbContext>(...)`):

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<PortalSaas.Data.IOrganizationScopeProvider, PortalSaas.Core.Seguridad.OrganizationScopeProvider>();
```

**Nota:** verificar si `ICurrentUserContext` ya depende de `IHttpContextAccessor` (probable, dado que lee claims de la request actual) — si `AddHttpContextAccessor()` ya está registrado en otro punto de `Program.cs`, no duplicar la línea.

- [ ] **Step 7: Confirmar que todas las creaciones directas de `PortalSaasDbContext` fuera de DI (tests, seeders) pasan el nuevo parámetro**

Buscar con `grep -rn "new PortalSaasDbContext(" src tests` y actualizar cada sitio (ej. `PlatformAdminSeeder.cs` si crea el contexto a mano) para pasar una implementación fija (`null` de organización, como el backoffice) en vez de romper la compilación.

- [ ] **Step 8: Ejecutar el test y confirmar que pasa**

Run: `dotnet build PortalSaas.sln && dotnet test tests/PortalSaas.Core.Tests --filter OrganizationQueryFilterTests`
Expected: PASS, 0/0 en build.

- [ ] **Step 9: Ejecutar la suite completa — este es el paso de mayor riesgo de regresión de todo el plan**

Run: `dotnet test tests/PortalSaas.Core.Tests`
Expected: todos los tests en verde. **Si algo falla acá, es la señal más importante de todo este plan**: algún test existente construye datos de una organización y consulta con el contexto scopeado a otra (o sin scope) esperando verlos — hay que revisar caso por caso si el test estaba asumiendo sin querer un comportamiento cross-tenant que el filtro nuevo ahora bloquea correctamente (ajustar el test) o si el filtro está mal aplicado a una entidad que no debía (ajustar el filtro). No forzar el test a pasar sin entender cuál de los dos casos es.

- [ ] **Step 10: Verificar manualmente contra Postgres real que el backoffice de plataforma sigue viendo todas las organizaciones**

Levantar el Host (`dotnet run --project src/PortalSaas.Host`), loguear como `PlatformAdmin`, confirmar que `/Admin/Organizations/Index` sigue listando todas las organizaciones existentes (no solo una) — es la prueba de que `CurrentOrganizationId == null` en esa sesión sigue funcionando como bypass.

- [ ] **Step 11: Commit**

```bash
git add src/PortalSaas.Data/IOrganizationScopeProvider.cs \
        src/PortalSaas.Data/PortalSaasDbContext.cs \
        src/PortalSaas.Core/Seguridad/OrganizationScopeProvider.cs \
        src/PortalSaas.Host/Program.cs \
        tests/PortalSaas.Core.Tests/Data/OrganizationQueryFilterTests.cs
git commit -m "feat(seguridad): filtro global de EF Core por OrganizationId como red de seguridad multi-tenant"
```

---

### Task 3: `SapSessionCache` — semáforo por compañía en vez de global

**Contexto:** `SapSessionCache` (`src/PortalSaas.Core/Infraestructura/SapSessionCache.cs`) usa un único `SemaphoreSlim(1,1)` de instancia para TODAS las compañías. No es una race condition (el double-checked locking es correcto), pero serializa la creación de sesión SAP entre compañías distintas en un cache-miss simultáneo — cuello de botella real en el arranque en frío de un web farm nuevo con muchas organizaciones. Se cambia a un semáforo por `companyId`.

**Files:**
- Modify: `src/PortalSaas.Core/Infraestructura/SapSessionCache.cs`
- Test: `tests/PortalSaas.Core.Tests/Infraestructura/SapSessionCacheTests.cs`

**Interfaces:**
- Consumes: nada nuevo.
- Produces: `ISapSessionCache.GetOrCreateAsync(Guid companyId, Func<Task<SLConnection>> create)` — firma sin cambios, los consumidores actuales no se tocan.

- [ ] **Step 1: Escribir el test que exige creación en paralelo sin serializar entre compañías distintas**

```csharp
using PortalSaas.Core.Infraestructura;
using Xunit;

namespace PortalSaas.Core.Tests.Infraestructura;

public class SapSessionCacheTests
{
    [Fact]
    public async Task GetOrCreateAsync_dos_compañias_distintas_no_se_serializan_entre_si()
    {
        var cache = new SapSessionCache();
        var company1Started = new TaskCompletionSource();
        var company2Started = new TaskCompletionSource();
        var releaseGate = new TaskCompletionSource();

        // Simula dos creaciones "lentas" en paralelo, cada una espera a que la OTRA
        // haya arrancado antes de terminar -- si el lock fuera global (bug original),
        // esto hace deadlock porque la segunda nunca llega a arrancar mientras la
        // primera sigue dentro del semáforo compartido.
        var task1 = cache.GetOrCreateAsync(Guid.NewGuid(), async () =>
        {
            company1Started.SetResult();
            await company2Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return null!;
        });

        var task2 = cache.GetOrCreateAsync(Guid.NewGuid(), async () =>
        {
            company2Started.SetResult();
            await company1Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return null!;
        });

        var completed = await Task.WhenAll(task1, task2).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, completed.Length);
    }
}
```

- [ ] **Step 2: Ejecutar el test para confirmar que falla (deadlock/timeout con el lock global actual)**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter SapSessionCacheTests`
Expected: FAIL por timeout (`TaskCanceledException`/`TimeoutException`) — el semáforo global actual hace que la segunda creación espere a la primera, que a su vez espera a la segunda: deadlock real.

- [ ] **Step 3: Reescribir `SapSessionCache` con un semáforo por compañía**

```csharp
using System.Collections.Concurrent;
using B1SLayer;
using PortalSaas.Core.Sap;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Cachea una SLConnection por clave (usuario TÉCNICO/de integración de la compañía, no
/// depende del usuario web conectado) -- Singleton. B1SLayer maneja el relogin/refresh de
/// token internamente.
///
/// Semáforo POR COMPAÑÍA (no uno global) -- con un solo semáforo compartido, un
/// cache-miss simultáneo de dos compañías distintas serializaba su creación de sesión
/// sin necesidad (cuello de botella real en arranque en frío de un web farm con
/// muchas organizaciones, ver docs/superpowers/plans). Cada companyId tiene su propio
/// lock, así que compañías distintas nunca se bloquean entre sí.
/// </summary>
public interface ISapSessionCache
{
    Task<SLConnection> GetOrCreateAsync(Guid companyId, Func<Task<SLConnection>> create);
}

public sealed class SapSessionCache : ISapSessionCache
{
    private readonly ConcurrentDictionary<Guid, SLConnection> _sessions = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<SLConnection> GetOrCreateAsync(Guid companyId, Func<Task<SLConnection>> create)
    {
        if (_sessions.TryGetValue(companyId, out var existing))
        {
            return existing;
        }

        var companyLock = _locks.GetOrAdd(companyId, _ => new SemaphoreSlim(1, 1));
        await companyLock.WaitAsync();
        try
        {
            if (_sessions.TryGetValue(companyId, out existing))
            {
                return existing;
            }

            var connection = await create();
            _sessions[companyId] = connection;
            return connection;
        }
        finally
        {
            companyLock.Release();
        }
    }
}
```

- [ ] **Step 4: Ejecutar el test y confirmar que pasa**

Run: `dotnet build PortalSaas.sln && dotnet test tests/PortalSaas.Core.Tests --filter SapSessionCacheTests`
Expected: PASS, sin timeout.

- [ ] **Step 5: Ejecutar la suite completa**

Run: `dotnet test tests/PortalSaas.Core.Tests`
Expected: todos los tests en verde, sin regresión.

- [ ] **Step 6: Commit**

```bash
git add src/PortalSaas.Core/Infraestructura/SapSessionCache.cs \
        tests/PortalSaas.Core.Tests/Infraestructura/SapSessionCacheTests.cs
git commit -m "fix(rendimiento): SapSessionCache usa un semáforo por compañía, no uno global"
```

---

### Task 4: Verificación final — nada más queda en memoria de proceso sin compartir

**Contexto:** Cierre del plan — confirmar por lectura de código (no suposición) que después de las Tasks 1-3 no queda ningún otro estado mutable de proceso que rompa en web farm, y dejar documentado en `CLAUDE.md` los dos puntos que quedan fuera de alcance de este plan a propósito (uploads a disco local, sin caché distribuido) para que no se pierdan de vista.

**Files:**
- Modify: `CLAUDE.md` (agregar una entrada breve a la sección de decisiones, no reescribir nada existente)

**Interfaces:** ninguna — tarea de verificación y documentación, sin código nuevo.

- [ ] **Step 1: Grep de singletons con estado mutable**

Run: `grep -rn "AddSingleton" "src/PortalSaas.Host/Program.cs"`
Expected: confirmar que `SapSessionCache` sigue siendo `AddSingleton` (correcto — es un caché legítimo compartido en memoria, con TTL implícito de la sesión SAP, no un problema de web farm porque cada instancia simplemente mantiene su propia caché de conexiones SAP, sin datos de negocio de usuario), que `IGenericImportProgressStore` ya NO aparece como singleton (Task 1), y que no se agregó ningún `AddSingleton` nuevo con estado mutable de negocio en las Tasks anteriores.

- [ ] **Step 2: Grep de campos `static` mutables fuera de catálogos inmutables conocidos**

Run: `grep -rn "static.*Dictionary\|static.*List<\|static.*ConcurrentDictionary" src/PortalSaas.Core src/PortalSaas.Host --include=*.cs`
Expected: cero resultados nuevos más allá de los catálogos `static readonly` ya documentados en la auditoría original (tipos de documento, reglas fijas) — si aparece algo nuevo, es un hallazgo a documentar, no a ignorar.

- [ ] **Step 3: Documentar en `CLAUDE.md` los dos puntos fuera de alcance de este plan**

Agregar, en la sección de decisiones ya tomadas del `CLAUDE.md` (después del bloque de reglas duras, sin reescribir nada existente):

```markdown
## Pendiente de escalabilidad horizontal (fuera de alcance del plan de 2026-08-11)

Cerrado en `docs/superpowers/plans/2026-08-11-escalabilidad-horizontal.md`: progreso de
importación ahora persistido en BD (no memoria de proceso), filtro global de EF Core por
`OrganizationId`, `SapSessionCache` con semáforo por compañía. **Quedan 2 puntos reales,
documentados a propósito, no resueltos ahí**:

- **Uploads a disco local** (`Pages/Admin/PlatformModules/Import.cshtml.cs`, paquetes de
  plugin) -- en un web farm de N instancias, un plugin subido a una instancia no está
  disponible en las otras sin sincronización manual. Resolver cuando exista un segundo
  ambiente real con más de una instancia IIS -- hoy sigue siendo YAGNI (un solo servidor
  en producción real).
- **Sin caché distribuido** -- no hay `IMemoryCache` ni Redis en el proyecto; cada
  consulta de permisos/catálogos golpea la base directo. No es un bloqueador de
  escalabilidad horizontal en sí (cada instancia puede consultar la misma base
  compartida sin corromper datos), pero sí un techo de rendimiento bajo carga alta --
  evaluar solo si un perfil de carga real lo justifica, no antes.
```

- [ ] **Step 4: Confirmar build limpio y suite completa una última vez**

Run: `dotnet build PortalSaas.sln && dotnet test tests/PortalSaas.Core.Tests`
Expected: 0 advertencias/0 errores, todos los tests en verde (109 originales + 3 de Task 1 + 3 de Task 2 + 1 de Task 3 = 116).

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: documentar alcance y pendientes reales del plan de escalabilidad horizontal"
```

---

---

### Task 5: Auditoría de indexado + queries — cierra huecos reales encontrados

**Contexto:** `PortalSaasDbContext.OnModelCreating` ya define `HasIndex` para las búsquedas obvias (slugs, códigos únicos, `(OrganizationId, IsRevoked)` en `UserSession`, etc.) y EF Core crea automáticamente un índice sobre cada columna FK. Revisando las queries reales contra ese modelo (`CurrentUserContext.HasActionAsync`, `MenuNavigationService`, `AuditLog`) aparecen 3 huecos concretos, no hipotéticos:

1. **`Menu.IsActive`** — `MenuNavigationService`/`MenuSyncService` filtran el árbol completo por `IsActive = true` en cada carga de sidebar (cada request de cada usuario) sin índice sobre esa columna — hoy no duele porque `menus` es una tabla chica (nodos de plugin, cientos de filas), pero es un full scan que crece con cada plugin nuevo.
2. **`AuditLog`** — solo indexado por `CreatedAt` (`entity.HasIndex(e => e.CreatedAt)`, `PortalSaasDbContext.cs:484`). Cualquier pantalla de auditoría futura que filtre "eventos de esta compañía en este rango de fechas" (el caso de uso obvio de una tabla de auditoría multi-tenant) hace un scan completo de `audit_logs` filtrando por `CompanyId` en memoria. No existe hoy una pantalla que consuma esto, pero el índice debe ir con el modelo, no después de que la tabla crezca (regla dura del proyecto: "toda tabla de negocio nueva lleva `organization_id`... sin excepción" — este es el mismo espíritu aplicado a cómo se consulta, no solo a qué columna existe).
3. **`User(OrganizationId, IsActive)`** — el login de tenant (`Pages/Account/Login.cshtml.cs`) resuelve el usuario por `(OrganizationId, Username)` (ya indexado, único) pero cualquier listado futuro de "usuarios activos de la organización" (`TenantUserAdminService.ListAsync`, ya existe) filtra `IsActive` sin índice compuesto — hoy tampoco duele (organizaciones con pocos usuarios), pero es el mismo patrón de deuda que los dos anteriores: correcto agregarlo ahora que se está tocando este archivo, no esperar a que un cliente real tenga miles de usuarios.

**No se tocan** los índices ya existentes ni se agrega nada especulativo sin una query real que lo use — los 3 de arriba están anclados a código que ya ejecuta esa query hoy (`MenuNavigationService`, `TenantUserAdminService`), salvo `AuditLog`, que se agrega porque la tabla ya está en producción de datos (audit trail) y el costo de una migración después de que tenga millones de filas es mucho mayor que agregarla ahora.

**Files:**
- Modify: `src/PortalSaas.Data/PortalSaasDbContext.cs`
- Create: migraciones generadas en `src/PortalSaas.Data.Migrations.PostgreSql/Migrations/` y `src/PortalSaas.Data.Migrations.SqlServer/Migrations/`

**Interfaces:** ninguna — cambio de metadatos de EF Core, sin tocar ningún contrato ni servicio.

- [ ] **Step 1: Agregar los 3 índices en `PortalSaasDbContext.OnModelCreating`**

En el bloque de `Menu` (línea ~373):

```csharp
        modelBuilder.Entity<Menu>(entity =>
        {
            entity.ToTable("menus");
            entity.HasIndex(e => new { e.OriginModule, e.Code }).IsUnique();
            entity.HasIndex(e => e.PagePath);
            // Toda carga del sidebar filtra por IsActive (MenuNavigationService,
            // en cada request de cada usuario) -- sin índice hasta ahora porque la
            // tabla era chica, agregado antes de que crezca con más plugins.
            entity.HasIndex(e => e.IsActive);
            ...
```

En el bloque de `AuditLog` (línea ~481):

```csharp
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasIndex(e => e.CreatedAt);
            // Compuesto para "eventos de esta compañía en este rango" -- el caso de
            // uso obvio de una tabla de auditoría multi-tenant, sin pantalla
            // consumidora todavía pero agregado con el modelo, no después de que la
            // tabla tenga millones de filas.
            entity.HasIndex(e => new { e.CompanyId, e.CreatedAt });
            ...
```

En el bloque de `User` (línea ~264):

```csharp
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasIndex(e => new { e.OrganizationId, e.Username }).IsUnique();
            entity.HasIndex(e => new { e.OrganizationId, e.Email }).IsUnique();
            // TenantUserAdminService.ListAsync ya filtra IsActive dentro de la
            // organización -- sin índice compuesto hasta ahora.
            entity.HasIndex(e => new { e.OrganizationId, e.IsActive });
            ...
```

- [ ] **Step 2: Generar y aplicar las migraciones en los dos motores**

Run:
```bash
dotnet tool run dotnet-ef migrations add AddIndexingHuecosReales \
  --project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj \
  --startup-project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj \
  --context PortalSaasDbContext

dotnet tool run dotnet-ef migrations add AddIndexingHuecosReales \
  --project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj \
  --startup-project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj \
  --context PortalSaasDbContext

dotnet ef database update \
  --project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj \
  --startup-project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj
```

Expected: 3 índices nuevos aplicados contra Postgres de desarrollo (y SQL Server si el entorno lo permite, mismo criterio de honestidad que Task 1 — dejar documentado si no se pudo verificar contra ese motor).

- [ ] **Step 3: Confirmar build limpio y suite en verde**

Run: `dotnet build PortalSaas.sln && dotnet test tests/PortalSaas.Core.Tests`
Expected: 0/0, sin regresión (agregar un índice no cambia comportamiento, solo plan de ejecución).

- [ ] **Step 4: Commit**

```bash
git add src/PortalSaas.Data/PortalSaasDbContext.cs \
        src/PortalSaas.Data.Migrations.PostgreSql/Migrations/ \
        src/PortalSaas.Data.Migrations.SqlServer/Migrations/
git commit -m "perf(indexado): agrega índices para IsActive de menú, auditoría por compañía+fecha, usuarios activos por organización"
```

---

### Task 6: Estrategia de caching para toda la aplicación

**Contexto:** Hoy no hay ningún caché de aplicación (`grep -rn "IMemoryCache\|IDistributedCache"` sobre `src/` no devuelve nada, salvo `SapSessionCache`, que cachea conexiones, no datos de negocio). Cada carga de sidebar, cada chequeo de permiso (`CurrentUserContext.HasActionAsync`), cada resolución de módulos contratados (`ModuleAccessService`) y cada verificación de límite de plan (`ContractLimitService`) golpea la base en cada request. Ninguno de estos datos cambia por request — cambian solo cuando un admin edita un `Profile`/`MenuGroup`/`Plan`/`OrganizationModule` desde el backoffice, que es infrecuente.

**Diseño de la estrategia** (antes de tocar código — esto es lo que se implementa en los steps siguientes):

- **Nivel de caché: `IMemoryCache` por instancia, no distribuido.** El proyecto no tiene Redis hoy (documentado como fuera de alcance en `CLAUDE.md`, sección de pendientes agregada en Task 4) — agregar un caché distribuido sin necesidad real sería sobre-construir. `IMemoryCache` por instancia SÍ es seguro en web farm para este caso concreto porque:
  - Los datos cacheados son **de lectura frecuente y escritura rara** (catálogos de permisos/módulos, no datos transaccionales).
  - El TTL corto (2–5 min) acota la ventana de inconsistencia entre instancias a algo aceptable para este tipo de dato — un admin que cambia un permiso no espera que se propague instantáneo a todas las sesiones activas de todas las instancias.
  - La invalidación activa (Step 4) reduce esa ventana a "solo la instancia que no recibió la escritura", no "todas hasta que expire" — igual de importante que el TTL.
- **Abstracción `ICacheService`** en `PortalSaas.Abstractions` (no referenciar `IMemoryCache` directo desde `Core`) — el único motivo es no atarse a la implementación: si el día de mañana el proyecto agrega Redis (cuando haya más de una instancia real en producción, ver el pendiente ya documentado), se cambia la implementación registrada en DI sin tocar un solo servicio consumidor.
- **Candidatos reales a cachear, en orden de impacto** (todos con evidencia de uso en cada request, no especulativo):
  1. `IModuleAccessService.GetContractedModuleCodesAsync(organizationId)` — usado por `MenuNavigationService` en cada carga de sidebar, resultado idéntico hasta que cambie `Subscription`/`OrganizationModule`/`PlanModule` de esa organización.
  2. `MenuNavigationService` — el árbol de menú activo global (antes del filtro por usuario) cambia solo cuando se carga/descarga un plugin (`MenuSyncService`, evento raro, en el arranque). Cachear el árbol completo activo, filtrar por usuario en memoria (ya se hace así, sin cambios ahí).
  3. `IContractLimitService.GetActivePlanAsync(organizationId)` — resuelto en cada creación de usuario/documento con límite, cambia solo con una `Subscription`/`OnPremiseLicense` nueva.
  4. `CurrentUserContext.HasActionAsync` — el más ejecutado de todos (cada acción de cada página lo llama), pero **NO se cachea con TTL** en este plan — se resuelve con memoización **dentro del mismo request** (un `Dictionary` en el propio `CurrentUserContext`, con scope de request vía DI `Scoped`), no entre requests: los permisos de un usuario son sensibles a seguridad, y un TTL de minutos ahí es un riesgo distinto de nivel al de un catálogo de módulos — se deja fuera de alcance de `ICacheService` a propósito, ver Step 5.
- **Invalidación activa, no solo TTL:** cada pantalla de admin que escribe sobre una de las 3 entidades cacheadas (`Plans/Edit`, `Organizations/Modules`, `Organizations/Subscriptions`, `Organizations/Licenses`, y `MenuSyncService` al sincronizar) llama `ICacheService.Remove(key)`/`RemoveByPrefix` después de `SaveChangesAsync` — el TTL es la red de seguridad para el caso no cubierto explícitamente, no el mecanismo principal.

**Files:**
- Create: `src/PortalSaas.Abstractions/Contratos/ICacheService.cs`
- Create: `src/PortalSaas.Core/Infraestructura/MemoryCacheService.cs`
- Modify: `src/PortalSaas.Core/Comercial/ModuleAccessService.cs`
- Modify: `src/PortalSaas.Core/Comercial/ContractLimitService.cs`
- Modify: `src/PortalSaas.Core/Infraestructura/MenuNavigationService.cs`
- Modify: `src/PortalSaas.Core/Infraestructura/MenuSyncService.cs` (invalidación al sincronizar)
- Modify: `src/PortalSaas.Host/Pages/Admin/Plans/Edit.cshtml.cs`, `src/PortalSaas.Host/Pages/Admin/Organizations/Modules.cshtml.cs`, `Subscriptions/Create.cshtml.cs`, `Subscriptions/Edit.cshtml.cs`, `Licenses/Create.cshtml.cs`, `Licenses/Edit.cshtml.cs` (invalidación al escribir)
- Modify: `src/PortalSaas.Host/Program.cs`
- Test: `tests/PortalSaas.Core.Tests/Infraestructura/MemoryCacheServiceTests.cs`, `tests/PortalSaas.Core.Tests/Comercial/ModuleAccessServiceCachingTests.cs`

**Interfaces:**
- Produces: `ICacheService.GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl)` / `void Remove(string key)` / `void RemoveByPrefix(string prefix)`.
- Consumes (Task siguiente y futuros): cualquier servicio nuevo que necesite cachear un catálogo de lectura frecuente/escritura rara usa esta misma interfaz, nunca `IMemoryCache` directo.

- [ ] **Step 1: Escribir el test de `MemoryCacheService`**

```csharp
using Microsoft.Extensions.Caching.Memory;
using PortalSaas.Core.Infraestructura;
using Xunit;

namespace PortalSaas.Core.Tests.Infraestructura;

public class MemoryCacheServiceTests
{
    private static MemoryCacheService CreateService() => new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task GetOrCreateAsync_segunda_llamada_no_ejecuta_la_factory_de_nuevo()
    {
        var cache = CreateService();
        var calls = 0;

        async Task<int> Factory()
        {
            calls++;
            await Task.CompletedTask;
            return 42;
        }

        var first = await cache.GetOrCreateAsync("clave-1", Factory, TimeSpan.FromMinutes(5));
        var second = await cache.GetOrCreateAsync("clave-1", Factory, TimeSpan.FromMinutes(5));

        Assert.Equal(42, first);
        Assert.Equal(42, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Remove_invalida_la_clave_y_la_siguiente_llamada_reejecuta_la_factory()
    {
        var cache = CreateService();
        var calls = 0;

        async Task<int> Factory()
        {
            calls++;
            await Task.CompletedTask;
            return calls;
        }

        await cache.GetOrCreateAsync("clave-2", Factory, TimeSpan.FromMinutes(5));
        cache.Remove("clave-2");
        var afterRemove = await cache.GetOrCreateAsync("clave-2", Factory, TimeSpan.FromMinutes(5));

        Assert.Equal(2, calls);
        Assert.Equal(2, afterRemove);
    }

    [Fact]
    public async Task RemoveByPrefix_invalida_solo_las_claves_que_empiezan_con_ese_prefijo()
    {
        var cache = CreateService();

        await cache.GetOrCreateAsync("modulos:org-1", () => Task.FromResult(1), TimeSpan.FromMinutes(5));
        await cache.GetOrCreateAsync("modulos:org-2", () => Task.FromResult(2), TimeSpan.FromMinutes(5));
        await cache.GetOrCreateAsync("otro:dato", () => Task.FromResult(3), TimeSpan.FromMinutes(5));

        cache.RemoveByPrefix("modulos:");

        var calls = 0;
        var org1AfterRemove = await cache.GetOrCreateAsync("modulos:org-1", () => { calls++; return Task.FromResult(99); }, TimeSpan.FromMinutes(5));
        var otroAfterRemove = await cache.GetOrCreateAsync("otro:dato", () => { calls++; return Task.FromResult(99); }, TimeSpan.FromMinutes(5));

        Assert.Equal(99, org1AfterRemove); // se reejecutó, valor nuevo
        Assert.Equal(3, otroAfterRemove);  // no se tocó, sigue el valor cacheado
        Assert.Equal(1, calls);
    }
}
```

- [ ] **Step 2: Ejecutar el test para confirmar que falla**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter MemoryCacheServiceTests`
Expected: FAIL (no compila — `ICacheService`/`MemoryCacheService` no existen)

- [ ] **Step 3: Crear `ICacheService` y `MemoryCacheService`**

```csharp
namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Abstracción de caché por instancia -- hoy implementada sobre IMemoryCache
/// (MemoryCacheService), sin distribuido. Segura para web farm SOLO para datos de
/// lectura frecuente/escritura rara con invalidación activa desde el punto de
/// escritura (ver docs/superpowers/plans, Task 6) -- nunca usar para datos donde una
/// ventana de inconsistencia entre instancias sea inaceptable (ej. permisos de
/// seguridad por request, contadores exactos).
/// </summary>
public interface ICacheService
{
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl);
    void Remove(string key);
    void RemoveByPrefix(string prefix);
}
```

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Core.Infraestructura;

/// <summary>Ver ICacheService para el criterio de qué cachear acá y qué no.</summary>
public sealed class MemoryCacheService(IMemoryCache cache) : ICacheService
{
    // IMemoryCache no expone enumeración de claves -- se lleva un registro propio
    // para poder implementar RemoveByPrefix (ej. invalidar "modulos:*" de una
    // organización sin conocer cada clave exacta de antemano).
    private readonly ConcurrentDictionary<string, byte> _knownKeys = new();

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl)
    {
        if (cache.TryGetValue(key, out T? cached))
        {
            return cached!;
        }

        var value = await factory();
        cache.Set(key, value, ttl);
        _knownKeys.TryAdd(key, 0);
        return value;
    }

    public void Remove(string key)
    {
        cache.Remove(key);
        _knownKeys.TryRemove(key, out _);
    }

    public void RemoveByPrefix(string prefix)
    {
        foreach (var key in _knownKeys.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            Remove(key);
        }
    }
}
```

- [ ] **Step 4: Ejecutar el test y confirmar que pasa**

Run: `dotnet build PortalSaas.sln && dotnet test tests/PortalSaas.Core.Tests --filter MemoryCacheServiceTests`
Expected: PASS.

- [ ] **Step 5: Aplicar el caché al primer consumidor real — `ModuleAccessService`, con test de invalidación**

Leer `src/PortalSaas.Core/Comercial/ModuleAccessService.cs` completo antes de tocarlo (no asumir la firma exacta de `GetContractedModuleCodesAsync` sin confirmarla). Envolver el cuerpo del método en `_cache.GetOrCreateAsync($"modulos-contratados:{organizationId}", async () => { /* cuerpo actual */ }, TimeSpan.FromMinutes(3))`, inyectando `ICacheService` por constructor.

Test nuevo (`ModuleAccessServiceCachingTests.cs`) siguiendo el patrón EF Core InMemory ya usado por `ModuleAccessServiceTests` existente: escribir un `OrganizationModule` nuevo, confirmar que `GetContractedModuleCodesAsync` sigue devolviendo el resultado viejo (cacheado) hasta que se llame `cache.Remove($"modulos-contratados:{organizationId}")`, y que después de eso sí refleja el cambio.

- [ ] **Step 6: Invalidar el caché desde los 4 puntos de escritura reales**

En cada handler de escritura (`Plans/Edit.cshtml.cs` `OnPostAsync`, `Organizations/Modules.cshtml.cs` `OnPostAsync`, `Subscriptions/Create.cshtml.cs`/`Edit.cshtml.cs`, `Licenses/Create.cshtml.cs`/`Edit.cshtml.cs`), inyectar `ICacheService` y, inmediatamente después de `await _db.SaveChangesAsync()`, llamar `_cache.RemoveByPrefix($"modulos-contratados:{organizationId}")` (los de `Plans` invalidan para TODAS las organizaciones con ese plan — `RemoveByPrefix("modulos-contratados:")` sin sufijo, más caro pero correcto; los de `Organizations/*` invalidan solo su propia organización).

- [ ] **Step 7: Registrar en DI**

En `src/PortalSaas.Host/Program.cs`, antes del registro de `PluginManager`:

```csharp
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICacheService, MemoryCacheService>();
```

**Nota:** `Singleton`, no `Scoped` — el caché debe sobrevivir entre requests dentro de la misma instancia, es la razón de ser de este servicio.

- [ ] **Step 8: Ejecutar la suite completa**

Run: `dotnet test tests/PortalSaas.Core.Tests`
Expected: todos los tests en verde, sin regresión.

- [ ] **Step 9: Verificar manualmente contra Postgres real**

Levantar el Host, cambiar un módulo asignado a una organización desde `/Admin/Organizations/Modules`, confirmar en el sidebar de esa organización (misma instancia, sin reiniciar el Host) que el cambio se refleja de inmediato — confirma que la invalidación activa funciona, no solo el TTL.

- [ ] **Step 10: Commit**

```bash
git add src/PortalSaas.Abstractions/Contratos/ICacheService.cs \
        src/PortalSaas.Core/Infraestructura/MemoryCacheService.cs \
        src/PortalSaas.Core/Comercial/ModuleAccessService.cs \
        src/PortalSaas.Host/Pages/Admin/Plans/Edit.cshtml.cs \
        src/PortalSaas.Host/Pages/Admin/Organizations/Modules.cshtml.cs \
        src/PortalSaas.Host/Pages/Admin/Organizations/Subscriptions/ \
        src/PortalSaas.Host/Pages/Admin/Organizations/Licenses/ \
        src/PortalSaas.Host/Program.cs \
        tests/PortalSaas.Core.Tests/Infraestructura/MemoryCacheServiceTests.cs \
        tests/PortalSaas.Core.Tests/Comercial/ModuleAccessServiceCachingTests.cs
git commit -m "feat(caching): agrega ICacheService (IMemoryCache) con invalidación activa, primer consumidor ModuleAccessService"
```

**Pendiente explícito, no cerrado en este task:** aplicar el mismo patrón a `IContractLimitService.GetActivePlanAsync` y al árbol activo de `MenuNavigationService` (candidatos #3 y #2 del diseño de arriba) — se deja como extensión directa del mismo mecanismo ya construido y probado acá, para no inflar este task con repeticiones del mismo patrón; el trabajo real (la abstracción + el primer caso completo con invalidación) ya queda hecho y verificado.

---

### Task 7: Procesamiento asíncrono en segundo plano para la importación genérica

**Contexto:** `IndexModel.OnPostConfirmAsync` (`plugins/Modulo.ImportacionGenerica/Pages/Importar/Index.cshtml.cs:120`) llama `await _importService.CreateDocumentsAsync(...)` **directo dentro del request HTTP** — con archivos de miles de filas (`DefaultBatchSize = 400`, varios lotes, cada uno un POST a Service Layer que puede tardar segundos), esto mantiene el hilo de request de IIS bloqueado por minutos. El cliente ya hace polling de progreso contra `OnGetProgressAsync` (`IGenericImportProgressStore`, ya persistido en BD desde Task 1) en **otro** request paralelo — la pieza que falta es que `OnPostConfirmAsync` no necesite esperar a que termine todo el proceso para devolver una respuesta: debe encolar el trabajo y devolver de inmediato, dejando que el polling ya existente siga funcionando igual.

El proyecto ya tiene el patrón exacto necesario: `LicenseActivatorBackgroundService` (`src/PortalSaas.Core/Comercial/Licenciamiento/LicenseActivatorBackgroundService.cs`) es un `BackgroundService` que usa `IServiceScopeFactory` para crear un scope propio (necesario porque `PortalSaasDbContext`/los servicios de negocio son `Scoped`, no pueden inyectarse directo en un `Singleton`/`BackgroundService`). Este task reusa ese mismo patrón para una **cola de trabajos de importación**, no un heartbeat periódico.

**Files:**
- Create: `src/PortalSaas.Abstractions/Contratos/IGenericImportJobQueue.cs`
- Create: `src/PortalSaas.Core/ImportacionGenerica/GenericImportJobQueue.cs`
- Create: `src/PortalSaas.Core/ImportacionGenerica/GenericImportBackgroundService.cs`
- Modify: `plugins/Modulo.ImportacionGenerica/Pages/Importar/Index.cshtml.cs`
- Modify: `src/PortalSaas.Host/Program.cs`
- Test: `tests/PortalSaas.Core.Tests/ImportacionGenerica/GenericImportJobQueueTests.cs`

**Interfaces:**
- Produces: `IGenericImportJobQueue.Enqueue(GenericImportJobRequest request)` (no async — solo encola, `Channel<T>` en memoria) / `Task<GenericImportJobRequest> DequeueAsync(CancellationToken ct)` (consumido únicamente por `GenericImportBackgroundService`).
- Consumes: `IGenericImportService.CreateDocumentsAsync` (ya existente, sin cambios de firma — Task 7 cambia QUIÉN lo llama y CUÁNDO, no la lógica interna de creación de documentos, que ya es correcta y ya tiene su propio reporte de progreso por lote).

**Límite reconocido de este diseño, documentado por transparencia:** la cola es un `Channel<T>` **en memoria del proceso**, igual de no-compartida entre instancias que el problema original de `GenericImportProgressStore` (Task 1) — si la instancia que encoló el trabajo se recicla (deploy, reciclaje de IIS) antes de que el `BackgroundService` termine de procesarlo, el trabajo se pierde sin reintento. Es una mejora real (libera el hilo de request, que es el problema inmediato) pero **no** es "colas durables". Resolverlo de verdad requeriría una tabla de jobs pendientes en la base (mismo patrón de Task 1) consumida por un `BackgroundService` con polling — se deja documentado como paso 2 natural, no se construye en este task para no inflarlo; el pendiente queda anotado en `CLAUDE.md` en el Step final.

- [ ] **Step 1: Escribir el test de la cola**

```csharp
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica;

public class GenericImportJobQueueTests
{
    [Fact]
    public async Task Enqueue_seguido_de_DequeueAsync_devuelve_el_mismo_trabajo()
    {
        var queue = new GenericImportJobQueue();
        var request = new GenericImportJobRequest("job-1", "usuario_prueba", new GenericImportParametersDto(), []);

        queue.Enqueue(request);
        var dequeued = await queue.DequeueAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("job-1", dequeued.JobId);
        Assert.Equal("usuario_prueba", dequeued.PortalUsername);
    }

    [Fact]
    public async Task DequeueAsync_respeta_el_orden_FIFO_de_varios_trabajos_encolados()
    {
        var queue = new GenericImportJobQueue();
        queue.Enqueue(new GenericImportJobRequest("job-a", "u", new GenericImportParametersDto(), []));
        queue.Enqueue(new GenericImportJobRequest("job-b", "u", new GenericImportParametersDto(), []));

        var first = await queue.DequeueAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        var second = await queue.DequeueAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("job-a", first.JobId);
        Assert.Equal("job-b", second.JobId);
    }
}
```

- [ ] **Step 2: Ejecutar el test para confirmar que falla**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter GenericImportJobQueueTests`
Expected: FAIL (no compila — `GenericImportJobQueue`/`GenericImportJobRequest` no existen todavía)

- [ ] **Step 3: Definir `GenericImportJobRequest` y `IGenericImportJobQueue`**

Confirmar primero, leyendo `plugins/Modulo.ImportacionGenerica/Pages/Importar/Index.cshtml.cs` completo, la firma exacta de `CreateDocumentsAsync` y qué datos ya tiene disponibles `OnPostConfirmAsync` en ese punto (`jobId`, `CurrentUser.Username`, `parameters`, `preview.Documents`) — el DTO de abajo debe llevar exactamente eso, sin inventar campos:

```csharp
namespace PortalSaas.Abstractions.Modelos;

/// <summary>Trabajo encolado para GenericImportBackgroundService -- mismos parámetros que ya recibía CreateDocumentsAsync cuando se llamaba directo desde el request HTTP.</summary>
public sealed record GenericImportJobRequest(
    string JobId,
    string PortalUsername,
    GenericImportParametersDto Parameters,
    IReadOnlyList<GenericImportDocumentDto> Documents);
```

```csharp
namespace PortalSaas.Abstractions.Contratos;

using PortalSaas.Abstractions.Modelos;

/// <summary>
/// Cola en memoria de proceso para desacoplar la creación de documentos de
/// importación del hilo de request HTTP -- ver docs/superpowers/plans Task 7 para el
/// límite reconocido (no durable entre reciclajes de proceso).
/// </summary>
public interface IGenericImportJobQueue
{
    void Enqueue(GenericImportJobRequest request);
    ValueTask<GenericImportJobRequest> DequeueAsync(CancellationToken ct);
}
```

- [ ] **Step 4: Implementar `GenericImportJobQueue` sobre `System.Threading.Channels`**

```csharp
using System.Threading.Channels;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>Ver IGenericImportJobQueue. Singleton -- un solo canal compartido por toda la instancia del proceso.</summary>
public sealed class GenericImportJobQueue : IGenericImportJobQueue
{
    private readonly Channel<GenericImportJobRequest> _channel = Channel.CreateUnbounded<GenericImportJobRequest>();

    public void Enqueue(GenericImportJobRequest request) => _channel.Writer.TryWrite(request);

    public ValueTask<GenericImportJobRequest> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);
}
```

- [ ] **Step 5: Ejecutar el test y confirmar que pasa**

Run: `dotnet build PortalSaas.sln && dotnet test tests/PortalSaas.Core.Tests --filter GenericImportJobQueueTests`
Expected: PASS.

- [ ] **Step 6: Implementar `GenericImportBackgroundService`, mismo patrón que `LicenseActivatorBackgroundService`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>
/// Consume IGenericImportJobQueue y llama IGenericImportService.CreateDocumentsAsync
/// fuera del hilo de request HTTP -- antes OnPostConfirmAsync (Pages/Importar/Index)
/// esperaba el proceso completo dentro del propio request, bloqueando el hilo de IIS
/// por minutos con archivos grandes. El progreso lo sigue reportando
/// CreateDocumentsAsync exactamente igual que antes (IGenericImportProgressStore,
/// consultado por polling desde el cliente) -- este servicio no cambia esa lógica,
/// solo QUIÉN y CUÁNDO la llama. Mismo patrón de scope que
/// LicenseActivatorBackgroundService (IServiceScopeFactory, porque los servicios de
/// negocio son Scoped y este es un Singleton de larga vida).
/// </summary>
public sealed class GenericImportBackgroundService(
    IGenericImportJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<GenericImportBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await queue.DequeueAsync(stoppingToken);

            using var scope = scopeFactory.CreateScope();
            var importService = scope.ServiceProvider.GetRequiredService<IGenericImportService>();

            try
            {
                await importService.CreateDocumentsAsync(job.JobId, job.PortalUsername, job.Parameters, job.Documents, stoppingToken);
            }
            catch (Exception ex)
            {
                // CreateDocumentsAsync ya reporta error por documento en el progreso
                // (ver GenericImportService) -- este catch es solo para un fallo
                // catastrófico fuera de ese try interno (ej. el propio DbContext sin
                // poder abrir conexión), para que UN trabajo roto no tumbe el
                // BackgroundService completo y deje de procesar los siguientes.
                logger.LogError(ex, "Fallo no controlado procesando el trabajo de importación {JobId}.", job.JobId);
            }
        }
    }
}
```

- [ ] **Step 7: Actualizar `OnPostConfirmAsync` para encolar en vez de esperar**

Leer `plugins/Modulo.ImportacionGenerica/Pages/Importar/Index.cshtml.cs` completo antes de tocar — el cambio real es reemplazar la línea 144 (`var result = await _importService.CreateDocumentsAsync(jobId, CurrentUser.Username, parameters, preview.Documents, ct);` seguida de lo que haga con `result`) por:

```csharp
        _jobQueue.Enqueue(new GenericImportJobRequest(jobId, CurrentUser.Username, parameters, preview.Documents));

        // Progreso inicial visible de inmediato -- antes de este cambio, el primer
        // progreso lo escribía CreateDocumentsAsync ya corriendo dentro del propio
        // request; ahora el trabajo recién se encoló, así que el propio handler dejo
        // un estado "en cola" para que el polling (OnGetProgressAsync) no encuentre
        // un jobId sin ninguna fila todavía durante la primera consulta.
        await _progress.UpdateAsync(jobId, new GenericImportProgressDto("En cola, esperando procesamiento...", 0, 0, true, null, false));

        return new JsonResult(new { jobId, queued = true });
```

Agregar `IGenericImportJobQueue _jobQueue` al constructor de `IndexModel` (mismo patrón de inyección que `_importService`/`_progress` ya existentes). **Confirmar contra el `.cshtml`** si el cliente ya maneja una respuesta `{ jobId, queued: true }` en vez del resultado final directo — si el JS del lado cliente (`Pages/Importar/Index.cshtml`, buscar el `fetch`/`$.post` a `OnPostConfirmAsync`) esperaba el resultado final en esa misma respuesta, ajustarlo para que, al recibir `queued: true`, simplemente empiece el polling ya existente contra `OnGetProgressAsync` de inmediato (que ya es el flujo real para el progreso intermedio — el único cambio de comportamiento visible es que el estado "en cola" aparece brevemente antes de "Iniciando creación de N documento(s)...").

- [ ] **Step 8: Registrar el queue (Singleton) y el `BackgroundService` en DI**

En `src/PortalSaas.Host/Program.cs`, junto al resto de registros de `PortalSaas.Core`:

```csharp
builder.Services.AddSingleton<IGenericImportJobQueue, GenericImportJobQueue>();
builder.Services.AddHostedService<GenericImportBackgroundService>();
```

- [ ] **Step 9: Ejecutar la suite completa**

Run: `dotnet test tests/PortalSaas.Core.Tests`
Expected: todos los tests en verde.

- [ ] **Step 10: Verificar manualmente contra un ambiente demo real (SQL Server o HANA)**

Levantar el Host, subir un archivo de importación real con varias decenas de filas, confirmar en el navegador que `OnPostConfirmAsync` devuelve de inmediato (no queda "colgado" el botón de confirmar), que el polling muestra "En cola..." brevemente y después el progreso normal por lote, y que los documentos terminan creados en SAP igual que antes del cambio (mismo resultado final, distinta forma de llegar ahí).

- [ ] **Step 11: Documentar el límite reconocido en `CLAUDE.md`**

Agregar a la sección "Pendiente de escalabilidad horizontal" ya creada en Task 4:

```markdown
- **Cola de importación en memoria de proceso** (`GenericImportJobQueue`, `Channel<T>`)
  -- libera el hilo de request HTTP (antes bloqueado minutos con archivos grandes),
  pero no es durable: un trabajo encolado se pierde si la instancia se recicla antes
  de procesarlo. Resolverlo de verdad requiere una tabla de jobs pendientes en BD
  (mismo patrón que `GenericImportJobProgress`) con polling desde el
  `BackgroundService` -- no construido todavía, evaluar cuando el volumen de
  importaciones reales lo justifique.
```

- [ ] **Step 12: Commit**

```bash
git add src/PortalSaas.Abstractions/Modelos/GenericImportJobRequest.cs \
        src/PortalSaas.Abstractions/Contratos/IGenericImportJobQueue.cs \
        src/PortalSaas.Core/ImportacionGenerica/GenericImportJobQueue.cs \
        src/PortalSaas.Core/ImportacionGenerica/GenericImportBackgroundService.cs \
        plugins/Modulo.ImportacionGenerica/Pages/Importar/Index.cshtml.cs \
        plugins/Modulo.ImportacionGenerica/Pages/Importar/Index.cshtml \
        src/PortalSaas.Host/Program.cs \
        CLAUDE.md \
        tests/PortalSaas.Core.Tests/ImportacionGenerica/GenericImportJobQueueTests.cs
git commit -m "perf(async): procesa la importación genérica en background, libera el hilo de request HTTP"
```

---

## Nota sobre `PortalSAP_v2` (fuera de alcance)

Este plan cubre exclusivamente `Portal SaaS - Core`. El sistema legado `PortalSAP_v2`
(producción real, Comercial Depor) es single-tenant por diseño de base de datos (vive
dentro del HANA de un único cliente) — no es un problema de "escalabilidad horizontal
del código", es una limitación estructural de dónde vive su base de datos, y **no se
resuelve con cambios de código en ese repo**: la vía de solución ya es este proyecto
paralelo. No se propone ningún cambio sobre `PortalSAP_v2` en este plan.
