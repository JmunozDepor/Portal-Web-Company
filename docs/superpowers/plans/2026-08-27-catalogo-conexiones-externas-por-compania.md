# Catálogo general de conexiones externas por compañía — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reemplazar `module_external_connections` por un catálogo de conexiones externas por compañía (`company_external_connections`) reutilizable, con un binding `(compañía, módulo, propósito) → conexión`, migrando los datos actuales y exponiendo su gestión tanto en `/Admin` como en el self-service `/organizacion/conexiones-externas`.

**Architecture:** La lógica vive en `CompanyExternalConnectionService` (`PortalSaas.Core`) detrás del contrato `ICompanyExternalConnectionService` (`PortalSaas.Abstractions`); recibe `organizationId`/`companyId` explícitos. `/Admin` los pasa por ruta; el plugin `Modulo.Administracion` los resuelve de `ICurrentUserContext.OrganizationId` + un selector de compañía. `IExternalDatabaseConnectionService` conserva su firma pública y pasa a resolver contra el binding + catálogo. Un servicio de backfill idempotente migra los datos legados.

**Tech Stack:** .NET 8, EF Core 8 (proveedor dual Postgres/SqlServer, `UseSnakeCaseNamingConvention`), Razor Pages, xUnit + `Microsoft.EntityFrameworkCore.InMemory`, AES-256-GCM (`ISecretoCifradoService`), `Npgsql` / `Microsoft.Data.SqlClient` / `Sap.Data.Hana`.

## Global Constraints

- El plugin `Modulo.Administracion` **solo** puede referenciar `PortalSaas.Abstractions` (nunca `PortalSaas.Core` / `PortalSaas.Data` / `PortalSaas.Host`).
- Toda migración nueva se genera en **ambos** proyectos: `src/PortalSaas.Data.Migrations.PostgreSql` y `src/PortalSaas.Data.Migrations.SqlServer`, con `--context PortalSaasDbContext`. Aplicar contra las 4 bases reales de `172.16.122.171` tras validar en local; preguntar si alguna queda pendiente.
- PK `long` (bigint identity) para las tablas nuevas, igual que `module_external_connections`. Los "tipos" se modelan como clase estática de constantes `string` + check constraint, no `enum` C#.
- **Las tablas nuevas (`company_external_connections`, `company_module_connection`) NO llevan columna `OrganizationId`** — regla dura del repo (CLAUDE.md 2026-08-08): `Company.OrganizationId` ya la resuelve, y `module_external_connections` la eliminó a propósito en la migración `EnforceCompanyScopeOnExternalConnections`. Donde CUALQUIER snippet de este plan (Tasks 1/3/4/6/7) asigne o lea `OrganizationId` en esas dos entidades, **omitirlo**: el scoping por organización se valida/deriva por join a `companies` (`c.OrganizationId == organizationId` en el servicio; `b.Company.OrganizationId` en el resolver). Los métodos del servicio siguen recibiendo `organizationId` como parámetro (para validar pertenencia de la compañía), pero no se persiste en fila.
- Secretos: siempre write-only. Ningún DTO de lectura expone `TechnicalSecretKey`. En edición, secreto en blanco = conservar el existente. Cifrado vía `ISecretoCifradoService.Encrypt` / `.Decrypt`.
- `IExternalDatabaseConnectionService.ResolveConnectionAsync(string moduleCode, Guid companyId, CancellationToken)` **no cambia de firma**. Se agrega un overload con `string purpose`.
- Tests: DbContext `UseInMemoryDatabase(Guid.NewGuid().ToString())`. El provider InMemory NO valida check constraints ni índices únicos: las pruebas de unicidad verifican la validación explícita del servicio, no una excepción de EF.
- Cada tarea termina compilando (`dotnet build`) y con la suite verde (`dotnet test`). Baseline actual: 183 passed / 0 failed.
- Mensajes de error de negocio: `InvalidOperationException` con texto presentable (patrón `OrganizationProfileService`).

---

### Task 1: Constantes de tipo + entidades EF + configuración en `PortalSaasDbContext`

**Files:**
- Create: `src/PortalSaas.Abstractions/Modelos/ExternalConnectionType.cs`
- Modify: `src/PortalSaas.Abstractions/Modelos/ExternalDatabaseConnection.cs` (añadir `Hana`)
- Create: `src/PortalSaas.Data/Entities/CompanyExternalConnection.cs`
- Create: `src/PortalSaas.Data/Entities/CompanyModuleConnection.cs`
- Modify: `src/PortalSaas.Data/PortalSaasDbContext.cs` (DbSets + `OnModelCreating`)
- Test: `tests/PortalSaas.Core.Tests/Administracion/CompanyExternalConnectionEntitiesTests.cs`

**Interfaces:**
- Produces:
  - `ExternalConnectionType.DbPostgres|DbSqlServer|DbHana|HttpApi` (`const string`), `ExternalConnectionType.All`.
  - `ExternalDatabaseEngineType.Hana = "hana"` (`const string`).
  - Entities `CompanyExternalConnection` (PK `long Id`, props: `Guid CompanyId`, `Company Company`, `string Nombre`, `string Tipo`, `string? Host`, `string? BaseUrl`, `int? Port`, `string? DatabaseName`, `string? TechnicalUsername`, `string? TechnicalSecretKey`, `string? ConfiguracionExtra`, `bool IsActive`, `DateTimeOffset CreatedAt`, `DateTimeOffset UpdatedAt`, `ICollection<CompanyModuleConnection> ModuleBindings`). **No `OrganizationId`.**
  - `CompanyModuleConnection` (PK `long Id`, props: `Guid CompanyId`, `Company Company`, `string ModuleCode`, `string Purpose` (default `"Default"`), `long ConnectionId`, `CompanyExternalConnection Connection`, `DateTimeOffset UpdatedAt`). **No `OrganizationId`.**
  - `PortalSaasDbContext.CompanyExternalConnections`, `.CompanyModuleConnections` (`DbSet<>`).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/PortalSaas.Core.Tests/Administracion/CompanyExternalConnectionEntitiesTests.cs
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Administracion;

public sealed class CompanyExternalConnectionEntitiesTests
{
    private static PortalSaasDbContext NuevoContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task PuedePersistirYLeerConexionConBinding()
    {
        var companyId = Guid.NewGuid();

        await using (var db = NuevoContexto())
        {
            var conn = new CompanyExternalConnection
            {
                CompanyId = companyId,
                Nombre = "BD WMS",
                Tipo = ExternalConnectionType.DbSqlServer,
                Host = "sqlsap.cdepor.cl",
                Port = 11433,
                DatabaseName = "PS_COMDEPOR_WMS_DEV",
                TechnicalUsername = "svc_wms",
                TechnicalSecretKey = "cipher",
            };
            db.CompanyExternalConnections.Add(conn);
            await db.SaveChangesAsync();

            db.CompanyModuleConnections.Add(new CompanyModuleConnection
            {
                CompanyId = companyId,
                ModuleCode = "Wms",
                Purpose = "Default",
                ConnectionId = conn.Id,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = NuevoContexto()) { /* contexto distinto: InMemory por nombre único, sólo valida el modelo compila+mapea */ }

        Assert.Contains(ExternalConnectionType.DbHana, ExternalConnectionType.All);
        Assert.Equal("hana", ExternalDatabaseEngineType.Hana);
    }
}
```

- [ ] **Step 2: Run test, verify it fails**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter CompanyExternalConnectionEntitiesTests`
Expected: FAIL — `ExternalConnectionType` / `CompanyExternalConnection` no existen (errores de compilación).

- [ ] **Step 3: Create `ExternalConnectionType`**

```csharp
// src/PortalSaas.Abstractions/Modelos/ExternalConnectionType.cs
namespace PortalSaas.Abstractions.Modelos;

/// <summary>Tipo de una conexión externa del catálogo por compañía.</summary>
public static class ExternalConnectionType
{
    public const string DbPostgres = "db_postgres";
    public const string DbSqlServer = "db_sqlserver";
    public const string DbHana = "db_hana";
    public const string HttpApi = "http_api";

    public static readonly IReadOnlyCollection<string> All = [DbPostgres, DbSqlServer, DbHana, HttpApi];

    public static bool IsDatabase(string tipo) => tipo is DbPostgres or DbSqlServer or DbHana;
}
```

- [ ] **Step 4: Añadir `Hana` a `ExternalDatabaseEngineType`**

En `src/PortalSaas.Abstractions/Modelos/ExternalDatabaseConnection.cs`, dentro de `public static class ExternalDatabaseEngineType`, añadir bajo `SqlServer`:

```csharp
    public const string Hana = "hana";
```

- [ ] **Step 5: Crear entidad `CompanyExternalConnection`**

```csharp
// src/PortalSaas.Data/Entities/CompanyExternalConnection.cs
namespace PortalSaas.Data.Entities;

public sealed class CompanyExternalConnection
{
    public long Id { get; set; }

    // Sin OrganizationId: se deriva de Company.OrganizationId (regla dura CLAUDE.md 2026-08-08).
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Único por compañía.</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>ExternalConnectionType.* ("db_postgres" | "db_sqlserver" | "db_hana" | "http_api").</summary>
    public string Tipo { get; set; } = null!;

    public string? Host { get; set; }
    public string? BaseUrl { get; set; }
    public int? Port { get; set; }
    public string? DatabaseName { get; set; }
    public string? TechnicalUsername { get; set; }

    /// <summary>Cifrado con ISecretoCifradoService. Write-only.</summary>
    public string? TechnicalSecretKey { get; set; }

    /// <summary>JSON con parámetros específicos del tipo/módulo.</summary>
    public string? ConfiguracionExtra { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<CompanyModuleConnection> ModuleBindings { get; set; } = new List<CompanyModuleConnection>();
}
```

- [ ] **Step 6: Crear entidad `CompanyModuleConnection`**

```csharp
// src/PortalSaas.Data/Entities/CompanyModuleConnection.cs
namespace PortalSaas.Data.Entities;

public sealed class CompanyModuleConnection
{
    public long Id { get; set; }

    // Sin OrganizationId: se deriva de Company.OrganizationId (regla dura CLAUDE.md 2026-08-08).
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>IModuloPortal.ModuleCode.</summary>
    public string ModuleCode { get; set; } = null!;

    /// <summary>Slot lógico dentro del módulo. Default = "Default".</summary>
    public string Purpose { get; set; } = "Default";

    public long ConnectionId { get; set; }
    public CompanyExternalConnection Connection { get; set; } = null!;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 7: Configurar en `PortalSaasDbContext`**

Añadir los DbSets junto a `ModuleExternalConnections` (línea ~40):

```csharp
public DbSet<CompanyExternalConnection> CompanyExternalConnections => Set<CompanyExternalConnection>();
public DbSet<CompanyModuleConnection> CompanyModuleConnections => Set<CompanyModuleConnection>();
```

En `OnModelCreating`, junto al bloque de `ModuleExternalConnection` (líneas ~229-247), añadir:

```csharp
modelBuilder.Entity<CompanyExternalConnection>(entity =>
{
    entity.ToTable("company_external_connections", t => t.HasCheckConstraint(
        "ck_company_external_connections_tipo",
        "tipo in ('db_postgres', 'db_sqlserver', 'db_hana', 'http_api')"));
    entity.HasIndex(e => new { e.CompanyId, e.Nombre })
        .IsUnique()
        .HasDatabaseName("uq_company_external_connections_company_nombre");
    entity.Property(e => e.Nombre).HasMaxLength(100);
    entity.Property(e => e.Tipo).HasMaxLength(20);
    entity.Property(e => e.Host).HasMaxLength(200);
    entity.Property(e => e.BaseUrl).HasMaxLength(500);
    entity.Property(e => e.DatabaseName).HasMaxLength(100);
    entity.Property(e => e.TechnicalUsername).HasMaxLength(100);
    entity.Property(e => e.TechnicalSecretKey).HasMaxLength(500);
    entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Cascade);
});

modelBuilder.Entity<CompanyModuleConnection>(entity =>
{
    entity.ToTable("company_module_connection");
    entity.HasIndex(e => new { e.CompanyId, e.ModuleCode, e.Purpose })
        .IsUnique()
        .HasDatabaseName("uq_company_module_connection_company_module_purpose");
    entity.Property(e => e.ModuleCode).HasMaxLength(50);
    entity.Property(e => e.Purpose).HasMaxLength(50);
    entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Cascade);
    entity.HasOne(e => e.Connection).WithMany(c => c.ModuleBindings).HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.Restrict);
});
```

- [ ] **Step 8: Run test, verify it passes**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter CompanyExternalConnectionEntitiesTests`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/PortalSaas.Abstractions/Modelos/ExternalConnectionType.cs \
        src/PortalSaas.Abstractions/Modelos/ExternalDatabaseConnection.cs \
        src/PortalSaas.Data/Entities/CompanyExternalConnection.cs \
        src/PortalSaas.Data/Entities/CompanyModuleConnection.cs \
        src/PortalSaas.Data/PortalSaasDbContext.cs \
        tests/PortalSaas.Core.Tests/Administracion/CompanyExternalConnectionEntitiesTests.cs
