# Motor de Integración ERP/Externo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construir el motor de integración genérico (`PortalSaas.Integrations`) — contratos, modelo de datos, orquestador `BackgroundService` y el conector SAP inicial — como base reutilizable para Wms↔SAP, Wms↔Sorter y, a futuro, Rendiciones↔ERP externo.

**Architecture:** Tres capas (conectores / mapeo de campos / orquestación) descritas en `docs/superpowers/specs/2026-08-14-motor-integracion-erp-design.md`. Contratos en `PortalSaas.Abstractions`, entidades+migraciones en `PortalSaas.Data`, implementación (orquestador, conectores, servicio de mapeo) en nuevo proyecto `PortalSaas.Integrations`, registrado desde `PortalSaas.Host`.

**Tech Stack:** .NET / EF Core Code First (Postgres + SQL Server), xUnit + EF Core InMemory, `BackgroundService`/`IServiceScopeFactory`, `ISecretoCifradoService` (AES-256-GCM) para `ConectorConfig`.

## Global Constraints

- Dirección de dependencias del `.sln`: `PortalSaas.Abstractions` la referencian todos; solo `PortalSaas.Host` referencia `PortalSaas.Core`; nadie referencia `Host`. `PortalSaas.Integrations` sigue el mismo patrón: contratos consumibles desde Abstractions, implementación en su propio proyecto, referenciado únicamente desde `Host`.
- `PortalSaas.Data` nunca importa paquete de proveedor (Postgres/SqlServer) — las migraciones concretas viven en `PortalSaas.Data.Migrations.PostgreSql` y `...SqlServer`.
- Nombres de tabla/columna en inglés, `snake_case`, tablas en plural, PK `id`, FK `<entidad>_id`, booleanos `is_`/`has_`, fechas `_at` (`timestamptz`), estados en columna `status` con `check` constraint — ver `docs/01-CONVENCION-NOMBRES-BD.md`.
- Nunca `HasDefaultValueSql` en `OnModelCreating`; los defaults se generan como inicializador de propiedad en C#.
- Todo scoping multi-tenant nuevo usa `ICurrentCompanyAccessor.CompanyId`, nunca `OrganizationId` como fallback.
- Credenciales/config de conector siempre cifradas con `ISecretoCifradoService` existente — nunca texto plano en `ConectorConfig`.
- DI se registra directo en `PortalSaas.Host/Program.cs` con `builder.Services.AddScoped<I,C>()` / `AddHostedService<T>()` — no se crean extension methods nuevos tipo `AddPortalSaasCore()`.
- `BackgroundService` inyecta `IServiceScopeFactory` (nunca `IServiceProvider` directo), abre `using var scope = _scopeFactory.CreateScope()` por ciclo.
- Tests: xUnit + `UseInMemoryDatabase(Guid.NewGuid().ToString())`, sin mocking framework — se prueban servicios reales contra el DbContext en memoria, siguiendo el patrón de `tests/PortalSaas.Core.Tests/ContractLimitServiceTests.cs`.
- Fuera de alcance de este plan (ver spec): no se toca `WmsSapIntegration.Service`; no se migra `Modulo.Wms`/`Modulo.Rendiciones` a consumir el motor (eso es un plan separado, posterior); no se implementan `RestConnector`/`ArchivoConnector` (solo `SapDocumentConnector` como conector inicial, para validar el motor end-to-end).

---

## File Structure

**Nuevos archivos:**

- `src/PortalSaas.Abstractions/Contratos/Integraciones/IIntegrationConnector.cs` — contrato de conector.
- `src/PortalSaas.Abstractions/Contratos/Integraciones/IIntegrationFieldMappingService.cs` — contrato de mapeo.
- `src/PortalSaas.Abstractions/Contratos/Integraciones/IIntegrationEntityReader.cs` / `IIntegrationEntityWriter.cs` — contratos que implementan los módulos consumidores.
- `src/PortalSaas.Abstractions/Contratos/Integraciones/IntegrationRecord.cs` — DTO neutro (diccionario de campos) que cruza conector↔mapeo↔módulo.
- `src/PortalSaas.Data/Entities/Integraciones/IntegrationDefinition.cs`
- `src/PortalSaas.Data/Entities/Integraciones/IntegrationFieldMapping.cs`
- `src/PortalSaas.Data/Entities/Integraciones/IntegrationRunLog.cs`
- `src/PortalSaas.Data/PortalSaasDbContext.cs` — modificar: agregar 3 `DbSet<T>` + configuración Fluent API en `OnModelCreating`.
- `src/PortalSaas.Data.Migrations.PostgreSql/Migrations/` — nueva migración (autogenerada por `dotnet ef`).
- `src/PortalSaas.Data.Migrations.SqlServer/Migrations/` — nueva migración (autogenerada por `dotnet ef`).
- `src/PortalSaas.Integrations/PortalSaas.Integrations.csproj` — nuevo proyecto, referencia `PortalSaas.Abstractions` + `PortalSaas.Data`.
- `src/PortalSaas.Integrations/IntegrationFieldMappingService.cs` (+ interfaz ya en Abstractions).
- `src/PortalSaas.Integrations/Connectors/SapDocumentConnector.cs`.
- `src/PortalSaas.Integrations/IntegrationSyncHostedService.cs`.
- `src/PortalSaas.Host/Program.cs` — modificar: registrar servicios + hosted service.
- `tests/PortalSaas.Core.Tests/Integraciones/IntegrationFieldMappingServiceTests.cs`
- `tests/PortalSaas.Core.Tests/Integraciones/IntegrationSyncHostedServiceTests.cs`

