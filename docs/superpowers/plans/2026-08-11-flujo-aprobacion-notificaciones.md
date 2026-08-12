# Flujo aprobación + notificaciones (Entrega 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Avisar al aprobador por correo y en pantalla cuando le toca actuar sobre un informe, más un recordatorio diario configurable, sin tocar SAP.

**Architecture:** Se reutiliza `IEmailSenderService` (ya existe en `PortalSaas.Abstractions`) inyectándolo directo en `ExpenseReportService` del plugin en los puntos de transición de estado (Submit/Approve/Reject). Dos vacíos reales de infraestructura, descubiertos durante la planificación y ya aprobados por el dueño del proyecto, se resuelven agregando dos métodos chicos y acotados a `PortalSaas.Abstractions`/`PortalSaas.Core` (repo `Portal SaaS - Core`): listar compañías activas de un módulo, y resolver el contacto (email/org/preferencia) de un usuario sin depender del contexto de sesión HTTP. El recordatorio diario vive en un `BackgroundService` nuevo dentro del plugin, mismo patrón que `LicenseActivatorBackgroundService`. El contador en pantalla YA EXISTE (`Aprobaciones/Index.cshtml` línea 17, `@Model.Pending.Count`) — no requiere código nuevo.

**Tech Stack:** .NET 8, ASP.NET Core Razor Pages, EF Core 8 (Npgsql + SqlServer, motor dual), xUnit + EF Core InMemory para tests.

## Global Constraints

- Un plugin referencia SOLO `PortalSaas.Abstractions`, nunca `PortalSaas.Core`/`PortalSaas.Host` (docs/09-GUIA-DESARROLLO-PLUGINS.md §1 del portal).
- Toda tabla nueva del plugin: inglés, plural, snake_case, `id` surrogate `bigint identity`, sin `HasColumnType` específico de un motor (usar `HasPrecision` si aplica) — motor dual Postgres/SqlServer siempre (docs/01-CONVENCION-NOMBRES-BD.md, docs/09-GUIA-DESARROLLO-PLUGINS.md §6).
- Nunca usar el nombre de ningún producto comercial de rendición de gastos existente en el mercado, ni en código ni en UI (ver `ModuloRendiciones.cs:13-15`).
- Un correo caído nunca debe bloquear una transición de estado real (aprobar/rechazar/enviar) — loguear y continuar.
- `CompanyId` siempre obligatorio al resolver `RendicionesDbContext`/`IExternalDatabaseConnectionService` — nunca fallback a Organization.
- Respetar `UserPreference.EmailNotificationsEnabled` antes de mandar cualquier correo de este flujo.
- Solo se notifica al aprobador del nivel que corresponde actuar AHORA (flujo secuencial), nunca a todos los niveles a la vez.

---

## Parte 1 — Cambios en el repo `Portal SaaS - Core`

### Task 1: `IExternalDatabaseConnectionService.ListActiveCompanyIdsAsync`

**Files:**
- Create: `Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/ModuleCompanyDto.cs`
- Modify: `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IExternalDatabaseConnectionService.cs`
- Modify: `Portal SaaS - Core/src/PortalSaas.Core/Infraestructura/ExternalDatabaseConnectionService.cs`
- Test: `Portal SaaS - Core/tests/PortalSaas.Core.Tests/Infraestructura/ExternalDatabaseConnectionServiceTests.cs`

**Interfaces:**
- Produces: `IExternalDatabaseConnectionService.ListActiveCompanyIdsAsync(string moduleCode, CancellationToken ct = default) : Task<IReadOnlyList<ModuleCompanyDto>>`, `ModuleCompanyDto(Guid CompanyId, Guid OrganizationId)`. Usado por Task 7 (el `BackgroundService` del recordatorio) para recorrer todas las compañías con Rendiciones activo.

- [ ] **Step 1: Escribir el test que falla**

```csharp
using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Tests.Infraestructura;

public class ExternalDatabaseConnectionServiceTests
{
    private static PortalSaasDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new PortalSaasDbContext(options);
    }

    [Fact]
    public async Task ListActiveCompanyIdsAsync_devuelve_solo_filas_activas_del_modulo_pedido()
    {
        await using var db = CreateDb(nameof(ListActiveCompanyIdsAsync_devuelve_solo_filas_activas_del_modulo_pedido));

        var org = new Organization { Id = Guid.NewGuid(), Name = "Org Test" };
        var companyA = new Company { Id = Guid.NewGuid(), OrganizationId = org.Id, Name = "Company A" };
        var companyB = new Company { Id = Guid.NewGuid(), OrganizationId = org.Id, Name = "Company B" };
        db.Organizations.Add(org);
        db.Companies.AddRange(companyA, companyB);

        db.ModuleExternalConnections.AddRange(
            new ModuleExternalConnection
            {
                CompanyId = companyA.Id, ModuleCode = "Rendiciones", IsActive = true,
                EngineType = ModuleExternalConnectionEngineType.Postgres,
                Host = "h", Port = 5432, DatabaseName = "db", TechnicalUsername = "u", TechnicalSecretKey = "k",
            },
            new ModuleExternalConnection
            {
                CompanyId = companyB.Id, ModuleCode = "Rendiciones", IsActive = false,
                EngineType = ModuleExternalConnectionEngineType.Postgres,
                Host = "h", Port = 5432, DatabaseName = "db", TechnicalUsername = "u", TechnicalSecretKey = "k",
            },
            new ModuleExternalConnection
            {
                CompanyId = companyA.Id, ModuleCode = "OtroModulo", IsActive = true,
                EngineType = ModuleExternalConnectionEngineType.Postgres,
                Host = "h", Port = 5432, DatabaseName = "db", TechnicalUsername = "u", TechnicalSecretKey = "k",
            });
        await db.SaveChangesAsync();

        var sut = new ExternalDatabaseConnectionService(db, new FakeSecretoCifradoService());

        var result = await sut.ListActiveCompanyIdsAsync("Rendiciones");

        var item = Assert.Single(result);
        Assert.Equal(companyA.Id, item.CompanyId);
        Assert.Equal(org.Id, item.OrganizationId);
    }

    private sealed class FakeSecretoCifradoService : PortalSaas.Abstractions.Contratos.ISecretoCifradoService
    {
        public string Encrypt(string plainText) => plainText;
        public string Decrypt(string cipherText) => cipherText;
    }
}
```

- [ ] **Step 2: Confirmar que falla**

Run: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter ExternalDatabaseConnectionServiceTests`
Expected: FAIL en compilación — `ListActiveCompanyIdsAsync` no existe todavía.

- [ ] **Step 3: Agregar el DTO**

`Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/ModuleCompanyDto.cs`:

```csharp
namespace PortalSaas.Abstractions.Modelos;

/// <summary>Compañía con una conexión externa activa para un módulo -- ver IExternalDatabaseConnectionService.ListActiveCompanyIdsAsync.</summary>
public sealed record ModuleCompanyDto(Guid CompanyId, Guid OrganizationId);
```

- [ ] **Step 4: Extender el contrato**

En `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IExternalDatabaseConnectionService.cs`, agregar dentro de la interfaz, después de `ResolveConnectionAsync`:

```csharp
    /// <summary>
    /// Lista (CompanyId, OrganizationId) de toda compañía con una conexión ACTIVA
    /// configurada para moduleCode -- para procesos sin sesión HTTP (background jobs)
    /// que necesitan recorrer todas las compañías de un módulo, algo que
    /// ResolveConnectionAsync no puede hacer porque ya exige conocer el companyId.
    /// </summary>
    Task<IReadOnlyList<ModuleCompanyDto>> ListActiveCompanyIdsAsync(
        string moduleCode,
        CancellationToken ct = default);
```

- [ ] **Step 5: Implementar**

En `Portal SaaS - Core/src/PortalSaas.Core/Infraestructura/ExternalDatabaseConnectionService.cs`, agregar el método a la clase:

```csharp
    public async Task<IReadOnlyList<ModuleCompanyDto>> ListActiveCompanyIdsAsync(
        string moduleCode,
        CancellationToken ct = default)
    {
        return await _db.ModuleExternalConnections
            .AsNoTracking()
            .Where(x => x.ModuleCode == moduleCode && x.IsActive)
            .Select(x => new ModuleCompanyDto(x.CompanyId, x.Company.OrganizationId))
            .Distinct()
            .ToListAsync(ct);
    }