git commit -m "feat: entidades y tipos del catalogo de conexiones externas por compania"
```

---

### Task 2: Migración EF de esquema `AddCompanyExternalConnections` (Postgres + SqlServer)

**Files:**
- Create: `src/PortalSaas.Data.Migrations.PostgreSql/Migrations/<timestamp>_AddCompanyExternalConnections.cs` (generado)
- Create: `src/PortalSaas.Data.Migrations.SqlServer/Migrations/<timestamp>_AddCompanyExternalConnections.cs` (generado)
- Modify: snapshots de ambos proyectos (generados)
- Test: n/a (verificación por comando)

**Interfaces:**
- Consumes: entidades de Task 1.
- Produces: tablas `company_external_connections` y `company_module_connection` en ambos motores.

- [ ] **Step 1: Generar la migración Postgres**

Run:
```bash
dotnet tool run dotnet-ef migrations add AddCompanyExternalConnections \
  --project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj \
  --startup-project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj \
  --context PortalSaasDbContext
```
Expected: crea el archivo de migración + actualiza `PortalSaasDbContextModelSnapshot.cs`.

- [ ] **Step 2: Generar la migración SqlServer**

Run el mismo comando cambiando `PostgreSql` → `SqlServer` en las tres rutas.

- [ ] **Step 3: Revisar ambos archivos generados**

Verificar en el `Up` de cada uno:
- `CreateTable("company_external_connections")` con columnas `id` (identity), `organization_id`, `company_id`, `nombre`, `tipo`, `host`, `base_url`, `port` (nullable), `database_name` (nullable), `technical_username` (nullable), `technical_secret_key` (nullable), `configuracion_extra` (nullable, tipo `text` / `nvarchar(max)`), `is_active`, `created_at`, `updated_at`.
- check constraint `ck_company_external_connections_tipo`.
- índice único `uq_company_external_connections_company_nombre`.
- `CreateTable("company_module_connection")` con `id`, `organization_id`, `company_id`, `module_code`, `purpose`, `connection_id`, `updated_at`; FK a `companies` (Cascade) y a `company_external_connections` (Restrict); índice único `uq_company_module_connection_company_module_purpose`.
- **No** debe tocar `module_external_connections` (se conserva).

Si `configuracion_extra` quedó como `text` con largo, quitar `maxLength`. Editar a mano si hace falta y volver a `dotnet ef migrations` NO — sólo ajustar el `.cs` generado.

- [ ] **Step 4: Aplicar en local (Postgres) y verificar**

Run:
```bash
dotnet ef database update \
  --project src/PortalSaas.Data.Migrations.PostgreSql \
  --startup-project src/PortalSaas.Data.Migrations.PostgreSql
```
Expected: aplica sin error. Verificar que las dos tablas existen (`\dt company_*` en psql).

- [ ] **Step 5: Compilar solución completa**

Run: `dotnet build`
Expected: 0 errores.

- [ ] **Step 6: Commit**

```bash
git add src/PortalSaas.Data.Migrations.PostgreSql src/PortalSaas.Data.Migrations.SqlServer
git commit -m "feat: migracion AddCompanyExternalConnections (postgres + sqlserver)"
```

---

### Task 3: Contratos y DTOs en `PortalSaas.Abstractions`

**Files:**
- Create: `src/PortalSaas.Abstractions/Modelos/CompanyExternalConnectionModels.cs`
- Create: `src/PortalSaas.Abstractions/Contratos/ICompanyExternalConnectionService.cs`
- Modify: `src/PortalSaas.Abstractions/Contratos/IModuloPortal.cs`
- Test: `tests/PortalSaas.Core.Tests/Administracion/ExternalConnectionContractsTests.cs`

**Interfaces:**
- Produces:
  - `enum ExternalConnectionKind { Database, HttpApi }`
  - `record ExternalConnectionRequirement(string Purpose, string DisplayName, ExternalConnectionKind Kind, bool Required)`
  - `record ExternalConnectionDto(long Id, Guid CompanyId, string Nombre, string Tipo, string? Host, string? BaseUrl, int? Port, string? DatabaseName, string? TechnicalUsername, string? ConfiguracionExtra, bool IsActive)`
  - `class ExternalConnectionEditModel { string Nombre; string Tipo; string? Host; string? BaseUrl; int? Port; string? DatabaseName; string? TechnicalUsername; string? TechnicalSecretKey; string? ConfiguracionExtra; bool IsActive; }`
  - `record ModuleConnectionBindingDto(string ModuleCode, string ModuleName, string Purpose, string PurposeDisplayName, ExternalConnectionKind Kind, bool Required, long? ConnectionId, string? ConnectionNombre)`
  - `record ConnectionTestResultDto(bool Ok, string? Error, long? ElapsedMs)`
  - `ICompanyExternalConnectionService` (firmas abajo)
  - `IModuloPortal.ExternalConnectionRequirements` → `IReadOnlyList<ExternalConnectionRequirement>` con default `[]`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/PortalSaas.Core.Tests/Administracion/ExternalConnectionContractsTests.cs
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using Xunit;

namespace PortalSaas.Core.Tests.Administracion;

public sealed class ExternalConnectionContractsTests
{
    [Fact]
    public void EditModelTieneDefaultsRazonables()
    {
        var m = new ExternalConnectionEditModel();
        Assert.Equal(ExternalConnectionType.DbSqlServer, m.Tipo);
        Assert.True(m.IsActive);
    }

    [Fact]
    public void ModuloSinRequisitosDevuelveListaVacia()
    {
        IModuloPortal modulo = new ModuloDummy();
        Assert.Empty(modulo.ExternalConnectionRequirements);
    }

    private sealed class ModuloDummy : IModuloPortal
    {
        public string ModuleCode => "Dummy";
        public string Name => "Dummy";
        public string Version => "1.0.0";
        public IEnumerable<MenuItemDefinition> GetMenu() => [];
        public void RegisterServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services) { }
    }
}
```

- [ ] **Step 2: Run test, verify it fails**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter ExternalConnectionContractsTests`
Expected: FAIL — tipos inexistentes.

- [ ] **Step 3: Crear los modelos**

```csharp
// src/PortalSaas.Abstractions/Modelos/CompanyExternalConnectionModels.cs
namespace PortalSaas.Abstractions.Modelos;

public enum ExternalConnectionKind
{
    Database,
    HttpApi,
}

/// <summary>Requisito de conexión externa declarado por un módulo.</summary>
public sealed record ExternalConnectionRequirement(
    string Purpose,
    string DisplayName,
    ExternalConnectionKind Kind,
    bool Required);