---

### Task 1: Contratos base en `PortalSaas.Abstractions`

**Files:**
- Create: `src/PortalSaas.Abstractions/Contratos/Integraciones/IntegrationRecord.cs`
- Create: `src/PortalSaas.Abstractions/Contratos/Integraciones/IIntegrationConnector.cs`
- Create: `src/PortalSaas.Abstractions/Contratos/Integraciones/IIntegrationFieldMappingService.cs`
- Create: `src/PortalSaas.Abstractions/Contratos/Integraciones/IIntegrationEntityReader.cs`
- Create: `src/PortalSaas.Abstractions/Contratos/Integraciones/IIntegrationEntityWriter.cs`

**Interfaces:**
- Produces: `IntegrationRecord` (`IReadOnlyDictionary<string,object?> Fields`), `IIntegrationConnector` (`PullAsync`/`PushAsync`), `IIntegrationFieldMappingService` (`MapToExternalAsync`/`MapToLocalAsync`), `IIntegrationEntityReader<T>`/`IIntegrationEntityWriter<T>` — firmas exactas usadas por todas las tareas siguientes.

- [ ] **Step 1: Crear `IntegrationRecord`**

```csharp
namespace PortalSaas.Abstractions.Contratos.Integraciones;

public sealed class IntegrationRecord
{
    public IntegrationRecord(IReadOnlyDictionary<string, object?> fields)
    {
        Fields = fields;
    }

    public IReadOnlyDictionary<string, object?> Fields { get; }

    public object? this[string campo] => Fields.TryGetValue(campo, out var valor) ? valor : null;
}
```

- [ ] **Step 2: Crear `IIntegrationConnector`**

```csharp
namespace PortalSaas.Abstractions.Contratos.Integraciones;

public interface IIntegrationConnector
{
    string Tipo { get; }

    Task<IReadOnlyList<IntegrationRecord>> PullAsync(
        string conectorConfigJson,
        CancellationToken cancellationToken);

    Task PushAsync(
        string conectorConfigJson,
        IReadOnlyList<IntegrationRecord> registros,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Crear `IIntegrationFieldMappingService`**

```csharp
namespace PortalSaas.Abstractions.Contratos.Integraciones;

public interface IIntegrationFieldMappingService
{
    Task<IntegrationRecord> MapToExternalAsync(Guid integrationDefinitionId, IntegrationRecord registroLocal);

    Task<IntegrationRecord> MapToLocalAsync(Guid integrationDefinitionId, IntegrationRecord registroExterno);
}
```

- [ ] **Step 4: Crear `IIntegrationEntityReader`/`IIntegrationEntityWriter`**

```csharp
namespace PortalSaas.Abstractions.Contratos.Integraciones;

public interface IIntegrationEntityReader<T>
{
    string EntidadNegocio { get; }

    Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken);
}

public interface IIntegrationEntityWriter<T>
{
    string EntidadNegocio { get; }

