# Instancia como servidor de BD de los módulos — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminar el catálogo `company_external_connections` y hacer que la base externa de cada módulo se configure como **Instancia + Base de datos** (mismo patrón que `Company`), con relación 1 a 1 `(compañía, módulo)` y credenciales opcionales propias por módulo.

**Architecture:** `Instance` (nivel organización) pasa a ser el "servidor de BD" genérico (agrega motor `postgres`). `CompanyModuleConnection` deja de apuntar a una conexión del catálogo y guarda `InstanceId + DatabaseName (+ usuario/clave opcionales)`, con índice único `(CompanyId, ModuleCode)`. El contrato que usan los plugins (`IExternalDatabaseConnectionService.ResolveConnectionAsync(moduleCode, companyId)`) **no cambia**; solo se elimina la sobrecarga con `purpose`, que nadie usa. Los módulos declaran que usan base externa con `IModuloPortal.RequiresExternalDatabase`. Una migración con transformación de datos agrupa las conexiones actuales en instancias, y un normalizador al arrancar el Host borra las credenciales propias redundantes.

**Tech Stack:** .NET 8, EF Core 8 (dual Postgres/SQL Server, `UseSnakeCaseNamingConvention`), Razor Pages, xUnit + `Microsoft.EntityFrameworkCore.InMemory`, `ISecretoCifradoService` (AES-256-GCM), `Npgsql`, `Microsoft.Data.SqlClient`, `Sap.Data.Hana`.

**Spec:** el diseño acordado en la conversación del 2026-10-13 (no hay un archivo de spec aparte). Decisiones fijadas:
1. Un módulo tiene **siempre** una sola base por compañía (1 a 1). Desaparece el concepto `Purpose`.
2. El servidor (host, puerto, motor, usuario y clave por defecto) vive en `Instance`. El módulo guarda solo la base y, **opcionalmente**, su propio usuario y clave.
3. El tipo `http_api` se elimina: no lo usa nadie. La integración WMS guarda su URL en su propia configuración, en `/Admin/Integraciones`.
4. Los plugins de `Portal SaaS - Plugins` llaman solo a `ResolveConnectionAsync(moduleCode, companyId[, ct])` y a `ListActiveCompanyIdsAsync`. Ninguno usa `purpose` (verificado con grep).
5. El admin del tenant (`/organizacion/conexiones-externas`) **elige** instancia y base, pero no crea instancias. Las instancias se crean en el backoffice `/Admin/Organizations/{id}/Instances`.

## Global Constraints

- Todos los paths de este plan son relativos a `Portal SaaS - Core/`, salvo los que empiezan con `../Portal SaaS - Plugins/`. Todo el código vive en **un solo repo git**: `C:\PROYECTOS\Proyecto Portal Web-Company`.
- **El árbol de trabajo tiene cambios ajenos sin commitear** (Importación Genérica, etc.). Trabajar en un worktree o rama nueva (`superpowers:using-git-worktrees`). En cada commit, `git add` **solo** los archivos de la tarea, nunca `git add -A`.
- El plugin `Modulo.Administracion` y los plugins de `Portal SaaS - Plugins` **solo** pueden referenciar `PortalSaas.Abstractions`, nunca `PortalSaas.Core`, `PortalSaas.Data` ni `PortalSaas.Host`.
- Toda migración se genera en **ambos** proyectos (`src/PortalSaas.Data.Migrations.PostgreSql` y `src/PortalSaas.Data.Migrations.SqlServer`), con `--context PortalSaasDbContext`. El Host **no** aplica migraciones al arrancar: son un paso manual (ver `docs/05-RUNBOOK-PRODUCCION.md`).
- Motores: los valores de `InstanceEngineType` (`"hana"`, `"sqlserver"`, `"postgres"`) son **idénticos** a los de `ExternalDatabaseEngineType`. Los plugins eligen `UseNpgsql`/`UseSqlServer` según ese string.
- Secretos: siempre write-only. Ningún DTO de lectura expone `TechnicalSecretKey`. En edición, clave en blanco = conservar la existente. Cifrado con `ISecretoCifradoService.Encrypt`/`.Decrypt`.
- Credenciales propias del módulo: van **las dos o ninguna** (`TechnicalUsername` y `TechnicalSecretKey` son ambas null o ambas no-null).
- Tests: `DbContext` con `UseInMemoryDatabase(Guid.NewGuid().ToString())`. InMemory **no** valida FKs, check constraints ni índices únicos, así que las reglas se prueban en la validación explícita del servicio.
- Errores de negocio: `InvalidOperationException` con un texto presentable al usuario, en español.
- **Estado del build entre tareas:** desde la Task 2 hasta la Task 6, `PortalSaas.Host` y `Modulo.Administracion` **no compilan** (consumen el contrato viejo). La compuerta de las Tasks 1 a 4 es `dotnet test tests/PortalSaas.Core.Tests` en verde. Esa suite no referencia el Host ni `Modulo.Administracion`. La compuerta de la Task 7 en adelante es la solución completa.
- Comando de tests (desde `Portal SaaS - Core/`): `dotnet test tests/PortalSaas.Core.Tests`.

## Review Focus

1. **Bindings que la migración descarta:** los que apuntan a una conexión inactiva o de tipo `http_api` se borran, porque hoy tampoco resuelven. Antes de migrar una base real, la query de pre-chequeo de la Task 9 debe mostrar cuántos hay, para que nadie pierda una asignación sin saberlo.
2. **Mismo host y usuario, pero distinta clave entre conexiones:** debe quedar **una** instancia, y la conexión cuya clave difiere debe conservar su credencial propia. Lo cubren los tests del normalizador (Task 2) y la verificación local con datos (Task 4).
3. **Instancia inactiva:** el resolver debe lanzar un mensaje presentable que nombre la instancia, y los background jobs (`ListActiveCompanyIdsAsync`) deben saltarse esa compañía. Test en la Task 2.
4. **Borrar una instancia que usa algún módulo:** debe bloquearse con un mensaje, no reventar con un `DbUpdateException` por la FK `Restrict`. Verificación manual en la Task 6.
5. **Selector de instancia en Compañías:** no debe ofrecer instancias `postgres`, porque una compañía SAP solo puede colgar de `hana` o `sqlserver`. El POST también debe rechazarlas. Verificación manual en la Task 6.

---

### Task 1: Constructor de connection strings compartido + motor `postgres`

Extrae a una sola función la construcción de connection strings que hoy está duplicada en el resolver y en "Probar", y agrega el motor `postgres` a `InstanceEngineType`. Es un cambio puramente aditivo: el modelo EF no cambia todavía.

**Files:**
- Modify: `src/PortalSaas.Data/Entities/Instance.cs`
- Create: `src/PortalSaas.Core/Infraestructura/ExternalDatabaseConnectionStringBuilder.cs`
- Test: `tests/PortalSaas.Core.Tests/Infraestructura/ExternalDatabaseConnectionStringBuilderTests.cs`

**Interfaces:**
- Produces:
  - `InstanceEngineType.Postgres = "postgres"`, `InstanceEngineType.All` (los 3), `InstanceEngineType.Sap` (`hana`, `sqlserver`), `InstanceEngineType.IsSap(string)`.
  - `internal static class ExternalDatabaseConnectionStringBuilder` con `public static string Build(string engineType, string host, int port, string databaseName, string username, string password, int? timeoutSeconds = null)`.

- [ ] **Step 0: Registrar la línea base**

Run: `dotnet test tests/PortalSaas.Core.Tests`
Expected: todo en verde. Anotar el total (`Passed: N`) en el commit de esta tarea.

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/PortalSaas.Core.Tests/Infraestructura/ExternalDatabaseConnectionStringBuilderTests.cs
using Microsoft.Data.SqlClient;
using Npgsql;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Infraestructura;

public sealed class ExternalDatabaseConnectionStringBuilderTests
{
    [Fact]
    public void Postgres_ArmaHostPuertoBaseYUsuario()
    {
        var cs = ExternalDatabaseConnectionStringBuilder.Build(
            InstanceEngineType.Postgres, "pg.local", 5432, "rendiciones", "app", "secreto");

        var b = new NpgsqlConnectionStringBuilder(cs);
        Assert.Equal("pg.local", b.Host);
        Assert.Equal(5432, b.Port);
        Assert.Equal("rendiciones", b.Database);
        Assert.Equal("app", b.Username);
        Assert.Equal("secreto", b.Password);
    }

    [Fact]
    public void SqlServer_UsaHostComaPuertoYConfiaEnElCertificado()
    {
        var cs = ExternalDatabaseConnectionStringBuilder.Build(
            InstanceEngineType.SqlServer, "sql.local", 1433, "gastos", "app", "secreto", timeoutSeconds: 5);

        var b = new SqlConnectionStringBuilder(cs);
        Assert.Equal("sql.local,1433", b.DataSource);
        Assert.Equal("gastos", b.InitialCatalog);
        Assert.Equal("app", b.UserID);
        Assert.True(b.TrustServerCertificate);
        Assert.Equal(5, b.ConnectTimeout);
    }

    [Fact]
    public void Hana_UsaLaBaseComoCurrentSchema()
    {
        var cs = ExternalDatabaseConnectionStringBuilder.Build(
            InstanceEngineType.Hana, "hana.local", 30015, "SBO_DEPOR", "app", "secreto");

        Assert.Equal("Server=hana.local:30015;UserID=app;Password=secreto;Current Schema=SBO_DEPOR", cs);
    }