/// <summary>Vista de lectura de una conexión del catálogo. NUNCA incluye el secreto.</summary>
public sealed record ExternalConnectionDto(
    long Id,
    Guid CompanyId,
    string Nombre,
    string Tipo,
    string? Host,
    string? BaseUrl,
    int? Port,
    string? DatabaseName,
    string? TechnicalUsername,
    string? ConfiguracionExtra,
    bool IsActive);

/// <summary>Entrada de alta/edición. Secreto write-only: null o vacío en edición = conservar.</summary>
public sealed class ExternalConnectionEditModel
{
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = ExternalConnectionType.DbSqlServer;
    public string? Host { get; set; }
    public string? BaseUrl { get; set; }
    public int? Port { get; set; }
    public string? DatabaseName { get; set; }
    public string? TechnicalUsername { get; set; }
    public string? TechnicalSecretKey { get; set; }
    public string? ConfiguracionExtra { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Una fila de la grilla de bindings módulo→conexión de una compañía.</summary>
public sealed record ModuleConnectionBindingDto(
    string ModuleCode,
    string ModuleName,
    string Purpose,
    string PurposeDisplayName,
    ExternalConnectionKind Kind,
    bool Required,
    long? ConnectionId,
    string? ConnectionNombre);

public sealed record ConnectionTestResultDto(bool Ok, string? Error, long? ElapsedMs);
```

- [ ] **Step 4: Crear el contrato del servicio**

```csharp
// src/PortalSaas.Abstractions/Contratos/ICompanyExternalConnectionService.cs
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Catálogo de conexiones externas por compañía + binding módulo→conexión.
/// Todos los métodos reciben organizationId y companyId explícitos: la página
/// /Admin los toma de la ruta; el plugin los toma de ICurrentUserContext + selector.
/// El servicio valida que companyId pertenezca a organizationId.
/// </summary>
public interface ICompanyExternalConnectionService
{
    Task<IReadOnlyList<ExternalConnectionDto>> ListAsync(Guid organizationId, Guid companyId, CancellationToken ct = default);
    Task<ExternalConnectionDto?> GetAsync(Guid organizationId, Guid companyId, long id, CancellationToken ct = default);
    Task<long> CreateAsync(Guid organizationId, Guid companyId, ExternalConnectionEditModel model, CancellationToken ct = default);
    Task UpdateAsync(Guid organizationId, Guid companyId, long id, ExternalConnectionEditModel model, CancellationToken ct = default);
    Task DeleteAsync(Guid organizationId, Guid companyId, long id, CancellationToken ct = default);
    Task<ConnectionTestResultDto> TestAsync(Guid organizationId, Guid companyId, long id, CancellationToken ct = default);

    Task<IReadOnlyList<ModuleConnectionBindingDto>> ListBindingsAsync(Guid organizationId, Guid companyId, CancellationToken ct = default);
    Task SetBindingAsync(Guid organizationId, Guid companyId, string moduleCode, string purpose, long connectionId, CancellationToken ct = default);
    Task ClearBindingAsync(Guid organizationId, Guid companyId, string moduleCode, string purpose, CancellationToken ct = default);
}
```

- [ ] **Step 5: Extender `IModuloPortal`**

En `src/PortalSaas.Abstractions/Contratos/IModuloPortal.cs`, añadir el miembro con default (no rompe plugins existentes):

```csharp
    /// <summary>
    /// Conexiones externas que el módulo necesita configurar. Vacío = un único
    /// requisito implícito Purpose="Default", Kind=Database, Required=true.
    /// </summary>
    IReadOnlyList<ExternalConnectionRequirement> ExternalConnectionRequirements => Array.Empty<ExternalConnectionRequirement>();
```

Añadir `using PortalSaas.Abstractions.Modelos;` si no está.

- [ ] **Step 6: Run test, verify it passes**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter ExternalConnectionContractsTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/PortalSaas.Abstractions
git commit -m "feat: contratos y DTOs de ICompanyExternalConnectionService"
```

---

### Task 4: `CompanyExternalConnectionService` — CRUD + bindings + scoping

**Files:**
- Create: `src/PortalSaas.Core/Administracion/CompanyExternalConnectionService.cs`
- Modify: `src/PortalSaas.Host/Program.cs` (registro DI)
- Test: `tests/PortalSaas.Core.Tests/Administracion/CompanyExternalConnectionServiceTests.cs`

**Interfaces:**
- Consumes: `ICompanyExternalConnectionService` (Task 3), entidades (Task 1), `ISecretoCifradoService`, `PortalSaas.Core.Infraestructura.PluginManager` (para `ListBindingsAsync`).
- Produces: `CompanyExternalConnectionService` registrado como `Scoped`.

**Constructor:**
```csharp
public CompanyExternalConnectionService(
    PortalSaasDbContext db,
    ISecretoCifradoService secretos,
    PortalSaas.Core.Infraestructura.PluginManager plugins)
```

**Reglas (implementar en el servicio, no sólo DataAnnotations):**
- Todo método: cargar `Company` por `id == companyId && OrganizationId == organizationId`; si no existe → `InvalidOperationException("Compañía no encontrada en la organización.")`.
- `Get`/`List`: filtran por `OrganizationId` y `CompanyId`. DTO nunca incluye el secreto.
- Validación por `Tipo` en `CreateAsync`/`UpdateAsync`:
  - `Nombre` requerido; único por `CompanyId` (case-insensitive) → `InvalidOperationException("Ya existe una conexión con ese nombre en la compañía.")`.
  - Si `ExternalConnectionType.IsDatabase(Tipo)`: `Host`, `Port`, `DatabaseName`, `TechnicalUsername` requeridos.
  - Si `Tipo == HttpApi`: `BaseUrl` requerido.
  - `Tipo` debe estar en `ExternalConnectionType.All`.
- `CreateAsync`: `TechnicalSecretKey` requerido si es DB; se guarda `_secretos.Encrypt(model.TechnicalSecretKey)`.
- `UpdateAsync`: re-cifra sólo si `model.TechnicalSecretKey` no es null/whitespace; setea `UpdatedAt = DateTimeOffset.UtcNow`.
- `DeleteAsync`: si existe algún `CompanyModuleConnection` con `ConnectionId == id` → `InvalidOperationException($"La conexión está en uso por {n} módulo(s).")`.
- `SetBindingAsync`: valida que `connectionId` exista y sea de la misma `CompanyId`; upsert por `(CompanyId, ModuleCode, Purpose)`.
- `ListBindingsAsync`: para cada `IModuloPortal` en `plugins.ModulosCargados`, expandir sus `ExternalConnectionRequirements` (si vacío → un requisito `("Default","Conexión principal",Database,true)`), y hacer left-join con los `CompanyModuleConnection` de la compañía. `ModuleName` = `modulo.Name`; `ConnectionNombre` = nombre de la conexión ligada o null.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/PortalSaas.Core.Tests/Administracion/CompanyExternalConnectionServiceTests.cs
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Administracion;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Administracion;

public sealed class CompanyExternalConnectionServiceTests
{
    private sealed class SecretosIdentidad : ISecretoCifradoService
    {
        public string Encrypt(string plainText) => "enc:" + plainText;
        public string Decrypt(string cipherText) => cipherText.StartsWith("enc:") ? cipherText[4..] : cipherText;
    }

    private static PortalSaasDbContext NuevoContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(Guid orgId, Guid companyId)> SembrarCompaniaAsync(PortalSaasDbContext db)
    {
        var orgId = Guid.NewGuid();
        var company = new Company { Id = Guid.NewGuid(), OrganizationId = orgId, Code = "DEPOR", Name = "Depor" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return (orgId, company.Id);
    }

    private static CompanyExternalConnectionService Crear(PortalSaasDbContext db) =>
        new(db, new SecretosIdentidad(), new PluginManager());

    private static ExternalConnectionEditModel ModeloDb(string nombre) => new()
    {
        Nombre = nombre, Tipo = ExternalConnectionType.DbSqlServer,
        Host = "h", Port = 1433, DatabaseName = "d", TechnicalUsername = "u", TechnicalSecretKey = "secreto",
    };

    [Fact]
    public async Task Create_YLuego_Get_NoExponeSecreto()
    {
        await using var db = NuevoContexto();
        var (orgId, companyId) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);

        var id = await svc.CreateAsync(orgId, companyId, ModeloDb("BD WMS"));
        var dto = await svc.GetAsync(orgId, companyId, id);

        Assert.NotNull(dto);
        Assert.Equal("BD WMS", dto!.Nombre);
        Assert.DoesNotContain("secreto", System.Text.Json.JsonSerializer.Serialize(dto));
    }

    [Fact]
    public async Task Update_SinSecreto_ConservaElAnterior()
    {
        await using var db = NuevoContexto();
        var (orgId, companyId) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);
        var id = await svc.CreateAsync(orgId, companyId, ModeloDb("BD"));

        var edit = ModeloDb("BD"); edit.TechnicalSecretKey = null; edit.Host = "nuevo-host";
        await svc.UpdateAsync(orgId, companyId, id, edit);

        var fila = await db.CompanyExternalConnections.AsNoTracking().FirstAsync(c => c.Id == id);
        Assert.Equal("enc:secreto", fila.TechnicalSecretKey);
        Assert.Equal("nuevo-host", fila.Host);
    }

    [Fact]
    public async Task Create_NombreDuplicadoEnLaMismaCompania_Rechaza()
    {
        await using var db = NuevoContexto();
        var (orgId, companyId) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);
        await svc.CreateAsync(orgId, companyId, ModeloDb("BD"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAsync(orgId, companyId, ModeloDb("bd")));
    }

    [Fact]
    public async Task Create_HttpApiSinBaseUrl_Rechaza()
    {
        await using var db = NuevoContexto();
        var (orgId, companyId) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);

        var m = new ExternalConnectionEditModel { Nombre = "API", Tipo = ExternalConnectionType.HttpApi };
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync(orgId, companyId, m));
    }

    [Fact]
    public async Task List_NoDevuelveConexionesDeOtraCompania()
    {
        await using var db = NuevoContexto();
        var (orgId, companyId) = await SembrarCompaniaAsync(db);
        var (otraOrg, otraCompany) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);
        await svc.CreateAsync(orgId, companyId, ModeloDb("mia"));
        await svc.CreateAsync(otraOrg, otraCompany, ModeloDb("ajena"));

        var lista = await svc.ListAsync(orgId, companyId);
        Assert.Single(lista);
        Assert.Equal("mia", lista[0].Nombre);
    }

    [Fact]
    public async Task Delete_ConBindingActivo_Rechaza()
    {
        await using var db = NuevoContexto();
        var (orgId, companyId) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);
        var id = await svc.CreateAsync(orgId, companyId, ModeloDb("BD"));
        await svc.SetBindingAsync(orgId, companyId, "Wms", "Default", id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteAsync(orgId, companyId, id));
    }

    [Fact]
    public async Task SetBinding_EsUpsertPorModuloYPurpose()
    {
        await using var db = NuevoContexto();
        var (orgId, companyId) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);
        var a = await svc.CreateAsync(orgId, companyId, ModeloDb("A"));
        var b = await svc.CreateAsync(orgId, companyId, ModeloDb("B"));

        await svc.SetBindingAsync(orgId, companyId, "Wms", "Default", a);
        await svc.SetBindingAsync(orgId, companyId, "Wms", "Default", b);

        var filas = await db.CompanyModuleConnections.Where(x => x.CompanyId == companyId && x.ModuleCode == "Wms").ToListAsync();
        Assert.Single(filas);
        Assert.Equal(b, filas[0].ConnectionId);
    }

    [Fact]
    public async Task Create_CompaniaDeOtraOrganizacion_Rechaza()
    {
        await using var db = NuevoContexto();
        var (_, companyId) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAsync(Guid.NewGuid(), companyId, ModeloDb("x")));
    }
}
```

> Nota: si `Company` requiere más columnas NOT NULL para persistir en InMemory, replicar exactamente el helper de `TenantUserAdminServiceTests` (`CrearOrganizacionConPlanAsync`). Ajustar `SembrarCompaniaAsync` a ese patrón si el `SaveChanges` falla.

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter CompanyExternalConnectionServiceTests`
Expected: FAIL — `CompanyExternalConnectionService` no existe.