    Task EscribirAsync(Guid companyId, IReadOnlyList<IntegrationRecord> registros, CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Compilar `PortalSaas.Abstractions`**

Run: `dotnet build src/PortalSaas.Abstractions/PortalSaas.Abstractions.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/PortalSaas.Abstractions/Contratos/Integraciones/
git commit -m "feat: agregar contratos base del motor de integración genérico"
```

---

### Task 2: Entidades y configuración EF Core en `PortalSaas.Data`

**Files:**
- Create: `src/PortalSaas.Data/Entities/Integraciones/IntegrationDefinition.cs`
- Create: `src/PortalSaas.Data/Entities/Integraciones/IntegrationFieldMapping.cs`
- Create: `src/PortalSaas.Data/Entities/Integraciones/IntegrationRunLog.cs`
- Modify: `src/PortalSaas.Data/PortalSaasDbContext.cs`

**Interfaces:**
- Consumes: nada de tareas anteriores (entidades de persistencia independientes de los contratos de Abstractions).
- Produces: `IntegrationDefinition`, `IntegrationFieldMapping`, `IntegrationRunLog` — tipos usados por `IntegrationFieldMappingService` (Task 3) y `IntegrationSyncHostedService` (Task 5).

- [ ] **Step 1: Crear entidad `IntegrationDefinition`**

```csharp
namespace PortalSaas.Data.Entities.Integraciones;

public enum IntegrationConectorTipo { Sap, Rest, Archivo }
public enum IntegrationDireccion { Subida, Bajada, Ambas }

public class IntegrationDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string ModuloOrigen { get; set; } = string.Empty;
    public string EntidadNegocio { get; set; } = string.Empty;
    public IntegrationConectorTipo ConectorTipo { get; set; }
    public string ConectorConfigCifrado { get; set; } = string.Empty;
    public IntegrationDireccion Direccion { get; set; }
    public bool Activo { get; set; } = true;
    public string? ProgramacionCron { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }

    public ICollection<IntegrationFieldMapping> Mapeos { get; set; } = new List<IntegrationFieldMapping>();
}
```

- [ ] **Step 2: Crear entidad `IntegrationFieldMapping`**

```csharp
namespace PortalSaas.Data.Entities.Integraciones;

public class IntegrationFieldMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IntegrationDefinitionId { get; set; }
    public string CampoLocal { get; set; } = string.Empty;
    public string CampoExterno { get; set; } = string.Empty;
    public string? Transformacion { get; set; }
    public bool Obligatorio { get; set; }

    public IntegrationDefinition IntegrationDefinition { get; set; } = null!;
}
```

- [ ] **Step 3: Crear entidad `IntegrationRunLog`**

```csharp
namespace PortalSaas.Data.Entities.Integraciones;

public enum IntegrationRunResultado { Exito, Error, Parcial }
public enum IntegrationRunDisparadoPor { Programado, Manual }

public class IntegrationRunLog
{
    public long Id { get; set; }
    public Guid IntegrationDefinitionId { get; set; }
    public DateTimeOffset IniciadoEn { get; set; }
    public DateTimeOffset? FinalizadoEn { get; set; }
    public IntegrationRunResultado Resultado { get; set; }
    public int RegistrosProcesados { get; set; }
    public int RegistrosConError { get; set; }
    public string? DetalleError { get; set; }
    public IntegrationRunDisparadoPor DisparadoPor { get; set; }
}
```

- [ ] **Step 4: Registrar `DbSet<T>` y Fluent API en `PortalSaasDbContext`**

Agregar junto a los `DbSet` existentes:

```csharp
public DbSet<IntegrationDefinition> IntegrationDefinitions => Set<IntegrationDefinition>();
public DbSet<IntegrationFieldMapping> IntegrationFieldMappings => Set<IntegrationFieldMapping>();
public DbSet<IntegrationRunLog> IntegrationRunLogs => Set<IntegrationRunLog>();
```

Dentro de `OnModelCreating`, siguiendo el patrón de `Organization`:

```csharp
modelBuilder.Entity<IntegrationDefinition>(entity =>
{
    entity.ToTable("integration_definitions", t =>
    {
        t.HasCheckConstraint("ck_integration_definitions_conector_tipo",
            "conector_tipo in ('Sap', 'Rest', 'Archivo')");
        t.HasCheckConstraint("ck_integration_definitions_direccion",
            "direccion in ('Subida', 'Bajada', 'Ambas')");
    });
    entity.Property(e => e.Id).HasColumnName("id");
    entity.Property(e => e.CompanyId).HasColumnName("company_id");
    entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(200);
    entity.Property(e => e.ModuloOrigen).HasColumnName("modulo_origen").HasMaxLength(100);
    entity.Property(e => e.EntidadNegocio).HasColumnName("entidad_negocio").HasMaxLength(100);
    entity.Property(e => e.ConectorTipo).HasColumnName("conector_tipo")
        .HasConversion<string>().HasMaxLength(20);
    entity.Property(e => e.ConectorConfigCifrado).HasColumnName("conector_config_cifrado");
    entity.Property(e => e.Direccion).HasColumnName("direccion")
        .HasConversion<string>().HasMaxLength(20);
    entity.Property(e => e.Activo).HasColumnName("is_active");
    entity.Property(e => e.ProgramacionCron).HasColumnName("programacion_cron").HasMaxLength(100);
    entity.Property(e => e.NextRunAt).HasColumnName("next_run_at");
    entity.HasIndex(e => new { e.CompanyId, e.Activo });
});

modelBuilder.Entity<IntegrationFieldMapping>(entity =>
{
    entity.ToTable("integration_field_mappings");
    entity.Property(e => e.Id).HasColumnName("id");
    entity.Property(e => e.IntegrationDefinitionId).HasColumnName("integration_definition_id");
    entity.Property(e => e.CampoLocal).HasColumnName("campo_local").HasMaxLength(100);
    entity.Property(e => e.CampoExterno).HasColumnName("campo_externo").HasMaxLength(100);
    entity.Property(e => e.Transformacion).HasColumnName("transformacion").HasMaxLength(100);
    entity.Property(e => e.Obligatorio).HasColumnName("is_required");
    entity.HasOne(e => e.IntegrationDefinition)
        .WithMany(d => d.Mapeos)
        .HasForeignKey(e => e.IntegrationDefinitionId)
        .OnDelete(DeleteBehavior.Cascade);
});

modelBuilder.Entity<IntegrationRunLog>(entity =>
{
    entity.ToTable("integration_run_logs", t =>
    {
        t.HasCheckConstraint("ck_integration_run_logs_resultado",
            "resultado in ('Exito', 'Error', 'Parcial')");
        t.HasCheckConstraint("ck_integration_run_logs_disparado_por",
            "disparado_por in ('Programado', 'Manual')");
    });
    entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
    entity.Property(e => e.IntegrationDefinitionId).HasColumnName("integration_definition_id");
    entity.Property(e => e.IniciadoEn).HasColumnName("iniciado_at");
    entity.Property(e => e.FinalizadoEn).HasColumnName("finalizado_at");
    entity.Property(e => e.Resultado).HasColumnName("resultado")
        .HasConversion<string>().HasMaxLength(20);
    entity.Property(e => e.RegistrosProcesados).HasColumnName("registros_procesados");
    entity.Property(e => e.RegistrosConError).HasColumnName("registros_con_error");
    entity.Property(e => e.DetalleError).HasColumnName("detalle_error");
    entity.Property(e => e.DisparadoPor).HasColumnName("disparado_por")
        .HasConversion<string>().HasMaxLength(20);
    entity.HasIndex(e => e.IntegrationDefinitionId);
});
```

- [ ] **Step 5: Compilar `PortalSaas.Data`**

Run: `dotnet build src/PortalSaas.Data/PortalSaas.Data.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 6: Generar migración Postgres**

Run: `dotnet ef migrations add AddIntegrationTables --project src/PortalSaas.Data.Migrations.PostgreSql --startup-project src/PortalSaas.Host`
Expected: se crea archivo de migración nuevo en `src/PortalSaas.Data.Migrations.PostgreSql/Migrations/`.

- [ ] **Step 7: Generar migración SQL Server**

Run: `dotnet ef migrations add AddIntegrationTables --project src/PortalSaas.Data.Migrations.SqlServer --startup-project src/PortalSaas.Host`
Expected: se crea archivo de migración nuevo en `src/PortalSaas.Data.Migrations.SqlServer/Migrations/`.

- [ ] **Step 8: Commit**

```bash
git add src/PortalSaas.Data/Entities/Integraciones/ src/PortalSaas.Data/PortalSaasDbContext.cs src/PortalSaas.Data.Migrations.PostgreSql/Migrations/ src/PortalSaas.Data.Migrations.SqlServer/Migrations/
git commit -m "feat: agregar entidades y migraciones de integration_definitions/field_mappings/run_logs"
```

---

### Task 3: Proyecto `PortalSaas.Integrations` + `IntegrationFieldMappingService`

**Files:**
- Create: `src/PortalSaas.Integrations/PortalSaas.Integrations.csproj`
- Create: `src/PortalSaas.Integrations/IntegrationFieldMappingService.cs`
- Test: `tests/PortalSaas.Core.Tests/Integraciones/IntegrationFieldMappingServiceTests.cs`
- Modify: `PortalSaas.sln` (agregar proyecto nuevo)

**Interfaces:**
- Consumes: `IIntegrationFieldMappingService`, `IntegrationRecord` (Task 1); `IntegrationDefinition`, `IntegrationFieldMapping`, `PortalSaasDbContext` (Task 2).
- Produces: `IntegrationFieldMappingService : IIntegrationFieldMappingService` — consumido por `SapDocumentConnector` uso indirecto y por `IntegrationSyncHostedService` (Task 5).

- [ ] **Step 1: Crear `PortalSaas.Integrations.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\PortalSaas.Abstractions\PortalSaas.Abstractions.csproj" />
    <ProjectReference Include="..\PortalSaas.Data\PortalSaas.Data.csproj" />
  </ItemGroup>
</Project>
```

Agregar el proyecto a `PortalSaas.sln` con `dotnet sln PortalSaas.sln add src/PortalSaas.Integrations/PortalSaas.Integrations.csproj`.

- [ ] **Step 2: Escribir el test que falla**

```csharp
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Data;
using PortalSaas.Data.Entities.Integraciones;
using PortalSaas.Integrations;
using Xunit;

namespace PortalSaas.Core.Tests.Integraciones;

public class IntegrationFieldMappingServiceTests
{
    private static PortalSaasDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PortalSaasDbContext(options);
    }

    [Fact]
    public async Task MapToExternalAsync_TraduceCampoLocalACampoExterno()
    {
        await using var contexto = CrearContexto();
        var definicion = new IntegrationDefinition
        {
            Nombre = "Wms a SAP",
            ModuloOrigen = "Wms",
            EntidadNegocio = "PickingConfirmado",
            ConectorTipo = IntegrationConectorTipo.Sap,
            ConectorConfigCifrado = "config",
            Direccion = IntegrationDireccion.Subida,
        };
        definicion.Mapeos.Add(new IntegrationFieldMapping
        {
            CampoLocal = "NumeroPedido",
            CampoExterno = "DocEntry",
            Obligatorio = true,
        });
        contexto.IntegrationDefinitions.Add(definicion);
        await contexto.SaveChangesAsync();

        var servicio = new IntegrationFieldMappingService(contexto);
        var registroLocal = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["NumeroPedido"] = "12345",
        });