    [Fact]
    public void MotorDesconocido_Lanza()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ExternalDatabaseConnectionStringBuilder.Build("oracle", "h", 1, "d", "u", "p"));
        Assert.Contains("oracle", ex.Message);
    }

    [Fact]
    public void LosMotoresDeInstanciaCoincidenConLosDelContratoDePlugins()
    {
        Assert.Equal(ExternalDatabaseEngineType.Postgres, InstanceEngineType.Postgres);
        Assert.Equal(ExternalDatabaseEngineType.SqlServer, InstanceEngineType.SqlServer);
        Assert.Equal(ExternalDatabaseEngineType.Hana, InstanceEngineType.Hana);
    }

    [Fact]
    public void Sap_SoloIncluyeHanaYSqlServer()
    {
        Assert.True(InstanceEngineType.IsSap(InstanceEngineType.Hana));
        Assert.True(InstanceEngineType.IsSap(InstanceEngineType.SqlServer));
        Assert.False(InstanceEngineType.IsSap(InstanceEngineType.Postgres));
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter "FullyQualifiedName~ExternalDatabaseConnectionStringBuilderTests"`
Expected: FAIL de compilación (`ExternalDatabaseConnectionStringBuilder` y `InstanceEngineType.Postgres` no existen).

- [ ] **Step 3: Agregar el motor `postgres` a `Instance.cs`**

Reemplazar el bloque `InstanceEngineType` y el `<summary>` de la clase en `src/PortalSaas.Data/Entities/Instance.cs`:

```csharp
/// <summary>
/// Un servidor de base de datos físico (HANA / SQL Server / Postgres) -- pertenece a una
/// Organization. De él cuelgan las Company SAP (Company.InstanceId + DatabaseName, solo
/// motores SAP) y las bases externas de los módulos (CompanyModuleConnection.InstanceId +
/// DatabaseName, cualquier motor). Usuario/clave técnicos son los por defecto; un módulo
/// puede sobreescribirlos (ver CompanyModuleConnection).
/// </summary>
public sealed class Instance
```

```csharp
/// <summary>
/// Motor de una Instance. Mismos strings que ExternalDatabaseEngineType (Abstractions) --
/// el resolver de bases externas los pasa tal cual al plugin.
/// </summary>
public static class InstanceEngineType
{
    public const string Hana = "hana";
    public const string SqlServer = "sqlserver";
    public const string Postgres = "postgres";

    public static readonly IReadOnlyCollection<string> All = [Hana, SqlServer, Postgres];

    /// <summary>Motores sobre los que puede correr una compañía SAP B1 (Company.InstanceId).</summary>
    public static readonly IReadOnlyCollection<string> Sap = [Hana, SqlServer];

    public static bool IsSap(string engineType) => Sap.Contains(engineType);
}
```

- [ ] **Step 4: Crear el builder**

```csharp
// src/PortalSaas.Core/Infraestructura/ExternalDatabaseConnectionStringBuilder.cs
using Microsoft.Data.SqlClient;
using Npgsql;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Arma la connection string de la base externa de un módulo a partir de los datos de la
/// Instance + la base + la credencial efectiva. Único lugar con este armado: lo usan el
/// resolver de runtime (ExternalDatabaseConnectionService) y el botón "Probar"
/// (CompanyExternalConnectionService.TestBindingAsync).
/// </summary>
internal static class ExternalDatabaseConnectionStringBuilder
{
    public static string Build(
        string engineType,
        string host,
        int port,
        string databaseName,
        string username,
        string password,
        int? timeoutSeconds = null)
        => engineType switch
        {
            InstanceEngineType.Postgres => new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = port,
                Database = databaseName,
                Username = username,
                Password = password,
                Timeout = timeoutSeconds ?? 15,
            }.ConnectionString,
            InstanceEngineType.SqlServer => new SqlConnectionStringBuilder
            {
                DataSource = $"{host},{port}",
                InitialCatalog = databaseName,
                UserID = username,
                Password = password,
                // Mismo motivo que Sap/SapConnectionStringFactory: instalaciones con
                // certificado propio (self-signed/CA interna) rechazan el login sin esto.
                TrustServerCertificate = true,
                ConnectTimeout = timeoutSeconds ?? 15,
            }.ConnectionString,
            InstanceEngineType.Hana =>
                $"Server={host}:{port};UserID={username};Password={password};Current Schema={databaseName}",
            _ => throw new InvalidOperationException($"Motor de base de datos no soportado: '{engineType}'."),
        };
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter "FullyQualifiedName~ExternalDatabaseConnectionStringBuilderTests"`
Expected: PASS (6 tests).

Run: `dotnet test tests/PortalSaas.Core.Tests`
Expected: verde, con línea base + 6.

- [ ] **Step 6: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Data/Entities/Instance.cs" \
        "Portal SaaS - Core/src/PortalSaas.Core/Infraestructura/ExternalDatabaseConnectionStringBuilder.cs" \
        "Portal SaaS - Core/tests/PortalSaas.Core.Tests/Infraestructura/ExternalDatabaseConnectionStringBuilderTests.cs"
git commit -m "feat(conexiones): motor postgres en Instance + builder unico de connection strings"
```

---

### Task 2: Modelo nuevo (contratos + entidades + resolver + normalizador)

Reemplaza el modelo de catálogo por `Instance + DatabaseName`. Borra el servicio de catálogo viejo, que se reescribe en la Task 3. Al terminar esta tarea, Core y Core.Tests compilan; el Host no (ver Global Constraints).

**Files:**
- Modify: `src/PortalSaas.Abstractions/Contratos/IModuloPortal.cs` (líneas 45-50)
- Modify: `src/PortalSaas.Abstractions/Contratos/IExternalDatabaseConnectionService.cs`
- Modify: `src/PortalSaas.Abstractions/Contratos/ICompanyExternalConnectionService.cs`
- Modify: `src/PortalSaas.Abstractions/Modelos/CompanyExternalConnectionModels.cs`
- Delete: `src/PortalSaas.Abstractions/Modelos/ExternalConnectionType.cs`
- Modify: `src/PortalSaas.Data/Entities/CompanyModuleConnection.cs`
- Modify: `src/PortalSaas.Data/Entities/Instance.cs` (navegación `ModuleConnections`)
- Modify: `src/PortalSaas.Data/Entities/PlatformModule.cs` (solo el comentario de la línea 20)
- Delete: `src/PortalSaas.Data/Entities/CompanyExternalConnection.cs`
- Delete: `src/PortalSaas.Data/Entities/ModuleExternalConnection.cs`
- Modify: `src/PortalSaas.Data/PortalSaasDbContext.cs` (DbSets líneas 41-43; config de `Instance` líneas 231-242; bloques `ModuleExternalConnection`, `CompanyExternalConnection` y `CompanyModuleConnection`, líneas 244-299)
- Create: `src/PortalSaas.Core/Infraestructura/ModuleDatabaseCredentials.cs`
- Modify: `src/PortalSaas.Core/Infraestructura/ExternalDatabaseConnectionService.cs`
- Create: `src/PortalSaas.Core/Administracion/ModuleDatabaseCredentialNormalizer.cs`
- Delete: `src/PortalSaas.Core/Administracion/LegacyExternalConnectionBackfill.cs`
- Delete: `src/PortalSaas.Core/Administracion/CompanyExternalConnectionService.cs` (se recrea en la Task 3)
- Delete: `tests/PortalSaas.Core.Tests/Administracion/CompanyExternalConnectionEntitiesTests.cs`
- Delete: `tests/PortalSaas.Core.Tests/Administracion/LegacyExternalConnectionBackfillTests.cs`
- Delete: `tests/PortalSaas.Core.Tests/Administracion/CompanyExternalConnectionServiceTests.cs` (se recrea en la Task 3)
- Modify: `tests/PortalSaas.Core.Tests/Administracion/ExternalConnectionContractsTests.cs`
- Modify (reescritura completa): `tests/PortalSaas.Core.Tests/Infraestructura/ExternalDatabaseConnectionServiceTests.cs`
- Create: `tests/PortalSaas.Core.Tests/Administracion/ModuleDatabaseCredentialNormalizerTests.cs`

**Interfaces:**
- Consumes: `ExternalDatabaseConnectionStringBuilder.Build(...)` y `InstanceEngineType` (Task 1).
- Produces:
  - `IModuloPortal.RequiresExternalDatabase` (`bool`, default `false`).
  - DTOs `DatabaseInstanceOptionDto(long Id, string Name, string EngineType, string Host, int Port)`, `ModuleConnectionBindingDto(string ModuleCode, string ModuleName, long? InstanceId, string? InstanceName, string? DatabaseName, string? OwnTechnicalUsername, bool HasOwnCredentials)`, clase `ModuleDatabaseBindingInput { long InstanceId; string DatabaseName; bool UseOwnCredentials; string? TechnicalUsername; string? TechnicalSecretKey; }`, `ConnectionTestResultDto(bool Ok, string? Error, long? ElapsedMs)` (sin cambios).
  - `ICompanyExternalConnectionService` con `ListInstancesAsync`, `ListBindingsAsync`, `GetBindingAsync`, `SetBindingAsync`, `ClearBindingAsync` y `TestBindingAsync` (firmas en el Step 1c).
  - Entidad `CompanyModuleConnection { long Id; Guid CompanyId; Company Company; string ModuleCode; long InstanceId; Instance Instance; string DatabaseName; string? TechnicalUsername; string? TechnicalSecretKey; DateTimeOffset UpdatedAt; }`.
  - `Instance.ModuleConnections` (`ICollection<CompanyModuleConnection>`).
  - `internal static class ModuleDatabaseCredentials` con `public static (string Username, string Password) Resolve(CompanyModuleConnection binding, ISecretoCifradoService secretos)`. Requiere `binding.Instance` cargado.
  - `public sealed class ModuleDatabaseCredentialNormalizer(PortalSaasDbContext db, ISecretoCifradoService secretos, ILogger<ModuleDatabaseCredentialNormalizer> logger)` con `Task<int> RunAsync(CancellationToken ct)`.

- [ ] **Step 1a: Contrato del módulo (`IModuloPortal.cs`)**

Reemplazar las líneas 45-50 (el doc-comment y la propiedad `ExternalConnectionRequirements`) por:

```csharp
    /// <summary>
    /// true = el módulo usa una base de datos EXTERNA propia (una sola por compañía),
    /// resuelta con IExternalDatabaseConnectionService.ResolveConnectionAsync y configurada
    /// en "Conexiones externas" como Instancia + Base de datos. Default false: los módulos
    /// que solo hablan con SAP (Compras, Ventas, Inventario, ...) no aparecen en esa grilla.
    /// </summary>
    bool RequiresExternalDatabase => false;
```

- [ ] **Step 1b: DTOs (`CompanyExternalConnectionModels.cs`, reescritura completa)**

```csharp
using System.ComponentModel.DataAnnotations;

namespace PortalSaas.Abstractions.Modelos;

/// <summary>Instancia (servidor de BD) de la organización, elegible para la base de un módulo. NUNCA incluye el secreto.</summary>
public sealed record DatabaseInstanceOptionDto(long Id, string Name, string EngineType, string Host, int Port);

/// <summary>
/// Una fila de la grilla módulo → (instancia, base) de una compañía. Solo módulos con
/// IModuloPortal.RequiresExternalDatabase. InstanceId null = sin asignar. NUNCA incluye el secreto.
/// </summary>
public sealed record ModuleConnectionBindingDto(
    string ModuleCode,
    string ModuleName,
    long? InstanceId,
    string? InstanceName,
    string? DatabaseName,
    string? OwnTechnicalUsername,
    bool HasOwnCredentials);

/// <summary>
/// Alta/edición de la base de un módulo. UseOwnCredentials=false ⇒ usa usuario/clave de la
/// instancia (y borra cualquier credencial propia guardada). UseOwnCredentials=true ⇒
/// usuario obligatorio; clave obligatoria solo si no había una propia guardada (en blanco =
/// conservar). Secreto write-only.
/// </summary>
public sealed class ModuleDatabaseBindingInput
{
    [Range(1, long.MaxValue, ErrorMessage = "Elige una instancia.")]
    public long InstanceId { get; set; }

    [Required(ErrorMessage = "Ingresa la base de datos.")]
    [StringLength(100)]
    public string DatabaseName { get; set; } = string.Empty;

    public bool UseOwnCredentials { get; set; }

    [StringLength(100)]
    public string? TechnicalUsername { get; set; }

    public string? TechnicalSecretKey { get; set; }
}

public sealed record ConnectionTestResultDto(bool Ok, string? Error, long? ElapsedMs);
```

Borrar `src/PortalSaas.Abstractions/Modelos/ExternalConnectionType.cs`.

- [ ] **Step 1c: Contratos de servicio**

`src/PortalSaas.Abstractions/Contratos/ICompanyExternalConnectionService.cs` (reescritura completa):

```csharp
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Base de datos externa de cada módulo de una compañía: Instancia (servidor de la
/// organización) + nombre de la base + credencial propia opcional. Relación 1 a 1
/// (compañía, módulo). Todos los métodos reciben organizationId y companyId explícitos:
/// /Admin los toma de la ruta; el plugin Modulo.Administracion, de ICurrentUserContext +
/// selector. El servicio valida que companyId pertenezca a organizationId.
/// </summary>
public interface ICompanyExternalConnectionService
{
    /// <summary>Instancias ACTIVAS de la organización (cualquier motor).</summary>
    Task<IReadOnlyList<DatabaseInstanceOptionDto>> ListInstancesAsync(Guid organizationId, Guid companyId, CancellationToken ct = default);

    /// <summary>Una fila por módulo cargado con RequiresExternalDatabase, asignado o no.</summary>
    Task<IReadOnlyList<ModuleConnectionBindingDto>> ListBindingsAsync(Guid organizationId, Guid companyId, CancellationToken ct = default);

    /// <summary>null si el módulo no está cargado o no usa base externa.</summary>
    Task<ModuleConnectionBindingDto?> GetBindingAsync(Guid organizationId, Guid companyId, string moduleCode, CancellationToken ct = default);

    Task SetBindingAsync(Guid organizationId, Guid companyId, string moduleCode, ModuleDatabaseBindingInput input, CancellationToken ct = default);
    Task ClearBindingAsync(Guid organizationId, Guid companyId, string moduleCode, CancellationToken ct = default);
    Task<ConnectionTestResultDto> TestBindingAsync(Guid organizationId, Guid companyId, string moduleCode, CancellationToken ct = default);
}
```

En `src/PortalSaas.Abstractions/Contratos/IExternalDatabaseConnectionService.cs`:
- Borrar la sobrecarga `ResolveConnectionAsync(string moduleCode, Guid companyId, string purpose, CancellationToken ct = default)` y su `<summary>`.
- Reemplazar el `<summary>` de `ListActiveCompanyIdsAsync` por:

```csharp
    /// <summary>
    /// Lista (CompanyId, OrganizationId) de toda compañía con una base asignada a
    /// moduleCode sobre una Instance ACTIVA -- para procesos sin sesión HTTP (background
    /// jobs) que necesitan recorrer todas las compañías de un módulo, algo que
    /// ResolveConnectionAsync no puede hacer porque ya exige conocer el companyId.
    /// </summary>
```

- [ ] **Step 1d: Entidades**

`src/PortalSaas.Data/Entities/CompanyModuleConnection.cs` (reescritura completa):

```csharp
namespace PortalSaas.Data.Entities;

/// <summary>
/// Base de datos externa de un módulo para una compañía -- relación 1 a 1
/// (CompanyId, ModuleCode). Mismo patrón que Company (InstanceId + DatabaseName): el
/// servidor y su credencial por defecto viven en Instance; acá solo la base y,
/// opcionalmente, una credencial propia (mínimo privilegio: el usuario de un módulo no
/// necesita ver las bases de los demás). TechnicalUsername/TechnicalSecretKey van los dos
/// o ninguno -- null = usa los de la instancia (ver ModuleDatabaseCredentials).
/// </summary>
public sealed class CompanyModuleConnection
{
    public long Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>IModuloPortal.ModuleCode.</summary>
    public string ModuleCode { get; set; } = null!;

    public long InstanceId { get; set; }
    public Instance Instance { get; set; } = null!;

    public string DatabaseName { get; set; } = null!;

    /// <summary>null = usa Instance.TechnicalUsername.</summary>
    public string? TechnicalUsername { get; set; }

    /// <summary>Cifrado con ISecretoCifradoService. Write-only. null = usa Instance.TechnicalSecretKey.</summary>
    public string? TechnicalSecretKey { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

En `src/PortalSaas.Data/Entities/Instance.cs`, debajo de `Companies`, agregar:

```csharp
    public ICollection<CompanyModuleConnection> ModuleConnections { get; set; } = new List<CompanyModuleConnection>();
```

Borrar `CompanyExternalConnection.cs` y `ModuleExternalConnection.cs` (el segundo archivo incluye `ModuleExternalConnectionEngineType`).

En `src/PortalSaas.Data/Entities/PlatformModule.cs`, línea 20: reemplazar la referencia a `ModuleExternalConnection.CompanyId` por `CompanyModuleConnection.CompanyId`, sin cambiar el sentido del comentario.

- [ ] **Step 1e: `PortalSaasDbContext.cs`**

1. Borrar los DbSets `ModuleExternalConnections` y `CompanyExternalConnections` (líneas 41-42). Se conserva `CompanyModuleConnections`.
2. En la config de `Instance`, reemplazar solo el check constraint. La relación con `ModuleConnections` se configura del lado de `CompanyModuleConnection` (punto 4):

```csharp
            entity.ToTable("instances", t => t.HasCheckConstraint("ck_instances_engine_type",
                $"engine_type in ('{InstanceEngineType.Hana}', '{InstanceEngineType.SqlServer}', '{InstanceEngineType.Postgres}')"));
```

3. Borrar los bloques `modelBuilder.Entity<ModuleExternalConnection>(...)` y `modelBuilder.Entity<CompanyExternalConnection>(...)` completos.
4. Reemplazar el bloque `modelBuilder.Entity<CompanyModuleConnection>(...)` por:

```csharp
        modelBuilder.Entity<CompanyModuleConnection>(entity =>
        {
            entity.ToTable("company_module_connections");
            // 1 a 1: un módulo tiene una sola base por compañía.
            entity.HasIndex(e => new { e.CompanyId, e.ModuleCode })
                .IsUnique()
                .HasDatabaseName("uq_company_module_connections_company_module");
            entity.HasIndex(e => e.InstanceId)
                .HasDatabaseName("ix_company_module_connections_instance_id");
            entity.Property(e => e.ModuleCode).HasMaxLength(50);
            entity.Property(e => e.DatabaseName).HasMaxLength(100);
            entity.Property(e => e.TechnicalUsername).HasMaxLength(100);
            entity.Property(e => e.TechnicalSecretKey).HasMaxLength(500);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
            // Restrict, no Cascade: Company → Instance ya es un camino en cascada; un
            // segundo camino rompe SQL Server (error 1785). Borrar una instancia en uso se
            // bloquea con un chequeo explícito en Instances/Index.
            entity.HasOne(e => e.Instance).WithMany(i => i.ModuleConnections).HasForeignKey(e => e.InstanceId).OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 2: Escribir los tests que fallan (resolver + normalizador + contrato)**

`tests/PortalSaas.Core.Tests/Infraestructura/ExternalDatabaseConnectionServiceTests.cs` (reescritura completa):

```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Infraestructura;

public sealed class ExternalDatabaseConnectionServiceTests
{
    private sealed class SecretosIdentidad : ISecretoCifradoService
    {
        public string Encrypt(string plainText) => "enc:" + plainText;
        public string Decrypt(string cipherText) => cipherText.StartsWith("enc:") ? cipherText[4..] : cipherText;
    }

    private static PortalSaasDbContext NuevoContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ExternalDatabaseConnectionService Crear(PortalSaasDbContext db) => new(db, new SecretosIdentidad());

    private static Company NuevaCompania(Guid orgId, long instanceId, string code) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = orgId, InstanceId = instanceId, Code = code, Name = code,
        DatabaseName = "SBO", ServiceLayerUrl = "https://x", IntegrationUsername = "x", IntegrationSecretKey = "x", Country = "CL",
    };

    private static Instance NuevaInstancia(Guid orgId, string name, bool activa = true) => new()
    {
        OrganizationId = orgId, Name = name, Host = "pg.local", Port = 5432,
        EngineType = InstanceEngineType.Postgres, TechnicalUsername = "inst_user",
        TechnicalSecretKey = "enc:inst_pass", IsActive = activa,
    };

    [Fact]
    public async Task ResolveConnectionAsync_SinCredencialPropia_UsaLaDeLaInstancia()
    {
        await using var db = NuevoContexto();
        var orgId = Guid.NewGuid();
        var inst = NuevaInstancia(orgId, "pg");
        db.Instances.Add(inst);
        await db.SaveChangesAsync();
        var company = NuevaCompania(orgId, inst.Id, "DEPOR");
        db.Companies.Add(company);
        db.CompanyModuleConnections.Add(new CompanyModuleConnection
        {
            CompanyId = company.Id, ModuleCode = "Rendiciones", InstanceId = inst.Id, DatabaseName = "ps_rendiciones",
        });
        await db.SaveChangesAsync();

        var r = await Crear(db).ResolveConnectionAsync("Rendiciones", company.Id);

        Assert.Equal(ExternalDatabaseEngineType.Postgres, r.EngineType);
        var b = new NpgsqlConnectionStringBuilder(r.ConnectionString);
        Assert.Equal("pg.local", b.Host);
        Assert.Equal("ps_rendiciones", b.Database);
        Assert.Equal("inst_user", b.Username);
        Assert.Equal("inst_pass", b.Password);
    }

    [Fact]
    public async Task ResolveConnectionAsync_ConCredencialPropia_LaUsaEnVezDeLaDeLaInstancia()
    {
        await using var db = NuevoContexto();
        var orgId = Guid.NewGuid();
        var inst = NuevaInstancia(orgId, "pg");
        db.Instances.Add(inst);
        await db.SaveChangesAsync();
        var company = NuevaCompania(orgId, inst.Id, "DEPOR");
        db.Companies.Add(company);
        db.CompanyModuleConnections.Add(new CompanyModuleConnection
        {
            CompanyId = company.Id, ModuleCode = "Wms", InstanceId = inst.Id, DatabaseName = "ps_wms",
            TechnicalUsername = "wms_user", TechnicalSecretKey = "enc:wms_pass",
        });
        await db.SaveChangesAsync();

        var r = await Crear(db).ResolveConnectionAsync("Wms", company.Id);

        var b = new NpgsqlConnectionStringBuilder(r.ConnectionString);
        Assert.Equal("wms_user", b.Username);
        Assert.Equal("wms_pass", b.Password);
    }

    [Fact]
    public async Task ResolveConnectionAsync_SinAsignacion_LanzaMensajePresentable()
    {
        await using var db = NuevoContexto();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).ResolveConnectionAsync("Rendiciones", Guid.NewGuid()));

        Assert.Contains("Rendiciones", ex.Message);
        Assert.Contains("Administración", ex.Message);
    }

    [Fact]
    public async Task ResolveConnectionAsync_InstanciaInactiva_LanzaNombrandoLaInstancia()
    {
        await using var db = NuevoContexto();
        var orgId = Guid.NewGuid();
        var inst = NuevaInstancia(orgId, "pg-apagado", activa: false);
        db.Instances.Add(inst);
        await db.SaveChangesAsync();
        var company = NuevaCompania(orgId, inst.Id, "DEPOR");
        db.Companies.Add(company);
        db.CompanyModuleConnections.Add(new CompanyModuleConnection
        {
            CompanyId = company.Id, ModuleCode = "SellOut", InstanceId = inst.Id, DatabaseName = "sellout",
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).ResolveConnectionAsync("SellOut", company.Id));

        Assert.Contains("pg-apagado", ex.Message);
    }

    [Fact]
    public async Task ListActiveCompanyIdsAsync_SoloCompaniasDelModuloConInstanciaActiva()
    {
        await using var db = NuevoContexto();
        var orgId = Guid.NewGuid();
        var activa = NuevaInstancia(orgId, "pg");
        var inactiva = NuevaInstancia(orgId, "pg-off", activa: false);
        db.Instances.AddRange(activa, inactiva);
        await db.SaveChangesAsync();
        var a = NuevaCompania(orgId, activa.Id, "A");
        var b = NuevaCompania(orgId, activa.Id, "B");
        db.Companies.AddRange(a, b);
        db.CompanyModuleConnections.AddRange(
            new CompanyModuleConnection { CompanyId = a.Id, ModuleCode = "Rendiciones", InstanceId = activa.Id, DatabaseName = "r" },
            new CompanyModuleConnection { CompanyId = b.Id, ModuleCode = "Rendiciones", InstanceId = inactiva.Id, DatabaseName = "r" },
            new CompanyModuleConnection { CompanyId = b.Id, ModuleCode = "OtroModulo", InstanceId = activa.Id, DatabaseName = "o" });
        await db.SaveChangesAsync();

        var r = await Crear(db).ListActiveCompanyIdsAsync("Rendiciones");

        var unica = Assert.Single(r);
        Assert.Equal(a.Id, unica.CompanyId);
        Assert.Equal(orgId, unica.OrganizationId);
    }
}
```

`tests/PortalSaas.Core.Tests/Administracion/ModuleDatabaseCredentialNormalizerTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Administracion;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Administracion;

public sealed class ModuleDatabaseCredentialNormalizerTests
{
    /// <summary>
    /// Simula AES-GCM: la misma clave cifrada dos veces da textos distintos ("a:x" vs "b:x"),
    /// pero descifra igual. Por eso el normalizador debe comparar en claro, no el cifrado.
    /// </summary>
    private sealed class SecretosConNonce : ISecretoCifradoService
    {
        public string Encrypt(string plainText) => "n:" + plainText;
        public string Decrypt(string cipherText) => cipherText[(cipherText.IndexOf(':') + 1)..];
    }

    private static PortalSaasDbContext NuevoContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<CompanyModuleConnection> SembrarAsync(
        PortalSaasDbContext db, string? usuarioPropio, string? clavePropiaCifrada)
    {
        var inst = new Instance
        {
            OrganizationId = Guid.NewGuid(), Name = "pg", Host = "h", Port = 5432,
            EngineType = InstanceEngineType.Postgres, TechnicalUsername = "app", TechnicalSecretKey = "a:clave",
        };
        db.Instances.Add(inst);
        await db.SaveChangesAsync();
        var b = new CompanyModuleConnection
        {
            CompanyId = Guid.NewGuid(), ModuleCode = "Rendiciones", InstanceId = inst.Id, DatabaseName = "r",
            TechnicalUsername = usuarioPropio, TechnicalSecretKey = clavePropiaCifrada,
        };
        db.CompanyModuleConnections.Add(b);
        await db.SaveChangesAsync();
        return b;
    }

    private static ModuleDatabaseCredentialNormalizer Crear(PortalSaasDbContext db) =>
        new(db, new SecretosConNonce(), NullLogger<ModuleDatabaseCredentialNormalizer>.Instance);

    [Fact]
    public async Task MismoUsuarioYMismaClaveEnClaro_BorraLaCredencialPropia()
    {
        await using var db = NuevoContexto();
        var b = await SembrarAsync(db, "app", "b:clave");

        var limpiados = await Crear(db).RunAsync(CancellationToken.None);

        Assert.Equal(1, limpiados);
        var recargado = await db.CompanyModuleConnections.AsNoTracking().SingleAsync(x => x.Id == b.Id);
        Assert.Null(recargado.TechnicalUsername);
        Assert.Null(recargado.TechnicalSecretKey);
    }

    [Fact]
    public async Task ClaveDistinta_ConservaLaCredencialPropia()
    {
        await using var db = NuevoContexto();
        var b = await SembrarAsync(db, "app", "b:otra-clave");

        var limpiados = await Crear(db).RunAsync(CancellationToken.None);

        Assert.Equal(0, limpiados);
        var recargado = await db.CompanyModuleConnections.AsNoTracking().SingleAsync(x => x.Id == b.Id);
        Assert.Equal("b:otra-clave", recargado.TechnicalSecretKey);
    }

    [Fact]
    public async Task UsuarioDistinto_ConservaLaCredencialPropia()
    {
        await using var db = NuevoContexto();
        await SembrarAsync(db, "otro_usuario", "b:clave");

        Assert.Equal(0, await Crear(db).RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SegundaCorrida_NoHaceNada()
    {
        await using var db = NuevoContexto();
        await SembrarAsync(db, "app", "b:clave");

        await Crear(db).RunAsync(CancellationToken.None);
        Assert.Equal(0, await Crear(db).RunAsync(CancellationToken.None));
    }
}
```

`tests/PortalSaas.Core.Tests/Administracion/ExternalConnectionContractsTests.cs` (reescritura completa):

```csharp
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using Xunit;

namespace PortalSaas.Core.Tests.Administracion;

public sealed class ExternalConnectionContractsTests
{
    [Fact]
    public void BindingInput_PorDefectoUsaLasCredencialesDeLaInstancia()
    {
        var m = new ModuleDatabaseBindingInput();
        Assert.False(m.UseOwnCredentials);
        Assert.Null(m.TechnicalUsername);
        Assert.Null(m.TechnicalSecretKey);
    }

    [Fact]
    public void ModuloQueNoLoDeclara_NoUsaBaseExterna()
    {
        IModuloPortal modulo = new ModuloDummy();
        Assert.False(modulo.RequiresExternalDatabase);
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

Borrar `CompanyExternalConnectionEntitiesTests.cs`, `LegacyExternalConnectionBackfillTests.cs` y `CompanyExternalConnectionServiceTests.cs`. Este último se recrea en la Task 3.

- [ ] **Step 3: Correr los tests y verificar que fallan**

Run: `dotnet test tests/PortalSaas.Core.Tests`
Expected: FAIL de compilación en Core (`CompanyExternalConnectionService.cs` y `LegacyExternalConnectionBackfill.cs` usan tipos borrados; `ModuleDatabaseCredentialNormalizer` no existe).

- [ ] **Step 4: Borrar el código viejo de Core**

Borrar `src/PortalSaas.Core/Administracion/LegacyExternalConnectionBackfill.cs` y `src/PortalSaas.Core/Administracion/CompanyExternalConnectionService.cs`.

- [ ] **Step 5: Credencial efectiva**

```csharp
// src/PortalSaas.Core/Infraestructura/ModuleDatabaseCredentials.cs
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Credencial efectiva de la base de un módulo: la propia del binding si la tiene, si no
/// la de su Instance. Requiere binding.Instance cargado (Include).
/// </summary>
internal static class ModuleDatabaseCredentials
{
    public static (string Username, string Password) Resolve(CompanyModuleConnection binding, ISecretoCifradoService secretos)
    {
        var tienePropia = !string.IsNullOrEmpty(binding.TechnicalSecretKey);
        var username = tienePropia && !string.IsNullOrWhiteSpace(binding.TechnicalUsername)
            ? binding.TechnicalUsername!
            : binding.Instance.TechnicalUsername;
        var cifrada = tienePropia ? binding.TechnicalSecretKey! : binding.Instance.TechnicalSecretKey;
        var password = string.IsNullOrEmpty(cifrada) ? "" : secretos.Decrypt(cifrada);
        return (username, password);
    }
}
```

- [ ] **Step 6: Resolver (`ExternalDatabaseConnectionService.cs`, reescritura completa)**

```csharp
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Implementación real de IExternalDatabaseConnectionService -- resuelve la base externa
/// de (módulo, compañía) contra PortalSaasDbContext.CompanyModuleConnections: Instance
/// (servidor + credencial por defecto) + DatabaseName + credencial propia opcional.
/// Nunca contra HANA/SAP. Sin caché (YAGNI, mismo criterio que antes).
/// </summary>
public sealed class ExternalDatabaseConnectionService : IExternalDatabaseConnectionService
{
    private readonly PortalSaasDbContext _db;
    private readonly ISecretoCifradoService _secretos;

    public ExternalDatabaseConnectionService(PortalSaasDbContext db, ISecretoCifradoService secretos)
    {
        _db = db;
        _secretos = secretos;
    }

    public async Task<ExternalDatabaseConnection> ResolveConnectionAsync(
        string moduleCode,
        Guid companyId,
        CancellationToken ct = default)
    {
        var binding = await _db.CompanyModuleConnections
                .AsNoTracking()
                .Include(b => b.Instance)
                .FirstOrDefaultAsync(b => b.ModuleCode == moduleCode && b.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException(
                $"No hay una base de datos externa asociada al módulo '{moduleCode}' para esta " +
                "compañía -- configurarla desde Administración.");

        if (!binding.Instance.IsActive)
        {
            throw new InvalidOperationException(
                $"La instancia '{binding.Instance.Name}' asignada al módulo '{moduleCode}' está inactiva -- " +
                "activarla o asignar otra desde Administración.");
        }

        var (username, password) = ModuleDatabaseCredentials.Resolve(binding, _secretos);

        return new ExternalDatabaseConnection
        {
            EngineType = binding.Instance.EngineType,
            ConnectionString = ExternalDatabaseConnectionStringBuilder.Build(
                binding.Instance.EngineType,
                binding.Instance.Host,
                binding.Instance.Port,
                binding.DatabaseName,
                username,
                password),
        };
    }

    public async Task<IReadOnlyList<ModuleCompanyDto>> ListActiveCompanyIdsAsync(
        string moduleCode,
        CancellationToken ct = default)
    {
        return await _db.CompanyModuleConnections
            .AsNoTracking()
            .Where(b => b.ModuleCode == moduleCode && b.Instance.IsActive)
            .Select(b => new ModuleCompanyDto(b.CompanyId, b.Company.OrganizationId))
            .Distinct()
            .ToListAsync(ct);
    }
}
```

- [ ] **Step 7: Normalizador**

```csharp
// src/PortalSaas.Core/Administracion/ModuleDatabaseCredentialNormalizer.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;

namespace PortalSaas.Core.Administracion;

/// <summary>
/// One-shot idempotente, corre al arrancar el Host. La migración
/// InstanceAsModuleDatabaseServer agrupa las conexiones viejas en instancias por
/// (motor, host, puerto, usuario), pero NO puede comparar claves en SQL: AES-GCM cifra la
/// misma clave en textos distintos. Por eso deja como "credencial propia" toda clave cuyo
/// texto cifrado no coincide con el de la instancia. Este paso descifra y borra las que
/// son en realidad la misma clave y el mismo usuario que la instancia. Correrlo de nuevo
/// no cambia nada.
/// </summary>
public sealed class ModuleDatabaseCredentialNormalizer(
    PortalSaasDbContext db,
    ISecretoCifradoService secretos,
    ILogger<ModuleDatabaseCredentialNormalizer> logger)
{
    /// <summary>Devuelve la cantidad de credenciales propias borradas por redundantes.</summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var conPropia = await db.CompanyModuleConnections
            .Include(b => b.Instance)
            .Where(b => b.TechnicalSecretKey != null)
            .ToListAsync(ct);

        var limpiados = 0;
        foreach (var b in conPropia)
        {
            var mismoUsuario = string.IsNullOrWhiteSpace(b.TechnicalUsername)
                || b.TechnicalUsername == b.Instance.TechnicalUsername;
            if (!mismoUsuario)
            {
                continue;
            }

            string propia;
            string deInstancia;
            try
            {
                propia = secretos.Decrypt(b.TechnicalSecretKey!);
                deInstancia = secretos.Decrypt(b.Instance.TechnicalSecretKey);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Normalizador: no se pudo descifrar la credencial del binding {BindingId} ({ModuleCode}); se conserva.",
                    b.Id, b.ModuleCode);
                continue;
            }

            if (propia != deInstancia)
            {
                continue;
            }

            b.TechnicalUsername = null;
            b.TechnicalSecretKey = null;
            b.UpdatedAt = DateTimeOffset.UtcNow;
            limpiados++;
        }

        if (limpiados > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return limpiados;
    }
}
```

- [ ] **Step 8: Correr los tests y verificar que pasan**

Run: `dotnet test tests/PortalSaas.Core.Tests`
Expected: verde. Total = línea base + 6 (Task 1) − (tests borrados de `CompanyExternalConnectionEntitiesTests`, `LegacyExternalConnectionBackfillTests`, `CompanyExternalConnectionServiceTests` y el viejo `ExternalDatabaseConnectionServiceTests`) + 5 (resolver) + 4 (normalizador). `ExternalConnectionContractsTests` sigue con 2. Verificar con `grep -c "\[Fact\]"` sobre los archivos borrados si el número no cuadra.

Si falla con "`CompanyExternalConnection` no existe" en algún archivo de `tests/`: buscar con `grep -rn "CompanyExternalConnection\b\|ModuleExternalConnection\|ExternalConnectionType\|LegacyExternalConnectionBackfill" tests src/PortalSaas.Core src/PortalSaas.Data src/PortalSaas.Abstractions --include=*.cs` y corregir. Fuera de `src/PortalSaas.Host` y `plugins/` no debe quedar ninguna referencia.

- [ ] **Step 9: Commit**

```bash
git add -- "Portal SaaS - Core/src/PortalSaas.Abstractions" "Portal SaaS - Core/src/PortalSaas.Data/Entities" \
           "Portal SaaS - Core/src/PortalSaas.Data/PortalSaasDbContext.cs" \
           "Portal SaaS - Core/src/PortalSaas.Core/Infraestructura/ModuleDatabaseCredentials.cs" \
           "Portal SaaS - Core/src/PortalSaas.Core/Infraestructura/ExternalDatabaseConnectionService.cs" \
           "Portal SaaS - Core/src/PortalSaas.Core/Administracion" \
           "Portal SaaS - Core/tests/PortalSaas.Core.Tests/Administracion" \
           "Portal SaaS - Core/tests/PortalSaas.Core.Tests/Infraestructura/ExternalDatabaseConnectionServiceTests.cs"
git status --short   # verificar que no entró nada de Importación Genérica
git commit -m "refactor(conexiones): base de modulo = Instance + DatabaseName, 1 a 1, sin catalogo ni purpose"
```

---

### Task 3: Servicio de administración de bases por módulo

**Files:**
- Create: `src/PortalSaas.Core/Administracion/CompanyExternalConnectionService.cs`
- Test: `tests/PortalSaas.Core.Tests/Administracion/CompanyExternalConnectionServiceTests.cs`

**Interfaces:**
- Consumes: `ICompanyExternalConnectionService` y los DTOs (Task 2), `ModuleDatabaseCredentials.Resolve` (Task 2), `ExternalDatabaseConnectionStringBuilder.Build` (Task 1), `PluginManager.ModulosCargados`.
- Produces: `public sealed class CompanyExternalConnectionService : ICompanyExternalConnectionService`.
  - Constructor público `(PortalSaasDbContext db, ISecretoCifradoService secretos, PluginManager plugins)`: lo usa DI, que ya lo tiene registrado en `Program.cs:169`.
  - Constructor `internal` `(PortalSaasDbContext db, ISecretoCifradoService secretos, Func<IReadOnlyList<IModuloPortal>> modulos)`: seam de tests. `PluginManager` no permite inyectar módulos en memoria, y `InternalsVisibleTo("PortalSaas.Core.Tests")` ya existe.

- [ ] **Step 1: Escribir los tests que fallan**

```csharp
// tests/PortalSaas.Core.Tests/Administracion/CompanyExternalConnectionServiceTests.cs
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Administracion;
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

    private sealed class ModuloFalso(string code, bool usaBase) : IModuloPortal
    {
        public string ModuleCode => code;
        public string Name => "Módulo " + code;
        public string Version => "1.0.0";
        public bool RequiresExternalDatabase => usaBase;
        public IEnumerable<MenuItemDefinition> GetMenu() => [];
        public void RegisterServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services) { }
    }

    private static readonly IReadOnlyList<IModuloPortal> Modulos =
    [
        new ModuloFalso("Rendiciones", usaBase: true),
        new ModuloFalso("Compras", usaBase: false),
    ];

    private static PortalSaasDbContext NuevoContexto() => new(
        new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CompanyExternalConnectionService Crear(PortalSaasDbContext db) =>
        new(db, new SecretosIdentidad(), () => Modulos);

    private sealed record Escenario(Guid OrgId, Guid CompanyId, long InstanceId, long InstanciaOtraOrgId, long InstanciaInactivaId);

    private static async Task<Escenario> SembrarAsync(PortalSaasDbContext db)
    {
        var orgId = Guid.NewGuid();
        var inst = new Instance
        {
            OrganizationId = orgId, Name = "pg", Host = "pg.local", Port = 5432,
            EngineType = InstanceEngineType.Postgres, TechnicalUsername = "app", TechnicalSecretKey = "enc:inst",
        };
        var inactiva = new Instance
        {
            OrganizationId = orgId, Name = "pg-off", Host = "pg2.local", Port = 5432,
            EngineType = InstanceEngineType.Postgres, TechnicalUsername = "app", TechnicalSecretKey = "enc:inst", IsActive = false,
        };
        var otraOrg = new Instance
        {
            OrganizationId = Guid.NewGuid(), Name = "ajena", Host = "x", Port = 5432,
            EngineType = InstanceEngineType.Postgres, TechnicalUsername = "x", TechnicalSecretKey = "enc:x",
        };
        db.Instances.AddRange(inst, inactiva, otraOrg);
        await db.SaveChangesAsync();
        var company = new Company
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, InstanceId = inst.Id, Code = "DEPOR", Name = "Depor",
            DatabaseName = "SBO", ServiceLayerUrl = "https://sl", IntegrationUsername = "svc", IntegrationSecretKey = "enc", Country = "CL",
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return new Escenario(orgId, company.Id, inst.Id, otraOrg.Id, inactiva.Id);
    }

    private static ModuleDatabaseBindingInput Input(long instanceId, string db = "ps_rendiciones") =>
        new() { InstanceId = instanceId, DatabaseName = db };

    [Fact]
    public async Task ListBindings_SoloModulosQueUsanBaseExterna_SinAsignar()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        var filas = await Crear(db).ListBindingsAsync(e.OrgId, e.CompanyId);

        var fila = Assert.Single(filas);
        Assert.Equal("Rendiciones", fila.ModuleCode);
        Assert.Null(fila.InstanceId);
        Assert.False(fila.HasOwnCredentials);
    }

    [Fact]
    public async Task ListInstances_SoloActivasDeLaOrganizacion()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        var instancias = await Crear(db).ListInstancesAsync(e.OrgId, e.CompanyId);

        var unica = Assert.Single(instancias);
        Assert.Equal(e.InstanceId, unica.Id);
        Assert.Equal(InstanceEngineType.Postgres, unica.EngineType);
    }

    [Fact]
    public async Task SetBinding_SinCredencialPropia_HeredaLaDeLaInstancia()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);
        var svc = Crear(db);

        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", Input(e.InstanceId));

        var fila = await db.CompanyModuleConnections.AsNoTracking().SingleAsync();
        Assert.Equal(e.InstanceId, fila.InstanceId);
        Assert.Equal("ps_rendiciones", fila.DatabaseName);
        Assert.Null(fila.TechnicalUsername);
        Assert.Null(fila.TechnicalSecretKey);
        var dto = await svc.GetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones");
        Assert.Equal("pg", dto!.InstanceName);
        Assert.False(dto.HasOwnCredentials);
    }

    [Fact]
    public async Task SetBinding_ConCredencialPropia_LaCifraYNoLaExpone()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);
        var svc = Crear(db);
        var input = Input(e.InstanceId);
        input.UseOwnCredentials = true;
        input.TechnicalUsername = "rend_user";
        input.TechnicalSecretKey = "clave-secreta";

        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", input);

        var fila = await db.CompanyModuleConnections.AsNoTracking().SingleAsync();
        Assert.Equal("rend_user", fila.TechnicalUsername);
        Assert.Equal("enc:clave-secreta", fila.TechnicalSecretKey);
        var dto = await svc.GetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones");
        Assert.True(dto!.HasOwnCredentials);
        Assert.Equal("rend_user", dto.OwnTechnicalUsername);
        Assert.DoesNotContain("clave-secreta", System.Text.Json.JsonSerializer.Serialize(dto));
    }

    [Fact]
    public async Task SetBinding_CredencialPropiaConClaveEnBlanco_ConservaLaAnterior()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);
        var svc = Crear(db);
        var alta = Input(e.InstanceId);
        alta.UseOwnCredentials = true;
        alta.TechnicalUsername = "rend_user";
        alta.TechnicalSecretKey = "clave-1";
        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", alta);

        var edicion = Input(e.InstanceId, "ps_rendiciones_v2");
        edicion.UseOwnCredentials = true;
        edicion.TechnicalUsername = "rend_user";
        edicion.TechnicalSecretKey = "";
        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", edicion);

        var fila = await db.CompanyModuleConnections.AsNoTracking().SingleAsync();
        Assert.Equal("ps_rendiciones_v2", fila.DatabaseName);
        Assert.Equal("enc:clave-1", fila.TechnicalSecretKey);
    }

    [Fact]
    public async Task SetBinding_CredencialPropiaNuevaSinClave_Lanza()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);
        var input = Input(e.InstanceId);
        input.UseOwnCredentials = true;
        input.TechnicalUsername = "rend_user";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", input));

        Assert.Contains("clave", ex.Message);
        Assert.Empty(db.CompanyModuleConnections);
    }

    [Fact]
    public async Task SetBinding_VolverAHeredar_BorraLaCredencialPropia()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);
        var svc = Crear(db);
        var alta = Input(e.InstanceId);
        alta.UseOwnCredentials = true;
        alta.TechnicalUsername = "rend_user";
        alta.TechnicalSecretKey = "clave-1";
        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", alta);

        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", Input(e.InstanceId));

        var fila = await db.CompanyModuleConnections.AsNoTracking().SingleAsync();
        Assert.Null(fila.TechnicalUsername);
        Assert.Null(fila.TechnicalSecretKey);
    }

    [Fact]
    public async Task SetBinding_EsUpsert_UnaSolaFilaPorModulo()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);
        var svc = Crear(db);

        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", Input(e.InstanceId, "a"));
        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", Input(e.InstanceId, "b"));

        var fila = await db.CompanyModuleConnections.AsNoTracking().SingleAsync();
        Assert.Equal("b", fila.DatabaseName);
    }

    [Fact]
    public async Task SetBinding_InstanciaDeOtraOrganizacion_Lanza()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", Input(e.InstanciaOtraOrgId)));
    }

    [Fact]
    public async Task SetBinding_InstanciaInactiva_Lanza()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", Input(e.InstanciaInactivaId)));
    }

    [Fact]
    public async Task SetBinding_BaseEnBlanco_Lanza()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", Input(e.InstanceId, "   ")));
    }

    [Fact]
    public async Task SetBinding_ModuloQueNoUsaBaseExterna_Lanza()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).SetBindingAsync(e.OrgId, e.CompanyId, "Compras", Input(e.InstanceId)));
        Assert.Contains("Compras", ex.Message);
    }

    [Fact]
    public async Task GetBinding_ModuloQueNoUsaBaseExterna_DevuelveNull()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        Assert.Null(await Crear(db).GetBindingAsync(e.OrgId, e.CompanyId, "Compras"));
    }

    [Fact]
    public async Task ClearBinding_BorraLaFila()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);
        var svc = Crear(db);
        await svc.SetBindingAsync(e.OrgId, e.CompanyId, "Rendiciones", Input(e.InstanceId));

        await svc.ClearBindingAsync(e.OrgId, e.CompanyId, "Rendiciones");

        Assert.Empty(db.CompanyModuleConnections);
    }

    [Fact]
    public async Task CompaniaDeOtraOrganizacion_Lanza()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).ListBindingsAsync(Guid.NewGuid(), e.CompanyId));
    }

    [Fact]
    public async Task TestBinding_SinAsignacion_Lanza()
    {
        await using var db = NuevoContexto();
        var e = await SembrarAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Crear(db).TestBindingAsync(e.OrgId, e.CompanyId, "Rendiciones"));
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter "FullyQualifiedName~CompanyExternalConnectionServiceTests"`
Expected: FAIL de compilación (`CompanyExternalConnectionService` no existe).

- [ ] **Step 3: Implementar el servicio**

```csharp
// src/PortalSaas.Core/Administracion/CompanyExternalConnectionService.cs
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Administracion;