- [ ] **Step 3: Implementar el servicio**

Crear `src/PortalSaas.Core/Administracion/CompanyExternalConnectionService.cs` implementando `ICompanyExternalConnectionService` según las "Reglas" de arriba. Estructura de referencia (patrón `OrganizationProfileService`):

```csharp
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Administracion;

public sealed class CompanyExternalConnectionService : ICompanyExternalConnectionService
{
    private readonly PortalSaasDbContext _db;
    private readonly ISecretoCifradoService _secretos;
    private readonly PluginManager _plugins;

    public CompanyExternalConnectionService(PortalSaasDbContext db, ISecretoCifradoService secretos, PluginManager plugins)
    {
        _db = db; _secretos = secretos; _plugins = plugins;
    }

    private async Task EnsureCompanyAsync(Guid organizationId, Guid companyId, CancellationToken ct)
    {
        var ok = await _db.Companies.AsNoTracking()
            .AnyAsync(c => c.Id == companyId && c.OrganizationId == organizationId, ct);
        if (!ok) throw new InvalidOperationException("Compañía no encontrada en la organización.");
    }

    private static ExternalConnectionDto Map(CompanyExternalConnection c) => new(
        c.Id, c.CompanyId, c.Nombre, c.Tipo, c.Host, c.BaseUrl, c.Port,
        c.DatabaseName, c.TechnicalUsername, c.ConfiguracionExtra, c.IsActive);

    private static void ValidarModelo(ExternalConnectionEditModel m)
    {
        if (string.IsNullOrWhiteSpace(m.Nombre))
            throw new InvalidOperationException("El nombre es obligatorio.");
        if (!ExternalConnectionType.All.Contains(m.Tipo))
            throw new InvalidOperationException($"Tipo de conexión inválido: '{m.Tipo}'.");
        if (ExternalConnectionType.IsDatabase(m.Tipo))
        {
            if (string.IsNullOrWhiteSpace(m.Host) || m.Port is null or <= 0
                || string.IsNullOrWhiteSpace(m.DatabaseName) || string.IsNullOrWhiteSpace(m.TechnicalUsername))
                throw new InvalidOperationException("Host, puerto, base y usuario son obligatorios para una conexión de base de datos.");
        }
        else if (m.Tipo == ExternalConnectionType.HttpApi)
        {
            if (string.IsNullOrWhiteSpace(m.BaseUrl))
                throw new InvalidOperationException("La URL base es obligatoria para una conexión HTTP.");
        }
    }

    // ListAsync / GetAsync / CreateAsync / UpdateAsync / DeleteAsync / TestAsync
    // ListBindingsAsync / SetBindingAsync / ClearBindingAsync
    // ... (implementar; TestAsync se completa en la Task 5, aquí puede lanzar NotImplementedException temporal
    //      SI ningún test de esta task lo cubre — pero preferible dejar el stub devolviendo
    //      new ConnectionTestResultDto(false, "no implementado", null) para no romper compilación.)
}
```

Implementar `CreateAsync` con verificación de nombre duplicado:
```csharp
public async Task<long> CreateAsync(Guid organizationId, Guid companyId, ExternalConnectionEditModel model, CancellationToken ct = default)
{
    await EnsureCompanyAsync(organizationId, companyId, ct);
    ValidarModelo(model);
    if (ExternalConnectionType.IsDatabase(model.Tipo) && string.IsNullOrWhiteSpace(model.TechnicalSecretKey))
        throw new InvalidOperationException("El secreto es obligatorio al crear una conexión de base de datos.");

    var nombreNormalizado = model.Nombre.Trim();
    var existe = await _db.CompanyExternalConnections
        .AnyAsync(c => c.CompanyId == companyId && c.Nombre.ToLower() == nombreNormalizado.ToLower(), ct);
    if (existe) throw new InvalidOperationException("Ya existe una conexión con ese nombre en la compañía.");

    var entity = new CompanyExternalConnection
    {
        CompanyId = companyId,
        Nombre = nombreNormalizado,
        Tipo = model.Tipo,
        Host = model.Host,
        BaseUrl = model.BaseUrl,
        Port = model.Port,
        DatabaseName = model.DatabaseName,
        TechnicalUsername = model.TechnicalUsername,
        TechnicalSecretKey = string.IsNullOrWhiteSpace(model.TechnicalSecretKey) ? null : _secretos.Encrypt(model.TechnicalSecretKey),
        ConfiguracionExtra = model.ConfiguracionExtra,
        IsActive = model.IsActive,
    };
    _db.CompanyExternalConnections.Add(entity);
    await _db.SaveChangesAsync(ct);
    return entity.Id;
}
```

`ListBindingsAsync`:
```csharp
public async Task<IReadOnlyList<ModuleConnectionBindingDto>> ListBindingsAsync(Guid organizationId, Guid companyId, CancellationToken ct = default)
{
    await EnsureCompanyAsync(organizationId, companyId, ct);

    var bindings = await _db.CompanyModuleConnections.AsNoTracking()
        .Include(b => b.Connection)
        .Where(b => b.CompanyId == companyId)
        .ToListAsync(ct);

    var result = new List<ModuleConnectionBindingDto>();
    foreach (var modulo in _plugins.ModulosCargados)
    {
        var reqs = modulo.ExternalConnectionRequirements.Count > 0
            ? modulo.ExternalConnectionRequirements
            : new[] { new ExternalConnectionRequirement("Default", "Conexión principal", ExternalConnectionKind.Database, true) };

        foreach (var req in reqs)
        {
            var b = bindings.FirstOrDefault(x => x.ModuleCode == modulo.ModuleCode && x.Purpose == req.Purpose);
            result.Add(new ModuleConnectionBindingDto(
                modulo.ModuleCode, modulo.Name, req.Purpose, req.DisplayName,
                req.Kind, req.Required, b?.ConnectionId, b?.Connection.Nombre));
        }
    }
    return result;
}
```

> `PluginManager.ModulosCargados` es la propiedad que ya usa `Create.cshtml.cs` de `/Admin` (ahí la castea a `List<IModuloPortal>`). Si el nombre real difiere, alinearlo con ese archivo.

- [ ] **Step 4: Registrar en DI**

En `src/PortalSaas.Host/Program.cs`, junto a `AddScoped<IOrganizationProfileService, OrganizationProfileService>()` (línea ~163):

```csharp
builder.Services.AddScoped<ICompanyExternalConnectionService, CompanyExternalConnectionService>();
```

- [ ] **Step 5: Run tests, verify they pass**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter CompanyExternalConnectionServiceTests`
Expected: PASS (todos).

- [ ] **Step 6: Commit**

```bash
git add src/PortalSaas.Core/Administracion/CompanyExternalConnectionService.cs \
        src/PortalSaas.Host/Program.cs \
        tests/PortalSaas.Core.Tests/Administracion/CompanyExternalConnectionServiceTests.cs