        var resultado = await servicio.MapToExternalAsync(definicion.Id, registroLocal);

        Assert.Equal("12345", resultado["DocEntry"]);
    }
}
```

- [ ] **Step 3: Ejecutar el test y verificar que falla**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter IntegrationFieldMappingServiceTests`
Expected: FAIL — `IntegrationFieldMappingService` no existe todavía.

- [ ] **Step 4: Implementar `IntegrationFieldMappingService`**

```csharp
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Data;

namespace PortalSaas.Integrations;

public class IntegrationFieldMappingService : IIntegrationFieldMappingService
{
    private readonly PortalSaasDbContext _contexto;

    public IntegrationFieldMappingService(PortalSaasDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<IntegrationRecord> MapToExternalAsync(Guid integrationDefinitionId, IntegrationRecord registroLocal)
    {
        var mapeos = await ObtenerMapeosAsync(integrationDefinitionId);
        var campos = new Dictionary<string, object?>();
        foreach (var mapeo in mapeos)
        {
            campos[mapeo.CampoExterno] = registroLocal[mapeo.CampoLocal];
        }
        return new IntegrationRecord(campos);
    }

    public async Task<IntegrationRecord> MapToLocalAsync(Guid integrationDefinitionId, IntegrationRecord registroExterno)
    {
        var mapeos = await ObtenerMapeosAsync(integrationDefinitionId);
        var campos = new Dictionary<string, object?>();
        foreach (var mapeo in mapeos)
        {
            campos[mapeo.CampoLocal] = registroExterno[mapeo.CampoExterno];
        }
        return new IntegrationRecord(campos);
    }

    private async Task<List<Data.Entities.Integraciones.IntegrationFieldMapping>> ObtenerMapeosAsync(Guid integrationDefinitionId)
    {
        return await _contexto.IntegrationFieldMappings
            .Where(m => m.IntegrationDefinitionId == integrationDefinitionId)
            .ToListAsync();
    }
}
```