```

- [ ] **Step 6: Confirmar que pasa**

Run: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter ExternalDatabaseConnectionServiceTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git -C "Portal SaaS - Core" add src/PortalSaas.Abstractions/Modelos/ModuleCompanyDto.cs src/PortalSaas.Abstractions/Contratos/IExternalDatabaseConnectionService.cs src/PortalSaas.Core/Infraestructura/ExternalDatabaseConnectionService.cs tests/PortalSaas.Core.Tests/Infraestructura/ExternalDatabaseConnectionServiceTests.cs
git -C "Portal SaaS - Core" commit -m "feat: listar compañías activas por módulo para jobs sin sesión HTTP"
```

---

### Task 2: `IUserContactLookupService`

**Files:**
- Create: `Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/UserContactDto.cs`
- Create: `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IUserContactLookupService.cs`
- Create: `Portal SaaS - Core/src/PortalSaas.Core/Infraestructura/UserContactLookupService.cs`
- Modify: `Portal SaaS - Core/src/PortalSaas.Host/Program.cs`
- Test: `Portal SaaS - Core/tests/PortalSaas.Core.Tests/Infraestructura/UserContactLookupServiceTests.cs`

**Interfaces:**
- Produces: `IUserContactLookupService.GetContactAsync(Guid userId, CancellationToken ct = default) : Task<UserContactDto?>`, `UserContactDto(Guid OrganizationId, string Email, bool EmailNotificationsEnabled)`. Usado por Task 6 (notificación en tiempo real) y Task 7 (recordatorio diario) del plugin -- ambos flujos necesitan el email de un usuario que no es necesariamente el usuario logueado en la sesión actual (o no hay sesión, en el caso del background job).

- [ ] **Step 1: Escribir el test que falla**

```csharp
using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Tests.Infraestructura;

public class UserContactLookupServiceTests
{
    private static PortalSaasDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new PortalSaasDbContext(options);
    }

    [Fact]
    public async Task GetContactAsync_devuelve_email_organizacion_y_preferencia_de_notificacion()
    {
        await using var db = CreateDb(nameof(GetContactAsync_devuelve_email_organizacion_y_preferencia_de_notificacion));

        var org = new Organization { Id = Guid.NewGuid(), Name = "Org Test" };
        var user = new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Username = "aprobador1",
            Email = "aprobador1@test.cl",
            PasswordHash = "x",
            PasswordSalt = "x",
        };
        db.Organizations.Add(org);
        db.Users.Add(user);
        db.UserPreferences.Add(new UserPreference { UserId = user.Id, EmailNotificationsEnabled = false });
        await db.SaveChangesAsync();

        var sut = new UserContactLookupService(db);

        var result = await sut.GetContactAsync(user.Id);

        Assert.NotNull(result);
        Assert.Equal(org.Id, result!.OrganizationId);
        Assert.Equal("aprobador1@test.cl", result.Email);
        Assert.False(result.EmailNotificationsEnabled);
    }

    [Fact]
    public async Task GetContactAsync_sin_fila_de_preferencia_asume_notificaciones_habilitadas()
    {
        await using var db = CreateDb(nameof(GetContactAsync_sin_fila_de_preferencia_asume_notificaciones_habilitadas));

        var org = new Organization { Id = Guid.NewGuid(), Name = "Org Test" };
        var user = new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Username = "aprobador2",
            Email = "aprobador2@test.cl",
            PasswordHash = "x",
            PasswordSalt = "x",
        };
        db.Organizations.Add(org);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = new UserContactLookupService(db);

        var result = await sut.GetContactAsync(user.Id);

        Assert.NotNull(result);
        Assert.True(result!.EmailNotificationsEnabled);
    }

    [Fact]
    public async Task GetContactAsync_usuario_inexistente_devuelve_null()
    {
        await using var db = CreateDb(nameof(GetContactAsync_usuario_inexistente_devuelve_null));
        var sut = new UserContactLookupService(db);

        var result = await sut.GetContactAsync(Guid.NewGuid());

        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Confirmar que falla**

Run: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter UserContactLookupServiceTests`
Expected: FAIL en compilación — `UserContactLookupService`/`IUserContactLookupService` no existen todavía.

- [ ] **Step 3: DTO + contrato**

`Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/UserContactDto.cs`:

```csharp
namespace PortalSaas.Abstractions.Modelos;

/// <summary>Datos de contacto mínimos de un usuario -- ver IUserContactLookupService.</summary>
public sealed record UserContactDto(Guid OrganizationId, string Email, bool EmailNotificationsEnabled);
```

`Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IUserContactLookupService.cs`:

```csharp
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Resuelve el contacto (organización, email, preferencia de notificaciones) de
/// CUALQUIER usuario de la plataforma por su Id -- a diferencia de
/// ICurrentUserContext (solo el usuario del request actual) o ITenantUserAdminService
/// (acotado a la organización del usuario logueado en sesión), este contrato no
/// depende de haber una sesión HTTP activa: lo usan procesos como notificaciones de
/// aprobación o background jobs que necesitan el email de un tercero (ej. el
/// aprobador de un nivel), o que corren sin ningún usuario logueado (ej. un
/// recordatorio diario programado). userId siempre proviene de datos ya resueltos y
/// de confianza del propio plugin (ej. ExpenseApprovalGroupLevel.UserId), nunca de un
/// valor recibido directo de la UI sin validar antes.
/// </summary>
public interface IUserContactLookupService
{
    Task<UserContactDto?> GetContactAsync(Guid userId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implementar**

`Portal SaaS - Core/src/PortalSaas.Core/Infraestructura/UserContactLookupService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;

namespace PortalSaas.Core.Infraestructura;

/// <summary>Implementación real de IUserContactLookupService -- lee directo de Users/UserPreferences, sin acotar por organización del llamador (ver el contrato para el porqué).</summary>
public sealed class UserContactLookupService : IUserContactLookupService
{
    private readonly PortalSaasDbContext _db;

    public UserContactLookupService(PortalSaasDbContext db)
    {
        _db = db;
    }

    public async Task<UserContactDto?> GetContactAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Preference)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
            return null;

        return new UserContactDto(
            user.OrganizationId,
            user.Email,
            user.Preference?.EmailNotificationsEnabled ?? true);
    }
}
```

- [ ] **Step 5: Registrar en DI**

En `Portal SaaS - Core/src/PortalSaas.Host/Program.cs`, junto a la línea 106 (`AddScoped<ITenantUserAdminService...`), agregar:

```csharp
builder.Services.AddScoped<IUserContactLookupService, UserContactLookupService>();
```

- [ ] **Step 6: Confirmar que pasa**

Run: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter UserContactLookupServiceTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git -C "Portal SaaS - Core" add src/PortalSaas.Abstractions/Modelos/UserContactDto.cs src/PortalSaas.Abstractions/Contratos/IUserContactLookupService.cs src/PortalSaas.Core/Infraestructura/UserContactLookupService.cs src/PortalSaas.Host/Program.cs tests/PortalSaas.Core.Tests/Infraestructura/UserContactLookupServiceTests.cs
git -C "Portal SaaS - Core" commit -m "feat: resolver contacto de cualquier usuario sin depender de sesión HTTP"
```

---

## Parte 2 — Cambios en el plugin `Modulo.Rendiciones`

### Task 3: Modelos y mapeo EF Core de `rendiciones_settings`/`rendiciones_reminder_log`

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Models/RendicionesSettings.cs`
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Models/ReminderLog.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Data/RendicionesDbContext.cs`

**Interfaces:**
- Produces: `RendicionesSettings { Id, ReminderHour: TimeOnly, ReminderEnabled: bool, UpdatedAt: DateTimeOffset }` (fila única, `Id` siempre `1`), `ReminderLog { Id, SentDate: DateOnly }`. Usados por Task 7 (BackgroundService) y Task 8 (página admin).

- [ ] **Step 1: Modelos**

`Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Models/RendicionesSettings.cs`:

```csharp
namespace Modulo.Rendiciones.Models;

/// <summary>
/// Configuración global del recordatorio diario -- fila única (Id siempre 1), no hay
/// concepto de "settings globales de plataforma" reusable en el portal todavía (ver
/// docs/superpowers/specs/2026-08-11-flujo-aprobacion-notificaciones-design.md),
/// así que vive acá, propia del plugin.
/// </summary>
public class RendicionesSettings
{
    public long Id { get; set; }

    public TimeOnly ReminderHour { get; set; } = new(8, 0);

    public bool ReminderEnabled { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

`Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Models/ReminderLog.cs`:

```csharp
namespace Modulo.Rendiciones.Models;