git commit -m "feat: CompanyExternalConnectionService (CRUD + bindings + scoping)"
```

---

### Task 5: `TestAsync` — probar conexión real por tipo

**Files:**
- Modify: `src/PortalSaas.Core/Administracion/CompanyExternalConnectionService.cs`
- Test: `tests/PortalSaas.Core.Tests/Administracion/CompanyExternalConnectionServiceTests.cs` (añadir casos)

**Interfaces:**
- Consumes: entidad + `ISecretoCifradoService`.
- Produces: `TestAsync` devuelve `ConnectionTestResultDto(Ok, Error, ElapsedMs)`; nunca incluye el secreto en `Error`.

**Comportamiento:**
- Carga la conexión por `(id, companyId, organizationId)`; si no existe → `InvalidOperationException`.
- Descifra el secreto.
- Según `Tipo`, con timeout corto (5 s):
  - `DbPostgres`: `new NpgsqlConnection(cs)` con `Timeout=5`; `await OpenAsync(cts.Token)`.
  - `DbSqlServer`: `SqlConnectionStringBuilder { ..., ConnectTimeout = 5, TrustServerCertificate = true }`; `await OpenAsync`.
  - `DbHana`: `new Sap.Data.Hana.HanaConnection(csHana)`; `await OpenAsync`. (Reutilizar el helper de connection string HANA que ya exista en Core — buscar `HanaConnection` / `SapConnectionStringFactory`; si no hay, `$"Server={Host}:{Port};UID={user};PWD={pwd}"`.)
  - `HttpApi`: `HttpClient` con `Timeout = 5s`, `GET BaseUrl`; `Ok` si status < 500.
- Cronometrar con `Stopwatch`. En excepción: `Ok=false`, `Error = ex.Message` (mensaje de la excepción de conexión, admin-only), `ElapsedMs` = lo transcurrido.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public async Task Test_ConHostInalcanzable_DevuelveOkFalseYNoFiltraSecreto()
    {
        await using var db = NuevoContexto();
        var (orgId, companyId) = await SembrarCompaniaAsync(db);
        var svc = Crear(db);
        var m = ModeloDb("BD");
        m.Host = "10.255.255.1"; m.Port = 1; m.TechnicalSecretKey = "P4ssw0rd-secreta";
        var id = await svc.CreateAsync(orgId, companyId, m);

        var r = await svc.TestAsync(orgId, companyId, id);

        Assert.False(r.Ok);
        Assert.NotNull(r.Error);
        Assert.DoesNotContain("P4ssw0rd-secreta", r.Error);
        Assert.NotNull(r.ElapsedMs);
    }
```

- [ ] **Step 2: Run test, verify it fails**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter "Test_ConHostInalcanzable"`
Expected: FAIL — `TestAsync` es stub / devuelve `"no implementado"`.

- [ ] **Step 3: Implementar `TestAsync`**

Implementar según "Comportamiento". Ejemplo del branch SqlServer + envoltura común:

```csharp
public async Task<ConnectionTestResultDto> TestAsync(Guid organizationId, Guid companyId, long id, CancellationToken ct = default)
{
    await EnsureCompanyAsync(organizationId, companyId, ct);
    var c = await _db.CompanyExternalConnections.AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct)
        ?? throw new InvalidOperationException("Conexión no encontrada.");

    var password = string.IsNullOrEmpty(c.TechnicalSecretKey) ? "" : _secretos.Decrypt(c.TechnicalSecretKey);
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(5));
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        switch (c.Tipo)
        {
            case ExternalConnectionType.DbSqlServer:
            {
                var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
                {
                    DataSource = $"{c.Host},{c.Port}", InitialCatalog = c.DatabaseName,
                    UserID = c.TechnicalUsername, Password = password,
                    TrustServerCertificate = true, ConnectTimeout = 5,
                }.ConnectionString;
                await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cs);
                await conn.OpenAsync(cts.Token);
                break;
            }
            case ExternalConnectionType.DbPostgres:
            {
                var cs = new Npgsql.NpgsqlConnectionStringBuilder
                {
                    Host = c.Host, Port = c.Port ?? 5432, Database = c.DatabaseName,
                    Username = c.TechnicalUsername, Password = password, Timeout = 5,
                }.ConnectionString;
                await using var conn = new Npgsql.NpgsqlConnection(cs);
                await conn.OpenAsync(cts.Token);
                break;
            }
            case ExternalConnectionType.DbHana:
            {
                var cs = $"Server={c.Host}:{c.Port};UID={c.TechnicalUsername};PWD={password}";
                await using var conn = new Sap.Data.Hana.HanaConnection(cs);
                await conn.OpenAsync(cts.Token);
                break;
            }
            case ExternalConnectionType.HttpApi:
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var resp = await http.GetAsync(c.BaseUrl, cts.Token);
                if ((int)resp.StatusCode >= 500)
                    return new ConnectionTestResultDto(false, $"HTTP {(int)resp.StatusCode}", sw.ElapsedMilliseconds);
                break;
            }
            default:
                return new ConnectionTestResultDto(false, $"Tipo no soportado: {c.Tipo}", sw.ElapsedMilliseconds);
        }
        return new ConnectionTestResultDto(true, null, sw.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
        return new ConnectionTestResultDto(false, ex.Message, sw.ElapsedMilliseconds);
    }
}
```

> Verificar que `PortalSaas.Core.csproj` ya referencia `Sap.Data.Hana` (la exploración indica que sí, vía `IHanaService`). Si no, añadir la referencia que use `PortalSaas.Integrations` / donde viva hoy.

- [ ] **Step 4: Run test, verify it passes**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter "Test_ConHostInalcanzable"`
Expected: PASS (en < ~10 s por el timeout).

- [ ] **Step 5: Commit**

```bash
git add src/PortalSaas.Core/Administracion/CompanyExternalConnectionService.cs \
        tests/PortalSaas.Core.Tests/Administracion/CompanyExternalConnectionServiceTests.cs
git commit -m "feat: TestAsync prueba conexion real por tipo (pg/mssql/hana/http)"
```

---

### Task 6: Rewire `ExternalDatabaseConnectionService` al binding + catálogo

**Files:**
- Modify: `src/PortalSaas.Abstractions/Contratos/IExternalDatabaseConnectionService.cs` (overload `purpose`)
- Modify: `src/PortalSaas.Core/Infraestructura/ExternalDatabaseConnectionService.cs`
- Test: `tests/PortalSaas.Core.Tests/Infraestructura/ExternalDatabaseConnectionServiceTests.cs` (extender)

**Interfaces:**
- Consumes: `CompanyExternalConnections` + `CompanyModuleConnections` (Task 1), `ExternalConnectionType`, `ExternalDatabaseEngineType.Hana`.
- Produces:
  - `ResolveConnectionAsync(string moduleCode, Guid companyId, CancellationToken ct = default)` — sin cambio, delega en `purpose="Default"`.
  - `ResolveConnectionAsync(string moduleCode, Guid companyId, string purpose, CancellationToken ct = default)` — nuevo.
  - `ListActiveCompanyIdsAsync` — misma firma, lee del nuevo modelo.

- [ ] **Step 1: Write the failing tests**

```csharp
// añadir en ExternalDatabaseConnectionServiceTests.cs
    [Fact]
    public async Task ResolveConnectionAsync_UsaElBindingDefaultDelCatalogo()
    {
        await using var db = NuevoContexto();
        var companyId = Guid.NewGuid();
        var conn = new CompanyExternalConnection
        {
            CompanyId = companyId, Nombre = "BD",
            Tipo = ExternalConnectionType.DbPostgres, Host = "h", Port = 5432,
            DatabaseName = "d", TechnicalUsername = "u", TechnicalSecretKey = "enc:p", IsActive = true,
        };
        db.CompanyExternalConnections.Add(conn);
        await db.SaveChangesAsync();
        db.CompanyModuleConnections.Add(new CompanyModuleConnection
        {
            CompanyId = companyId,
            ModuleCode = "Wms", Purpose = "Default", ConnectionId = conn.Id,
        });
        await db.SaveChangesAsync();

        var svc = new ExternalDatabaseConnectionService(db, new FakeSecretos());
        var r = await svc.ResolveConnectionAsync("Wms", companyId);

        Assert.Equal(ExternalDatabaseEngineType.Postgres, r.EngineType);
        Assert.Contains("Host=h", r.ConnectionString);
    }

    [Fact]
    public async Task ResolveConnectionAsync_SinBinding_Lanza()
    {
        await using var db = NuevoContexto();
        var svc = new ExternalDatabaseConnectionService(db, new FakeSecretos());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ResolveConnectionAsync("Wms", Guid.NewGuid()));
    }

    [Fact]
    public async Task ResolveConnectionAsync_ConPurpose_ResuelveElSlotIndicado()
    {
        await using var db = NuevoContexto();
        var companyId = Guid.NewGuid();
        var sap = new CompanyExternalConnection
        {
            CompanyId = companyId, Nombre = "SAP",
            Tipo = ExternalConnectionType.DbSqlServer, Host = "sap", Port = 1433,
            DatabaseName = "SBO", TechnicalUsername = "u", TechnicalSecretKey = "enc:p", IsActive = true,
        };
        db.CompanyExternalConnections.Add(sap);
        await db.SaveChangesAsync();
        db.CompanyModuleConnections.Add(new CompanyModuleConnection
        {
            CompanyId = companyId,
            ModuleCode = "Wms", Purpose = "SapSource", ConnectionId = sap.Id,
        });
        await db.SaveChangesAsync();

        var svc = new ExternalDatabaseConnectionService(db, new FakeSecretos());
        var r = await svc.ResolveConnectionAsync("Wms", companyId, "SapSource");
        Assert.Equal(ExternalDatabaseEngineType.SqlServer, r.EngineType);
    }
```

> Reusar/crear `FakeSecretos` (identidad) y `NuevoContexto()` como en el resto del archivo. Mantener el test existente `ListActiveCompanyIdsAsync_devuelve_solo_filas_activas_del_modulo_pedido` pero adaptando su siembra al nuevo modelo (binding + catálogo, `Connection.IsActive`). Como `ListActiveCompanyIdsAsync` proyecta `b.Company.OrganizationId`, ese test **debe sembrar también una entidad `Company`** (`new Company { Id = companyId, OrganizationId = ..., Code, Name }`) para cada `companyId` usado, o el `Select` con el join a `companies` devolverá `OrganizationId` vacío / fallará. Los tests `ResolveConnectionAsync_*` no llaman ese método y no necesitan `Company`.

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter ExternalDatabaseConnectionServiceTests`
Expected: FAIL — el servicio aún lee `ModuleExternalConnections`.

- [ ] **Step 3: Añadir el overload al contrato**

En `IExternalDatabaseConnectionService`, bajo el método actual:

```csharp
    /// <summary>Resuelve la conexión del slot lógico indicado (Purpose). "Default" = conexión principal.</summary>
    Task<ExternalDatabaseConnection> ResolveConnectionAsync(
        string moduleCode,
        Guid companyId,
        string purpose,
        CancellationToken ct = default);