Agregar `using Microsoft.EntityFrameworkCore;` al inicio del archivo (para `ToListAsync`).

- [ ] **Step 5: Ejecutar el test y verificar que pasa**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter IntegrationFieldMappingServiceTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add PortalSaas.sln src/PortalSaas.Integrations/ tests/PortalSaas.Core.Tests/Integraciones/IntegrationFieldMappingServiceTests.cs
git commit -m "feat: crear proyecto PortalSaas.Integrations con IntegrationFieldMappingService"
```

---

### Task 4: `SapDocumentConnector`

**Files:**
- Create: `src/PortalSaas.Integrations/Connectors/SapDocumentConnector.cs`
- Test: `tests/PortalSaas.Core.Tests/Integraciones/SapDocumentConnectorTests.cs`

**Interfaces:**
- Consumes: `IIntegrationConnector`, `IntegrationRecord` (Task 1). No depende de `SalesDocumentService`/etc. directamente en este task — se define el contrato y una implementación mínima que registra el `Tipo` y valida `conectorConfigJson`; la integración real contra los document services de `PortalSaas.Core` queda marcada como punto de extensión explícito (`TODO` es responsabilidad de un plan posterior una vez exista acceso desde `PortalSaas.Integrations` a `PortalSaas.Core`, dado que hoy la dirección de dependencias no permite que `Integrations` referencie `Core`).
- Produces: `SapDocumentConnector : IIntegrationConnector` con `Tipo == "Sap"`, consumido por `IntegrationSyncHostedService` (Task 5) vía resolución por `ConectorTipo`.

> **Nota de arquitectura importante:** `PortalSaas.Core` contiene `SalesDocumentService`/`PurchaseDocumentService`/`InventoryDocumentService`, pero por la regla de dependencias (`Integrations` no puede referenciar `Core`, solo `Host` referencia `Core`), el envoltorio real hacia esos servicios debe resolverse en `Host` o requiere invertir la dependencia (mover esos document services, o una interfaz de ellos, a `Abstractions`). Este task deja `SapDocumentConnector` con la interfaz completa y lanza `NotSupportedException` documentada en el cuerpo — la implementación real contra los document services es tarea de un plan de seguimiento una vez decidido cómo resolver esa dirección de dependencia (fuera de alcance de este plan, ver spec sección "Fuera de alcance").

- [ ] **Step 1: Escribir el test que falla**

```csharp
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Integrations.Connectors;
using Xunit;