/// <summary>
/// Implementación real de ICompanyExternalConnectionService: asigna a cada módulo con
/// RequiresExternalDatabase una Instance de la organización + el nombre de la base, con
/// credencial propia opcional. Alcance por compañía: todo método valida que companyId
/// pertenezca a organizationId (las filas no llevan OrganizationId, ver CLAUDE.md regla
/// dura 2026-08-08). Secreto write-only: se cifra al guardar, ningún DTO lo expone, y en
/// blanco se conserva el existente.
/// </summary>
public sealed class CompanyExternalConnectionService : ICompanyExternalConnectionService
{
    private readonly PortalSaasDbContext _db;
    private readonly ISecretoCifradoService _secretos;
    private readonly Func<IReadOnlyList<IModuloPortal>> _modulos;

    public CompanyExternalConnectionService(PortalSaasDbContext db, ISecretoCifradoService secretos, PluginManager plugins)
        : this(db, secretos, () => plugins.ModulosCargados)
    {
    }

    // Seam de tests: PluginManager no permite inyectar módulos en memoria.
    internal CompanyExternalConnectionService(PortalSaasDbContext db, ISecretoCifradoService secretos, Func<IReadOnlyList<IModuloPortal>> modulos)
    {
        _db = db;
        _secretos = secretos;
        _modulos = modulos;
    }