```

- [ ] **Step 4: Reimplementar el servicio**

Reescribir `ExternalDatabaseConnectionService` para:
- método de 3 args delega: `=> ResolveConnectionAsync(moduleCode, companyId, "Default", ct);`
- método de 4 args:

```csharp
public async Task<ExternalDatabaseConnection> ResolveConnectionAsync(
    string moduleCode, Guid companyId, string purpose, CancellationToken ct = default)
{
    var binding = await _db.CompanyModuleConnections.AsNoTracking()
        .Include(b => b.Connection)
        .FirstOrDefaultAsync(b => b.ModuleCode == moduleCode
                                  && b.CompanyId == companyId
                                  && b.Purpose == purpose
                                  && b.Connection.IsActive, ct)
        ?? throw new InvalidOperationException(
            $"No hay una base de datos externa asociada al módulo '{moduleCode}' " +
            $"(propósito '{purpose}') para esta compañía -- configurarla desde Administración.");

    var c = binding.Connection;
    var password = string.IsNullOrEmpty(c.TechnicalSecretKey) ? "" : _secretos.Decrypt(c.TechnicalSecretKey);

    (string engine, string cs) = c.Tipo switch
    {
        ExternalConnectionType.DbPostgres => (ExternalDatabaseEngineType.Postgres, new NpgsqlConnectionStringBuilder
        {
            Host = c.Host, Port = c.Port ?? 5432, Database = c.DatabaseName,
            Username = c.TechnicalUsername, Password = password,
        }.ConnectionString),
        ExternalConnectionType.DbSqlServer => (ExternalDatabaseEngineType.SqlServer, new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = $"{c.Host},{c.Port}", InitialCatalog = c.DatabaseName,
            UserID = c.TechnicalUsername, Password = password, TrustServerCertificate = true,
        }.ConnectionString),
        ExternalConnectionType.DbHana => (ExternalDatabaseEngineType.Hana,
            $"Server={c.Host}:{c.Port};UID={c.TechnicalUsername};PWD={password}"),
        _ => throw new InvalidOperationException(
            $"El módulo '{moduleCode}' requiere una conexión de base de datos, " +
            $"pero '{c.Nombre}' es de tipo '{c.Tipo}'."),
    };

    return new ExternalDatabaseConnection { EngineType = engine, ConnectionString = cs };
}
```

`ListActiveCompanyIdsAsync`:
```csharp
public async Task<IReadOnlyList<ModuleCompanyDto>> ListActiveCompanyIdsAsync(string moduleCode, CancellationToken ct = default)
{
    return await _db.CompanyModuleConnections.AsNoTracking()
        .Where(b => b.ModuleCode == moduleCode && b.Purpose == "Default" && b.Connection.IsActive)
        .Select(b => new ModuleCompanyDto(b.CompanyId, b.Company.OrganizationId))
        .Distinct()
        .ToListAsync(ct);
}
```

- [ ] **Step 5: Run tests, verify they pass**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter ExternalDatabaseConnectionServiceTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PortalSaas.Abstractions/Contratos/IExternalDatabaseConnectionService.cs \
        src/PortalSaas.Core/Infraestructura/ExternalDatabaseConnectionService.cs \
        tests/PortalSaas.Core.Tests/Infraestructura/ExternalDatabaseConnectionServiceTests.cs
git commit -m "feat: ExternalDatabaseConnectionService resuelve contra binding+catalogo"
```

---

### Task 7: Backfill idempotente de `module_external_connections` → catálogo + binding

**Files:**
- Create: `src/PortalSaas.Core/Administracion/LegacyExternalConnectionBackfill.cs`
- Modify: `src/PortalSaas.Host/Program.cs` (ejecutar el backfill al arranque)
- Test: `tests/PortalSaas.Core.Tests/Administracion/LegacyExternalConnectionBackfillTests.cs`

**Interfaces:**
- Consumes: `PortalSaasDbContext`, `ILogger<LegacyExternalConnectionBackfill>`.
- Produces: `class LegacyExternalConnectionBackfill` con `Task<int> RunAsync(CancellationToken ct)` — devuelve nº de bindings creados. Idempotente.

**Comportamiento:**
- Para cada `ModuleExternalConnection` (`me`) sin `CompanyModuleConnection` existente con `(me.CompanyId, me.ModuleCode, "Default")`:
  - Buscar/crear `CompanyExternalConnection` en la misma compañía con misma tupla `(Tipo, Host, Port, DatabaseName)` — dedupe. `Tipo`: `"postgres"→db_postgres`, `"sqlserver"→db_sqlserver`. (Sin `OrganizationId` — la entidad no lo tiene.) `Nombre` = `me.ModuleCode`; si choca con una existente de esa compañía, sufijo ` (2)`, ` (3)`… `TechnicalSecretKey` = `me.TechnicalSecretKey` **tal cual** (ya cifrado con el mismo servicio). `IsActive = me.IsActive`.
  - Crear `CompanyModuleConnection` `(me.CompanyId, me.ModuleCode, "Default")` → esa conexión.
- Si ya existe un binding `(CompanyId, ModuleCode, "Default")` apuntando a otra conexión distinta a la que resolvería `me` → `logger.LogWarning("Backfill: binding en conflicto para company {CompanyId} module {ModuleCode}: se conserva {ExistingId}, se ignora {LegacyId}", ...)` y no tocar.
- `module_external_connections` no se modifica ni se borra.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/PortalSaas.Core.Tests/Administracion/LegacyExternalConnectionBackfillTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Administracion;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Administracion;

public sealed class LegacyExternalConnectionBackfillTests
{
    private static PortalSaasDbContext NuevoContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<Company> SembrarCompaniaAsync(PortalSaasDbContext db)
    {
        var c = new Company { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), Code = "C", Name = "C" };
        db.Companies.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    [Fact]
    public async Task DesduplicaDosModulosQueApuntanAlaMismaBD()
    {
        await using var db = NuevoContexto();
        var c = await SembrarCompaniaAsync(db);
        db.ModuleExternalConnections.AddRange(
            new ModuleExternalConnection { CompanyId = c.Id, ModuleCode = "Wms", EngineType = "sqlserver", Host = "h", Port = 1, DatabaseName = "d", TechnicalUsername = "u", TechnicalSecretKey = "e", IsActive = true },
            new ModuleExternalConnection { CompanyId = c.Id, ModuleCode = "Rendiciones", EngineType = "sqlserver", Host = "h", Port = 1, DatabaseName = "d", TechnicalUsername = "u", TechnicalSecretKey = "e", IsActive = true });
        await db.SaveChangesAsync();

        var creados = await new LegacyExternalConnectionBackfill(db, NullLogger<LegacyExternalConnectionBackfill>.Instance).RunAsync(default);

        Assert.Equal(2, creados);
        Assert.Single(db.CompanyExternalConnections);
        Assert.Equal(2, db.CompanyModuleConnections.Count());
    }