namespace PortalSaas.Core.Tests.Integraciones;

public class SapDocumentConnectorTests
{
    [Fact]
    public void Tipo_EsSap()
    {
        var conector = new SapDocumentConnector();

        Assert.Equal("Sap", conector.Tipo);
    }

    [Fact]
    public async Task PushAsync_SinImplementacionReal_LanzaNotSupportedException()
    {
        var conector = new SapDocumentConnector();
        var registros = new List<IntegrationRecord> { new(new Dictionary<string, object?>()) };

        await Assert.ThrowsAsync<NotSupportedException>(
            () => conector.PushAsync("{}", registros, CancellationToken.None));
    }
}
```

- [ ] **Step 2: Ejecutar el test y verificar que falla**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter SapDocumentConnectorTests`
Expected: FAIL — `SapDocumentConnector` no existe.

- [ ] **Step 3: Implementar `SapDocumentConnector`**

```csharp
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace PortalSaas.Integrations.Connectors;

public class SapDocumentConnector : IIntegrationConnector
{
    public string Tipo => "Sap";

    public Task<IReadOnlyList<IntegrationRecord>> PullAsync(
        string conectorConfigJson,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException(
            "SapDocumentConnector.PullAsync pendiente: requiere resolver la dirección de " +
            "dependencia hacia SalesDocumentService/PurchaseDocumentService/InventoryDocumentService " +
            "de PortalSaas.Core (ver nota de arquitectura en el plan de implementación).");
    }

    public Task PushAsync(
        string conectorConfigJson,
        IReadOnlyList<IntegrationRecord> registros,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException(
            "SapDocumentConnector.PushAsync pendiente: requiere resolver la dirección de " +
            "dependencia hacia SalesDocumentService/PurchaseDocumentService/InventoryDocumentService " +
            "de PortalSaas.Core (ver nota de arquitectura en el plan de implementación).");
    }
}
```

- [ ] **Step 4: Ejecutar el test y verificar que pasa**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter SapDocumentConnectorTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PortalSaas.Integrations/Connectors/ tests/PortalSaas.Core.Tests/Integraciones/SapDocumentConnectorTests.cs
git commit -m "feat: agregar SapDocumentConnector con contrato Tipo=Sap (Pull/Push pendientes de resolución de dependencias)"
```

---

### Task 5: `IntegrationSyncHostedService`

**Files:**
- Create: `src/PortalSaas.Integrations/IntegrationSyncHostedService.cs`
- Test: `tests/PortalSaas.Core.Tests/Integraciones/IntegrationSyncHostedServiceTests.cs`

**Interfaces:**
- Consumes: `IntegrationDefinition`, `IntegrationRunLog`, `PortalSaasDbContext` (Task 2); `IIntegrationConnector`, `IIntegrationFieldMappingService` (Task 1/3).
- Produces: `IntegrationSyncHostedService : BackgroundService` — registrado en `PortalSaas.Host/Program.cs` (Task 6). Expone `internal Task EjecutarCicloAsync(CancellationToken)` (visible a tests vía `[InternalsVisibleTo]`) para probar un ciclo sin depender del loop `while` completo.

- [ ] **Step 1: Habilitar `InternalsVisibleTo` para tests**

Agregar a `src/PortalSaas.Integrations/PortalSaas.Integrations.csproj` dentro de `<ItemGroup>`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="PortalSaas.Core.Tests" />
</ItemGroup>
```

- [ ] **Step 2: Escribir el test que falla**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Data;
using PortalSaas.Data.Entities.Integraciones;
using PortalSaas.Integrations;
using Xunit;

namespace PortalSaas.Core.Tests.Integraciones;

public class IntegrationSyncHostedServiceTests
{
    private class ConectorFalso : IIntegrationConnector
    {
        public string Tipo => "Sap";
        public bool PushLlamado { get; private set; }