    public async Task<IReadOnlyList<DatabaseInstanceOptionDto>> ListInstancesAsync(Guid organizationId, Guid companyId, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        return await _db.Instances.AsNoTracking()
            .Where(i => i.OrganizationId == organizationId && i.IsActive)
            .OrderBy(i => i.Name)
            .Select(i => new DatabaseInstanceOptionDto(i.Id, i.Name, i.EngineType, i.Host, i.Port))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ModuleConnectionBindingDto>> ListBindingsAsync(Guid organizationId, Guid companyId, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        var bindings = await _db.CompanyModuleConnections.AsNoTracking()
            .Include(b => b.Instance)
            .Where(b => b.CompanyId == companyId)
            .ToListAsync(ct);

        return _modulos()
            .Where(m => m.RequiresExternalDatabase)
            .OrderBy(m => m.Name)
            .Select(m => Map(m, bindings.FirstOrDefault(b => b.ModuleCode == m.ModuleCode)))
            .ToList();
    }

    public async Task<ModuleConnectionBindingDto?> GetBindingAsync(Guid organizationId, Guid companyId, string moduleCode, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        var modulo = BuscarModuloConBase(moduleCode);
        if (modulo is null)
        {
            return null;
        }

        var binding = await _db.CompanyModuleConnections.AsNoTracking()
            .Include(b => b.Instance)
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.ModuleCode == moduleCode, ct);

        return Map(modulo, binding);
    }

    public async Task SetBindingAsync(Guid organizationId, Guid companyId, string moduleCode, ModuleDatabaseBindingInput input, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        if (BuscarModuloConBase(moduleCode) is null)
        {
            throw new InvalidOperationException($"El módulo '{moduleCode}' no usa una base de datos externa.");
        }

        var databaseName = input.DatabaseName?.Trim();
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("La base de datos es obligatoria.");
        }

        var instanciaValida = await _db.Instances.AsNoTracking()
            .AnyAsync(i => i.Id == input.InstanceId && i.OrganizationId == organizationId && i.IsActive, ct);
        if (!instanciaValida)
        {
            throw new InvalidOperationException("La instancia indicada no existe en la organización o está inactiva.");
        }

        var binding = await _db.CompanyModuleConnections
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.ModuleCode == moduleCode, ct);

        string? usuario = null;
        string? claveCifrada = null;
        if (input.UseOwnCredentials)
        {
            usuario = input.TechnicalUsername?.Trim();
            if (string.IsNullOrWhiteSpace(usuario))
            {
                throw new InvalidOperationException("Ingresa el usuario propio del módulo.");
            }

            if (!string.IsNullOrWhiteSpace(input.TechnicalSecretKey))
            {
                claveCifrada = _secretos.Encrypt(input.TechnicalSecretKey);
            }
            else if (binding?.TechnicalSecretKey is { } claveExistente)
            {
                claveCifrada = claveExistente;
            }
            else
            {
                throw new InvalidOperationException("Ingresa la clave propia del módulo.");
            }
        }

        if (binding is null)
        {
            binding = new CompanyModuleConnection { CompanyId = companyId, ModuleCode = moduleCode };
            _db.CompanyModuleConnections.Add(binding);
        }

        binding.InstanceId = input.InstanceId;
        binding.DatabaseName = databaseName;
        binding.TechnicalUsername = usuario;
        binding.TechnicalSecretKey = claveCifrada;
        binding.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task ClearBindingAsync(Guid organizationId, Guid companyId, string moduleCode, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        var binding = await _db.CompanyModuleConnections
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.ModuleCode == moduleCode, ct);
        if (binding is null)
        {
            return;
        }

        _db.CompanyModuleConnections.Remove(binding);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ConnectionTestResultDto> TestBindingAsync(Guid organizationId, Guid companyId, string moduleCode, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        var binding = await _db.CompanyModuleConnections.AsNoTracking()
            .Include(b => b.Instance)
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.ModuleCode == moduleCode, ct)
            ?? throw new InvalidOperationException("El módulo no tiene una base de datos asignada en esta compañía.");

        var (usuario, password) = ModuleDatabaseCredentials.Resolve(binding, _secretos);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var cs = ExternalDatabaseConnectionStringBuilder.Build(
                binding.Instance.EngineType, binding.Instance.Host, binding.Instance.Port,
                binding.DatabaseName, usuario, password, timeoutSeconds: 5);

            switch (binding.Instance.EngineType)
            {
                case InstanceEngineType.Postgres:
                {
                    await using var conn = new Npgsql.NpgsqlConnection(cs);
                    await conn.OpenAsync(cts.Token);
                    break;
                }
                case InstanceEngineType.SqlServer:
                {
                    await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cs);
                    await conn.OpenAsync(cts.Token);
                    break;
                }
                case InstanceEngineType.Hana:
                {
                    await using var conn = new global::Sap.Data.Hana.HanaConnection(cs);
                    await conn.OpenAsync(cts.Token);
                    break;
                }
                default:
                    return new ConnectionTestResultDto(false, $"Motor no soportado: {binding.Instance.EngineType}", sw.ElapsedMilliseconds);
            }

            return new ConnectionTestResultDto(true, null, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // Error admin-only. Los builders no vuelcan credenciales en ex.Message, pero
            // se sanea por si acaso.
            var mensaje = string.IsNullOrEmpty(password) ? ex.Message : ex.Message.Replace(password, "***");
            return new ConnectionTestResultDto(false, mensaje, sw.ElapsedMilliseconds);
        }
    }

    private IModuloPortal? BuscarModuloConBase(string moduleCode) =>
        _modulos().FirstOrDefault(m => m.ModuleCode == moduleCode && m.RequiresExternalDatabase);

    private async Task EnsureCompanyAsync(Guid organizationId, Guid companyId, CancellationToken ct)
    {
        var ok = await _db.Companies.AsNoTracking()
            .AnyAsync(c => c.Id == companyId && c.OrganizationId == organizationId, ct);
        if (!ok)
        {
            throw new InvalidOperationException("Compañía no encontrada en la organización.");
        }
    }

    private static ModuleConnectionBindingDto Map(IModuloPortal modulo, CompanyModuleConnection? b) => new(
        modulo.ModuleCode,
        modulo.Name,
        b?.InstanceId,
        b?.Instance.Name,
        b?.DatabaseName,
        b?.TechnicalUsername,
        b?.TechnicalSecretKey is not null);
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter "FullyQualifiedName~CompanyExternalConnectionServiceTests"`
Expected: PASS (16 tests).

Run: `dotnet test tests/PortalSaas.Core.Tests`
Expected: verde.

- [ ] **Step 5: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Core/Administracion/CompanyExternalConnectionService.cs" \
        "Portal SaaS - Core/tests/PortalSaas.Core.Tests/Administracion/CompanyExternalConnectionServiceTests.cs"
git commit -m "feat(conexiones): servicio de base por modulo (instancia + base + credencial propia opcional)"
```