/// <summary>
/// Dedupe del recordatorio diario -- una fila por día en que ya se mandó, evita
/// reenviar el mismo día si RendicionesReminderBackgroundService reinicia.
/// </summary>
public class ReminderLog
{
    public long Id { get; set; }

    public required DateOnly SentDate { get; set; }
}
```

- [ ] **Step 2: Mapeo en el DbContext**

En `RendicionesDbContext.cs`, agregar los dos `DbSet` (junto a los existentes, línea 40):

```csharp
    public DbSet<RendicionesSettings> RendicionesSettings => Set<RendicionesSettings>();
    public DbSet<ReminderLog> ReminderLogs => Set<ReminderLog>();
```

Y dentro de `OnModelCreating`, después del bloque de `ExternalServiceUsage` (línea 308, antes del cierre del método):

```csharp
        modelBuilder.Entity<RendicionesSettings>(e =>
        {
            e.ToTable("rendiciones_settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ReminderHour).HasColumnName("reminder_hour").IsRequired();
            e.Property(x => x.ReminderEnabled).HasColumnName("reminder_enabled");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<ReminderLog>(e =>
        {
            e.ToTable("rendiciones_reminder_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.SentDate).HasColumnName("sent_date").IsRequired();
            e.HasIndex(x => x.SentDate).IsUnique().HasDatabaseName("uq_rendiciones_reminder_log_sent_date");
        });
```

- [ ] **Step 3: Verificar que compila**

Run: `dotnet build "Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Modulo.Rendiciones.csproj"`
Expected: Build succeeded, 0 errores.

- [ ] **Step 4: Commit**

```bash
cd "Portal SaaS - Plugins/Modulo.Rendiciones"
git add src/Modulo.Rendiciones/Models/RendicionesSettings.cs src/Modulo.Rendiciones/Models/ReminderLog.cs src/Modulo.Rendiciones/Data/RendicionesDbContext.cs
git commit -m "feat: modelos rendiciones_settings/rendiciones_reminder_log"
```

---

### Task 4: Migraciones EF Core (Postgres + SqlServer)

**Files:**
- Create (generado por `dotnet ef`): `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Migrations.Postgres/Migrations/*_AddRendicionesSettingsAndReminderLog.cs` (+ `.Designer.cs`, snapshot actualizado)
- Create (generado por `dotnet ef`): `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Migrations.SqlServer/Migrations/*_AddRendicionesSettingsAndReminderLog.cs` (+ `.Designer.cs`, snapshot actualizado)

**Interfaces:**
- Consumes: `RendicionesSettings`/`ReminderLog` de Task 3.

- [ ] **Step 1: Generar la migración Postgres**

Run (desde la raíz del repo del plugin):
```bash
cd "Portal SaaS - Plugins/Modulo.Rendiciones"
dotnet ef migrations add AddRendicionesSettingsAndReminderLog \
  --project src/Modulo.Rendiciones.Migrations.Postgres \
  --startup-project src/Modulo.Rendiciones.Migrations.Postgres
```
Expected: dos archivos nuevos bajo `Migrations/`, con `CreateTable("rendiciones_settings", ...)` y `CreateTable("rendiciones_reminder_log", ...)` en el `Up()` — revisar el `.cs` generado antes de aplicar, confirmar que NO aparece ningún `AlterColumn` sobre tablas existentes (mismo bug real ya documentado en `PENDIENTE.md` Fase 4 si los proyectos de migraciones se mezclaran).

- [ ] **Step 2: Generar la migración SQL Server**

```bash
dotnet ef migrations add AddRendicionesSettingsAndReminderLog \
  --project src/Modulo.Rendiciones.Migrations.SqlServer \
  --startup-project src/Modulo.Rendiciones.Migrations.SqlServer
```
Expected: mismo resultado, dos `CreateTable` reales.

- [ ] **Step 3: Aplicar contra las bases de desarrollo reales**

```bash
dotnet ef database update --project src/Modulo.Rendiciones.Migrations.Postgres --startup-project src/Modulo.Rendiciones.Migrations.Postgres
dotnet ef database update --project src/Modulo.Rendiciones.Migrations.SqlServer --startup-project src/Modulo.Rendiciones.Migrations.SqlServer
```
Expected: sin errores; confirmar con `\d rendiciones_settings` / `\d rendiciones_reminder_log` en Postgres (o `sp_help` en SQL Server) que las columnas y el índice único quedaron creados.

- [ ] **Step 4: Insertar la fila única de settings**

La tabla `rendiciones_settings` necesita su única fila (`Id = 1`) antes de que la página admin (Task 8) pueda editarla — insertarla a mano una vez contra cada base de desarrollo:

Postgres: `INSERT INTO rendiciones_settings (id, reminder_hour, reminder_enabled, updated_at) VALUES (1, '08:00:00', true, now());`
SQL Server: `INSERT INTO rendiciones_settings (id, reminder_hour, reminder_enabled, updated_at) VALUES (1, '08:00:00', 1, SYSDATETIMEOFFSET());`

- [ ] **Step 5: Commit**

```bash
git add src/Modulo.Rendiciones.Migrations.Postgres/Migrations/ src/Modulo.Rendiciones.Migrations.SqlServer/Migrations/
git commit -m "feat: migraciones rendiciones_settings/rendiciones_reminder_log (Postgres + SqlServer)"
```

---

### Task 5: Proyecto de tests del plugin (scaffolding, no existía)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Modulo.Rendiciones.Tests.csproj`
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Fakes/FakeEmailSenderService.cs`
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Fakes/FakeUserContactLookupService.cs`

**Interfaces:**
- Produces: `FakeEmailSenderService : IEmailSenderService` con `List<(Guid OrganizationId, EmailMessage Message)> Sent` capturando cada llamada. `FakeUserContactLookupService : IUserContactLookupService` con un diccionario `Guid -> UserContactDto` cargable desde el test. Usados por Task 6 y Task 7.

No hay proyecto de tests en este repo todavía (`PENDIENTE.md` solo menciona los 109/109 del repo `Portal SaaS - Core`) — se crea siguiendo el mismo esqueleto que `PortalSaas.Core.Tests`.

- [ ] **Step 1: Crear el csproj**

`Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Modulo.Rendiciones.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.8" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Modulo.Rendiciones\Modulo.Rendiciones.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Fakes**

`Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Fakes/FakeEmailSenderService.cs`:

```csharp
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Tests.Fakes;

public sealed class FakeEmailSenderService : IEmailSenderService
{
    public List<(Guid OrganizationId, EmailMessage Message)> Sent { get; } = new();

    public Task SendAsync(Guid organizationId, EmailMessage message, CancellationToken ct = default)
    {
        Sent.Add((organizationId, message));
        return Task.CompletedTask;
    }
}
```

`Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Fakes/FakeUserContactLookupService.cs`:

```csharp
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Tests.Fakes;

public sealed class FakeUserContactLookupService : IUserContactLookupService
{
    private readonly Dictionary<Guid, UserContactDto> _contacts = new();

    public FakeUserContactLookupService With(Guid userId, UserContactDto contact)
    {
        _contacts[userId] = contact;
        return this;
    }

    public Task<UserContactDto?> GetContactAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_contacts.TryGetValue(userId, out var contact) ? contact : null);
}
```

- [ ] **Step 3: Verificar que compila (sin tests todavía, es normal)**

Run: `dotnet build "Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Modulo.Rendiciones.Tests.csproj"`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Modulo.Rendiciones.Tests/
git commit -m "test: scaffolding de proyecto de tests del plugin (no existía)"
```

---

### Task 6: Notificación por correo en `ExpenseReportService` (Submit/Approve/Reject)

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/ExpenseReportService.cs`
- Test: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Servicios/ExpenseReportServiceNotificationTests.cs`

**Interfaces:**
- Consumes: `IEmailSenderService.SendAsync(Guid organizationId, EmailMessage message, ct)`, `IUserContactLookupService.GetContactAsync(Guid userId, ct)` (Task 2), `IExpenseApprovalGroupService.GetLevelsAsync(long groupId, ct) : Task<IReadOnlyDictionary<int, Guid>>` (ya existe).
- Produces: `ExpenseReportService` con dos dependencias nuevas en el constructor (`IEmailSenderService`, `IUserContactLookupService`) — Task 9 debe actualizar el registro en `ModuloRendiciones.RegisterServices` si hace falta (no debería, `IEmailSenderService` ya viene resuelto por el Host vía `PortalSaas.Abstractions`, y `IUserContactLookupService` también, ambos quedaron registrados en Task 2).

- [ ] **Step 1: Escribir los tests que fallan**

`Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Servicios/ExpenseReportServiceNotificationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using Modulo.Rendiciones.Tests.Fakes;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Tests.Servicios;

public class ExpenseReportServiceNotificationTests
{
    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
    }

    private sealed class StubApprovalGroupService : IExpenseApprovalGroupService
    {
        public Guid Level1Approver;
        public Guid Level2Approver;

        public Task<IReadOnlyList<ExpenseApprovalGroup>> ListAsync(Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseApprovalGroup?> GetAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseApprovalGroup?> GetUserGroupAsync(Guid companyId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<ExpenseApprovalGroup?>(new ExpenseApprovalGroup { Id = 1, CompanyId = companyId, Name = "Grupo Test" });
        public Task<IReadOnlyList<ExpenseApprovalGroupMember>> ListMembersAsync(long groupId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<int, Guid>> GetLevelsAsync(long groupId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, Guid>>(new Dictionary<int, Guid> { [1] = Level1Approver, [2] = Level2Approver });
        public Task<long> CreateAsync(Guid companyId, string name, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddMemberAsync(long groupId, Guid companyId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveMemberAsync(long groupId, Guid companyId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetLevelAsync(long groupId, Guid companyId, int level, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveLevelAsync(long groupId, Guid companyId, int level, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class NoopFundService : IExpenseFundService
    {
        public Task<IReadOnlyList<ExpenseFund>> ListByUserAsync(Guid companyId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> CreateAsync(ExpenseFund fund, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<decimal> CalculatePendingBalanceAsync(long fundId, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RecalculateStatusAsync(long fundId, Guid companyId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopAttachmentService : IAttachmentStorageService
    {
        public Task<long> SaveAsync(Guid companyId, Guid userId, string fileName, string mimeType, byte[] content, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpenseReceipt?> GetAsync(long id, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task SubmitAsync_notifica_por_correo_al_aprobador_de_nivel_1()
    {
        var companyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var approver1Id = Guid.NewGuid();

        await using var db = CreateDb(nameof(SubmitAsync_notifica_por_correo_al_aprobador_de_nivel_1));
        var report = new ExpenseReport { CompanyId = companyId, UserId = requesterId, Status = "Draft" };
        db.ExpenseReports.Add(report);
        db.ExpenseReportLines.Add(new ExpenseReportLine
        {
            CompanyId = companyId, UserId = requesterId, Status = "InReport", ExpenseReportId = report.Id,
            Amount = 1000, Currency = "CLP",
        });
        await db.SaveChangesAsync();

        var groups = new StubApprovalGroupService { Level1Approver = approver1Id, Level2Approver = approver1Id };
        var emails = new FakeEmailSenderService();
        var orgId = Guid.NewGuid();
        var contacts = new FakeUserContactLookupService()
            .With(approver1Id, new UserContactDto(orgId, "aprobador1@test.cl", true));

        var sut = new ExpenseReportService(db, groups, new NoopFundService(), new NoopAttachmentService(), emails, contacts);

        await sut.SubmitAsync(report.Id, companyId);

        var sent = Assert.Single(emails.Sent);
        Assert.Equal(orgId, sent.OrganizationId);
        Assert.Equal("aprobador1@test.cl", sent.Message.ToEmail);
    }

    [Fact]
    public async Task SubmitAsync_no_notifica_si_el_aprobador_desactivo_las_notificaciones_por_correo()
    {
        var companyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var approver1Id = Guid.NewGuid();

        await using var db = CreateDb(nameof(SubmitAsync_no_notifica_si_el_aprobador_desactivo_las_notificaciones_por_correo));
        var report = new ExpenseReport { CompanyId = companyId, UserId = requesterId, Status = "Draft" };
        db.ExpenseReports.Add(report);
        db.ExpenseReportLines.Add(new ExpenseReportLine
        {
            CompanyId = companyId, UserId = requesterId, Status = "InReport", ExpenseReportId = report.Id,
            Amount = 1000, Currency = "CLP",
        });
        await db.SaveChangesAsync();

        var groups = new StubApprovalGroupService { Level1Approver = approver1Id, Level2Approver = approver1Id };
        var emails = new FakeEmailSenderService();
        var contacts = new FakeUserContactLookupService()
            .With(approver1Id, new UserContactDto(Guid.NewGuid(), "aprobador1@test.cl", false));

        var sut = new ExpenseReportService(db, groups, new NoopFundService(), new NoopAttachmentService(), emails, contacts);

        await sut.SubmitAsync(report.Id, companyId);

        Assert.Empty(emails.Sent);
    }

    [Fact]
    public async Task ApproveAsync_con_mas_niveles_notifica_al_siguiente_aprobador()
    {
        var companyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var approver1Id = Guid.NewGuid();
        var approver2Id = Guid.NewGuid();

        await using var db = CreateDb(nameof(ApproveAsync_con_mas_niveles_notifica_al_siguiente_aprobador));
        var report = new ExpenseReport
        {
            CompanyId = companyId, UserId = requesterId, Status = "Pending",
            ExpenseApprovalGroupId = 1, CurrentLevel = 1,
        };
        db.ExpenseReports.Add(report);
        await db.SaveChangesAsync();

        var groups = new StubApprovalGroupService { Level1Approver = approver1Id, Level2Approver = approver2Id };
        var emails = new FakeEmailSenderService();
        var orgId = Guid.NewGuid();
        var contacts = new FakeUserContactLookupService()
            .With(approver2Id, new UserContactDto(orgId, "aprobador2@test.cl", true));

        var sut = new ExpenseReportService(db, groups, new NoopFundService(), new NoopAttachmentService(), emails, contacts);

        await sut.ApproveAsync(report.Id, companyId, approver1Id, comment: null);

        var sent = Assert.Single(emails.Sent);
        Assert.Equal("aprobador2@test.cl", sent.Message.ToEmail);
    }

    [Fact]
    public async Task ApproveAsync_ultimo_nivel_notifica_al_dueno_del_informe()
    {
        var companyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var approver1Id = Guid.NewGuid();

        await using var db = CreateDb(nameof(ApproveAsync_ultimo_nivel_notifica_al_dueno_del_informe));
        var report = new ExpenseReport
        {
            CompanyId = companyId, UserId = requesterId, Status = "Pending",
            ExpenseApprovalGroupId = 1, CurrentLevel = 1,
        };
        db.ExpenseReports.Add(report);
        await db.SaveChangesAsync();

        // Nivel 2 == solicitante -> ResolveNextLevel salta ese nivel -> queda Approved.
        var groups = new StubApprovalGroupService { Level1Approver = approver1Id, Level2Approver = requesterId };
        var emails = new FakeEmailSenderService();
        var orgId = Guid.NewGuid();
        var contacts = new FakeUserContactLookupService()
            .With(requesterId, new UserContactDto(orgId, "dueno@test.cl", true));

        var sut = new ExpenseReportService(db, groups, new NoopFundService(), new NoopAttachmentService(), emails, contacts);

        await sut.ApproveAsync(report.Id, companyId, approver1Id, comment: null);

        var sent = Assert.Single(emails.Sent);
        Assert.Equal("dueno@test.cl", sent.Message.ToEmail);
    }

    [Fact]
    public async Task RejectAsync_notifica_al_dueno_del_informe()
    {
        var companyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var approver1Id = Guid.NewGuid();

        await using var db = CreateDb(nameof(RejectAsync_notifica_al_dueno_del_informe));
        var report = new ExpenseReport
        {
            CompanyId = companyId, UserId = requesterId, Status = "Pending",
            ExpenseApprovalGroupId = 1, CurrentLevel = 1,
        };
        db.ExpenseReports.Add(report);
        await db.SaveChangesAsync();

        var groups = new StubApprovalGroupService { Level1Approver = approver1Id, Level2Approver = approver1Id };
        var emails = new FakeEmailSenderService();
        var orgId = Guid.NewGuid();
        var contacts = new FakeUserContactLookupService()
            .With(requesterId, new UserContactDto(orgId, "dueno@test.cl", true));

        var sut = new ExpenseReportService(db, groups, new NoopFundService(), new NoopAttachmentService(), emails, contacts);

        await sut.RejectAsync(report.Id, companyId, approver1Id, comment: "Falta comprobante");

        var sent = Assert.Single(emails.Sent);
        Assert.Equal("dueno@test.cl", sent.Message.ToEmail);
        Assert.Contains("Falta comprobante", sent.Message.HtmlBody);
    }

    [Fact]
    public async Task SubmitAsync_continua_si_el_envio_de_correo_falla()
    {
        var companyId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var approver1Id = Guid.NewGuid();

        await using var db = CreateDb(nameof(SubmitAsync_continua_si_el_envio_de_correo_falla));
        var report = new ExpenseReport { CompanyId = companyId, UserId = requesterId, Status = "Draft" };
        db.ExpenseReports.Add(report);
        db.ExpenseReportLines.Add(new ExpenseReportLine
        {
            CompanyId = companyId, UserId = requesterId, Status = "InReport", ExpenseReportId = report.Id,
            Amount = 1000, Currency = "CLP",
        });
        await db.SaveChangesAsync();

        var groups = new StubApprovalGroupService { Level1Approver = approver1Id, Level2Approver = approver1Id };
        var contacts = new FakeUserContactLookupService()
            .With(approver1Id, new UserContactDto(Guid.NewGuid(), "aprobador1@test.cl", true));

        var sut = new ExpenseReportService(db, groups, new NoopFundService(), new NoopAttachmentService(), new ThrowingEmailSenderService(), contacts);

        await sut.SubmitAsync(report.Id, companyId);

        var reloaded = await db.ExpenseReports.FirstAsync(r => r.Id == report.Id);
        Assert.Equal("Pending", reloaded.Status);
    }

    private sealed class ThrowingEmailSenderService : PortalSaas.Abstractions.Contratos.IEmailSenderService
    {
        public Task SendAsync(Guid organizationId, EmailMessage message, CancellationToken ct = default) =>
            throw new InvalidOperationException("Proveedor de correo no configurado (simulado en el test).");
    }
}
```

- [ ] **Step 2: Confirmar que falla**

Run: `dotnet test "Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests" --filter ExpenseReportServiceNotificationTests`
Expected: FAIL en compilación — el constructor de `ExpenseReportService` todavía no acepta `IEmailSenderService`/`IUserContactLookupService`.

- [ ] **Step 3: Implementar**

Reescribir `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/ExpenseReportService.cs` completo:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Servicios;

public sealed class ExpenseReportService : IExpenseReportService
{
    private readonly RendicionesDbContext _db;
    private readonly IExpenseApprovalGroupService _groups;
    private readonly IExpenseFundService _funds;
    private readonly IAttachmentStorageService _attachments;
    private readonly IEmailSenderService _emailSender;
    private readonly IUserContactLookupService _contacts;

    public ExpenseReportService(RendicionesDbContext db, IExpenseApprovalGroupService groups, IExpenseFundService funds,
        IAttachmentStorageService attachments, IEmailSenderService emailSender, IUserContactLookupService contacts)
    {
        _db = db;
        _groups = groups;
        _funds = funds;
        _attachments = attachments;
        _emailSender = emailSender;
        _contacts = contacts;
    }

    public async Task<IReadOnlyList<ExpenseReport>> ListByUserAsync(Guid companyId, Guid userId, CancellationToken ct = default) =>
        await _db.ExpenseReports
            .Include(r => r.Lines)
            .Where(r => r.CompanyId == companyId && r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<ExpenseReport?> GetAsync(long id, Guid companyId, CancellationToken ct = default) =>
        await _db.ExpenseReports
            .Include(r => r.Lines).ThenInclude(d => d.ExpenseType)
            .FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId, ct);

    public async Task<long> CreateReportAsync(Guid companyId, Guid userId, IReadOnlyList<long> expenseIds, long? expenseFundId,
        string? costCenterCode, string? costCenterName, CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var report = new ExpenseReport
        {
            CompanyId = companyId,
            UserId = userId,
            ExpenseFundId = expenseFundId,
            CostCenterCode = costCenterCode,
            CostCenterName = costCenterName,
        };
        _db.ExpenseReports.Add(report);
        await _db.SaveChangesAsync(ct);

        if (expenseIds.Count > 0)
            await AttachExpensesAsync(report.Id, companyId, expenseIds, ct);

        await transaction.CommitAsync(ct);
        return report.Id;
    }

    public async Task UpdateHeaderAsync(long reportId, Guid companyId, long? expenseFundId,
        string? costCenterCode, string? costCenterName, CancellationToken ct = default)
    {
        var report = await RequireEditableAsync(reportId, companyId, ct);
        report.ExpenseFundId = expenseFundId;
        report.CostCenterCode = costCenterCode;
        report.CostCenterName = costCenterName;
        await _db.SaveChangesAsync(ct);
    }

    public async Task AttachExpensesAsync(long reportId, Guid companyId, IReadOnlyList<long> expenseIds, CancellationToken ct = default)
    {
        if (expenseIds.Count == 0)
            return;

        var report = await RequireEditableAsync(reportId, companyId, ct);

        var expenses = await _db.ExpenseReportLines
            .Where(d => expenseIds.Contains(d.Id) && d.CompanyId == companyId && d.UserId == report.UserId && d.Status == "Loose")
            .ToListAsync(ct);

        if (expenses.Count != expenseIds.Count)
            throw new InvalidOperationException("Alguno de los gastos seleccionados no existe, no es tuyo, o ya no está Loose.");

        if (expenses.Any(g => g.ExpenseTypeId is null))
            throw new InvalidOperationException("Hay gastos sin categoría -- completala antes de agregarlos a un informe (por ejemplo, los importados por OCR que todavía no se revisaron).");

        foreach (var expense in expenses)
        {
            expense.ExpenseReportId = reportId;
            expense.Status = "InReport";
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task DetachExpenseAsync(long reportId, long lineId, Guid companyId, CancellationToken ct = default)
    {
        await RequireEditableAsync(reportId, companyId, ct);

        var line = await _db.ExpenseReportLines
            .FirstOrDefaultAsync(d => d.Id == lineId && d.ExpenseReportId == reportId, ct)
            ?? throw new InvalidOperationException("El gasto no existe en este informe.");

        line.ExpenseReportId = null;
        line.Status = "Loose";
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveReceiptAsync(long reportId, long lineId, Guid companyId, CancellationToken ct = default)
    {
        await RequireEditableAsync(reportId, companyId, ct);

        var line = await _db.ExpenseReportLines
            .FirstOrDefaultAsync(d => d.Id == lineId && d.ExpenseReportId == reportId, ct)
            ?? throw new InvalidOperationException("El gasto no existe en este informe.");

        if (line.ExpenseReceiptId is not { } receiptId)
            return;

        line.ExpenseReceiptId = null;
        await _db.SaveChangesAsync(ct);
        await _attachments.DeleteAsync(receiptId, companyId, ct);
    }

    public async Task DeleteReportAsync(long reportId, Guid companyId, Guid userId, CancellationToken ct = default)
    {
        var report = await _db.ExpenseReports
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == reportId && r.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("El informe no existe o no pertenece a esta compañía.");

        if (report.Status != "Draft")
            throw new InvalidOperationException("Solo se puede eliminar un informe en estado Draft.");

        if (report.UserId != userId)
            throw new InvalidOperationException("Solo quien creó el informe puede eliminarlo.");

        foreach (var line in report.Lines)
        {
            line.ExpenseReportId = null;
            line.Status = "Loose";
        }

        _db.ExpenseReports.Remove(report);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SubmitAsync(long reportId, Guid companyId, CancellationToken ct = default)
    {
        var report = await RequireEditableAsync(reportId, companyId, ct);

        var hasLines = await _db.ExpenseReportLines.AnyAsync(d => d.ExpenseReportId == reportId, ct);
        if (!hasLines)
            throw new InvalidOperationException("La rendición necesita al menos una línea de gasto antes de enviarla.");

        var group = await _groups.GetUserGroupAsync(companyId, report.UserId, ct);
        report.SubmittedAt = DateTimeOffset.UtcNow;

        if (group is null)
        {
            report.Status = "Approved";
            report.ResolvedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            await RecalculateFundIfApplicableAsync(report, companyId, ct);
            return;
        }

        var levels = await _groups.GetLevelsAsync(group.Id, ct);
        var nextLevel = IExpenseApprovalGroupService.ResolveNextLevel(levels, levelFrom: 1, report.UserId);

        report.ExpenseApprovalGroupId = group.Id;
        if (nextLevel is null)
        {
            report.Status = "Approved";
            report.ResolvedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            report.Status = "Pending";
            report.CurrentLevel = nextLevel;
        }

        await _db.SaveChangesAsync(ct);

        if (report.Status == "Approved")
        {
            await RecalculateFundIfApplicableAsync(report, companyId, ct);
        }
        else
        {
            await NotifyApproverAsync(report, levels[nextLevel!.Value], ct);
        }
    }

    public async Task ApproveAsync(long reportId, Guid companyId, Guid approverUserId, string? comment, CancellationToken ct = default)
    {
        var report = await RequirePendingAndApproverAsync(reportId, companyId, approverUserId, ct);
        var resolvedLevel = report.CurrentLevel!.Value;

        _db.ExpenseReportActions.Add(new ExpenseReportAction
        {
            ExpenseReportId = reportId,
            Level = resolvedLevel,
            UserId = approverUserId,
            Decision = "Approved",
            Comment = comment,
        });

        var levels = await _groups.GetLevelsAsync(report.ExpenseApprovalGroupId!.Value, ct);
        var nextLevel = IExpenseApprovalGroupService.ResolveNextLevel(levels, levelFrom: resolvedLevel + 1, report.UserId);

        if (nextLevel is null)
        {
            report.Status = "Approved";
            report.CurrentLevel = null;
            report.ResolvedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            report.CurrentLevel = nextLevel;
        }

        await _db.SaveChangesAsync(ct);

        if (report.Status == "Approved")
        {
            await RecalculateFundIfApplicableAsync(report, companyId, ct);
            await NotifyReportOwnerAsync(report, "Tu informe fue aprobado.", ct);
        }
        else
        {
            await NotifyApproverAsync(report, levels[nextLevel!.Value], ct);
        }
    }

    public async Task RejectAsync(long reportId, Guid companyId, Guid approverUserId, string? comment, CancellationToken ct = default)
    {
        var report = await RequirePendingAndApproverAsync(reportId, companyId, approverUserId, ct);

        _db.ExpenseReportActions.Add(new ExpenseReportAction
        {
            ExpenseReportId = reportId,
            Level = report.CurrentLevel!.Value,
            UserId = approverUserId,
            Decision = "Rejected",
            Comment = comment,
        });

        report.Status = "Rejected";
        report.CurrentLevel = null;
        report.ResolvedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        var motivo = string.IsNullOrWhiteSpace(comment) ? "sin motivo indicado" : comment;
        await NotifyReportOwnerAsync(report, $"Tu informe fue rechazado. Motivo: {motivo}", ct);
    }

    public async Task ReopenAsync(long reportId, Guid companyId, Guid userId, CancellationToken ct = default)
    {
        var report = await _db.ExpenseReports
            .FirstOrDefaultAsync(r => r.Id == reportId && r.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("La rendición no existe o no pertenece a esta compañía.");

        if (report.Status != "Rejected")
            throw new InvalidOperationException("Solo se puede reabrir una rendición Rejected.");

        if (report.UserId != userId)
            throw new InvalidOperationException("Solo quien creó la rendición puede reabrirla.");

        report.Status = "Draft";
        report.Round += 1;
        report.CurrentLevel = null;
        report.SubmittedAt = null;
        report.ResolvedAt = null;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ExpenseReportAction>> ListHistoryAsync(long reportId, CancellationToken ct = default) =>
        await _db.ExpenseReportActions
            .Where(a => a.ExpenseReportId == reportId)
            .OrderBy(a => a.OccurredAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ExpenseReport>> ListPendingForApproverAsync(Guid companyId, Guid approverUserId, CancellationToken ct = default) =>
        await _db.ExpenseReports
            .Include(r => r.Lines)
            .Where(r => r.CompanyId == companyId && r.Status == "Pending"
                && r.ExpenseApprovalGroupId != null && r.CurrentLevel != null)
            .Join(_db.ExpenseApprovalGroupLevels.Where(n => n.UserId == approverUserId),
                r => new { GroupId = r.ExpenseApprovalGroupId!.Value, Level = r.CurrentLevel!.Value },
                n => new { GroupId = n.ExpenseApprovalGroupId, n.Level },
                (r, n) => r)
            .OrderBy(r => r.SubmittedAt)
            .ToListAsync(ct);

    private async Task<ExpenseReport> RequirePendingAndApproverAsync(long reportId, Guid companyId, Guid approverUserId, CancellationToken ct)
    {
        var report = await _db.ExpenseReports
            .FirstOrDefaultAsync(r => r.Id == reportId && r.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("La rendición no existe o no pertenece a esta compañía.");

        if (report.Status != "Pending" || report.CurrentLevel is null || report.ExpenseApprovalGroupId is null)
            throw new InvalidOperationException("La rendición no está pendiente de aprobación.");

        var levels = await _groups.GetLevelsAsync(report.ExpenseApprovalGroupId.Value, ct);
        if (!levels.TryGetValue(report.CurrentLevel.Value, out var expectedApprover) || expectedApprover != approverUserId)
            throw new InvalidOperationException("No sos el aprobador del nivel actual de esta rendición.");

        return report;
    }

    private async Task RecalculateFundIfApplicableAsync(ExpenseReport report, Guid companyId, CancellationToken ct)
    {
        if (report.ExpenseFundId is { } fundId)
            await _funds.RecalculateStatusAsync(fundId, companyId, ct);
    }

    private async Task<ExpenseReport> RequireEditableAsync(long reportId, Guid companyId, CancellationToken ct)
    {
        var report = await _db.ExpenseReports
            .FirstOrDefaultAsync(r => r.Id == reportId && r.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("La rendición no existe o no pertenece a esta compañía.");

        if (report.Status != "Draft")
            throw new InvalidOperationException("Solo se puede modificar una rendición en estado Draft.");

        return report;
    }

    /// <summary>Un correo caído nunca debe tumbar una transición de estado real -- loguear y seguir (ver el spec de esta feature).</summary>
    private async Task NotifyApproverAsync(ExpenseReport report, Guid approverUserId, CancellationToken ct) =>
        await NotifyAsync(approverUserId, $"Informe #{report.Id} pendiente de tu aprobación",
            $"<p>Tenés un informe de rendición de gastos (#{report.Id}) esperando tu decisión.</p>", ct);

    private async Task NotifyReportOwnerAsync(ExpenseReport report, string message, CancellationToken ct) =>
        await NotifyAsync(report.UserId, $"Informe #{report.Id} — actualización",
            $"<p>{message}</p>", ct);

    private async Task NotifyAsync(Guid userId, string subject, string htmlBody, CancellationToken ct)
    {
        var contact = await _contacts.GetContactAsync(userId, ct);
        if (contact is null || !contact.EmailNotificationsEnabled)
            return;

        try
        {
            await _emailSender.SendAsync(contact.OrganizationId, new EmailMessage(contact.Email, subject, htmlBody), ct);
        }
        catch (Exception)
        {
            // Correo caído (ej. organización sin proveedor configurado) no debe bloquear
            // la transacción de negocio ya confirmada -- ver constraint global del plan.
        }
    }
}
```

- [ ] **Step 4: Confirmar que pasa**

Run: `dotnet test "Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests" --filter ExpenseReportServiceNotificationTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Modulo.Rendiciones/Servicios/ExpenseReportService.cs src/Modulo.Rendiciones.Tests/Servicios/ExpenseReportServiceNotificationTests.cs
git commit -m "feat: notificar por correo al aprobador/dueño en Submit/Approve/Reject"
```

---

### Task 7: `RendicionesReminderBackgroundService`

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/ReminderGrouping.cs`
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/RendicionesReminderBackgroundService.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/ModuloRendiciones.cs`
- Test: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Servicios/ReminderGroupingTests.cs`

**Interfaces:**
- Consumes: `IExternalDatabaseConnectionService.ListActiveCompanyIdsAsync`/`ResolveConnectionAsync` (Task 1), `IUserContactLookupService.GetContactAsync` (Task 2), `IEmailSenderService.SendAsync`, `RendicionesSettings`/`ReminderLog` (Task 3/4).
- Produces: `ReminderGrouping.GroupPendingByApprover(IReadOnlyList<ExpenseReport> pending, IReadOnlyDictionary<int, Guid> levelsByGroupId...)` -- función pura testeable, extraída para no necesitar levantar todo el `BackgroundService` en el test. Firma exacta abajo.

La lógica de agrupación se extrae a una clase estática pura (`ReminderGrouping`) para poder testearla sin EF Core ni scopes -- el `BackgroundService` en sí solo hace orquestación (loop de tiempo, resolver conexión por compañía, leer de la base, llamar a `ReminderGrouping`, mandar los correos).

- [ ] **Step 1: Escribir el test de agrupación que falla**

`Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Servicios/ReminderGroupingTests.cs`:

```csharp
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

public class ReminderGroupingTests
{
    [Fact]
    public void GroupPendingByApprover_agrupa_varios_informes_del_mismo_aprobador()
    {
        var approverA = Guid.NewGuid();
        var approverB = Guid.NewGuid();

        var reports = new List<ExpenseReport>
        {
            new() { Id = 1, ExpenseApprovalGroupId = 10, CurrentLevel = 1 },
            new() { Id = 2, ExpenseApprovalGroupId = 10, CurrentLevel = 1 },
            new() { Id = 3, ExpenseApprovalGroupId = 20, CurrentLevel = 2 },
        };

        var levelsByGroup = new Dictionary<long, IReadOnlyDictionary<int, Guid>>
        {
            [10] = new Dictionary<int, Guid> { [1] = approverA },
            [20] = new Dictionary<int, Guid> { [1] = approverB, [2] = approverB },
        };

        var result = ReminderGrouping.GroupPendingByApprover(reports, levelsByGroup);

        Assert.Equal(2, result.Count);
        Assert.Equal(new long[] { 1, 2 }, result[approverA].Select(r => r.Id).OrderBy(x => x).ToArray());
        Assert.Equal(new long[] { 3 }, result[approverB].Select(r => r.Id).ToArray());
    }

    [Fact]
    public void GroupPendingByApprover_ignora_informes_sin_grupo_o_nivel_resuelto()
    {
        var reports = new List<ExpenseReport>
        {
            new() { Id = 1, ExpenseApprovalGroupId = null, CurrentLevel = null },
        };
        var levelsByGroup = new Dictionary<long, IReadOnlyDictionary<int, Guid>>();

        var result = ReminderGrouping.GroupPendingByApprover(reports, levelsByGroup);

        Assert.Empty(result);
    }
}
```

- [ ] **Step 2: Confirmar que falla**

Run: `dotnet test "Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests" --filter ReminderGroupingTests`
Expected: FAIL en compilación — `ReminderGrouping` no existe.

- [ ] **Step 3: Implementar `ReminderGrouping`**

`Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/ReminderGrouping.cs`:

```csharp
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

/// <summary>Función pura, sin dependencias de EF Core, para poder testearla sin base de datos -- ver RendicionesReminderBackgroundService.</summary>
public static class ReminderGrouping
{
    public static IReadOnlyDictionary<Guid, List<ExpenseReport>> GroupPendingByApprover(
        IReadOnlyList<ExpenseReport> pending,
        IReadOnlyDictionary<long, IReadOnlyDictionary<int, Guid>> levelsByGroup)
    {
        var result = new Dictionary<Guid, List<ExpenseReport>>();

        foreach (var report in pending)
        {
            if (report.ExpenseApprovalGroupId is not { } groupId || report.CurrentLevel is not { } level)
                continue;

            if (!levelsByGroup.TryGetValue(groupId, out var levels) || !levels.TryGetValue(level, out var approverId))
                continue;

            if (!result.TryGetValue(approverId, out var list))
            {
                list = new List<ExpenseReport>();
                result[approverId] = list;
            }

            list.Add(report);
        }

        return result;
    }
}
```

- [ ] **Step 4: Confirmar que pasa**

Run: `dotnet test "Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests" --filter ReminderGroupingTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Implementar el `BackgroundService`**

`Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/RendicionesReminderBackgroundService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Recordatorio diario de informes pendientes por aprobador -- recorre TODAS las
/// compañías con Rendiciones activo (ListActiveCompanyIdsAsync, ver
/// docs/superpowers/specs/2026-08-11-flujo-aprobacion-notificaciones-design.md),
/// resolviendo un RendicionesDbContext manual por compañía (no puede usar el
/// registrado por DI: ese depende de ICurrentCompanyAccessor, que exige un request
/// HTTP en curso -- acá no hay ninguno).
/// </summary>
public sealed class RendicionesReminderBackgroundService : BackgroundService
{
    private const string ModuleCode = "Rendiciones";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RendicionesReminderBackgroundService> _logger;

    public RendicionesReminderBackgroundService(IServiceScopeFactory scopeFactory, ILogger<RendicionesReminderBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo en el ciclo del recordatorio diario de Rendiciones -- se reintenta en el próximo ciclo.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var externalDb = sp.GetRequiredService<IExternalDatabaseConnectionService>();
        var contacts = sp.GetRequiredService<IUserContactLookupService>();
        var emailSender = sp.GetRequiredService<IEmailSenderService>();

        var companies = await externalDb.ListActiveCompanyIdsAsync(ModuleCode, ct);

        foreach (var company in companies)
        {
            try
            {
                await ProcessCompanyAsync(company, externalDb, contacts, emailSender, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo procesar el recordatorio diario para la compañía {CompanyId} -- se sigue con las demás.", company.CompanyId);
            }
        }
    }

    private async Task ProcessCompanyAsync(
        ModuleCompanyDto company,
        IExternalDatabaseConnectionService externalDb,
        IUserContactLookupService contacts,
        IEmailSenderService emailSender,
        CancellationToken ct)
    {
        var connection = await externalDb.ResolveConnectionAsync(ModuleCode, company.CompanyId, ct);

        var optionsBuilder = new DbContextOptionsBuilder<RendicionesDbContext>();
        switch (connection.EngineType)
        {
            case ExternalDatabaseEngineType.Postgres:
                optionsBuilder.UseNpgsql(connection.ConnectionString);
                break;
            case ExternalDatabaseEngineType.SqlServer:
                optionsBuilder.UseSqlServer(connection.ConnectionString);
                break;
            default:
                throw new InvalidOperationException($"Motor de base de datos externa no soportado: '{connection.EngineType}'.");
        }

        await using var db = new RendicionesDbContext(optionsBuilder.Options);

        var settings = await db.RendicionesSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (settings is null || !settings.ReminderEnabled)
            return;

        var nowLocal = TimeOnly.FromDateTime(DateTime.Now);
        var todayLocal = DateOnly.FromDateTime(DateTime.Now);

        // Ventana de +/- la mitad del intervalo de polling alrededor de la hora
        // configurada -- evita depender de que el ciclo caiga justo en el minuto exacto.
        var withinWindow = Math.Abs((nowLocal.ToTimeSpan() - settings.ReminderHour.ToTimeSpan()).TotalMinutes) <= PollInterval.TotalMinutes / 2;
        if (!withinWindow)
            return;

        var alreadySentToday = await db.ReminderLogs.AsNoTracking().AnyAsync(x => x.SentDate == todayLocal, ct);
        if (alreadySentToday)
            return;

        var pending = await db.ExpenseReports
            .Where(r => r.CompanyId == company.CompanyId && r.Status == "Pending"
                && r.ExpenseApprovalGroupId != null && r.CurrentLevel != null)
            .ToListAsync(ct);

        if (pending.Count > 0)
        {
            var groupIds = pending.Select(r => r.ExpenseApprovalGroupId!.Value).Distinct().ToList();
            var levels = await db.ExpenseApprovalGroupLevels
                .Where(l => groupIds.Contains(l.ExpenseApprovalGroupId))
                .ToListAsync(ct);

            var levelsByGroup = levels
                .GroupBy(l => l.ExpenseApprovalGroupId)
                .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<int, Guid>)g.ToDictionary(l => l.Level, l => l.UserId));

            var byApprover = ReminderGrouping.GroupPendingByApprover(pending, levelsByGroup);

            foreach (var (approverUserId, reports) in byApprover)
            {
                var contact = await contacts.GetContactAsync(approverUserId, ct);
                if (contact is null || !contact.EmailNotificationsEnabled)
                    continue;

                var body = $"<p>Tenés {reports.Count} informe(s) de rendición de gastos pendientes de tu aprobación:</p><ul>"
                    + string.Join("", reports.Select(r => $"<li>Informe #{r.Id}</li>"))
                    + "</ul>";

                try
                {
                    await emailSender.SendAsync(contact.OrganizationId, new EmailMessage(contact.Email, "Recordatorio: informes pendientes de aprobar", body), ct);
                }
                catch (Exception)
                {
                    // Mismo criterio que ExpenseReportService: un correo caído no debe
                    // frenar el resto de la ronda de recordatorios.
                }
            }
        }

        db.ReminderLogs.Add(new ReminderLog { SentDate = todayLocal });
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 6: Registrar el servicio y sus dependencias en `RegisterServices`**

En `ModuloRendiciones.cs`, agregar dentro de `RegisterServices`, después de la línea `services.AddScoped<IExpenseReportService, ExpenseReportService>();`:

```csharp
        services.AddHostedService<RendicionesReminderBackgroundService>();
```

- [ ] **Step 7: Verificar que compila**

Run: `dotnet build "Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Modulo.Rendiciones.csproj"`
Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add src/Modulo.Rendiciones/Servicios/ReminderGrouping.cs src/Modulo.Rendiciones/Servicios/RendicionesReminderBackgroundService.cs src/Modulo.Rendiciones/ModuloRendiciones.cs src/Modulo.Rendiciones.Tests/Servicios/ReminderGroupingTests.cs
git commit -m "feat: recordatorio diario de informes pendientes por aprobador"
```

---

### Task 8: Página admin `Configuracion/Notificaciones` (hora + habilitado)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Configuracion/Notificaciones/Index.cshtml.cs`
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Configuracion/Notificaciones/Index.cshtml`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/ModuloRendiciones.cs`

**Interfaces:**
- Consumes: `RendicionesDbContext.RendicionesSettings` (Task 3).

Página chica, un solo `POST`, sigue el mismo patrón que `Configuracion/TiposGasto/Index` (form directo contra el PageModel, sin service dedicado -- no hay lógica de negocio más allá de leer/escribir la fila única).

- [ ] **Step 1: PageModel**

`Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Configuracion/Notificaciones/Index.cshtml.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Pages.Configuracion.Notificaciones;

public sealed class IndexModel : RendicionesPageModelBase
{
    private readonly RendicionesDbContext _db;

    public IndexModel(RendicionesDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public NotificationSettingsInput Settings { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var settings = await _db.RendicionesSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (settings is not null)
        {
            Settings.ReminderHour = settings.ReminderHour;
            Settings.ReminderEnabled = settings.ReminderEnabled;
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Page();

        var settings = await _db.RendicionesSettings.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (settings is null)
        {
            settings = new RendicionesSettings { Id = 1 };
            _db.RendicionesSettings.Add(settings);
        }

        settings.ReminderHour = Settings.ReminderHour;
        settings.ReminderEnabled = Settings.ReminderEnabled;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        SuccessMessage = "Configuración de notificaciones guardada.";
        return RedirectToPage();
    }

    public sealed class NotificationSettingsInput
    {
        [Required]
        public TimeOnly ReminderHour { get; set; } = new(8, 0);

        public bool ReminderEnabled { get; set; } = true;
    }
}
```

- [ ] **Step 2: Vista**

`Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Configuracion/Notificaciones/Index.cshtml`:

```cshtml
@page "/rendiciones/configuracion/notificaciones"
@model Modulo.Rendiciones.Pages.Configuracion.Notificaciones.IndexModel
@{
    ViewData["Title"] = "Notificaciones";
}
@section Styles {
    <link rel="stylesheet" href="~/css/rendiciones.css" asp-append-version="true" />
}

<div class="card-ps admin-card">
    <div class="admin-card-header">
        <h2>Notificaciones</h2>
    </div>

@if (Model.SuccessMessage is not null)
{
    <div class="admin-alert admin-alert-success">@Model.SuccessMessage</div>
}

<p class="admin-muted">Recordatorio diario por correo a cada aprobador con informes pendientes -- un solo correo resumen por día, agrupando todos sus pendientes.</p>

<form method="post" class="admin-form-asignar">
    <div class="form-group">
        <label asp-for="Settings.ReminderHour">Hora de envío</label>
        <input asp-for="Settings.ReminderHour" type="time" class="form-control" />
    </div>
    <div class="form-group form-row-check">
        <label><input asp-for="Settings.ReminderEnabled" type="checkbox" /> Recordatorio diario habilitado</label>
    </div>
    <button type="submit" class="btn-erp-primary">Guardar</button>
</form>
</div>
```

- [ ] **Step 3: Agregar al menú**

En `ModuloRendiciones.cs`, dentro de `GetMenu()`, agregar después del ítem `config-consumo-servicios` (línea 185):

```csharp
        yield return new MenuItemDefinition
        {
            Code = "config-notificaciones",
            ParentCode = "grupo-administrador",
            Name = "Notificaciones",
            PageRoute = "/rendiciones/configuracion/notificaciones",
            Order = 9,
        };
```

- [ ] **Step 4: Verificar que compila**

Run: `dotnet build "Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Modulo.Rendiciones.csproj"`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/Modulo.Rendiciones/Pages/Configuracion/Notificaciones/ src/Modulo.Rendiciones/ModuloRendiciones.cs
git commit -m "feat: pantalla admin para configurar el recordatorio diario"
```

---

### Task 9: Build completo + verificación manual end-to-end

**Files:** ninguno nuevo -- tarea de verificación.

- [ ] **Step 1: Build de los dos repos**

```bash
dotnet build "Portal SaaS - Core/PortalSaas.sln"
dotnet build "Portal SaaS - Plugins/Modulo.Rendiciones/Modulo.Rendiciones.sln"
```
Expected: 0 errores en ambos.

- [ ] **Step 2: Correr toda la batería de tests**

```bash
dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests"
dotnet test "Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests"
```
Expected: todo verde (109 + los preexistentes del Core, más los ~11 nuevos de este plan en Core y los ~8 nuevos del plugin).

- [ ] **Step 3: Copiar el plugin actualizado al Host y levantar**

```bash
cd "Portal SaaS - Plugins/Modulo.Rendiciones"
dotnet build src/Modulo.Rendiciones/Modulo.Rendiciones.csproj -c Release
# copiar dist/Modulo.Rendiciones/1.0.0/* a artifacts/plugins/Modulo.Rendiciones/1.0.0/ del Host,
# mismo procedimiento ya usado en la Fase 4 (ver PENDIENTE.md)
cd "Portal SaaS - Core"
dotnet run --project src/PortalSaas.Host --launch-profile https
```
Expected: log de arranque `"Módulo Rendiciones v1.0.0 cargado (17 entradas de menú)"` (antes 16 -- la entrada nueva "Notificaciones"), sin excepciones.

- [ ] **Step 4: Flujo real en navegador con el usuario tenant de prueba**

Retoma el punto ya bloqueado en `PENDIENTE.md` ("Orden de prioridad de los pendientes", punto 3): con el usuario tenant creado en Comercial Depor, hacer login real (`/Account/Login?org=cl-depor`) y verificar de punta a punta:
1. Registrar un gasto suelto (`/rendiciones/gastos`).
2. Armar un informe con ese gasto (`/rendiciones/informes`) y enviarlo.
3. Confirmar que el aprobador configurado en el grupo de aprobación recibe el correo (revisar bandeja real o logs de `EmailSenderService` si el proveedor de la organización está en modo de prueba).
4. Login como el aprobador, entrar a `/rendiciones/aprobaciones`, confirmar que el contador (`@Model.Pending.Count`) muestra el pendiente, aprobar o rechazar.
5. Confirmar que el dueño del informe recibe el correo de resultado.
6. En `/rendiciones/configuracion/notificaciones`, cambiar la hora a un minuto cercano al actual, esperar el ciclo de `RendicionesReminderBackgroundService` (hasta 15 min) con al menos un informe pendiente, confirmar que llega el correo resumen y que no se duplica en un segundo ciclo el mismo día (`rendiciones_reminder_log` tiene la fila del día).

- [ ] **Step 5: Commit final (si hubo ajustes durante la verificación manual)**

```bash
git add -A
git commit -m "chore: ajustes de verificación end-to-end del flujo de aprobación + notificaciones"
```

---

## Self-Review — cobertura contra el spec

- Correo al aprobador al enviar/subir de nivel → Task 6. ✅
- Correo al dueño al aprobar/rechazar → Task 6. ✅
- Respeta `EmailNotificationsEnabled` → Task 6 (test `SubmitAsync_no_notifica_si_...`) + Task 7 (reminder). ✅
- Correo caído no bloquea la transacción → Task 6 (test `SubmitAsync_continua_si_...`) + Task 7 (`try/catch` por aprobador). ✅
- Contador en pantalla → ya existía (`Aprobaciones/Index.cshtml:17`), verificado en Task 9 Step 4, sin código nuevo. ✅
- Recordatorio diario, un correo agrupado por aprobador → Task 7. ✅
- Hora configurable por admin → Task 3 (tabla) + Task 8 (pantalla). ✅
- No reenvía el mismo día → Task 7 (`ReminderLog`/dedupe). ✅
- No notifica en paralelo a todos los niveles → Task 6 (`NotifyApproverAsync` solo llama con el nivel resuelto por `ResolveNextLevel`). ✅
- Fondo por Rendir sin tocar → ningún task lo modifica. ✅
- Sin cambios a SAP/Fase 7 → ningún task los toca. ✅