        public Task<IReadOnlyList<IntegrationRecord>> PullAsync(string conectorConfigJson, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IntegrationRecord>>(Array.Empty<IntegrationRecord>());

        public Task PushAsync(string conectorConfigJson, IReadOnlyList<IntegrationRecord> registros, CancellationToken cancellationToken)
        {
            PushLlamado = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task EjecutarCicloAsync_IntegracionVencida_EjecutaYRegistraLog()
    {
        var dbName = Guid.NewGuid().ToString();
        var conectorFalso = new ConectorFalso();

        var services = new ServiceCollection();
        services.AddDbContext<PortalSaasDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IIntegrationConnector>(conectorFalso);
        services.AddScoped<IIntegrationFieldMappingService, IntegrationFieldMappingService>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<IntegrationSyncHostedService>>(NullLogger<IntegrationSyncHostedService>.Instance);
        var proveedor = services.BuildServiceProvider();

        var definicion = new IntegrationDefinition
        {
            Nombre = "Test",
            ModuloOrigen = "Wms",
            EntidadNegocio = "PickingConfirmado",
            ConectorTipo = IntegrationConectorTipo.Sap,
            ConectorConfigCifrado = "{}",
            Direccion = IntegrationDireccion.Subida,
            Activo = true,
            NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        using (var scope = proveedor.CreateScope())
        {
            var contexto = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
            contexto.IntegrationDefinitions.Add(definicion);
            await contexto.SaveChangesAsync();
        }

        var servicio = new IntegrationSyncHostedService(
            proveedor.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IntegrationSyncHostedService>.Instance);

        await servicio.EjecutarCicloAsync(CancellationToken.None);

        using var scopeVerificacion = proveedor.CreateScope();
        var contextoVerificacion = scopeVerificacion.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
        var logs = await contextoVerificacion.IntegrationRunLogs
            .Where(l => l.IntegrationDefinitionId == definicion.Id)
            .ToListAsync();

        Assert.Single(logs);
        Assert.Equal(IntegrationRunResultado.Exito, logs[0].Resultado);
    }
}
```

- [ ] **Step 3: Ejecutar el test y verificar que falla**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter IntegrationSyncHostedServiceTests`
Expected: FAIL — `IntegrationSyncHostedService` no existe.

- [ ] **Step 4: Implementar `IntegrationSyncHostedService`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Data;
using PortalSaas.Data.Entities.Integraciones;

namespace PortalSaas.Integrations;

public sealed class IntegrationSyncHostedService : BackgroundService
{
    private static readonly TimeSpan IntervaloCiclo = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IntegrationSyncHostedService> _logger;

    public IntegrationSyncHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<IntegrationSyncHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await EjecutarCicloAsync(stoppingToken);

            try
            {
                await Task.Delay(IntervaloCiclo, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task EjecutarCicloAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
        var mapeoServicio = scope.ServiceProvider.GetRequiredService<IIntegrationFieldMappingService>();
        var conectores = scope.ServiceProvider.GetServices<IIntegrationConnector>().ToList();

        var ahora = DateTimeOffset.UtcNow;
        var pendientes = await contexto.IntegrationDefinitions
            .Where(d => d.Activo && d.NextRunAt != null && d.NextRunAt <= ahora)
            .ToListAsync(cancellationToken);

        foreach (var definicion in pendientes)
        {
            await EjecutarIntegracionAsync(contexto, mapeoServicio, conectores, definicion, cancellationToken);
        }
    }

    private async Task EjecutarIntegracionAsync(
        PortalSaasDbContext contexto,
        IIntegrationFieldMappingService mapeoServicio,
        List<IIntegrationConnector> conectores,
        IntegrationDefinition definicion,
        CancellationToken cancellationToken)
    {
        var log = new IntegrationRunLog
        {
            IntegrationDefinitionId = definicion.Id,
            IniciadoEn = DateTimeOffset.UtcNow,
            DisparadoPor = IntegrationRunDisparadoPor.Programado,
        };

        try
        {
            var conector = conectores.FirstOrDefault(c => c.Tipo == definicion.ConectorTipo.ToString())
                ?? throw new InvalidOperationException($"No hay conector registrado para tipo '{definicion.ConectorTipo}'.");

            if (definicion.Direccion is IntegrationDireccion.Subida or IntegrationDireccion.Ambas)
            {
                // La lectura de pendientes desde el módulo origen (IIntegrationEntityReader<T>)
                // se resuelve en el plan de migración del módulo consumidor (ver spec, fuera de alcance aquí).
                var registrosExternos = new List<IntegrationRecord>();
                await conector.PushAsync(definicion.ConectorConfigCifrado, registrosExternos, cancellationToken);
            }

            log.Resultado = IntegrationRunResultado.Exito;
            log.RegistrosProcesados = 0;
        }
        catch (Exception ex)
        {
            log.Resultado = IntegrationRunResultado.Error;
            log.DetalleError = ex.Message;
            _logger.LogError(ex, "Error ejecutando integración {IntegrationDefinitionId}", definicion.Id);
        }
        finally
        {
            log.FinalizadoEn = DateTimeOffset.UtcNow;
            definicion.NextRunAt = null;
            contexto.IntegrationRunLogs.Add(log);
            await contexto.SaveChangesAsync(cancellationToken);
        }
    }
}
```

- [ ] **Step 5: Ejecutar el test y verificar que pasa**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter IntegrationSyncHostedServiceTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PortalSaas.Integrations/IntegrationSyncHostedService.cs src/PortalSaas.Integrations/PortalSaas.Integrations.csproj tests/PortalSaas.Core.Tests/Integraciones/IntegrationSyncHostedServiceTests.cs
git commit -m "feat: agregar IntegrationSyncHostedService con ciclo de orquestación y bitácora"
```

---

### Task 6: Registro DI en `PortalSaas.Host`

**Files:**
- Modify: `src/PortalSaas.Host/Program.cs`
- Modify: `src/PortalSaas.Host/PortalSaas.Host.csproj` (agregar `ProjectReference` a `PortalSaas.Integrations`)

**Interfaces:**
- Consumes: `IntegrationFieldMappingService` (Task 3), `SapDocumentConnector` (Task 4), `IntegrationSyncHostedService` (Task 5).
- Produces: aplicación arrancando con el motor activo — deliverable final del plan, verificable manualmente.

- [ ] **Step 1: Agregar `ProjectReference`**

En `src/PortalSaas.Host/PortalSaas.Host.csproj`, dentro de `<ItemGroup>` de `ProjectReference`:

```xml
<ProjectReference Include="..\PortalSaas.Integrations\PortalSaas.Integrations.csproj" />
```

- [ ] **Step 2: Registrar servicios en `Program.cs`**

Ubicar el bloque donde se registra `builder.Services.AddScoped<ISecretoCifradoService, SecretoCifradoService>();` y agregar inmediatamente después:

```csharp
builder.Services.AddScoped<IIntegrationFieldMappingService, IntegrationFieldMappingService>();
builder.Services.AddSingleton<IIntegrationConnector, SapDocumentConnector>();
builder.Services.AddHostedService<IntegrationSyncHostedService>();
```

Agregar los `using` correspondientes al inicio de `Program.cs`:

```csharp
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Integrations;
using PortalSaas.Integrations.Connectors;
```

- [ ] **Step 3: Compilar la solución completa**

Run: `dotnet build PortalSaas.sln`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 4: Ejecutar toda la suite de tests**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj`
Expected: todos los tests pasan, incluyendo los 4 nuevos de este plan.

- [ ] **Step 5: Arrancar el Host y confirmar que no hay excepciones al iniciar el hosted service**

Run: `dotnet run --project src/PortalSaas.Host/PortalSaas.Host.csproj` (esperar 5-10s, luego detener con Ctrl+C)
Expected: log de arranque sin excepciones relacionadas a `IntegrationSyncHostedService` ni a resolución de DI.

- [ ] **Step 6: Commit**

```bash
git add src/PortalSaas.Host/Program.cs src/PortalSaas.Host/PortalSaas.Host.csproj
git commit -m "feat: registrar motor de integración (IntegrationSyncHostedService) en el Host"
```

---

## Self-Review

**1. Cobertura del spec:** Arquitectura (3 capas) → Tasks 1/3/4/5. Modelo de datos (3 tablas) → Task 2. Flujo de sincronización → Task 5. Cifrado de `ConectorConfig` → referenciado como constraint global (`ISecretoCifradoService`), el cifrado real ocurre en la futura UI admin de creación de `IntegrationDefinition` — **fuera de alcance de este plan** (no hay UI en este plan; se deja explícito abajo). Tareas de `Modulo.Wms`/`Modulo.Rendiciones` del spec: explícitamente fuera de alcance de este plan (motor base primero, consumo por módulo es un plan de seguimiento), consistente con el "Global Constraints".

**Gaps identificados y decisión:**
- UI admin (listar integraciones, bitácora, "ejecutar ahora") no está en este plan — es una capa de presentación que depende de que el motor base exista y compile primero. Debe ser un Task 7+/plan de seguimiento, no bloquea la entrega de este plan (el motor es funcional sin UI, operable vía datos sembrados directamente o futura migración de Wms).
- `SapDocumentConnector.PullAsync`/`PushAsync` reales (contra `SalesDocumentService` etc.) quedan explícitamente pendientes por una restricción de dirección de dependencias no resuelta en el spec original — documentado como nota de arquitectura en Task 4 en vez de dejarse como placeholder implícito.

**2. Placeholder scan:** sin "TBD"/"TODO" genéricos sin explicación — el único `NotSupportedException` en Task 4 lleva mensaje explícito y está justificado con una nota de arquitectura, no es un placeholder de "implementar después" sin contexto.

**3. Consistencia de tipos:** `IIntegrationConnector.Tipo` es `string` (Task 1) y se compara contra `definicion.ConectorTipo.ToString()` en Task 5 — consistente porque `IntegrationConectorTipo` (Task 2) es un enum con valores `Sap`/`Rest`/`Archivo`, cuyo `.ToString()` coincide con el `Tipo` (`"Sap"`) devuelto por `SapDocumentConnector` (Task 4). `IntegrationRecord`, `IIntegrationFieldMappingService`, `IIntegrationConnector` usados con las mismas firmas en Tasks 3, 4 y 5.