---

### Task 4: Migración `InstanceAsModuleDatabaseServer` (estructura + datos)

Una sola migración por proveedor. Hace cuatro cosas: (1) habilita `postgres` en `instances`; (2) crea una instancia por servidor distinto del catálogo viejo, reutilizando una existente si coincide; (3) pasa cada binding a `instance_id` + `database_name`; (4) borra `purpose`, `connection_id` y las tablas `company_external_connections` y `module_external_connections`. EF genera estos pasos en un orden que **borra la tabla vieja antes de leerla**, así que el `Up()` se reescribe a mano en el orden de abajo.

**Files:**
- Create (generado y luego editado): `src/PortalSaas.Data.Migrations.PostgreSql/Migrations/<timestamp>_InstanceAsModuleDatabaseServer.cs` (+ `.Designer.cs`; el snapshot se actualiza solo)
- Create (generado y luego editado): `src/PortalSaas.Data.Migrations.SqlServer/Migrations/<timestamp>_InstanceAsModuleDatabaseServer.cs` (+ `.Designer.cs`)
- Create: `scripts/verificacion-instance-as-module-db.sql`

**Interfaces:**
- Consumes: el modelo EF de la Task 2.
- Produces: el esquema final. `company_module_connections(id, company_id, module_code, instance_id NOT NULL, database_name NOT NULL, technical_username NULL, technical_secret_key NULL, updated_at)`, índice único `uq_company_module_connections_company_module`, FK `Restrict` a `instances`.

- [ ] **Step 1: Generar las dos migraciones**

```bash
dotnet tool run dotnet-ef migrations add InstanceAsModuleDatabaseServer \
  --project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj \
  --startup-project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj \
  --context PortalSaasDbContext

dotnet tool run dotnet-ef migrations add InstanceAsModuleDatabaseServer \
  --project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj \
  --startup-project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj \
  --context PortalSaasDbContext
```

Expected: dos archivos nuevos. Abrir cada uno y confirmar que el `Up()` generado contiene, en cualquier orden: `DropForeignKey` de `connection_id`, `DropTable` de `company_external_connections` y `module_external_connections`, `DropIndex` de `ix_company_module_connections_connection_id` y `uq_company_module_connections_company_module_purpose`, `DropCheckConstraint`/`AddCheckConstraint` de `ck_instances_engine_type`, `DropColumn` de `connection_id` y `purpose`, `AddColumn` de las 4 columnas nuevas, `CreateIndex` de los 2 índices nuevos y `AddForeignKey` a `instances`. Si **falta** alguno, el modelo de la Task 2 no quedó como se pide: corregirlo y regenerar. **No** editar el `.Designer.cs` ni el snapshot.

- [ ] **Step 2: Reescribir `Up()` y `Down()` de la migración Postgres**