    [Fact]
    public async Task EsIdempotente()
    {
        await using var db = NuevoContexto();
        var c = await SembrarCompaniaAsync(db);
        db.ModuleExternalConnections.Add(new ModuleExternalConnection { CompanyId = c.Id, ModuleCode = "Wms", EngineType = "postgres", Host = "h", Port = 5432, DatabaseName = "d", TechnicalUsername = "u", TechnicalSecretKey = "e", IsActive = true });
        await db.SaveChangesAsync();
        var backfill = new LegacyExternalConnectionBackfill(db, NullLogger<LegacyExternalConnectionBackfill>.Instance);

        await backfill.RunAsync(default);
        var segunda = await backfill.RunAsync(default);

        Assert.Equal(0, segunda);
        Assert.Single(db.CompanyExternalConnections);
        Assert.Single(db.CompanyModuleConnections);
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter LegacyExternalConnectionBackfillTests`
Expected: FAIL — clase inexistente.

- [ ] **Step 3: Implementar el backfill**

Crear `LegacyExternalConnectionBackfill` según "Comportamiento". Mapa de motor:
```csharp
private static string MapTipo(string engineType) => engineType switch
{
    ModuleExternalConnectionEngineType.Postgres => ExternalConnectionType.DbPostgres,
    ModuleExternalConnectionEngineType.SqlServer => ExternalConnectionType.DbSqlServer,
    _ => throw new InvalidOperationException($"Motor legado no soportado: '{engineType}'."),
};
```

- [ ] **Step 4: Ejecutar el backfill al arranque del Host**

En `Program.cs`, tras `app.Build()` y tras aplicar migraciones (buscar dónde el Host hace `db.Database.Migrate()` / `MigrateAsync`), añadir:

```csharp
using (var scope = app.Services.CreateScope())
{
    var backfill = new PortalSaas.Core.Administracion.LegacyExternalConnectionBackfill(
        scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>(),
        scope.ServiceProvider.GetRequiredService<ILogger<PortalSaas.Core.Administracion.LegacyExternalConnectionBackfill>>());
    await backfill.RunAsync(default);
}
```

> Si el Host no aplica migraciones automáticamente en este entorno, dejar igualmente el bloque: es no-op si no hay filas legadas.

- [ ] **Step 5: Run tests, verify they pass**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter LegacyExternalConnectionBackfillTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PortalSaas.Core/Administracion/LegacyExternalConnectionBackfill.cs \
        src/PortalSaas.Host/Program.cs \
        tests/PortalSaas.Core.Tests/Administracion/LegacyExternalConnectionBackfillTests.cs
git commit -m "feat: backfill idempotente de module_external_connections al catalogo"
```

---

### Task 8: Refactor de las páginas `/Admin/Organizations/Companies/ExternalConnections/*`

**Files:**
- Modify: `src/PortalSaas.Host/Pages/Admin/Organizations/Companies/ExternalConnections/Index.cshtml` + `Index.cshtml.cs`
- Modify: `.../ExternalConnections/Create.cshtml` + `Create.cshtml.cs`
- Modify: `.../ExternalConnections/Edit.cshtml` + `Edit.cshtml.cs`
- Test: n/a (Razor Pages — verificación manual)

**Interfaces:**
- Consumes: `ICompanyExternalConnectionService` (inyectado), `PortalSaasDbContext` (sólo para cargar `Company`/`Organization` de cabecera).

**Cambios:**
- Las tres PageModels: quitar el uso directo de `_db` para escribir `ModuleExternalConnection`; inyectar `ICompanyExternalConnectionService _svc`. Mantener `[Authorize(AuthenticationSchemes = "PlatformAdmin")]`.
- `Index.OnGetAsync(Guid companyId)`:
  - `Company` / `Organization` desde `_db` como hoy.
  - `Connections = await _svc.ListAsync(Company.OrganizationId, companyId)` → `IReadOnlyList<ExternalConnectionDto>`.
  - `Bindings = await _svc.ListBindingsAsync(Company.OrganizationId, companyId)`.
- `Index.OnPostTestConnectionAsync(Guid companyId, long id)` → `var r = await _svc.TestAsync(Company.OrganizationId, companyId, id);` y set `TestSuccess = r.Ok` / `TestError = r.Error`.
- `Index.OnPostDeleteAsync(Guid companyId, long id)` → `try { await _svc.DeleteAsync(org, companyId, id); } catch (InvalidOperationException ex) { ErrorMessage = ex.Message; }` y `RedirectToPage(new { companyId })`.
- Nuevo `Index.OnPostSetBindingAsync(Guid companyId, string moduleCode, string purpose, long? connectionId)`:
  ```csharp
  if (connectionId is null or 0)
      await _svc.ClearBindingAsync(Company.OrganizationId, companyId, moduleCode, purpose);
  else
      await _svc.SetBindingAsync(Company.OrganizationId, companyId, moduleCode, purpose, connectionId.Value);
  return RedirectToPage(new { companyId });
  ```
- `Create` / `Edit`: reemplazar el `InputModel` propio por bind directo a `ExternalConnectionEditModel` (+ un `long Id` en Edit). Añadir campo `Tipo` (`<select>` con `ExternalConnectionType.All`) y mostrar/ocultar Host/Port/DatabaseName vs BaseUrl según tipo (JS mínimo o simplemente mostrar todos los campos con etiquetas claras). `ConfiguracionExtra`: `<textarea>` (JSON crudo).
  - `Create.OnPostAsync(Guid companyId)` → `try { var id = await _svc.CreateAsync(Company.OrganizationId, companyId, Input); return RedirectToPage("Index", new { companyId }); } catch (InvalidOperationException ex) { ModelState.AddModelError(string.Empty, ex.Message); /* recargar cabecera */ return Page(); }`.
  - `Edit.OnGetAsync(long id)` → `var dto = await _svc.GetAsync(org, companyId, id)`; poblar `Input` desde el DTO (sin secreto). `Edit.OnPostAsync()` → `await _svc.UpdateAsync(org, companyId, Input.Id, Input)`.
- El `<select>` de módulo de `Create` (que hoy elige `ModuleCode`) desaparece: el `ModuleCode` ya no vive en la conexión; la asociación se hace en la grilla de bindings de `Index`.
- La vista `Index.cshtml`: añadir una tabla "Asignación por módulo" que itere `Model.Bindings` y por fila renderice un `<form method="post" asp-page-handler="SetBinding">` con `<select name="connectionId">` (opciones = `Model.Connections`, opción vacía = "— sin asignar —"), `<input type="hidden" name="moduleCode">`, `name="purpose"`, `name="companyId"`, y un botón "Guardar". Marcar visualmente las filas `Required && ConnectionId == null`.

- [ ] **Step 1: Refactor `Index.cshtml.cs`** — inyectar `ICompanyExternalConnectionService`, reemplazar carga y handlers según arriba. Añadir props `IReadOnlyList<ExternalConnectionDto> Connections`, `IReadOnlyList<ModuleConnectionBindingDto> Bindings`.

- [ ] **Step 2: Refactor `Create.cshtml.cs` + `Create.cshtml`** — bind a `ExternalConnectionEditModel`, quitar `PluginManager`/`AvailablePlugins` y el `<select>` de módulo, añadir `<select>` de `Tipo`.

- [ ] **Step 3: Refactor `Edit.cshtml.cs` + `Edit.cshtml`** — igual, + `Id`, secreto write-only con label "dejar en blanco para no cambiarla".

- [ ] **Step 4: Actualizar `Index.cshtml`** — grilla de conexiones (nombre/tipo/host o URL/activo/acciones) + grilla de bindings por módulo.

- [ ] **Step 5: Build + smoke test manual**

Run: `dotnet build`
Luego correr el Host (`dotnet run --project src/PortalSaas.Host`), loguear como PlatformAdmin, ir a `/Admin/Organizations`, entrar a una organización → Compañías → una compañía → Conexiones externas. Verificar:
- Crear una conexión `db_sqlserver`.
- "Probar conexión" muestra Ok/errores.
- Asignar esa conexión al módulo `Wms` (Purpose `Default`) en la grilla de bindings.
- Intentar borrar la conexión asignada → mensaje "en uso por 1 módulo(s)".

- [ ] **Step 6: Commit**

```bash
git add src/PortalSaas.Host/Pages/Admin/Organizations/Companies/ExternalConnections
git commit -m "refactor: /Admin ExternalConnections usa ICompanyExternalConnectionService + bindings"
```

---

### Task 9: Página self-service `/organizacion/conexiones-externas` (plugin `Modulo.Administracion`)

**Files:**
- Modify: `plugins/Modulo.Administracion/ModuloAdministracion.cs` (nodo de menú)
- Create: `plugins/Modulo.Administracion/Pages/ConexionesExternas/Index.cshtml` + `Index.cshtml.cs`
- Create: `plugins/Modulo.Administracion/Pages/ConexionesExternas/Editar.cshtml` + `Editar.cshtml.cs`
- Test: n/a (Razor Pages — verificación manual)

**Interfaces:**
- Consumes: `ICompanyExternalConnectionService`, `ITenantUserAdminService` (para `ListCompaniesAsync()` → `IReadOnlyList<CompanyOptionDto>` con `Id`/`Code`/`Name`), `ICurrentUserContext` (base).
- El plugin ya "ve" `ICompanyExternalConnectionService` porque está en `PortalSaas.Abstractions` y lo registra el Host (Task 4).

- [ ] **Step 1: Añadir el nodo de menú**

En `ModuloAdministracion.GetMenu()`, tras el `yield return` de `perfiles`:

```csharp
        yield return new MenuItemDefinition { Code = "conexiones-externas", ParentCode = "raiz", Name = "Conexiones externas", Icon = "bi bi-plug", PageRoute = "/organizacion/conexiones-externas", Order = 6 };
```

- [ ] **Step 2: `Index.cshtml.cs`**

```csharp
// plugins/Modulo.Administracion/Pages/ConexionesExternas/Index.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Administracion.Pages.ConexionesExternas;

public sealed class IndexModel : AdminPageModelBase
{
    private readonly ICompanyExternalConnectionService _svc;
    private readonly ITenantUserAdminService _tenant;

    public IndexModel(ICompanyExternalConnectionService svc, ITenantUserAdminService tenant, ICurrentUserContext currentUser)
        : base(currentUser)
    {
        _svc = svc;
        _tenant = tenant;
    }

    public IReadOnlyList<CompanyOptionDto> Companies { get; private set; } = [];
    public Guid? CompanyId { get; private set; }
    public IReadOnlyList<ExternalConnectionDto> Connections { get; private set; } = [];
    public IReadOnlyList<ModuleConnectionBindingDto> Bindings { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid? companyId)
    {
        Companies = await _tenant.ListCompaniesAsync();
        if (Companies.Count == 0) return Page();

        CompanyId = companyId ?? Companies[0].Id;
        if (!Companies.Any(c => c.Id == CompanyId)) { MensajeError = "Compañía inválida."; CompanyId = Companies[0].Id; }

        var orgId = CurrentUser.OrganizationId;
        Connections = await _svc.ListAsync(orgId, CompanyId.Value);
        Bindings = await _svc.ListBindingsAsync(orgId, CompanyId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostTestAsync(Guid companyId, long id)
    {
        try
        {
            var r = await _svc.TestAsync(CurrentUser.OrganizationId, companyId, id);
            MensajeExito = r.Ok ? $"Conexión OK ({r.ElapsedMs} ms)." : null;
            MensajeError = r.Ok ? null : $"Falló la conexión: {r.Error}";
        }
        catch (Exception ex) { MensajeError = ObtenerMensajeError(ex); }
        return RedirectToPage(new { companyId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid companyId, long id)
    {
        try { await _svc.DeleteAsync(CurrentUser.OrganizationId, companyId, id); MensajeExito = "Conexión eliminada."; }
        catch (Exception ex) { MensajeError = ObtenerMensajeError(ex); }
        return RedirectToPage(new { companyId });
    }

    public async Task<IActionResult> OnPostSetBindingAsync(Guid companyId, string moduleCode, string purpose, long? connectionId)
    {
        try
        {
            if (connectionId is null or 0)
                await _svc.ClearBindingAsync(CurrentUser.OrganizationId, companyId, moduleCode, purpose);
            else
                await _svc.SetBindingAsync(CurrentUser.OrganizationId, companyId, moduleCode, purpose, connectionId.Value);
            MensajeExito = "Asignación actualizada.";
        }
        catch (Exception ex) { MensajeError = ObtenerMensajeError(ex); }
        return RedirectToPage(new { companyId });
    }
}
```

- [ ] **Step 3: `Editar.cshtml.cs`**

```csharp
// plugins/Modulo.Administracion/Pages/ConexionesExternas/Editar.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Administracion.Pages.ConexionesExternas;

public sealed class EditarModel : AdminPageModelBase
{
    private readonly ICompanyExternalConnectionService _svc;
    private readonly ITenantUserAdminService _tenant;

    public EditarModel(ICompanyExternalConnectionService svc, ITenantUserAdminService tenant, ICurrentUserContext currentUser)
        : base(currentUser) { _svc = svc; _tenant = tenant; }

    [BindProperty] public ExternalConnectionEditModel Input { get; set; } = new();
    [BindProperty] public long? Id { get; set; }
    [BindProperty(SupportsGet = true)] public Guid CompanyId { get; set; }
    public bool EsNuevo => Id is null;
    public IReadOnlyList<string> Tipos => ExternalConnectionType.All.ToList();

    public async Task<IActionResult> OnGetAsync(long? id)
    {
        await ValidarCompaniaAsync();
        if (id is not null)
        {
            var dto = await _svc.GetAsync(CurrentUser.OrganizationId, CompanyId, id.Value);
            if (dto is null) return NotFound();
            Id = dto.Id;
            Input = new ExternalConnectionEditModel
            {
                Nombre = dto.Nombre, Tipo = dto.Tipo, Host = dto.Host, BaseUrl = dto.BaseUrl,
                Port = dto.Port, DatabaseName = dto.DatabaseName, TechnicalUsername = dto.TechnicalUsername,
                ConfiguracionExtra = dto.ConfiguracionExtra, IsActive = dto.IsActive,
            };
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await ValidarCompaniaAsync();
        if (!ModelState.IsValid) return Page();
        try
        {
            if (Id is null)
                await _svc.CreateAsync(CurrentUser.OrganizationId, CompanyId, Input);
            else
                await _svc.UpdateAsync(CurrentUser.OrganizationId, CompanyId, Id.Value, Input);
            MensajeExito = "Conexión guardada.";
            return RedirectToPage("Index", new { companyId = CompanyId });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    private async Task ValidarCompaniaAsync()
    {
        var companies = await _tenant.ListCompaniesAsync();
        if (!companies.Any(c => c.Id == CompanyId))
            throw new InvalidOperationException("Compañía inválida para esta organización.");
    }
}
```

- [ ] **Step 4: Vistas `.cshtml`**

`Index.cshtml`: patrón visual "ListadoOrganizacion" (copiar cabecera/estructura de `Pages/Usuarios/Index.cshtml`). Incluir:
- `<select>` de compañía que hace GET con `?companyId=` (submit onchange).
- Tabla de `Model.Connections`: Nombre · Tipo · Host/URL · Activo · [Probar] [Editar] [Eliminar] (forms POST con `asp-page-handler`).
- Botón "Nueva conexión" → `Editar` con `companyId`.
- Tabla "Asignación por módulo" iterando `Model.Bindings`: Módulo · Propósito · `<select name="connectionId">` (opciones `Model.Connections` + vacío) dentro de `<form method="post" asp-page-handler="SetBinding">` con hidden `companyId`/`moduleCode`/`purpose` + botón Guardar. Resaltar `Required && ConnectionId is null`.

`Editar.cshtml`: form con `asp-for="Input.Nombre"`, `<select asp-for="Input.Tipo" asp-items="..."/>`, campos Host/Port/DatabaseName/TechnicalUsername (para `Db*`), BaseUrl (para `HttpApi`), `asp-for="Input.TechnicalSecretKey"` type=password con hint "dejar en blanco para no cambiarla", `<textarea asp-for="Input.ConfiguracionExtra">`, checkbox `asp-for="Input.IsActive"`, hidden `asp-for="Id"` y `asp-for="CompanyId"`. Mostrar `<div asp-validation-summary="All">`.

- [ ] **Step 5: Build plugin + smoke test manual**

Run: `dotnet build` (compila el plugin junto con la solución).
Correr el Host, loguear como **admin de tenant** (no PlatformAdmin), ir a `/organizacion/conexiones-externas`. Verificar:
- Aparece "Conexiones externas" en el submenú de Administración.
- El selector de compañía lista las compañías de la organización.
- Crear/editar/probar/eliminar una conexión.
- Asignar la conexión al módulo en la grilla; el módulo (p. ej. Ventas/WMS) resuelve su BD por esa asignación.
- Un usuario no-admin recibe 403.

- [ ] **Step 6: Commit**

```bash
git add plugins/Modulo.Administracion/ModuloAdministracion.cs \
        plugins/Modulo.Administracion/Pages/ConexionesExternas
git commit -m "feat: pagina self-service /organizacion/conexiones-externas"
```

---

### Task 10: Verificación integral + limpieza

**Files:**
- Modify: `CLAUDE.md` (actualizar la sección de conexiones externas)
- Modify: `docs/superpowers/specs/2026-08-27-catalogo-conexiones-externas-por-compania-design.md` (marcar estado "implementado")
- Test: suite completa

- [ ] **Step 1: Suite completa**

Run: `dotnet test`
Expected: todo verde; el conteo sube respecto a 183 por los tests nuevos (Tasks 1, 3, 4, 5, 6, 7). 0 failed.

- [ ] **Step 2: Build Release**

Run: `dotnet build -c Release`
Expected: 0 errores, 0 warnings nuevos.

- [ ] **Step 3: Verificar que nada más lee `ModuleExternalConnections`**

Run: `grep -rn "ModuleExternalConnections" src/ plugins/`
Expected: sólo aparece en `PortalSaasDbContext.cs` (definición), la migración original, y `LegacyExternalConnectionBackfill.cs`. Ningún consumidor de runtime. Si aparece otro, migrarlo al nuevo servicio.

- [ ] **Step 4: Aplicar migración en las 4 bases reales**

Para cada base de `172.16.122.171` (las 4 bases Postgres operativas):
```bash
ConnectionStrings__Default="<cadena de la base N>" dotnet ef database update \
  --project src/PortalSaas.Data.Migrations.PostgreSql \
  --startup-project src/PortalSaas.Data.Migrations.PostgreSql
```
Arrancar el Host apuntando a cada base para disparar el backfill; revisar el log por `WARN` de conflictos y resolverlos a mano. Anotar si alguna base queda pendiente y **preguntar**.

- [ ] **Step 5: Actualizar `CLAUDE.md`**

Reemplazar la sección que describe `module_external_connections` / `ResolveConnectionAsync` por la nueva realidad: catálogo `company_external_connections` por compañía + binding `company_module_connection` `(CompanyId, ModuleCode, Purpose)`, overload `ResolveConnectionAsync(moduleCode, companyId, purpose)`, gestión en `/Admin/.../ExternalConnections` y `/organizacion/conexiones-externas`. Nota: `module_external_connections` queda obsoleta, se elimina en un spec posterior.

- [ ] **Step 6: Commit**

```bash
git add CLAUDE.md docs/superpowers/specs/2026-08-27-catalogo-conexiones-externas-por-compania-design.md
git commit -m "docs: actualizar CLAUDE.md y spec tras catalogo de conexiones externas"
```

---

## Self-Review

**1. Spec coverage:**
- Modelo `company_external_connections` + `company_module_connection` → Task 1.
- `ExternalConnectionType` con `HttpApi` + `ConfiguracionExtra` JSON → Task 1, Task 3.
- `IModuloPortal.ExternalConnectionRequirements` + default implícito → Task 3, Task 4 (`ListBindingsAsync`).
- Migración de esquema dual → Task 2.
- Contratos/DTOs (`ICompanyExternalConnectionService`, `ConnectionTestResultDto`, etc.) → Task 3.
- Servicio Core con scoping, write-only secret, validación por tipo, unicidad de nombre, binding upsert, delete bloqueado → Task 4.
- "Probar conexión" real pg/mssql/hana/http → Task 5.
- `IExternalDatabaseConnectionService` sin cambio de firma + overload `purpose` + `ExternalDatabaseEngineType.Hana` → Task 1 (Hana const), Task 6.
- Migración de datos con dedupe + `WARN` en conflicto, `module_external_connections` conservada → Task 7.
- Refactor `/Admin` ExternalConnections → Task 8.
- Página self-service `/organizacion/conexiones-externas` + nodo de menú + selector de compañía solo lectura → Task 9.
- Manejo de errores (TempData `MensajeError`, delete en uso, validación) → Tasks 4, 8, 9.
- Pruebas enumeradas en spec §9 → Tasks 1, 3, 4, 5, 6, 7.
- Aplicar contra las 4 bases reales → Task 10 Step 4.
- Fuera de alcance (canales HTTP del WMS, CRUD de Compañías, otras secciones) → respetado; el enum incluye `HttpApi` pero ningún consumidor lo usa (Task 3).

**2. Placeholder scan:** El único stub deliberado es `TestAsync` entre Task 4 y Task 5 (devuelve `ConnectionTestResultDto(false, "no implementado", null)` para no romper la compilación); se completa en Task 5 Step 3 con código real. No hay "TBD"/"agregar validación apropiada" sin cuerpo.

**3. Type consistency:** `ExternalConnectionEditModel` (clase, mutable, secreto write-only) y `ExternalConnectionDto` (record, sin secreto) usados consistentemente en Tasks 3/4/8/9. `ModuleConnectionBindingDto` con los 8 campos idénticos en Task 3 y consumido en Tasks 4/8/9. `ResolveConnectionAsync` overload de 4 args con `string purpose` en Task 6 coincide con el uso en tests de Task 6. `PluginManager.ModulosCargados` — nombre a confirmar contra `Create.cshtml.cs` existente (nota en Task 4 Step 3). `Company` seeding en tests — nota de fallback al helper de `TenantUserAdminServiceTests` en Task 4.