Reemplazar **solo** los cuerpos de `Up` y `Down` (dejar la clase, el namespace y los `using` generados):

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) instances acepta postgres.
            migrationBuilder.DropCheckConstraint(name: "ck_instances_engine_type", table: "instances");
            migrationBuilder.AddCheckConstraint(
                name: "ck_instances_engine_type",
                table: "instances",
                sql: "engine_type in ('hana', 'sqlserver', 'postgres')");

            // 2) Columnas nuevas, nulables mientras se pueblan.
            migrationBuilder.AddColumn<long>(name: "instance_id", table: "company_module_connections", type: "bigint", nullable: true);
            migrationBuilder.AddColumn<string>(name: "database_name", table: "company_module_connections", type: "character varying(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<string>(name: "technical_username", table: "company_module_connections", type: "character varying(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<string>(name: "technical_secret_key", table: "company_module_connections", type: "character varying(500)", maxLength: 500, nullable: true);

            // 3) Una instancia por servidor distinto (org, motor, host, puerto, usuario) de
            //    las conexiones de BD ACTIVAS; si ya hay una instancia igual, se reutiliza.
            migrationBuilder.Sql(@"
INSERT INTO instances (organization_id, name, host, port, engine_type, technical_username, technical_secret_key, is_active)
SELECT g.organization_id,
       LEFT(CONCAT(g.engine_type, ' ', g.host, ':', g.port, ' ', g.technical_username), 50),
       g.host, g.port, g.engine_type, g.technical_username, g.technical_secret_key, TRUE
FROM (
    SELECT s.organization_id, s.engine_type, s.host, s.port, s.technical_username,
           MIN(s.technical_secret_key) AS technical_secret_key
    FROM (
        SELECT c.organization_id,
               CASE x.tipo WHEN 'db_postgres' THEN 'postgres' WHEN 'db_sqlserver' THEN 'sqlserver' ELSE 'hana' END AS engine_type,
               COALESCE(x.host, '') AS host,
               COALESCE(x.port, CASE x.tipo WHEN 'db_postgres' THEN 5432 WHEN 'db_sqlserver' THEN 1433 ELSE 30015 END) AS port,
               COALESCE(x.technical_username, '') AS technical_username,
               COALESCE(x.technical_secret_key, '') AS technical_secret_key
        FROM company_external_connections x
        JOIN companies c ON c.id = x.company_id
        WHERE x.tipo IN ('db_postgres', 'db_sqlserver', 'db_hana') AND x.is_active = TRUE
    ) s
    GROUP BY s.organization_id, s.engine_type, s.host, s.port, s.technical_username
) g
WHERE NOT EXISTS (
    SELECT 1 FROM instances i
    WHERE i.organization_id = g.organization_id AND i.engine_type = g.engine_type
      AND i.host = g.host AND i.port = g.port AND i.technical_username = g.technical_username);
");

            // 4) Cada binding toma instancia + base. Si el cifrado de la clave no es idéntico
            //    al de la instancia, queda como credencial propia; ModuleDatabaseCredentialNormalizer
            //    (al arrancar el Host) borra las que en claro resultan iguales.
            migrationBuilder.Sql(@"
UPDATE company_module_connections b
SET instance_id = i.id,
    database_name = COALESCE(x.database_name, ''),
    technical_username = CASE WHEN COALESCE(x.technical_secret_key, '') = i.technical_secret_key THEN NULL ELSE COALESCE(x.technical_username, '') END,
    technical_secret_key = CASE WHEN COALESCE(x.technical_secret_key, '') = i.technical_secret_key THEN NULL ELSE x.technical_secret_key END
FROM company_external_connections x
JOIN companies c ON c.id = x.company_id
JOIN instances i
  ON i.organization_id = c.organization_id
 AND i.engine_type = CASE x.tipo WHEN 'db_postgres' THEN 'postgres' WHEN 'db_sqlserver' THEN 'sqlserver' ELSE 'hana' END
 AND i.host = COALESCE(x.host, '')
 AND i.port = COALESCE(x.port, CASE x.tipo WHEN 'db_postgres' THEN 5432 WHEN 'db_sqlserver' THEN 1433 ELSE 30015 END)
 AND i.technical_username = COALESCE(x.technical_username, '')
WHERE b.connection_id = x.id
  AND x.tipo IN ('db_postgres', 'db_sqlserver', 'db_hana')
  AND x.is_active = TRUE;
");

            // 5) Descartar lo que hoy tampoco resuelve: bindings a conexiones inactivas o
            //    http_api, y cualquier purpose distinto de Default (ningún plugin lo usa).
            migrationBuilder.Sql("DELETE FROM company_module_connections WHERE instance_id IS NULL OR purpose <> 'Default';");

            // 6) Estructura final.
            migrationBuilder.DropForeignKey(
                name: "fk_company_module_connections_company_external_connections_con",
                table: "company_module_connections");
            migrationBuilder.DropIndex(name: "ix_company_module_connections_connection_id", table: "company_module_connections");
            migrationBuilder.DropIndex(name: "uq_company_module_connections_company_module_purpose", table: "company_module_connections");
            migrationBuilder.DropColumn(name: "connection_id", table: "company_module_connections");
            migrationBuilder.DropColumn(name: "purpose", table: "company_module_connections");

            migrationBuilder.AlterColumn<long>(
                name: "instance_id", table: "company_module_connections", type: "bigint", nullable: false,
                oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
            migrationBuilder.AlterColumn<string>(
                name: "database_name", table: "company_module_connections", type: "character varying(100)", maxLength: 100, nullable: false,
                oldClrType: typeof(string), oldType: "character varying(100)", oldMaxLength: 100, oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_company_module_connections_company_module",
                table: "company_module_connections",
                columns: new[] { "company_id", "module_code" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "ix_company_module_connections_instance_id",
                table: "company_module_connections",
                column: "instance_id");
            migrationBuilder.AddForeignKey(
                name: "fk_company_module_connections_instances_instance_id",
                table: "company_module_connections",
                column: "instance_id",
                principalTable: "instances",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // 7) Catálogo viejo y tabla legada.
            migrationBuilder.DropTable(name: "company_external_connections");
            migrationBuilder.DropTable(name: "module_external_connections");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Transformación de datos con pérdida (conexiones agrupadas en instancias,
            // tablas borradas): no hay reversa automática. Rollback = restaurar el backup
            // tomado antes de aplicar (ver Task 9).
            throw new NotSupportedException(
                "InstanceAsModuleDatabaseServer no es reversible: restaurar el backup previo a la migración.");
        }
```

**Antes de pegar, comparar dos nombres con el `Up()` generado:** el de la FK nueva (`fk_company_module_connections_instances_instance_id`) y el tipo de las columnas. Si EF generó otro nombre, por ejemplo por el truncado a 63 caracteres de Postgres, usar el que generó EF, porque el snapshot lo espera así.

- [ ] **Step 3: Reescribir `Up()` y `Down()` de la migración SQL Server**

Mismo orden. Cambian cuatro cosas: el tipo de las columnas (`nvarchar`), los booleanos (`1`), la sintaxis del `UPDATE ... FROM` y el nombre completo de la FK vieja.

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "ck_instances_engine_type", table: "instances");
            migrationBuilder.AddCheckConstraint(
                name: "ck_instances_engine_type",
                table: "instances",
                sql: "engine_type in ('hana', 'sqlserver', 'postgres')");

            migrationBuilder.AddColumn<long>(name: "instance_id", table: "company_module_connections", type: "bigint", nullable: true);
            migrationBuilder.AddColumn<string>(name: "database_name", table: "company_module_connections", type: "nvarchar(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<string>(name: "technical_username", table: "company_module_connections", type: "nvarchar(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<string>(name: "technical_secret_key", table: "company_module_connections", type: "nvarchar(500)", maxLength: 500, nullable: true);

            migrationBuilder.Sql(@"
INSERT INTO instances (organization_id, name, host, port, engine_type, technical_username, technical_secret_key, is_active)
SELECT g.organization_id,
       LEFT(CONCAT(g.engine_type, ' ', g.host, ':', g.port, ' ', g.technical_username), 50),
       g.host, g.port, g.engine_type, g.technical_username, g.technical_secret_key, 1
FROM (
    SELECT s.organization_id, s.engine_type, s.host, s.port, s.technical_username,
           MIN(s.technical_secret_key) AS technical_secret_key
    FROM (
        SELECT c.organization_id,
               CASE x.tipo WHEN 'db_postgres' THEN 'postgres' WHEN 'db_sqlserver' THEN 'sqlserver' ELSE 'hana' END AS engine_type,
               COALESCE(x.host, '') AS host,
               COALESCE(x.port, CASE x.tipo WHEN 'db_postgres' THEN 5432 WHEN 'db_sqlserver' THEN 1433 ELSE 30015 END) AS port,
               COALESCE(x.technical_username, '') AS technical_username,
               COALESCE(x.technical_secret_key, '') AS technical_secret_key
        FROM company_external_connections x
        JOIN companies c ON c.id = x.company_id
        WHERE x.tipo IN ('db_postgres', 'db_sqlserver', 'db_hana') AND x.is_active = 1
    ) s
    GROUP BY s.organization_id, s.engine_type, s.host, s.port, s.technical_username
) g
WHERE NOT EXISTS (
    SELECT 1 FROM instances i
    WHERE i.organization_id = g.organization_id AND i.engine_type = g.engine_type
      AND i.host = g.host AND i.port = g.port AND i.technical_username = g.technical_username);
");

            migrationBuilder.Sql(@"
UPDATE b
SET b.instance_id = i.id,
    b.database_name = COALESCE(x.database_name, ''),
    b.technical_username = CASE WHEN COALESCE(x.technical_secret_key, '') = i.technical_secret_key THEN NULL ELSE COALESCE(x.technical_username, '') END,
    b.technical_secret_key = CASE WHEN COALESCE(x.technical_secret_key, '') = i.technical_secret_key THEN NULL ELSE x.technical_secret_key END
FROM company_module_connections b
JOIN company_external_connections x ON x.id = b.connection_id
JOIN companies c ON c.id = x.company_id
JOIN instances i
  ON i.organization_id = c.organization_id
 AND i.engine_type = CASE x.tipo WHEN 'db_postgres' THEN 'postgres' WHEN 'db_sqlserver' THEN 'sqlserver' ELSE 'hana' END
 AND i.host = COALESCE(x.host, '')
 AND i.port = COALESCE(x.port, CASE x.tipo WHEN 'db_postgres' THEN 5432 WHEN 'db_sqlserver' THEN 1433 ELSE 30015 END)
 AND i.technical_username = COALESCE(x.technical_username, '')
WHERE x.tipo IN ('db_postgres', 'db_sqlserver', 'db_hana')
  AND x.is_active = 1;
");

            migrationBuilder.Sql("DELETE FROM company_module_connections WHERE instance_id IS NULL OR purpose <> 'Default';");

            migrationBuilder.DropForeignKey(
                name: "fk_company_module_connections_company_external_connections_connection_id",
                table: "company_module_connections");
            migrationBuilder.DropIndex(name: "ix_company_module_connections_connection_id", table: "company_module_connections");
            migrationBuilder.DropIndex(name: "uq_company_module_connections_company_module_purpose", table: "company_module_connections");
            migrationBuilder.DropColumn(name: "connection_id", table: "company_module_connections");
            migrationBuilder.DropColumn(name: "purpose", table: "company_module_connections");

            migrationBuilder.AlterColumn<long>(
                name: "instance_id", table: "company_module_connections", type: "bigint", nullable: false,
                oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
            migrationBuilder.AlterColumn<string>(
                name: "database_name", table: "company_module_connections", type: "nvarchar(100)", maxLength: 100, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(100)", oldMaxLength: 100, oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_company_module_connections_company_module",
                table: "company_module_connections",
                columns: new[] { "company_id", "module_code" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "ix_company_module_connections_instance_id",
                table: "company_module_connections",
                column: "instance_id");
            migrationBuilder.AddForeignKey(
                name: "fk_company_module_connections_instances_instance_id",
                table: "company_module_connections",
                column: "instance_id",
                principalTable: "instances",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropTable(name: "company_external_connections");
            migrationBuilder.DropTable(name: "module_external_connections");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "InstanceAsModuleDatabaseServer no es reversible: restaurar el backup previo a la migración.");
        }
```

Misma advertencia que en el Step 2: el nombre de la FK nueva y los tipos deben coincidir con los que generó EF.

- [ ] **Step 4: Compilar los proyectos de migraciones**

Run: `dotnet build src/PortalSaas.Data.Migrations.PostgreSql` y `dotnet build src/PortalSaas.Data.Migrations.SqlServer`
Expected: 0 errores.

Run: `dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj --startup-project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj --context PortalSaasDbContext`. Repetir con SqlServer.
Expected: "No changes have been made to the model since the last migration." Si reporta cambios, el `Up()` editado se apartó del snapshot. Comparar nombres de índices y FKs.

- [ ] **Step 5: Script de verificación con datos**

Crear `scripts/verificacion-instance-as-module-db.sql` (Postgres, para la base **local de Docker**, nunca una real). Siembra el escenario de Review Focus 1 y 2 **antes** de aplicar la migración nueva:

```sql
-- Correr con la base local en la migración ANTERIOR (AllowMultipleImportFormatsPerPartner).
-- Requiere al menos una compañía en la base local. Escenario:
--   A y B: mismo servidor y usuario, misma clave en claro pero cifrado distinto (se
--          simula con textos distintos) → 1 instancia; B queda con credencial propia hasta
--          que el normalizador la evalúe.
--   C: conexión inactiva → su binding se descarta.
--   D: http_api → su binding se descarta.
WITH comp AS (SELECT id FROM companies ORDER BY code LIMIT 1)
INSERT INTO company_external_connections (company_id, nombre, tipo, host, port, database_name, technical_username, technical_secret_key, is_active, created_at, updated_at)
SELECT comp.id, v.nombre, v.tipo, v.host, v.port, v.db, v.usr, v.sec, v.act, now(), now()
FROM comp, (VALUES
  ('VERIF-A', 'db_postgres', '10.0.0.9', 5432, 'ps_a', 'app', 'cifrado-1', TRUE),
  ('VERIF-B', 'db_postgres', '10.0.0.9', 5432, 'ps_b', 'app', 'cifrado-2', TRUE),
  ('VERIF-C', 'db_postgres', '10.0.0.8', 5432, 'ps_c', 'app', 'cifrado-3', FALSE),
  ('VERIF-D', 'http_api',    NULL,       NULL, NULL,   NULL,  NULL,        TRUE)
) AS v(nombre, tipo, host, port, db, usr, sec, act);

WITH comp AS (SELECT id FROM companies ORDER BY code LIMIT 1)
INSERT INTO company_module_connections (company_id, module_code, purpose, connection_id, updated_at)
SELECT comp.id, 'VERIF_' || substr(x.nombre, 7), 'Default', x.id, now()
FROM comp JOIN company_external_connections x ON x.company_id = comp.id AND x.nombre LIKE 'VERIF-%';

-- ===== Después de aplicar InstanceAsModuleDatabaseServer, correr esto: =====
-- Esperado: 1 fila, engine postgres, host 10.0.0.9, usuario app.
SELECT name, engine_type, host, port, technical_username FROM instances WHERE host IN ('10.0.0.9', '10.0.0.8');
-- Esperado: 2 filas (VERIF_A, VERIF_B), misma instance_id, bases ps_a / ps_b;
-- VERIF_A sin credencial propia (su clave fue la elegida por MIN), VERIF_B con technical_username='app'.
SELECT module_code, instance_id, database_name, technical_username, technical_secret_key IS NOT NULL AS tiene_clave_propia
FROM company_module_connections WHERE module_code LIKE 'VERIF_%' ORDER BY module_code;
-- Esperado: error "relation does not exist" en las dos.
SELECT 1 FROM company_external_connections;
SELECT 1 FROM module_external_connections;

-- Limpieza.
DELETE FROM company_module_connections WHERE module_code LIKE 'VERIF_%';
DELETE FROM instances WHERE host = '10.0.0.9';
```

- [ ] **Step 6: Verificar la migración contra Postgres local (Docker)**

1. `docker compose up -d` (Postgres local, puerto 5432; ver CLAUDE.md, "Cómo levantar el entorno de desarrollo Postgres").
2. **Confirmar que el connection string de `src/PortalSaas.Data.Migrations.PostgreSql/appsettings.Development.json` apunta a `localhost`.** Si apunta a `172.16.122.171`, detenerse: esa es una base real.
3. Llevar la base local a la migración anterior: `dotnet tool run dotnet-ef database update AllowMultipleImportFormatsPerPartner --project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj --startup-project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj --context PortalSaasDbContext`
4. Correr la parte de siembra del script (hasta el comentario `===== Después ...`).
5. `dotnet tool run dotnet-ef database update --project ... --startup-project ... --context PortalSaasDbContext` (mismos flags, sin nombre de migración).
6. Correr las queries de verificación del script.

Expected: lo que dice cada comentario "Esperado". Si algo no coincide, corregir el SQL del Step 2 y repetir desde el punto 3. `database update` hacia atrás falla por el `Down()` que lanza: para repetir, recrear el contenedor (`docker compose down -v && docker compose up -d`) y aplicar desde cero.

- [ ] **Step 7: Verificar la migración contra SQL Server local**

Si hay un SQL Server local configurado en `src/PortalSaas.Data.Migrations.SqlServer/appsettings.Development.json`: aplicar `database update` y confirmar que corre sin error (la base local de SQL Server no tiene conexiones, así que solo valida la estructura y la sintaxis). Si no hay SQL Server local, dejarlo anotado como **pendiente** en el commit y en el reporte final. No inventar el resultado.

- [ ] **Step 8: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Data.Migrations.PostgreSql/Migrations" \
        "Portal SaaS - Core/src/PortalSaas.Data.Migrations.SqlServer/Migrations" \
        "Portal SaaS - Core/scripts/verificacion-instance-as-module-db.sql"
git commit -m "feat(db): migracion InstanceAsModuleDatabaseServer (agrupa conexiones en instancias)"
```

---

### Task 5: Host — arranque y página de conexiones externas en `/Admin`

**Files:**
- Modify: `src/PortalSaas.Host/Program.cs` (bloque del backfill, líneas ~423-444)
- Modify: `src/PortalSaas.Host/Pages/Admin/Organizations/Companies/ExternalConnections/Index.cshtml` + `.cshtml.cs` (reescritura)
- Modify: `src/PortalSaas.Host/Pages/Admin/Organizations/Companies/ExternalConnections/Edit.cshtml` + `.cshtml.cs` (reescritura: edita la base de **un módulo**)
- Delete: `src/PortalSaas.Host/Pages/Admin/Organizations/Companies/ExternalConnections/Create.cshtml` + `.cshtml.cs`

**Interfaces:**
- Consumes: `ICompanyExternalConnectionService` (Task 2/3), `ModuleDatabaseCredentialNormalizer` (Task 2).
- Produces: las rutas `/Admin/Organizations/Companies/ExternalConnections/{companyId}` (grilla) y `/Admin/Organizations/Companies/ExternalConnections/Edit/{companyId}/{moduleCode}` (edición).

- [ ] **Step 1: `Program.cs` — normalizador en vez del backfill**

Reemplazar el bloque completo que empieza con el comentario `// Backfill one-shot idempotente: copia las filas legadas de module_external_connections` y termina en el `}` del `using (var backfillScope ...)` por:

```csharp
// One-shot idempotente: tras la migración InstanceAsModuleDatabaseServer, borra las
// credenciales propias de módulo que son en realidad la misma que la de su instancia
// (la migración no puede compararlas en SQL: AES-GCM cifra distinto la misma clave).
// No-op en cada arranque siguiente.
using (var normalizerScope = app.Services.CreateScope())
{
    var normalizerLogger = normalizerScope.ServiceProvider.GetRequiredService<ILogger<ModuleDatabaseCredentialNormalizer>>();
    try
    {
        var normalizer = new ModuleDatabaseCredentialNormalizer(
            normalizerScope.ServiceProvider.GetRequiredService<PortalSaasDbContext>(),
            normalizerScope.ServiceProvider.GetRequiredService<ISecretoCifradoService>(),
            normalizerLogger);
        using var normalizerCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var limpiados = await normalizer.RunAsync(normalizerCts.Token);
        if (limpiados > 0)
        {
            normalizerLogger.LogInformation("Normalizador: {Count} credencial(es) propia(s) de módulo eran iguales a la de su instancia y se borraron.", limpiados);
        }
    }
    catch (Exception ex)
    {
        // Nunca abortar el arranque del Host por esto -- se registra y se continúa.
        normalizerLogger.LogError(ex, "El normalizador de credenciales de módulo falló; el Host continúa el arranque.");
    }
}
```

- [ ] **Step 2: Grilla (`Index.cshtml.cs`, reescritura completa)**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Companies.ExternalConnections;

/// <summary>
/// Base de datos externa de cada módulo de la compañía: Instancia + Base (+ credencial
/// propia opcional). Una fila por módulo con RequiresExternalDatabase. Los servidores se
/// administran en Instancias (/Admin/Organizations/Instances).
/// </summary>
[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly ICompanyExternalConnectionService _svc;

    public IndexModel(PortalSaasDbContext db, ICompanyExternalConnectionService svc)
    {
        _db = db;
        _svc = svc;
    }

    public Company Company { get; private set; } = null!;
    public IReadOnlyList<ModuleConnectionBindingDto> Bindings { get; private set; } = [];

    public string? TestedModuleCode { get; private set; }
    public bool? TestSuccess { get; private set; }
    public string? TestError { get; private set; }
    public long? TestElapsedMs { get; private set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid companyId)
    {
        var company = await _db.Companies.FindAsync(companyId);
        if (company is null)
        {
            return NotFound();
        }

        Company = company;
        Bindings = await _svc.ListBindingsAsync(company.OrganizationId, companyId);
        return Page();
    }

    public async Task<IActionResult> OnPostTestAsync(Guid companyId, string moduleCode)
    {
        var getResult = await OnGetAsync(companyId);
        if (getResult is not PageResult)
        {
            return getResult;
        }

        TestedModuleCode = moduleCode;
        try
        {
            var r = await _svc.TestBindingAsync(Company.OrganizationId, companyId, moduleCode);
            TestSuccess = r.Ok;
            TestError = r.Error;
            TestElapsedMs = r.ElapsedMs;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostClearAsync(Guid companyId, string moduleCode)
    {
        var company = await _db.Companies.FindAsync(companyId);
        if (company is null)
        {
            return NotFound();
        }

        try
        {
            await _svc.ClearBindingAsync(company.OrganizationId, companyId, moduleCode);
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(new { companyId });
    }
}
```

- [ ] **Step 3: Grilla (`Index.cshtml`, reescritura completa)**

```cshtml
@page "{companyId:guid}"
@model PortalSaas.Host.Pages.Admin.Organizations.Companies.ExternalConnections.IndexModel
@{
    ViewData["Title"] = $"Conexiones externas · {Model.Company.Code}";
}

<div class="d-flex justify-content-between align-items-center mb-3">
    <div>
        <h1 class="h3 mb-0">Conexiones externas de @Model.Company.Code &mdash; @Model.Company.Name</h1>
        <a asp-page="/Admin/Organizations/Companies/Index" asp-route-organizationId="@Model.Company.OrganizationId">Volver a compañías</a>
    </div>
    <a asp-page="/Admin/Organizations/Instances/Index" asp-route-organizationId="@Model.Company.OrganizationId" class="btn btn-secondary">Administrar instancias</a>
</div>

@if (Model.ErrorMessage is not null)
{
    <div class="alert alert-danger">@Model.ErrorMessage</div>
}

<p class="text-muted">
    Base de datos EXTERNA (ajena al SAP de la compañía) de cada módulo que la necesita:
    se elige la <strong>instancia</strong> (servidor) y el nombre de la <strong>base</strong>.
    Por defecto el módulo usa el usuario y la clave de la instancia; se puede definir una
    credencial propia por módulo.
</p>

@if (Model.Bindings.Count == 0)
{
    <p class="text-muted">Ningún módulo cargado usa una base de datos externa.</p>
}
else
{
    <div class="document-list-table-wrapper">
        <table class="table document-list-table">
            <thead>
                <tr>
                    <th>Módulo</th>
                    <th>Instancia</th>
                    <th>Base de datos</th>
                    <th>Credencial</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
            @foreach (var b in Model.Bindings)
            {
                var asignado = b.InstanceId is not null;
                <tr class="@(asignado ? null : "table-warning")">
                    <td>@b.ModuleName <span class="text-muted">(@b.ModuleCode)</span></td>
                    <td>@(b.InstanceName ?? "— sin asignar —")</td>
                    <td>@b.DatabaseName</td>
                    <td>@(asignado ? (b.HasOwnCredentials ? $"Propia ({b.OwnTechnicalUsername})" : "De la instancia") : "")</td>
                    <td>
                        <a asp-page="./Edit" asp-route-companyId="@Model.Company.Id" asp-route-moduleCode="@b.ModuleCode">@(asignado ? "Editar" : "Asignar")</a>
                        @if (asignado)
                        {
                            <text>·</text>
                            <form method="post" asp-page-handler="Test" asp-route-companyId="@Model.Company.Id" asp-route-moduleCode="@b.ModuleCode" class="d-inline">
                                <button type="submit" class="btn btn-link p-0 align-baseline">Probar</button>
                            </form>
                            <text>·</text>
                            <form method="post" asp-page-handler="Clear" asp-route-companyId="@Model.Company.Id" asp-route-moduleCode="@b.ModuleCode" class="d-inline"
                                  onsubmit="return confirm('¿Quitar la base asignada a @b.ModuleName?');">
                                <button type="submit" class="btn btn-link text-danger p-0 align-baseline">Quitar</button>
                            </form>
                        }
                    </td>
                </tr>
                @if (Model.TestedModuleCode == b.ModuleCode && Model.TestSuccess is not null)
                {
                    <tr>
                        <td colspan="5">
                            <div class="alert @(Model.TestSuccess == true ? "alert-success" : "alert-warning") mb-0">
                                @if (Model.TestSuccess == true)
                                {
                                    <strong class="text-success">OK</strong> <span>&mdash; conexión probada correctamente (@Model.TestElapsedMs ms).</span>
                                }
                                else
                                {
                                    <strong class="text-danger">FALLÓ</strong> <span>&mdash;</span> <code>@Model.TestError</code>
                                }
                            </div>
                        </td>
                    </tr>
                }
            }
            </tbody>
        </table>
    </div>
}
```

- [ ] **Step 4: Edición (`Edit.cshtml.cs`, reescritura completa)**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Companies.ExternalConnections;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class EditModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly ICompanyExternalConnectionService _svc;

    public EditModel(PortalSaasDbContext db, ICompanyExternalConnectionService svc)
    {
        _db = db;
        _svc = svc;
    }

    public Company Company { get; private set; } = null!;
    public Organization Organization { get; private set; } = null!;
    public ModuleConnectionBindingDto Binding { get; private set; } = null!;
    public IReadOnlyList<DatabaseInstanceOptionDto> Instances { get; private set; } = [];

    [BindProperty]
    public ModuleDatabaseBindingInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid companyId, string moduleCode)
    {
        if (!await CargarAsync(companyId, moduleCode))
        {
            return NotFound();
        }

        Input = new ModuleDatabaseBindingInput
        {
            InstanceId = Binding.InstanceId ?? 0,
            DatabaseName = Binding.DatabaseName ?? string.Empty,
            UseOwnCredentials = Binding.HasOwnCredentials,
            TechnicalUsername = Binding.OwnTechnicalUsername,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid companyId, string moduleCode)
    {
        if (!await CargarAsync(companyId, moduleCode))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await _svc.SetBindingAsync(Company.OrganizationId, companyId, moduleCode, Input);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        return RedirectToPage("./Index", new { companyId });
    }

    private async Task<bool> CargarAsync(Guid companyId, string moduleCode)
    {
        var company = await _db.Companies.FindAsync(companyId);
        if (company is null)
        {
            return false;
        }

        var binding = await _svc.GetBindingAsync(company.OrganizationId, companyId, moduleCode);
        if (binding is null)
        {
            return false;
        }

        Company = company;
        Organization = (await _db.Organizations.FindAsync(company.OrganizationId))!;
        Binding = binding;
        Instances = await _svc.ListInstancesAsync(company.OrganizationId, companyId);
        return true;
    }
}
```

- [ ] **Step 5: Edición (`Edit.cshtml`, reescritura completa)**

```cshtml
@page "{companyId:guid}/{moduleCode}"
@model PortalSaas.Host.Pages.Admin.Organizations.Companies.ExternalConnections.EditModel
@{
    ViewData["Title"] = $"Base de {Model.Binding.ModuleName} · {Model.Company.Code}";
}

<partial name="~/Pages/Admin/Organizations/_OrganizationSubnav.cshtml" model="@(new OrganizationSubnavViewModel { OrganizationId = Model.Organization.Id, OrganizationName = Model.Organization.LegalName, Mode = Model.Organization.Mode, ActivePage = "Companies" })" />

<div class="card-surface mb-4">
    <h2 class="h5 mb-3">Base de datos de @Model.Binding.ModuleName &mdash; @Model.Company.Code</h2>

    @if (Model.Instances.Count == 0)
    {
        <div class="alert alert-warning">
            La organización no tiene instancias activas.
            <a asp-page="/Admin/Organizations/Instances/Create" asp-route-organizationId="@Model.Organization.Id">Crear una instancia</a> primero.
        </div>
    }

    <form method="post" asp-antiforgery="true">
        <div asp-validation-summary="All" class="text-danger"></div>

        <div class="row g-3">
            <div class="col-md-6">
                <label asp-for="Input.InstanceId" class="form-label">Instancia</label>
                <select asp-for="Input.InstanceId" class="form-select">
                    <option value="0">— elegir —</option>
                    @foreach (var i in Model.Instances)
                    {
                        <option value="@i.Id">@i.Name (@i.EngineType · @i.Host:@i.Port)</option>
                    }
                </select>
                <span asp-validation-for="Input.InstanceId" class="text-danger"></span>
            </div>

            <div class="col-md-6">
                <label asp-for="Input.DatabaseName" class="form-label">Base de datos</label>
                <input asp-for="Input.DatabaseName" class="form-control" />
                <span asp-validation-for="Input.DatabaseName" class="text-danger"></span>
            </div>

            <div class="col-12">
                <div class="form-check">
                    <input asp-for="Input.UseOwnCredentials" class="form-check-input" id="Input_UseOwnCredentials" />
                    <label asp-for="Input.UseOwnCredentials" class="form-check-label">Usar un usuario y clave propios para este módulo (si no, usa los de la instancia)</label>
                </div>
            </div>

            <div class="col-md-6 own-cred">
                <label asp-for="Input.TechnicalUsername" class="form-label">Usuario</label>
                <input asp-for="Input.TechnicalUsername" class="form-control" autocomplete="off" />
                <span asp-validation-for="Input.TechnicalUsername" class="text-danger"></span>
            </div>

            <div class="col-md-6 own-cred">
                <label asp-for="Input.TechnicalSecretKey" class="form-label">Clave @(Model.Binding.HasOwnCredentials ? "(en blanco = no cambiarla)" : "")</label>
                <input asp-for="Input.TechnicalSecretKey" type="password" class="form-control" autocomplete="new-password" />
            </div>
        </div>

        <div class="d-flex gap-2 mt-4">
            <button type="submit" class="btn-primary">Guardar</button>
            <a asp-page="./Index" asp-route-companyId="@Model.Company.Id" class="btn-secondary">Cancelar</a>
        </div>
    </form>
</div>

@section Scripts {
    <partial name="_ValidationScriptsPartial" />
    <script>
        (function () {
            var chk = document.getElementById('Input_UseOwnCredentials');
            if (!chk) { return; }
            function apply() {
                document.querySelectorAll('.own-cred').forEach(function (el) { el.style.display = chk.checked ? '' : 'none'; });
            }
            chk.addEventListener('change', apply);
            apply();
        })();
    </script>
}
```

Borrar `Create.cshtml` y `Create.cshtml.cs` de esa carpeta.

- [ ] **Step 6: Compilar el Host**

Run: `dotnet build src/PortalSaas.Host`
Expected: errores solo en `Pages/Admin/Organizations/Companies/Index.cshtml.cs` (usa `ModuleExternalConnections`/`CompanyExternalConnections`). Se arregla en la Task 6. Ninguno en `ExternalConnections/` ni en `Program.cs`.

- [ ] **Step 7: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Host/Program.cs" \
        "Portal SaaS - Core/src/PortalSaas.Host/Pages/Admin/Organizations/Companies/ExternalConnections"
git commit -m "feat(admin): conexiones externas = grilla modulo -> instancia + base"
```

---

### Task 6: Host — Instancias y Compañías

**Files:**
- Modify: `src/PortalSaas.Host/Pages/Admin/Organizations/Instances/Index.cshtml.cs` (`OnPostDeleteAsync`, líneas 43-62)
- Modify: `src/PortalSaas.Host/Pages/Admin/Organizations/Instances/Index.cshtml` (texto bajo la tabla)
- Modify: `src/PortalSaas.Host/Pages/Admin/Organizations/Companies/Index.cshtml.cs` (líneas 76-83)
- Modify: `src/PortalSaas.Host/Pages/Admin/Organizations/Companies/Create.cshtml.cs` (líneas 67 y 103-110)
- Modify: `src/PortalSaas.Host/Pages/Admin/Organizations/Companies/Edit.cshtml.cs` (línea 76 y 113-120)

**Interfaces:**
- Consumes: `InstanceEngineType.Sap` / `IsSap` (Task 1), `PortalSaasDbContext.CompanyModuleConnections` (Task 2).

- [ ] **Step 1: Bloquear el borrado de una instancia en uso por un módulo**

En `Instances/Index.cshtml.cs`, reemplazar el comentario y el chequeo al inicio de `OnPostDeleteAsync`:

```csharp
    // Bloquea el borrado si cuelga de esta instancia alguna Company (perdería su conexión
    // SAP) o la base de algún módulo (la FK es Restrict: sin este chequeo, el borrado
    // reventaría con un DbUpdateException).
    public async Task<IActionResult> OnPostDeleteAsync(Guid organizationId, long id)
    {
        if (await _db.Companies.AnyAsync(c => c.InstanceId == id))
        {
            ErrorMessage = "No se puede eliminar: hay compañías creadas sobre esta instancia.";
            return RedirectToPage(new { organizationId });
        }

        if (await _db.CompanyModuleConnections.AnyAsync(b => b.InstanceId == id))
        {
            ErrorMessage = "No se puede eliminar: hay módulos que usan una base de datos de esta instancia (ver Conexiones externas de cada compañía).";
            return RedirectToPage(new { organizationId });
        }
```

(el resto del método no cambia).

- [ ] **Step 2: Texto de la página de Instancias**

En `Instances/Index.cshtml`, buscar el párrafo que empieza con "Compañías (schemas SAP) cuelgan de una instancia" y reemplazarlo, conservando el link existente a compañías:

```cshtml
<p class="text-muted mt-3">
    Una instancia es un servidor de base de datos de la organización. De ella cuelgan las
    compañías SAP (solo motores <code>hana</code> / <code>sqlserver</code>) y las bases externas de
    los módulos (cualquier motor, incluido <code>postgres</code>), que se asignan en
    <em>Conexiones externas</em> de cada compañía. Crea al menos una acá antes de
    <a asp-page="/Admin/Organizations/Companies/Index" asp-route-organizationId="@Model.Organization.Id">agregar compañías</a>.
</p>
```

Si el link original usa otra ruta o parámetros, conservar los originales.

- [ ] **Step 3: Chequeo de dependientes al borrar una compañía**

En `Companies/Index.cshtml.cs`, borrar las dos líneas `ModuleExternalConnections` y `CompanyExternalConnections` del `inUse`, de modo que quede:

```csharp
        var inUse = await _db.UserMenuGroups.AnyAsync(g => g.CompanyId == id)
            || await _db.UserMenuProfiles.AnyAsync(p => p.CompanyId == id)
            || await _db.CompanyModuleConnections.AnyAsync(b => b.CompanyId == id)
            || await _db.ApiClientCredentials.AnyAsync(c => c.CompanyId == id && c.Activo);
```

- [ ] **Step 4: Compañías solo sobre instancias SAP**

En `Companies/Create.cshtml.cs` y `Companies/Edit.cshtml.cs`, en el método que carga `Instances`:

```csharp
        Instances = await _db.Instances
            .Where(i => i.OrganizationId == organizationId && InstanceEngineType.Sap.Contains(i.EngineType))
            .OrderBy(i => i.Name)
            .Select(i => new SelectListItem(i.Name, i.Id.ToString()))
            .ToListAsync();
```

En la validación del POST (`instanciaValida`) de ambos archivos, agregar la misma condición:

```csharp
        var instanciaValida = await _db.Instances.AnyAsync(i => i.Id == Input.InstanceId
            && i.OrganizationId == organizationId
            && InstanceEngineType.Sap.Contains(i.EngineType));
```

En `Edit.cshtml.cs` la variable es `company.OrganizationId`, no `organizationId`: mantener la que ya usa el archivo. Si EF no puede traducir `Sap.Contains` sobre un `IReadOnlyCollection<string>`, usar `(i.EngineType == InstanceEngineType.Hana || i.EngineType == InstanceEngineType.SqlServer)`.

- [ ] **Step 5: Compilar el Host**

Run: `dotnet build src/PortalSaas.Host`
Expected: 0 errores. Si el Host referencia `Modulo.Administracion` en el build, sus errores se arreglan en la Task 7. En ese caso, confirmar que los únicos errores son de `plugins/Modulo.Administracion/Pages/ConexionesExternas/`.

- [ ] **Step 6: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Host/Pages/Admin/Organizations/Instances" \
        "Portal SaaS - Core/src/PortalSaas.Host/Pages/Admin/Organizations/Companies/Index.cshtml.cs" \
        "Portal SaaS - Core/src/PortalSaas.Host/Pages/Admin/Organizations/Companies/Create.cshtml.cs" \
        "Portal SaaS - Core/src/PortalSaas.Host/Pages/Admin/Organizations/Companies/Edit.cshtml.cs"
git commit -m "feat(admin): instancias como servidor de BD (postgres) y companias solo sobre motores SAP"
```

---

### Task 7: Self-service del tenant (`Modulo.Administracion`)

**Files:**
- Modify (reescritura): `plugins/Modulo.Administracion/Pages/ConexionesExternas/Index.cshtml` + `.cshtml.cs`
- Modify (reescritura): `plugins/Modulo.Administracion/Pages/ConexionesExternas/Editar.cshtml` + `.cshtml.cs`

**Interfaces:**
- Consumes: `ICompanyExternalConnectionService`, `ITenantUserAdminService.ListCompaniesAsync()`, `AdminPageModelBase` (`CurrentUser`, `MensajeExito`, `MensajeError`, `ObtenerMensajeError`).
- Produces: las rutas `/organizacion/conexiones-externas` y `/organizacion/conexiones-externas/editar/{moduleCode}?companyId=`.

- [ ] **Step 1: `Index.cshtml.cs` (reescritura completa)**

```csharp
// plugins/Modulo.Administracion/Pages/ConexionesExternas/Index.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Administracion.Pages.ConexionesExternas;

/// <summary>
/// Self-service del admin de un tenant: base de datos externa de cada módulo (instancia +
/// base + credencial propia opcional) en las compañías de la PROPIA organización
/// (ICurrentUserContext.OrganizationId). Las instancias las administra la plataforma; acá
/// solo se eligen. Selector de compañía de solo lectura (nunca recibe organizationId de la UI).
/// </summary>
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
    public IReadOnlyList<ModuleConnectionBindingDto> Bindings { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid? companyId)
    {
        Companies = await _tenant.ListCompaniesAsync();
        if (Companies.Count == 0) return Page();

        CompanyId = companyId ?? Companies[0].Id;
        if (!Companies.Any(c => c.Id == CompanyId)) { MensajeError = "Compañía inválida."; CompanyId = Companies[0].Id; }

        Bindings = await _svc.ListBindingsAsync(CurrentUser.OrganizationId, CompanyId.Value);
        return Page();
    }

    private async Task<bool> EsCompaniaValidaAsync(Guid companyId)
        => (await _tenant.ListCompaniesAsync()).Any(c => c.Id == companyId);

    public async Task<IActionResult> OnPostTestAsync(Guid companyId, string moduleCode)
    {
        if (!await EsCompaniaValidaAsync(companyId)) return Forbid();
        try
        {
            var r = await _svc.TestBindingAsync(CurrentUser.OrganizationId, companyId, moduleCode);
            MensajeExito = r.Ok ? $"Conexión OK ({r.ElapsedMs} ms)." : null;
            MensajeError = r.Ok ? null : $"Falló la conexión: {r.Error}";
        }
        catch (Exception ex) { MensajeError = ObtenerMensajeError(ex); }
        return RedirectToPage(new { companyId });
    }

    public async Task<IActionResult> OnPostClearAsync(Guid companyId, string moduleCode)
    {
        if (!await EsCompaniaValidaAsync(companyId)) return Forbid();
        try { await _svc.ClearBindingAsync(CurrentUser.OrganizationId, companyId, moduleCode); MensajeExito = "Base quitada."; }
        catch (Exception ex) { MensajeError = ObtenerMensajeError(ex); }
        return RedirectToPage(new { companyId });
    }
}
```

- [ ] **Step 2: `Index.cshtml` (reescritura completa)**

```cshtml
@page "/organizacion/conexiones-externas"
@using PortalSaas.Abstractions.Modelos
@model Modulo.Administracion.Pages.ConexionesExternas.IndexModel
@{
    ViewData["Title"] = "Conexiones externas";
    string? CompanyIdStr = Model.CompanyId?.ToString();
}
@* Patrón ListadoOrganizacion sobre el Design System unificado (.card-surface +
   .table-ps + .badge-status). El selector de compañía es de SOLO LECTURA: hace GET con
   ?companyId= para recargar la grilla de esa compañía. *@

<div class="card-surface">
    <h2 class="mb-1">Conexiones externas</h2>
    <p class="text-muted mb-3">
        Base de datos externa que usa cada módulo de tu organización: el servidor
        (<strong>instancia</strong>) y el nombre de la <strong>base</strong>. Se configura compañía por
        compañía. Si necesitas un servidor nuevo, pídeselo al administrador de la plataforma.
    </p>

@if (Model.MensajeExito is not null)
{
    <div class="alert alert-success">@Model.MensajeExito</div>
}
@if (Model.MensajeError is not null)
{
    <div class="alert alert-danger">@Model.MensajeError</div>
}

@if (Model.Companies.Count == 0)
{
    <p class="text-muted">Tu organización todavía no tiene ninguna compañía configurada.</p>
}
else
{
    <form method="get" class="filter-card mb-3">
        <div class="col-auto">
            <label class="form-label">Compañía</label>
            <select name="companyId" class="form-select" onchange="this.form.submit()">
                @foreach (var company in Model.Companies)
                {
                    <option value="@company.Id" selected="@(company.Id == Model.CompanyId)">@company.Code — @company.Name</option>
                }
            </select>
        </div>
    </form>

    <table class="table-ps">
        <thead>
            <tr>
                <th>Módulo</th>
                <th>Instancia</th>
                <th>Base de datos</th>
                <th>Credencial</th>
                <th class="col-actions"></th>
            </tr>
        </thead>
        <tbody>
            @foreach (var b in Model.Bindings)
            {
                var asignado = b.InstanceId is not null;
                <tr>
                    <td>@b.ModuleName</td>
                    <td>
                        @if (asignado)
                        {
                            @b.InstanceName
                        }
                        else
                        {
                            <span class="badge-status badge-status--warning">Sin asignar</span>
                        }
                    </td>
                    <td>@b.DatabaseName</td>
                    <td>@(asignado ? (b.HasOwnCredentials ? $"Propia ({b.OwnTechnicalUsername})" : "De la instancia") : "")</td>
                    <td class="col-actions">
                        <div class="admin-row-actions">
                            <a class="btn-ghost" title="@(asignado ? "Editar" : "Asignar")" href="~/organizacion/conexiones-externas/editar/@b.ModuleCode?companyId=@CompanyIdStr"><i class="bi bi-pencil"></i></a>
                            @if (asignado)
                            {
                                <form method="post" asp-page-handler="Test" class="d-inline-block">
                                    <input type="hidden" name="companyId" value="@CompanyIdStr" />
                                    <input type="hidden" name="moduleCode" value="@b.ModuleCode" />
                                    <button type="submit" class="btn-ghost" title="Probar"><i class="bi bi-plug"></i></button>
                                </form>
                                <form method="post" asp-page-handler="Clear" class="d-inline-block" onsubmit="return confirm('¿Quitar la base asignada a @b.ModuleName?');">
                                    <input type="hidden" name="companyId" value="@CompanyIdStr" />
                                    <input type="hidden" name="moduleCode" value="@b.ModuleCode" />
                                    <button type="submit" class="btn-ghost btn-ghost--danger" title="Quitar"><i class="bi bi-trash"></i></button>
                                </form>
                            }
                        </div>
                    </td>
                </tr>
            }
        </tbody>
    </table>
    @if (Model.Bindings.Count == 0)
    {
        <p class="text-muted">Ningún módulo de tu organización usa una base de datos externa.</p>
    }
}
</div>
```

- [ ] **Step 3: `Editar.cshtml.cs` (reescritura completa)**

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

    [BindProperty] public ModuleDatabaseBindingInput Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public Guid CompanyId { get; set; }
    public ModuleConnectionBindingDto Binding { get; private set; } = null!;
    public IReadOnlyList<DatabaseInstanceOptionDto> Instances { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string moduleCode)
    {
        if (!await EsCompaniaValidaAsync()) return RedirectToPage("Index");
        if (!await CargarAsync(moduleCode)) return NotFound();

        Input = new ModuleDatabaseBindingInput
        {
            InstanceId = Binding.InstanceId ?? 0,
            DatabaseName = Binding.DatabaseName ?? string.Empty,
            UseOwnCredentials = Binding.HasOwnCredentials,
            TechnicalUsername = Binding.OwnTechnicalUsername,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string moduleCode)
    {
        if (!await EsCompaniaValidaAsync()) return Forbid();
        if (!await CargarAsync(moduleCode)) return NotFound();
        if (!ModelState.IsValid) return Page();
        try
        {
            await _svc.SetBindingAsync(CurrentUser.OrganizationId, CompanyId, moduleCode, Input);
            MensajeExito = "Base de datos guardada.";
            return RedirectToPage("Index", new { companyId = CompanyId });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    private async Task<bool> CargarAsync(string moduleCode)
    {
        var binding = await _svc.GetBindingAsync(CurrentUser.OrganizationId, CompanyId, moduleCode);
        if (binding is null) return false;
        Binding = binding;
        Instances = await _svc.ListInstancesAsync(CurrentUser.OrganizationId, CompanyId);
        return true;
    }

    private async Task<bool> EsCompaniaValidaAsync()
        => (await _tenant.ListCompaniesAsync()).Any(c => c.Id == CompanyId);
}
```

- [ ] **Step 4: `Editar.cshtml` (reescritura completa)**

```cshtml
@page "/organizacion/conexiones-externas/editar/{moduleCode}"
@model Modulo.Administracion.Pages.ConexionesExternas.EditarModel
@{
    ViewData["Title"] = $"Base de {Model.Binding.ModuleName}";
}

<a href="~/organizacion/conexiones-externas?companyId=@Model.CompanyId" class="doc-form-volver"><i class="bi bi-arrow-left"></i> Volver a conexiones externas</a>

@if (Model.MensajeError is not null)
{
    <div class="alert alert-danger">@Model.MensajeError</div>
}

<div class="card-surface" style="max-width:680px;">
    <h2 class="h5 mb-3">Base de datos de @Model.Binding.ModuleName</h2>

    @if (Model.Instances.Count == 0)
    {
        <div class="alert alert-warning">Tu organización no tiene instancias activas. Pídele al administrador de la plataforma que cree una.</div>
    }

    <form method="post" asp-antiforgery="true">
        <div asp-validation-summary="All" class="text-danger"></div>
        <input type="hidden" asp-for="CompanyId" />

        <div class="mb-3">
            <label asp-for="Input.InstanceId" class="form-label">Instancia</label>
            <select asp-for="Input.InstanceId" class="form-select">
                <option value="0">— elegir —</option>
                @foreach (var i in Model.Instances)
                {
                    <option value="@i.Id">@i.Name (@i.EngineType · @i.Host:@i.Port)</option>
                }
            </select>
            <span asp-validation-for="Input.InstanceId" class="text-danger"></span>
        </div>

        <div class="mb-3">
            <label asp-for="Input.DatabaseName" class="form-label">Base de datos</label>
            <input asp-for="Input.DatabaseName" class="form-control" />
            <span asp-validation-for="Input.DatabaseName" class="text-danger"></span>
        </div>

        <div class="mb-3">
            <label class="d-flex align-items-center gap-2">
                <input asp-for="Input.UseOwnCredentials" type="checkbox" id="Input_UseOwnCredentials" />
                Usar un usuario y clave propios para este módulo (si no, usa los de la instancia)
            </label>
        </div>

        <div class="mb-3 own-cred">
            <label asp-for="Input.TechnicalUsername" class="form-label">Usuario</label>
            <input asp-for="Input.TechnicalUsername" class="form-control" autocomplete="off" />
        </div>

        <div class="mb-3 own-cred">
            <label asp-for="Input.TechnicalSecretKey" class="form-label">Clave</label>
            <input asp-for="Input.TechnicalSecretKey" class="form-control" type="password" autocomplete="new-password" />
            @if (Model.Binding.HasOwnCredentials)
            {
                <div class="form-text">Dejar en blanco para no cambiarla.</div>
            }
        </div>

        <button type="submit" class="btn-primary">Guardar</button>
    </form>
</div>

@section Scripts {
    <partial name="_ValidationScriptsPartial" />
    <script>
        (function () {
            var chk = document.getElementById('Input_UseOwnCredentials');
            if (!chk) { return; }
            function apply() {
                document.querySelectorAll('.own-cred').forEach(function (el) { el.style.display = chk.checked ? '' : 'none'; });
            }
            chk.addEventListener('change', apply);
            apply();
        })();
    </script>
}
```

- [ ] **Step 5: Compilar la solución**

Run: `dotnet build PortalSaas.sln`
Expected: 0 errores. Las advertencias no deben aumentar respecto de la línea base.

Run: `grep -rn "ExternalConnectionType\|ExternalConnectionDto\|ExternalConnectionEditModel\|ExternalConnectionRequirement\|CompanyExternalConnections\|ModuleExternalConnection\|purpose" --include=*.cs --include=*.cshtml src plugins | grep -v "/obj/\|/bin/\|Migrations/"`
Expected: sin resultados. `purpose` puede aparecer en otros contextos ajenos; revisar cada coincidencia.

- [ ] **Step 6: Commit**

```bash
git add "Portal SaaS - Core/plugins/Modulo.Administracion/Pages/ConexionesExternas"
git commit -m "feat(organizacion): conexiones externas self-service = instancia + base por modulo"
```

---

### Task 8: Plugins que declaran base externa + verificación de punta a punta

**Files:**
- Modify: `../Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/ModuloRendiciones.cs`
- Modify: `../Portal SaaS - Plugins/Modulo.GestionDistribucionGastos/src/Modulo.GestionDistribucionGastos/ModuloGestionDistribucionGastos.cs`
- Modify: `../Portal SaaS - Plugins/Modulo.SellOut/src/Modulo.SellOut/ModuloSellOut.cs`
- Modify: `../Portal SaaS - Plugins/Modulo.AuditoriaInventario/src/Modulo.AuditoriaInventario/AuditoriaInventarioModule.cs`
- Modify: `../Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs`
- Modify: `CLAUDE.md` (sección nueva al final)
- Modify: `docs/09-GUIA-DESARROLLO-PLUGINS.md` (si menciona `ExternalConnectionRequirements` o `purpose`)

**Interfaces:**
- Consumes: `IModuloPortal.RequiresExternalDatabase` (Task 2).

- [ ] **Step 1: Declarar la base externa en los 5 plugins**

En cada uno de los 5 archivos, justo debajo de la línea `public string Name => ...;`, agregar:

```csharp
    /// <summary>Base externa propia (instancia + base) -- ver IModuloPortal.RequiresExternalDatabase.</summary>
    public bool RequiresExternalDatabase => true;
```

- [ ] **Step 2: Confirmar que ningún plugin usa lo eliminado**

Run (desde `C:\PROYECTOS\Proyecto Portal Web-Company`): `grep -rn "ExternalConnectionRequirements\|ExternalConnectionType\|ResolveConnectionAsync([^)]*,[^)]*,[^)]*\"" --include=*.cs "Portal SaaS - Plugins" | grep -v "/obj/\|/bin/\|\.claude/worktrees"`
Expected: sin resultados.

- [ ] **Step 3: Compilar y testear todo**

Run (desde `C:\PROYECTOS\Proyecto Portal Web-Company`): compilar cada plugin, por ejemplo `dotnet build "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Modulo.Wms.csproj"`, y luego los otros 4 `.csproj` de la lista de Files.
Expected: 0 errores en los 5.

Run (desde `Portal SaaS - Core/`): `dotnet build PortalSaas.sln` y `dotnet test tests/PortalSaas.Core.Tests`
Expected: 0 errores y suite verde. Correr también los tests de los plugins que los tengan, por ejemplo `dotnet test "../Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests"`. Expected: verde.

- [ ] **Step 4: Verificación manual en el Host local contra la base Docker migrada (Task 4)**

Levantar el Host contra la base **local** de Docker (no con `build-all.ps1`, que apunta a las bases reales; usar el perfil de desarrollo de `PortalSaas.Host` con el connection string local) y comprobar, una por una:

1. `/Admin/Organizations/{org}/Instances` → "Nueva instancia" ofrece `postgres`. Crear `pg-local` (localhost:5432, usuario y clave de Docker).
2. `/Admin/Organizations/Companies/Create` y `/Edit` → el selector de instancia **no** muestra `pg-local` (Review Focus 5).
3. `/Admin/Organizations/Companies/ExternalConnections/{companyId}` → aparecen **solo** los 5 módulos con base externa (no Compras, Ventas, Inventario, Administración ni Importación Genérica). Asignar `pg-local` + una base existente a Rendiciones → Guardar → "Probar" da OK.
4. Editar Rendiciones con "credencial propia" y clave errónea → "Probar" da FALLÓ con mensaje y sin la clave en el texto. Volver a "usar la de la instancia" → OK.
5. Intentar eliminar `pg-local` en Instancias → mensaje "hay módulos que usan…", sin error 500 (Review Focus 4).
6. `/organizacion/conexiones-externas` como admin del tenant → misma grilla; asignar y quitar funcionan; no hay botón para crear instancias.
7. Log del arranque: sin `ERR` del normalizador.

Anotar el resultado de cada punto. Si alguno falla, corregir antes de seguir. No marcar la tarea como hecha con puntos sin verificar.

- [ ] **Step 5: Documentación**

1. Al final de `CLAUDE.md`, agregar una sección con fecha **2026-10-13** que explique: el modelo (`Instance` = servidor de BD con `postgres`; `CompanyModuleConnection` = `InstanceId + DatabaseName + credencial propia opcional`, 1 a 1); que `company_external_connections`, `module_external_connections`, `Purpose`, `ExternalConnectionRequirements` y `http_api` se eliminaron; la regla "un plugin con base propia **debe** declarar `RequiresExternalDatabase => true` o no aparece en Conexiones externas"; el normalizador del arranque; y el estado del despliegue (pendiente de la Task 9).
2. Corregir las secciones previas de `CLAUDE.md` que describen el catálogo (`AddCompanyExternalConnections`, `LegacyExternalConnectionBackfill`, alrededor de la línea 2550): agregar una nota "**Reemplazado 2026-10-13** — ver sección …" en vez de borrar el historial.
3. `grep -n "ExternalConnectionRequirements\|Purpose\|conexión externa" docs/09-GUIA-DESARROLLO-PLUGINS.md`. Donde explique cómo un plugin declara su base, reemplazar por `RequiresExternalDatabase => true` + `ResolveConnectionAsync(ModuleCode, companyId)`.

- [ ] **Step 6: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/ModuloRendiciones.cs" \
        "Portal SaaS - Plugins/Modulo.GestionDistribucionGastos/src/Modulo.GestionDistribucionGastos/ModuloGestionDistribucionGastos.cs" \
        "Portal SaaS - Plugins/Modulo.SellOut/src/Modulo.SellOut/ModuloSellOut.cs" \
        "Portal SaaS - Plugins/Modulo.AuditoriaInventario/src/Modulo.AuditoriaInventario/AuditoriaInventarioModule.cs" \
        "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs" \
        "Portal SaaS - Core/CLAUDE.md" "Portal SaaS - Core/docs/09-GUIA-DESARROLLO-PLUGINS.md"
git commit -m "feat(plugins): declarar RequiresExternalDatabase en los 5 modulos con base propia + docs"
```

---

### Task 9: Despliegue a las bases reales (manual, con confirmación del dueño)

**No ejecutar ningún paso de esta tarea sin la confirmación explícita del usuario en ese momento.** Toca las 4 bases de plataforma reales de `172.16.122.171` y el IIS del piloto.

**Files:** ninguno. Solo operación.

- [ ] **Step 1: Pre-chequeo en cada una de las 4 bases (solo lectura)**

```sql
-- a) Bindings que la migración DESCARTARÁ (conexión inactiva, http_api o purpose ≠ Default).
--    Si hay filas, mostrárselas al usuario y obtener su OK antes de seguir (Review Focus 1).
SELECT b.company_id, b.module_code, b.purpose, x.nombre, x.tipo, x.is_active
FROM company_module_connections b
JOIN company_external_connections x ON x.id = b.connection_id
WHERE x.is_active = FALSE OR x.tipo NOT IN ('db_postgres', 'db_sqlserver', 'db_hana') OR b.purpose <> 'Default';

-- b) Filas legadas nunca migradas al catálogo (la tabla se borra). Esperado: 0 filas.
SELECT m.company_id, m.module_code FROM module_external_connections m
WHERE NOT EXISTS (SELECT 1 FROM company_module_connections b
                  WHERE b.company_id = m.company_id AND b.module_code = m.module_code);

-- c) Instancias que se crearán (vista previa).
SELECT DISTINCT c.organization_id, x.tipo, x.host, x.port, x.technical_username
FROM company_external_connections x JOIN companies c ON c.id = x.company_id
WHERE x.tipo IN ('db_postgres', 'db_sqlserver', 'db_hana') AND x.is_active = TRUE;

-- d) Largo de las claves cifradas existentes. Esperado: <= 200 (instances.technical_secret_key es varchar(200)).
SELECT max(length(technical_secret_key)) FROM company_external_connections;

-- e) Instancia reutilizada pero inactiva: algún servidor db_* activo coincide con una instancia ya
--    existente con is_active = FALSE (la migración la reutilizaría y el binding quedaría sobre una
--    instancia inactiva). Esperado: 0 filas.
SELECT s.organization_id, s.engine_type, s.host, s.port, s.technical_username, i.id AS instance_id, i.name
FROM (
    SELECT DISTINCT c.organization_id,
           CASE x.tipo WHEN 'db_postgres' THEN 'postgres' WHEN 'db_sqlserver' THEN 'sqlserver' ELSE 'hana' END AS engine_type,
           COALESCE(x.host, '') AS host,
           COALESCE(x.port, CASE x.tipo WHEN 'db_postgres' THEN 5432 WHEN 'db_sqlserver' THEN 1433 ELSE 30015 END) AS port,
           COALESCE(x.technical_username, '') AS technical_username
    FROM company_external_connections x JOIN companies c ON c.id = x.company_id
    WHERE x.tipo IN ('db_postgres', 'db_sqlserver', 'db_hana') AND x.is_active = TRUE
) s
JOIN instances i ON i.organization_id = s.organization_id AND i.engine_type = s.engine_type
                AND i.host = s.host AND i.port = s.port AND i.technical_username = s.technical_username
WHERE i.is_active = FALSE;

-- f) Bindings Default de un módulo que no declara base externa: quedarían invisibles en la grilla.
--    Esperado: 0 filas (si hay, resolver con el usuario antes de migrar).
SELECT b.company_id, b.module_code FROM company_module_connections b
WHERE b.purpose = 'Default'
  AND b.module_code NOT IN ('Rendiciones', 'GestionGastos', 'SellOut', 'AuditoriaInventario', 'Wms');
```

Si b) devuelve filas: **detenerse**. Hay que resolverlas a mano con el usuario antes de migrar.

- [ ] **Step 2: Backup**

`pg_dump` de cada una de las 4 bases antes de aplicar. El `Down()` no es reversible. Confirmar que el archivo existe y no está vacío.

- [ ] **Step 3: Aplicar la migración**

`dotnet ef database update` con los flags de Postgres (ver Global Constraints), apuntando a cada base, una por una. Después de cada una, correr las queries de verificación de `scripts/verificacion-instance-as-module-db.sql` adaptadas: instancias nuevas, y bindings con `instance_id`/`database_name` y sin tablas viejas.

- [ ] **Step 4: Desplegar y reiniciar**

Publicar el Host **y los 5 plugins juntos** (`publish-all.ps1` / runbook `docs/10-RUNBOOK-IIS-PILOTO.md`). Si se despliega el Core sin los plugins recompilados, esos módulos desaparecen de la grilla. Seguirían resolviendo su base, pero no se podrían editar. Reiniciar el Host y revisar el log: línea `Normalizador: N credencial(es)…` y ningún `ERR`.

- [ ] **Step 5: Humo en producción**

Abrir Rendiciones, Sell Out, Gestión de Gastos, Auditoría de Inventario y WMS, y confirmar que cargan datos. En Conexiones externas de DEPOR, "Probar" debe dar OK en los 5.

- [ ] **Step 6: Cierre**

Actualizar la sección de `CLAUDE.md` de la Task 8 con el estado real del despliegue: qué bases se migraron, cuántas instancias se crearon y cuántas credenciales limpió el normalizador. Commit.
