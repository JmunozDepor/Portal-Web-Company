# Motor de Reglas de Validación Pre-Carga (Importación Genérica) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agregar un catálogo de 8 tipos de regla de validación (2 fijas siempre-Block +
6 configurables por Formato de importación con severidad Bloqueante/Alerta), un motor
que las corre sobre las filas ya resueltas de un archivo de importación genérica antes
de confirmar la carga a SAP, y la UI para configurarlas y ver/descargar el resultado.

**Architecture:** Nuevo namespace `PortalSaas.Core.ImportacionGenerica.Reglas` con una
interfaz única `IGenericImportValidationRule` (cada regla resuelve su propio batch-fetch
contra SAP y devuelve mensajes por número de fila) + un `GenericImportValidationRuleEngine`
orquestador. Las asignaciones (qué regla, con qué severidad y parámetros, para qué
Formato) viven en una tabla nueva `generic_import_validation_rule_assignments`
(1:N con `GenericImportConfig`, mismo criterio de personalización por Company). El motor
se integra en `GenericImportService.ProcessFileAsync`, después de que `ProcessRow` ya
resolvió cada fila contra los catálogos SAP existentes.

**Tech Stack:** .NET 8, EF Core (Postgres + SQL Server duales), ClosedXML (reporte
.xlsx), xUnit (tests con fakes escritos a mano -- este proyecto no usa Moq/NSubstitute).

**Desviación deliberada del spec, resuelta en planificación:** el spec listaba 8 tipos
de regla en el catálogo, incluidos `ItemCodeExists`/`SkuCrossReferenceExists`
("estructurales, Block fijo"). Este plan los deja FUERA del enum/motor -- siguen
existiendo exactamente como hoy, hardcodeados dentro de `ProcessRow` (no son
ejecutables vía el motor porque, sin `ItemCode` resuelto, no hay validaciones de
`UnitPrice`/`Warehouse`/etc. que correr sobre esa fila en absoluto). El catálogo
configurable en la UI queda en 6 tipos (los que sí son toggle-ables/parametrizables),
más las 2 estructurales `PositiveQuantity`/`ValidDiscountPercent` que SÍ pasan al motor
nuevo (siempre activas, no aparecen en la UI). Esto no cambia ningún comportamiento
observable del spec -- Errors/bloqueo siguen exactamente igual para paridad/artículo
inexistente -- solo simplifica dónde vive el código. También se colapsó la distinción
spec de interfaces `IGenericImportRowValidationRule`/`IGenericImportBatchValidationRule`
en una sola `IGenericImportValidationRule.ValidateAsync(rows, ...)` -- toda regla recibe
el archivo completo de una vez (incluso las "por fila" necesitan batch-fetch contra SAP
antes de poder validar cada fila, igual que `StockAvailable`), evitando duplicar esa
lógica de resolución en dos formas.

## Global Constraints

- Toda tabla nueva se personaliza por `CompanyId` (vía `GenericImportConfig.CompanyId`),
  nunca `OrganizationId` directo -- regla dura del proyecto (ver `CLAUDE.md`).
- Migraciones se generan y aplican contra **los dos motores** (Postgres y SQL Server) --
  nunca solo uno.
- `src/PortalSaas.Data` nunca importa un paquete de proveedor ni referencia
  `PortalSaas.Abstractions` -- enums de negocio se guardan como `string` en las
  entidades (mismo patrón que `GenericImportConfig.Module`/`LineType`).
- Ningún catálogo nuevo hace `SELECT` sin acotar (batch por lista de códigos, nunca
  listado completo de una tabla SAP grande) -- mismo criterio anti-N+1 ya establecido.
- Comentarios/mensajes de log/texto de UI en español.
- `dotnet build "Portal SaaS - Core/PortalSaas.sln"` en 0 advertencias/0 errores y
  `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests"` en verde después de
  cada tarea.

---

## Mapa de archivos

**Nuevos:**
- `src/PortalSaas.Abstractions/Modelos/GenericImportValidationRuleType.cs`
- `src/PortalSaas.Abstractions/Modelos/GenericImportValidationSeverity.cs`
- `src/PortalSaas.Abstractions/Modelos/GenericImportValidationRuleAssignmentDto.cs`
- `src/PortalSaas.Data/Entities/GenericImportValidationRuleAssignment.cs`
- `src/PortalSaas.Data.Migrations.PostgreSql/Migrations/*_AddGenericImportValidationRules.cs` (generada)
- `src/PortalSaas.Data.Migrations.SqlServer/Migrations/*_AddGenericImportValidationRules.cs` (generada)
- `src/PortalSaas.Abstractions/Contratos/ICustomerShipToAddressService.cs`
- `src/PortalSaas.Core/Catalogos/CustomerShipToAddressService.cs`
- `src/PortalSaas.Core/ImportacionGenerica/Reglas/IGenericImportValidationRule.cs`
- `src/PortalSaas.Core/ImportacionGenerica/Reglas/PositiveQuantityRule.cs`
- `src/PortalSaas.Core/ImportacionGenerica/Reglas/ValidDiscountPercentRule.cs`
- `src/PortalSaas.Core/ImportacionGenerica/Reglas/CustomerActiveInSapRule.cs`
- `src/PortalSaas.Core/ImportacionGenerica/Reglas/ItemActiveInSapRule.cs`
- `src/PortalSaas.Core/ImportacionGenerica/Reglas/PriceVsFixedListRule.cs`
- `src/PortalSaas.Core/ImportacionGenerica/Reglas/PriceVsCustomerListRule.cs`
- `src/PortalSaas.Core/ImportacionGenerica/Reglas/StockAvailableRule.cs`
- `src/PortalSaas.Core/ImportacionGenerica/Reglas/CustomerBranchValidRule.cs`
- `src/PortalSaas.Core/ImportacionGenerica/Reglas/GenericImportValidationRuleEngine.cs`
- `tests/PortalSaas.Core.Tests/ImportacionGenerica/Reglas/GenericImportValidationRuleEngineTests.cs`
- `tests/PortalSaas.Core.Tests/ImportacionGenerica/GenericImportConfigServiceValidationRulesTests.cs`

**Modificados:**
- `src/PortalSaas.Abstractions/Modelos/GenericImportRowDto.cs` -- campo `Warnings`.
- `src/PortalSaas.Abstractions/Modelos/GenericImportConfigDto.cs` -- campo `ValidationRules`.
- `src/PortalSaas.Abstractions/Contratos/IGenericImportConfigService.cs` -- `SaveValidationRulesAsync`.
- `src/PortalSaas.Abstractions/Contratos/IItemStockService.cs` -- `GetAvailableStockAsync`.
- `src/PortalSaas.Abstractions/Contratos/ICustomerCatalogService.cs` -- `GetActiveStatusAsync`.
- `src/PortalSaas.Abstractions/Contratos/ISupplierCatalogService.cs` -- `GetActiveStatusAsync`.
- `src/PortalSaas.Abstractions/Contratos/IItemCatalogService.cs` -- `GetActiveStatusAsync`.
- `src/PortalSaas.Abstractions/Contratos/IGenericImportService.cs` -- `GenerateValidationReportAsync`.
- `src/PortalSaas.Data/Entities/GenericImportConfig.cs` -- navegación `ValidationRules`.
- `src/PortalSaas.Data/PortalSaasDbContext.cs` -- `DbSet` + fluent config.
- `src/PortalSaas.Core/Catalogos/ItemStockService.cs` -- impl batch.
- `src/PortalSaas.Core/Catalogos/CustomerCatalogService.cs` -- impl batch.
- `src/PortalSaas.Core/Catalogos/SupplierCatalogService.cs` -- impl batch.
- `src/PortalSaas.Core/Catalogos/ItemCatalogService.cs` -- impl batch.
- `src/PortalSaas.Core/ImportacionGenerica/GenericImportConfigService.cs` -- `SaveValidationRulesAsync` + `Map` incluye reglas.
- `src/PortalSaas.Core/ImportacionGenerica/GenericImportService.cs` -- integra el motor, `GenerateValidationReportAsync`, saca `BuiltInRules`/loop viejo.
- `src/PortalSaas.Core/ImportacionGenerica/IGenericImportValidationRule.cs` -- **eliminado** (reemplazado por `Reglas/IGenericImportValidationRule.cs`).
- `src/PortalSaas.Host/Program.cs` -- DI de los 8 rules + engine + `ICustomerShipToAddressService`.
- `plugins/Modulo.ImportacionGenerica/Pages/Configuracion/Index.cshtml(.cs)` -- sección "Reglas de validación".
- `plugins/Modulo.ImportacionGenerica/Pages/Importar/Index.cshtml(.cs)` -- columna Advertencias, botón de reporte.

---

### Task 1: DTOs y enums nuevos en Abstractions

**Files:**
- Create: `src/PortalSaas.Abstractions/Modelos/GenericImportValidationRuleType.cs`
- Create: `src/PortalSaas.Abstractions/Modelos/GenericImportValidationSeverity.cs`
- Create: `src/PortalSaas.Abstractions/Modelos/GenericImportValidationRuleAssignmentDto.cs`
- Modify: `src/PortalSaas.Abstractions/Modelos/GenericImportRowDto.cs`
- Modify: `src/PortalSaas.Abstractions/Modelos/GenericImportConfigDto.cs`

**Interfaces:**
- Produces: `GenericImportValidationRuleType` (enum, 8 valores), `GenericImportValidationSeverity`
  (enum, `Block`/`Warning`), `GenericImportValidationRuleAssignmentDto` (record),
  `GenericImportRowDto.Warnings` (`IReadOnlyList<string>`), `GenericImportConfigDto.ValidationRules`
  (`IReadOnlyList<GenericImportValidationRuleAssignmentDto>`).

- [ ] **Step 1: Crear el enum de tipos de regla**

```csharp
// src/PortalSaas.Abstractions/Modelos/GenericImportValidationRuleType.cs
namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Catálogo de tipos de regla de validación pre-carga del módulo de importación
/// genérica -- ver docs/superpowers/specs/2026-09-07-reglas-validacion-importacion-
/// generica-design.md. PositiveQuantity/ValidDiscountPercent son estructurales (siempre
/// activas, severidad fija en Block, nunca aparecen en la UI de configuración por
/// Formato -- reemplazan a IGenericImportValidationRule.BuiltInRules). Los otros 6 se
/// activan/parametrizan por GenericImportConfig (ver GenericImportValidationRuleAssignmentDto).
/// </summary>
public enum GenericImportValidationRuleType
{
    PositiveQuantity,
    ValidDiscountPercent,
    CustomerActiveInSap,
    ItemActiveInSap,
    PriceVsFixedList,
    PriceVsCustomerList,
    StockAvailable,
    CustomerBranchValid,
}
```

- [ ] **Step 2: Crear el enum de severidad**

```csharp
// src/PortalSaas.Abstractions/Modelos/GenericImportValidationSeverity.cs
namespace PortalSaas.Abstractions.Modelos;

/// <summary>Bloqueante = va a Errors (tumba IsValid). Alerta = va a Warnings (nunca bloquea).</summary>
public enum GenericImportValidationSeverity
{
    Block,
    Warning,
}
```

- [ ] **Step 3: Crear el DTO de asignación de regla**

```csharp
// src/PortalSaas.Abstractions/Modelos/GenericImportValidationRuleAssignmentDto.cs
namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Una regla del catálogo activada (o no) para un GenericImportConfig puntual.
/// Parameters usa claves específicas de cada RuleType -- PriceVsFixedList espera
/// "priceListNum" (int) y "tolerancePercent" (decimal); PriceVsCustomerList/
/// StockAvailable/CustomerActiveInSap/ItemActiveInSap/CustomerBranchValid solo
/// "tolerancePercent" (decimal, StockAvailable la aplica al déficit, las demás no la
/// usan -- ver cada Rule.ValidateAsync). Id = 0 para una asignación nueva sin persistir.
/// </summary>
public sealed record GenericImportValidationRuleAssignmentDto(
    int Id,
    GenericImportValidationRuleType RuleType,
    GenericImportValidationSeverity Severity,
    bool IsActive,
    IReadOnlyDictionary<string, object?> Parameters);
```

- [ ] **Step 4: Agregar `Warnings` a `GenericImportRowDto`**

En `src/PortalSaas.Abstractions/Modelos/GenericImportRowDto.cs`, después de la
propiedad `Errors` (línea 71):

```csharp
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Mensajes de reglas configurables en severidad Alerta -- nunca afecta IsValid, a diferencia de Errors. Ver GenericImportValidationRuleEngine.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
```

- [ ] **Step 5: Agregar `ValidationRules` a `GenericImportConfigDto`**

En `src/PortalSaas.Abstractions/Modelos/GenericImportConfigDto.cs`, agregar un
parámetro más al record (al final, con default, para no romper los `new(...)`
posicionales existentes en `GenericImportConfigService.Map`/tests):

```csharp
public sealed record GenericImportConfigDto(
    int Id,
    Guid CompanyId,
    GenericImportModule Module,
    string DocumentType,
    GenericImportLineType LineType,
    string? BusinessPartnerCardCode,
    string? GroupingColumn,
    bool SkuIsCustomerOwn,
    string Alias,
    bool IsActive,
    IReadOnlyList<GenericImportConfigFieldDto> Fields,
    GenericImportPriceSource PriceSource = GenericImportPriceSource.BusinessPartner,
    int? SystemPriceListCode = null,
    bool BusinessPartnerFromFile = false,
    /// <summary>Reglas de validación activas para este Formato (ver GenericImportValidationRuleAssignmentDto). Vacía = ninguna regla configurable activa (solo corren las 2 estructurales fijas).</summary>
    IReadOnlyList<GenericImportValidationRuleAssignmentDto>? ValidationRules = null)
{
    public IReadOnlyList<GenericImportValidationRuleAssignmentDto> ValidationRules { get; init; } = ValidationRules ?? [];
}
```

- [ ] **Step 6: Compilar**

Run: `dotnet build "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\PortalSaas.sln"`
Expected: 0 errores (puede haber warnings de nulabilidad en `Map`/constructores
existentes que usan la forma posicional vieja de `GenericImportConfigDto` -- el nuevo
parámetro tiene default, no debería romper nada).

- [ ] **Step 7: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Abstractions"
git commit -m "$(cat <<'EOF'
feat(importacion-generica): DTOs y enums del motor de reglas de validación

Catálogo de 8 tipos de regla (2 estructurales fijas + 6 configurables por
Formato), severidad Bloqueante/Alerta, y los campos nuevos Warnings/
ValidationRules en los DTOs existentes.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Entidad, DbContext y migraciones

**Files:**
- Create: `src/PortalSaas.Data/Entities/GenericImportValidationRuleAssignment.cs`
- Modify: `src/PortalSaas.Data/Entities/GenericImportConfig.cs`
- Modify: `src/PortalSaas.Data/PortalSaasDbContext.cs`
- Create (generadas): migraciones Postgres + SQL Server

**Interfaces:**
- Consumes: nada nuevo (entidad pura EF Core).
- Produces: `GenericImportValidationRuleAssignment` (entidad), tabla
  `generic_import_validation_rule_assignments`.

- [ ] **Step 1: Crear la entidad**

```csharp
// src/PortalSaas.Data/Entities/GenericImportValidationRuleAssignment.cs
namespace PortalSaas.Data.Entities;

/// <summary>
/// Una regla del catálogo de validación pre-carga activada para un GenericImportConfig
/// puntual (ver docs/superpowers/specs/2026-09-07-reglas-validacion-importacion-
/// generica-design.md). RuleType/Severity como string -- PortalSaas.Data nunca
/// referencia PortalSaas.Abstractions (mismo criterio que GenericImportConfig.Module).
/// </summary>
public sealed class GenericImportValidationRuleAssignment
{
    public int Id { get; set; }

    public int ConfigId { get; set; }
    public GenericImportConfig Config { get; set; } = null!;

    /// <summary>Nombre del enum GenericImportValidationRuleType (ej. "PriceVsFixedList").</summary>
    public string RuleType { get; set; } = null!;

    /// <summary>"Block" | "Warning".</summary>
    public string Severity { get; set; } = "Warning";

    public bool IsActive { get; set; } = true;

    /// <summary>JSON con los parámetros del tipo de regla (ej. {"priceListNum":2,"tolerancePercent":1.5}). Null si el tipo no tiene parámetros propios.</summary>
    public string? ParametersJson { get; set; }
}
```

- [ ] **Step 2: Agregar la navegación en `GenericImportConfig`**

En `src/PortalSaas.Data/Entities/GenericImportConfig.cs`, después de
`public List<GenericImportConfigField> Fields { get; set; } = [];`:

```csharp
    public List<GenericImportValidationRuleAssignment> ValidationRules { get; set; } = [];
```

- [ ] **Step 3: Registrar el `DbSet` y la config fluent en `PortalSaasDbContext`**

En `src/PortalSaas.Data/PortalSaasDbContext.cs`, junto a los `DbSet` de
`GenericImportConfig`/`GenericImportConfigField` (línea 34-35):

```csharp
    public DbSet<GenericImportValidationRuleAssignment> GenericImportValidationRuleAssignments => Set<GenericImportValidationRuleAssignment>();
```

Y en `OnModelCreating`, después del bloque `modelBuilder.Entity<GenericImportConfigField>(...)` (línea 174-182):

```csharp
        modelBuilder.Entity<GenericImportValidationRuleAssignment>(entity =>
        {
            entity.ToTable("generic_import_validation_rule_assignments");
            entity.HasIndex(e => new { e.ConfigId, e.RuleType }).IsUnique();
            entity.Property(e => e.RuleType).HasMaxLength(30);
            entity.Property(e => e.Severity).HasMaxLength(10);
            entity.HasOne(e => e.Config).WithMany(c => c.ValidationRules).HasForeignKey(e => e.ConfigId).OnDelete(DeleteBehavior.Cascade);
        });
```

- [ ] **Step 4: Compilar `PortalSaas.Data`**

Run: `dotnet build "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\src\PortalSaas.Data\PortalSaas.Data.csproj"`
Expected: 0 errores.

- [ ] **Step 5: Generar la migración Postgres**

Run (desde `C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core`):
```
dotnet tool run dotnet-ef migrations add AddGenericImportValidationRules `
  --project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj `
  --startup-project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj `
  --context PortalSaasDbContext
```
Expected: genera `Migrations/*_AddGenericImportValidationRules.cs` sin error.

- [ ] **Step 6: Generar la migración SQL Server**

Run:
```
dotnet tool run dotnet-ef migrations add AddGenericImportValidationRules `
  --project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj `
  --startup-project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj `
  --context PortalSaasDbContext
```
Expected: genera la migración equivalente sin error (sin el error 1785 de rutas de
cascada múltiples -- la única FK nueva es `ConfigId`, un solo camino).

- [ ] **Step 7: Aplicar las dos migraciones contra las bases de desarrollo**

Run (repetir con `--project`/`--startup-project` de SqlServer para la segunda base):
```
dotnet tool run dotnet-ef database update `
  --project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj `
  --startup-project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj `
  --context PortalSaasDbContext
```
Expected: `Done.` sin error, tabla `generic_import_validation_rule_assignments` visible
en la base.

- [ ] **Step 8: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Data" "Portal SaaS - Core/src/PortalSaas.Data.Migrations.PostgreSql" "Portal SaaS - Core/src/PortalSaas.Data.Migrations.SqlServer"
git commit -m "$(cat <<'EOF'
feat(importacion-generica): tabla de asignaciones de reglas de validación

generic_import_validation_rule_assignments -- 1:N con generic_import_configs,
DeleteBehavior.Cascade (la regla no tiene valor sin su Formato). Migración
aplicada contra Postgres y SQL Server de desarrollo.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: `SaveValidationRulesAsync` en `IGenericImportConfigService`

**Files:**
- Modify: `src/PortalSaas.Abstractions/Contratos/IGenericImportConfigService.cs`
- Modify: `src/PortalSaas.Core/ImportacionGenerica/GenericImportConfigService.cs`
- Test: `tests/PortalSaas.Core.Tests/ImportacionGenerica/GenericImportConfigServiceValidationRulesTests.cs`

**Interfaces:**
- Consumes: `GenericImportValidationRuleAssignmentDto`, `GenericImportValidationRuleType`,
  `GenericImportValidationSeverity` (Task 1); `GenericImportValidationRuleAssignment`,
  `PortalSaasDbContext.GenericImportValidationRuleAssignments` (Task 2).
- Produces: `IGenericImportConfigService.SaveValidationRulesAsync(int configId, IReadOnlyList<GenericImportValidationRuleAssignmentDto> rules, CancellationToken ct = default)`.

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/PortalSaas.Core.Tests/ImportacionGenerica/GenericImportConfigServiceValidationRulesTests.cs
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica;
using PortalSaas.Data;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica;

public sealed class GenericImportConfigServiceValidationRulesTests
{
    private static PortalSaasDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new PortalSaasDbContext(options);
    }

    private static async Task<int> SeedConfigAsync(PortalSaasDbContext db, Guid companyId)
    {
        var entity = new Data.Entities.GenericImportConfig
        {
            CompanyId = companyId,
            Module = "Sales",
            DocumentType = "SalesOrder",
            LineType = "Item",
            Alias = "Estándar",
        };
        db.GenericImportConfigs.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    [Fact]
    public async Task SaveValidationRulesAsync_reemplaza_el_set_completo()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SaveValidationRulesAsync_reemplaza_el_set_completo));
        var configId = await SeedConfigAsync(db, companyId);
        var service = new GenericImportConfigService(db, new FakeCurrentCompanyAccessor(companyId));

        await service.SaveValidationRulesAsync(configId,
        [
            new GenericImportValidationRuleAssignmentDto(0, GenericImportValidationRuleType.StockAvailable,
                GenericImportValidationSeverity.Warning, true, new Dictionary<string, object?> { ["tolerancePercent"] = 0m }),
        ]);

        await service.SaveValidationRulesAsync(configId,
        [
            new GenericImportValidationRuleAssignmentDto(0, GenericImportValidationRuleType.CustomerActiveInSap,
                GenericImportValidationSeverity.Block, true, new Dictionary<string, object?>()),
        ]);

        var config = await service.GetAsync(configId);
        Assert.NotNull(config);
        var rule = Assert.Single(config!.ValidationRules);
        Assert.Equal(GenericImportValidationRuleType.CustomerActiveInSap, rule.RuleType);
    }

    [Fact]
    public async Task SaveValidationRulesAsync_rechaza_dos_reglas_de_precio_a_la_vez()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SaveValidationRulesAsync_rechaza_dos_reglas_de_precio_a_la_vez));
        var configId = await SeedConfigAsync(db, companyId);
        var service = new GenericImportConfigService(db, new FakeCurrentCompanyAccessor(companyId));

        var rules = new List<GenericImportValidationRuleAssignmentDto>
        {
            new(0, GenericImportValidationRuleType.PriceVsFixedList, GenericImportValidationSeverity.Warning, true,
                new Dictionary<string, object?> { ["priceListNum"] = 1, ["tolerancePercent"] = 0m }),
            new(0, GenericImportValidationRuleType.PriceVsCustomerList, GenericImportValidationSeverity.Warning, true,
                new Dictionary<string, object?> { ["tolerancePercent"] = 0m }),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveValidationRulesAsync(configId, rules));
    }
}
```

`FakeCurrentCompanyAccessor` -- si ya existe un fake equivalente en el proyecto de
tests, reusarlo (buscar con `grep -rn "class Fake.*CurrentCompany" tests/`); si no
existe, crear uno mínimo en el mismo archivo:

```csharp
internal sealed class FakeCurrentCompanyAccessor : PortalSaas.Abstractions.Contratos.ICurrentCompanyAccessor
{
    public FakeCurrentCompanyAccessor(Guid companyId) => CompanyId = companyId;
    public Guid CompanyId { get; }
    public bool HasCompany => true;
    public string? CompanyCode => "TEST";
    public string? CompanyDatabase => null;
    public string? CompanyServiceLayerUrl => null;
    public string? CompanyCountry => null;
}
```

(Ajustar la lista de miembros exactos de `ICurrentCompanyAccessor` leyendo la interfaz
real antes de compilar -- si difiere de lo de arriba, implementar los miembros que
realmente declare.)

- [ ] **Step 2: Correr el test y confirmar que falla**

Run: `dotnet test "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\tests\PortalSaas.Core.Tests" --filter "FullyQualifiedName~GenericImportConfigServiceValidationRulesTests"`
Expected: FAIL -- `SaveValidationRulesAsync` no existe todavía en `IGenericImportConfigService` (error de compilación).

- [ ] **Step 3: Agregar el método a la interfaz**

En `src/PortalSaas.Abstractions/Contratos/IGenericImportConfigService.cs`, después de
`UpdateAsync` (línea 35-40 según el archivo actual):

```csharp
    /// <summary>
    /// Reemplaza TODO el set de reglas de validación activas para este Formato (borra +
    /// crea, mismo criterio que el detalle de Fields en UpdateAsync). Rechaza con
    /// InvalidOperationException si `rules` trae más de una regla de tipo
    /// PriceVsFixedList/PriceVsCustomerList activa a la vez -- son mutuamente excluyentes.
    /// </summary>
    Task SaveValidationRulesAsync(int configId, IReadOnlyList<GenericImportValidationRuleAssignmentDto> rules, CancellationToken ct = default);
```

- [ ] **Step 4: Implementar en `GenericImportConfigService`**

En `src/PortalSaas.Core/ImportacionGenerica/GenericImportConfigService.cs`, agregar
después de `DeleteAsync`:

```csharp
    private static readonly IReadOnlyList<GenericImportValidationRuleType> PriceRuleTypes =
        [GenericImportValidationRuleType.PriceVsFixedList, GenericImportValidationRuleType.PriceVsCustomerList];

    public async Task SaveValidationRulesAsync(int configId, IReadOnlyList<GenericImportValidationRuleAssignmentDto> rules, CancellationToken ct = default)
    {
        var activePriceRules = rules.Where(r => r.IsActive && PriceRuleTypes.Contains(r.RuleType)).ToList();
        if (activePriceRules.Count > 1)
        {
            throw new InvalidOperationException(
                "Solo se puede activar una regla de precio a la vez (\"Precio vs. lista fija\" o \"Precio vs. lista del cliente\").");
        }

        var entity = await _db.GenericImportConfigs.Include(c => c.ValidationRules)
            .FirstOrDefaultAsync(c => c.Id == configId && c.CompanyId == _currentCompany.CompanyId, ct)
            ?? throw new InvalidOperationException("Configuración no encontrada.");

        _db.GenericImportValidationRuleAssignments.RemoveRange(entity.ValidationRules);
        entity.ValidationRules = rules.Select(MapRuleAssignment).ToList();

        await _db.SaveChangesAsync(ct);
    }

    private static GenericImportValidationRuleAssignment MapRuleAssignment(GenericImportValidationRuleAssignmentDto dto) => new()
    {
        RuleType = dto.RuleType.ToString(),
        Severity = dto.Severity.ToString(),
        IsActive = dto.IsActive,
        ParametersJson = dto.Parameters.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(dto.Parameters) : null,
    };
```

- [ ] **Step 5: Incluir `ValidationRules` en las consultas y en `Map`**

En `ListAsync`/`GetAsync`/`ResolveAsync`, agregar `.Include(c => c.ValidationRules)`
junto a los `.Include(c => c.Fields)` existentes (4 lugares: línea 32, 45, 59, 69 del
archivo actual).

En `Map` (al final del archivo), agregar el argumento nuevo al `new(...)`:

```csharp
    private static GenericImportConfigDto Map(GenericImportConfig row) => new(
        row.Id,
        row.CompanyId,
        Enum.Parse<GenericImportModule>(row.Module),
        row.DocumentType,
        Enum.Parse<GenericImportLineType>(row.LineType),
        row.BusinessPartnerCardCode,
        row.GroupingColumn,
        row.SkuIsCustomerOwn,
        row.Alias,
        row.IsActive,
        row.Fields.Select(f => new GenericImportConfigFieldDto(
            f.Id,
            Enum.Parse<GenericImportLogicalField>(f.LogicalField),
            f.ExcelColumn,
            f.IsRequired,
            f.FixedValue,
            f.UserFieldId)).ToList(),
        Enum.Parse<GenericImportPriceSource>(row.PriceSource),
        row.SystemPriceListCode,
        row.BusinessPartnerFromFile,
        row.ValidationRules.Select(MapRuleAssignmentDto).ToList());

    private static GenericImportValidationRuleAssignmentDto MapRuleAssignmentDto(GenericImportValidationRuleAssignment row) => new(
        row.Id,
        Enum.Parse<GenericImportValidationRuleType>(row.RuleType),
        Enum.Parse<GenericImportValidationSeverity>(row.Severity),
        row.IsActive,
        string.IsNullOrEmpty(row.ParametersJson)
            ? new Dictionary<string, object?>()
            : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(row.ParametersJson)!);
```

- [ ] **Step 6: Correr los tests y confirmar que pasan**

Run: `dotnet test "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\tests\PortalSaas.Core.Tests" --filter "FullyQualifiedName~GenericImportConfigServiceValidationRulesTests"`
Expected: PASS (2/2).

- [ ] **Step 7: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Abstractions" "Portal SaaS - Core/src/PortalSaas.Core/ImportacionGenerica/GenericImportConfigService.cs" "Portal SaaS - Core/tests"
git commit -m "$(cat <<'EOF'
feat(importacion-generica): SaveValidationRulesAsync + reglas incluidas en GenericImportConfigDto

Reemplaza el set completo por Formato (mismo criterio que Fields), rechaza
activar PriceVsFixedList y PriceVsCustomerList a la vez.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Métodos batch nuevos en los catálogos SAP

**Files:**
- Modify: `src/PortalSaas.Abstractions/Contratos/IItemStockService.cs`
- Modify: `src/PortalSaas.Core/Catalogos/ItemStockService.cs`
- Modify: `src/PortalSaas.Abstractions/Contratos/ICustomerCatalogService.cs`
- Modify: `src/PortalSaas.Core/Catalogos/CustomerCatalogService.cs`
- Modify: `src/PortalSaas.Abstractions/Contratos/ISupplierCatalogService.cs`
- Modify: `src/PortalSaas.Core/Catalogos/SupplierCatalogService.cs`
- Modify: `src/PortalSaas.Abstractions/Contratos/IItemCatalogService.cs`
- Modify: `src/PortalSaas.Core/Catalogos/ItemCatalogService.cs`
- Create: `src/PortalSaas.Abstractions/Contratos/ICustomerShipToAddressService.cs`
- Create: `src/PortalSaas.Core/Catalogos/CustomerShipToAddressService.cs`

**Interfaces:**
- Produces:
  - `IItemStockService.GetAvailableStockAsync(IReadOnlyList<(string ItemCode, string WhsCode)> pairs, CancellationToken ct = default) -> Task<IReadOnlyDictionary<(string ItemCode, string WhsCode), decimal>>`
  - `ICustomerCatalogService.GetActiveStatusAsync(IReadOnlyList<string> cardCodes, CancellationToken ct = default) -> Task<IReadOnlyDictionary<string, bool>>`
  - `ISupplierCatalogService.GetActiveStatusAsync(IReadOnlyList<string> cardCodes, CancellationToken ct = default) -> Task<IReadOnlyDictionary<string, bool>>`
  - `IItemCatalogService.GetActiveStatusAsync(IReadOnlyList<string> itemCodes, CancellationToken ct = default) -> Task<IReadOnlyDictionary<string, bool>>`
  - `ICustomerShipToAddressService.GetShipToAddressCodesAsync(string cardCode, CancellationToken ct = default) -> Task<IReadOnlyList<string>>`

Sin tests dedicados -- mismo criterio ya establecido en el proyecto para catálogos SAP
de solo lectura (verificados E2E contra SAP real cuando tienen consumidor, ver
`CLAUDE.md`).

- [ ] **Step 1: `IItemStockService.GetAvailableStockAsync`**

En `src/PortalSaas.Abstractions/Contratos/IItemStockService.cs`:

```csharp
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Stock por almacén (OITW) de un artículo puntual, para el visor Maestro de Producto.</summary>
public interface IItemStockService
{
    /// <summary>Lista vacía si el artículo no tiene registro en ningún almacén.</summary>
    Task<IReadOnlyList<WarehouseStockDto>> GetStockByItemAsync(string itemCode, CancellationToken ct = default);

    /// <summary>
    /// Disponible (OnHand - IsCommited) de varios pares (Artículo, Bodega) en una sola
    /// consulta -- usado por GenericImportService.Reglas.StockAvailableRule para no
    /// consultar una vez por línea al importar un archivo con muchas filas. Un par sin
    /// registro en OITW no aparece en el resultado (disponible implícito 0).
    /// </summary>
    Task<IReadOnlyDictionary<(string ItemCode, string WhsCode), decimal>> GetAvailableStockAsync(
        IReadOnlyList<(string ItemCode, string WhsCode)> pairs, CancellationToken ct = default);
}
```

En `src/PortalSaas.Core/Catalogos/ItemStockService.cs`, agregar el método:

```csharp
    public async Task<IReadOnlyDictionary<(string ItemCode, string WhsCode), decimal>> GetAvailableStockAsync(
        IReadOnlyList<(string ItemCode, string WhsCode)> pairs, CancellationToken ct = default)
    {
        if (pairs.Count == 0)
        {
            return new Dictionary<(string, string), decimal>();
        }

        var parameters = new Dictionary<string, object?>();
        var clauses = new List<string>();
        var i = 0;
        foreach (var (itemCode, whsCode) in pairs)
        {
            var itemParam = $"itemCode{i}";
            var whsParam = $"whsCode{i}";
            clauses.Add($"(T0.\"ItemCode\" = :{itemParam} AND T0.\"WhsCode\" = :{whsParam})");
            parameters[itemParam] = itemCode;
            parameters[whsParam] = whsCode;
            i++;
        }

        var sql = $"""
            SELECT T0."ItemCode" AS "ItemCode", T0."WhsCode" AS "WhsCode",
                   T0."OnHand" AS "OnHand", T0."IsCommited" AS "IsCommited"
            FROM "OITW" T0
            WHERE {string.Join(" OR ", clauses)}
            """;

        var rows = await _hana.QueryAsync<ItemWarehouseStockRow>(sql, parameters, ct);
        return rows.ToDictionary(r => (r.ItemCode, r.WhsCode), r => r.OnHand - r.IsCommited);
    }

    private sealed record ItemWarehouseStockRow
    {
        public string ItemCode { get; init; } = null!;
        public string WhsCode { get; init; } = null!;
        public decimal OnHand { get; init; }
        public decimal IsCommited { get; init; }
    }
```

(`ItemWarehouseStockRow` non-positional, mismo motivo que `WarehouseStockDto` -- mapeo
por reflection de `IHanaService.QueryAsync`.)

- [ ] **Step 2: `ICustomerCatalogService.GetActiveStatusAsync`**

En `IContratos/ICustomerCatalogService.cs`:

```csharp
    /// <summary>
    /// CardCode -> validFor == "Y" en OCRD, para varios códigos en una sola consulta --
    /// usado por Modulo.ImportacionGenerica (regla CustomerActiveInSap). Un CardCode que
    /// no existe simplemente no aparece en el resultado.
    /// </summary>
    Task<IReadOnlyDictionary<string, bool>> GetActiveStatusAsync(IReadOnlyList<string> cardCodes, CancellationToken ct = default);
```

En `CustomerCatalogService.cs`:

```csharp
    public async Task<IReadOnlyDictionary<string, bool>> GetActiveStatusAsync(IReadOnlyList<string> cardCodes, CancellationToken ct = default)
    {
        if (cardCodes.Count == 0)
        {
            return new Dictionary<string, bool>();
        }

        var parameters = new Dictionary<string, object?>();
        var placeholders = new List<string>();
        var i = 0;
        foreach (var cardCode in cardCodes)
        {
            var name = $"cardCode{i++}";
            placeholders.Add($":{name}");
            parameters[name] = cardCode;
        }

        var sql = $"""
            SELECT "CardCode", "validFor" FROM "OCRD"
            WHERE "CardType" = 'C' AND "CardCode" IN ({string.Join(",", placeholders)})
            """;

        var rows = await _hana.QueryAsync<ActiveStatusRow>(sql, parameters, ct);
        return rows.ToDictionary(r => r.CardCode, r => r.ValidFor == "Y");
    }

    private sealed record ActiveStatusRow
    {
        public string CardCode { get; init; } = null!;
        public string ValidFor { get; init; } = null!;
    }
```

- [ ] **Step 3: `ISupplierCatalogService.GetActiveStatusAsync`**

Mismo patrón que el Step 2, con `"CardType" = 'S'` en `SupplierCatalogService.cs`
(leer primero el `SELECT` real de `GetAsync`/`ListAsync` en ese archivo para copiar el
nombre exacto de columnas que ya usa -- debería ser idéntico a `CustomerCatalogService`
salvo el filtro de `CardType`).

- [ ] **Step 4: `IItemCatalogService.GetActiveStatusAsync`**

En `IItemCatalogService.cs`:

```csharp
    /// <summary>ItemCode -> validFor == "Y" en OITM, para varios códigos en una sola consulta -- usado por la regla ItemActiveInSap.</summary>
    Task<IReadOnlyDictionary<string, bool>> GetActiveStatusAsync(IReadOnlyList<string> itemCodes, CancellationToken ct = default);
```

En `ItemCatalogService.cs`, mismo patrón de `GetByCodesAsync` (placeholders + `IN`)
pero contra `OITM` seleccionando `"ItemCode", "validFor"`.

- [ ] **Step 5: `ICustomerShipToAddressService` nuevo**

```csharp
// src/PortalSaas.Abstractions/Contratos/ICustomerShipToAddressService.cs
namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Direcciones de despacho (CRD1, AddressType = 'S') de un cliente -- usado por
/// Modulo.ImportacionGenerica (regla CustomerBranchValid) para validar que la
/// sucursal informada en una fila importada corresponda a ese cliente.
/// </summary>
public interface ICustomerShipToAddressService
{
    /// <summary>Lista vacía si el cliente no tiene direcciones de despacho configuradas.</summary>
    Task<IReadOnlyList<string>> GetShipToAddressCodesAsync(string cardCode, CancellationToken ct = default);
}
```

```csharp
// src/PortalSaas.Core/Catalogos/CustomerShipToAddressService.cs
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Direcciones de despacho (CRD1) de un cliente -- ver ICustomerShipToAddressService.</summary>
public sealed class CustomerShipToAddressService : ICustomerShipToAddressService
{
    private readonly IHanaService _hana;

    public CustomerShipToAddressService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<string>> GetShipToAddressCodesAsync(string cardCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT "Address" AS "Code" FROM "CRD1"
            WHERE "CardCode" = :cardCode AND "AddressType" = 'S'
            """;

        var rows = await _hana.QueryAsync<AddressCodeRow>(sql, new { cardCode }, ct);
        return rows.Select(r => r.Code).ToList();
    }

    private sealed record AddressCodeRow
    {
        public string Code { get; init; } = null!;
    }
}
```

- [ ] **Step 6: Compilar**

Run: `dotnet build "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\PortalSaas.sln"`
Expected: 0 errores.

- [ ] **Step 7: Registrar `ICustomerShipToAddressService` en DI**

En `src/PortalSaas.Host/Program.cs`, junto a `ICustomerCatalogService`/`IItemCatalogService`
(línea 205-206):

```csharp
builder.Services.AddScoped<ICustomerShipToAddressService, CustomerShipToAddressService>();
```

- [ ] **Step 8: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos" "Portal SaaS - Core/src/PortalSaas.Core/Catalogos" "Portal SaaS - Core/src/PortalSaas.Host/Program.cs"
git commit -m "$(cat <<'EOF'
feat(catalogos): métodos batch de stock disponible, activo/inactivo y direcciones de despacho

Prerrequisitos del motor de reglas de validación pre-carga de Importación
Genérica -- consultas batch (nunca N+1) contra OITW/OCRD/OITM/CRD1.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Interfaz del motor + las 2 reglas estructurales fijas

**Files:**
- Create: `src/PortalSaas.Core/ImportacionGenerica/Reglas/IGenericImportValidationRule.cs`
- Create: `src/PortalSaas.Core/ImportacionGenerica/Reglas/PositiveQuantityRule.cs`
- Create: `src/PortalSaas.Core/ImportacionGenerica/Reglas/ValidDiscountPercentRule.cs`
- Delete: `src/PortalSaas.Core/ImportacionGenerica/IGenericImportValidationRule.cs`
- Test: `tests/PortalSaas.Core.Tests/ImportacionGenerica/Reglas/GenericImportValidationRuleEngineTests.cs` (arranca acá, se completa en las tareas siguientes)

**Interfaces:**
- Produces: `IGenericImportValidationRule` (interfaz), `PositiveQuantityRule`,
  `ValidDiscountPercentRule`.

- [ ] **Step 1: Escribir la interfaz del motor**

```csharp
// src/PortalSaas.Core/ImportacionGenerica/Reglas/IGenericImportValidationRule.cs
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>
/// Una regla del catálogo de validación pre-carga (ver docs/superpowers/specs/2026-09-
/// 07-reglas-validacion-importacion-generica-design.md). A diferencia del spec original
/// (que separaba reglas "por fila" de "por lote"), acá TODAS las reglas reciben el
/// archivo completo de una vez -- incluso una regla conceptualmente "por fila" (ej.
/// PriceVsFixedList) necesita batch-fetch contra SAP de todos los ItemCode distintos
/// antes de poder validar cada fila, exactamente el mismo criterio anti-N+1 que ya usa
/// GenericImportService.ProcessFileAsync para items/almacenes/cuentas -- una interfaz
/// única evita duplicar esa lógica de batch-fetch en dos formas distintas.
/// </summary>
public interface IGenericImportValidationRule
{
    GenericImportValidationRuleType RuleType { get; }

    /// <summary>Módulos a los que aplica -- el motor no corre la regla fuera de su módulo aunque esté configurada (defensa en profundidad).</summary>
    IReadOnlyList<GenericImportModule> ApplicableModules { get; }

    /// <summary>false = la severidad SIEMPRE es Block sin importar lo que traiga el RuleAssignment (PositiveQuantity/ValidDiscountPercent) -- esas dos ni siquiera necesitan un RuleAssignment, el motor las corre siempre.</summary>
    bool SeverityIsConfigurable { get; }

    /// <summary>
    /// Corre sobre TODAS las filas del archivo de una vez. Devuelve, por cada
    /// RowNumber con problema, la lista de mensajes de ESA regla para esa fila (vacío
    /// si no aplica esta regla a ninguna fila). `parameters` viene de
    /// GenericImportValidationRuleAssignmentDto.Parameters -- vacío para las reglas
    /// estructurales fijas.
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct);
}
```

- [ ] **Step 2: Escribir el test de las 2 reglas fijas**

```csharp
// tests/PortalSaas.Core.Tests/ImportacionGenerica/Reglas/GenericImportValidationRuleEngineTests.cs
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica.Reglas;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica.Reglas;

public sealed class GenericImportValidationRuleEngineTests
{
    private static GenericImportRowDto Row(int rowNumber, decimal? quantity = 1, decimal? discountPercent = null) => new()
    {
        RowNumber = rowNumber,
        GroupingKey = "G1",
        IsValid = true,
        Quantity = quantity,
        DiscountPercent = discountPercent,
    };

    [Fact]
    public async Task PositiveQuantityRule_marca_cantidad_no_positiva()
    {
        var rule = new PositiveQuantityRule();
        var rows = new[] { Row(1, quantity: 1), Row(2, quantity: 0), Row(3, quantity: null) };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?>(), CancellationToken.None);

        Assert.False(result.ContainsKey(1));
        Assert.True(result.ContainsKey(2));
        Assert.True(result.ContainsKey(3));
    }

    [Fact]
    public async Task ValidDiscountPercentRule_marca_descuento_fuera_de_rango()
    {
        var rule = new ValidDiscountPercentRule();
        var rows = new[] { Row(1, discountPercent: 10), Row(2, discountPercent: -1), Row(3, discountPercent: 101) };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?>(), CancellationToken.None);

        Assert.False(result.ContainsKey(1));
        Assert.True(result.ContainsKey(2));
        Assert.True(result.ContainsKey(3));
    }
}
```

- [ ] **Step 3: Correr el test y confirmar que falla**

Run: `dotnet test "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\tests\PortalSaas.Core.Tests" --filter "FullyQualifiedName~GenericImportValidationRuleEngineTests"`
Expected: FAIL -- `PositiveQuantityRule`/`ValidDiscountPercentRule` no existen todavía
en el namespace `Reglas`.

- [ ] **Step 4: Implementar las 2 reglas**

```csharp
// src/PortalSaas.Core/ImportacionGenerica/Reglas/PositiveQuantityRule.cs
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>Cantidad > 0 -- estructural, siempre activa, severidad fija en Block. Reemplaza al PositiveQuantityRule viejo (ImportacionGenerica/IGenericImportValidationRule.cs).</summary>
public sealed class PositiveQuantityRule : IGenericImportValidationRule
{
    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.PositiveQuantity;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = Enum.GetValues<GenericImportModule>();
    public bool SeverityIsConfigurable => false;

    public Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var row in rows)
        {
            if (row.Quantity is null or <= 0)
            {
                result[row.RowNumber] = ["Cantidad debe ser mayor a 0."];
            }
        }
        return Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<string>>>(result);
    }
}
```

```csharp
// src/PortalSaas.Core/ImportacionGenerica/Reglas/ValidDiscountPercentRule.cs
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>Descuento entre 0 y 100 -- estructural, siempre activa, severidad fija en Block.</summary>
public sealed class ValidDiscountPercentRule : IGenericImportValidationRule
{
    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.ValidDiscountPercent;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = [GenericImportModule.Sales, GenericImportModule.Purchase];
    public bool SeverityIsConfigurable => false;

    public Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var row in rows)
        {
            if (row.DiscountPercent is { } discount && (discount < 0 || discount > 100))
            {
                result[row.RowNumber] = ["PorcentajeDescuento debe estar entre 0 y 100."];
            }
        }
        return Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<string>>>(result);
    }
}
```

- [ ] **Step 5: Borrar el archivo viejo**

Borrar `src/PortalSaas.Core/ImportacionGenerica/IGenericImportValidationRule.cs`
(las clases `PositiveQuantityRule`/`ValidDiscountPercentRule`/la interfaz vieja quedan
reemplazadas por lo de arriba -- `GenericImportService.BuiltInRules` se actualiza en
la Task 11, no romper la compilación acá: dejar el `using PortalSaas.Core.
ImportacionGenerica;` de `GenericImportService.cs` sin tocar todavía, la Task 11 se
encarga de la integración completa).

- [ ] **Step 6: Correr los tests y confirmar que pasan**

Run: `dotnet test "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\tests\PortalSaas.Core.Tests" --filter "FullyQualifiedName~GenericImportValidationRuleEngineTests"`
Expected: PASS (2/2). (`GenericImportService.cs` puede quedar con un error de
compilación referenciando `BuiltInRules`/las clases viejas hasta la Task 11 -- si el
build completo falla acá, es esperado; correr los tests con `--filter` acota la
recompilación al proyecto de test, que no depende de que `GenericImportService.cs`
compile si se aísla correctamente. Si `dotnet test` igual falla por esto, adelantar el
Step 1 de la Task 11 -- comentar temporalmente el `foreach (var rule in BuiltInRules)`
-- antes de continuar.)

- [ ] **Step 7: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Core/ImportacionGenerica" "Portal SaaS - Core/tests"
git commit -m "$(cat <<'EOF'
feat(importacion-generica): interfaz del motor de reglas + PositiveQuantity/ValidDiscountPercent

Reemplaza IGenericImportValidationRule (namespace viejo) por la versión nueva
en Reglas/ -- interfaz única (sin distinguir fila/lote), cada regla resuelve
su propio batch-fetch.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Reglas `CustomerActiveInSap` / `ItemActiveInSap`

**Files:**
- Create: `src/PortalSaas.Core/ImportacionGenerica/Reglas/CustomerActiveInSapRule.cs`
- Create: `src/PortalSaas.Core/ImportacionGenerica/Reglas/ItemActiveInSapRule.cs`
- Modify: `tests/.../Reglas/GenericImportValidationRuleEngineTests.cs`

**Interfaces:**
- Consumes: `ICustomerCatalogService.GetActiveStatusAsync`,
  `ISupplierCatalogService.GetActiveStatusAsync`, `IItemCatalogService.GetActiveStatusAsync` (Task 4).
- Produces: `CustomerActiveInSapRule`, `ItemActiveInSapRule`.

- [ ] **Step 1: Escribir los tests que fallan**

Agregar al archivo de tests, con fakes mínimos escritos a mano (mismo criterio del
proyecto, sin Moq):

```csharp
    private sealed class FakeCustomerCatalogService : PortalSaas.Abstractions.Contratos.ICustomerCatalogService
    {
        private readonly IReadOnlyDictionary<string, bool> _active;
        public FakeCustomerCatalogService(IReadOnlyDictionary<string, bool> active) => _active = active;
        public Task<IReadOnlyList<CustomerDto>> ListAsync(CustomerFilter? filter = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CustomerDto>>([]);
        public Task<CustomerDto?> GetAsync(string cardCode, CancellationToken ct = default) => Task.FromResult<CustomerDto?>(null);
        public Task<IReadOnlyDictionary<string, bool>> GetActiveStatusAsync(IReadOnlyList<string> cardCodes, CancellationToken ct = default) => Task.FromResult(_active);
    }

    private sealed class FakeSupplierCatalogService : PortalSaas.Abstractions.Contratos.ISupplierCatalogService
    {
        private readonly IReadOnlyDictionary<string, bool> _active;
        public FakeSupplierCatalogService(IReadOnlyDictionary<string, bool> active) => _active = active;
        public Task<IReadOnlyList<SupplierDto>> ListAsync(SupplierFilter? filter = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SupplierDto>>([]);
        public Task<SupplierDto?> GetAsync(string cardCode, CancellationToken ct = default) => Task.FromResult<SupplierDto?>(null);
        public Task<IReadOnlyDictionary<string, bool>> GetActiveStatusAsync(IReadOnlyList<string> cardCodes, CancellationToken ct = default) => Task.FromResult(_active);
    }

    private static GenericImportRowDto RowWithPartner(int rowNumber, string cardCode, string? itemCode = null) => new()
    {
        RowNumber = rowNumber,
        GroupingKey = "G1",
        IsValid = true,
        BusinessPartnerCardCode = cardCode,
        ItemCode = itemCode,
    };

    [Fact]
    public async Task CustomerActiveInSapRule_marca_cliente_inactivo_en_Venta()
    {
        var rule = new CustomerActiveInSapRule(
            new FakeCustomerCatalogService(new Dictionary<string, bool> { ["C001"] = false, ["C002"] = true }),
            new FakeSupplierCatalogService(new Dictionary<string, bool>()));
        var rows = new[] { RowWithPartner(1, "C001"), RowWithPartner(2, "C002") };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?>(), CancellationToken.None);

        Assert.True(result.ContainsKey(1));
        Assert.False(result.ContainsKey(2));
    }

    [Fact]
    public async Task ItemActiveInSapRule_marca_articulo_inactivo()
    {
        var rule = new ItemActiveInSapRule(new FakeItemCatalogService(new Dictionary<string, bool> { ["I001"] = false, ["I002"] = true }));
        var rows = new[] { RowWithPartner(1, "C001", "I001"), RowWithPartner(2, "C001", "I002") };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?>(), CancellationToken.None);

        Assert.True(result.ContainsKey(1));
        Assert.False(result.ContainsKey(2));
    }
```

Y el fake de `IItemCatalogService`:

```csharp
    private sealed class FakeItemCatalogService : PortalSaas.Abstractions.Contratos.IItemCatalogService
    {
        private readonly IReadOnlyDictionary<string, bool> _active;
        public FakeItemCatalogService(IReadOnlyDictionary<string, bool> active) => _active = active;
        public Task<IReadOnlyList<ItemDto>> SearchAsync(string text, int limit = 30, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ItemDto>>([]);
        public Task<IReadOnlyList<ItemDto>> GetByCodesAsync(IReadOnlyCollection<string> itemCodes, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ItemDto>>([]);
        public Task<IReadOnlyList<ItemDto>> GetTopAsync(int limit = 30, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ItemDto>>([]);
        public Task<IReadOnlyDictionary<string, bool>> GetActiveStatusAsync(IReadOnlyList<string> itemCodes, CancellationToken ct = default) => Task.FromResult(_active);
    }
```

(Ajustar los `using PortalSaas.Abstractions.Modelos;`/`Contratos;` al tope del archivo
de test según haga falta para que `CustomerDto`/`SupplierDto`/`ItemDto`/`CustomerFilter`/
`SupplierFilter` resuelvan.)

- [ ] **Step 2: Correr y confirmar que falla**

Run: `dotnet test "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\tests\PortalSaas.Core.Tests" --filter "FullyQualifiedName~GenericImportValidationRuleEngineTests"`
Expected: FAIL -- `CustomerActiveInSapRule`/`ItemActiveInSapRule` no existen.

- [ ] **Step 3: Implementar `CustomerActiveInSapRule`**

```csharp
// src/PortalSaas.Core/ImportacionGenerica/Reglas/CustomerActiveInSapRule.cs
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>
/// El socio de negocio de la fila está Activo en SAP -- Cliente (OCRD, CardType='C')
/// para Venta, Proveedor (CardType='S') para Compra. Sin BusinessPartnerCardCode en la
/// fila (modo normal, socio fijo del wizard -- ver GenericImportRowDto.
/// BusinessPartnerCardCode) no aplica, esta regla solo valida carga multi-socio.
/// </summary>
public sealed class CustomerActiveInSapRule : IGenericImportValidationRule
{
    private readonly ICustomerCatalogService _customers;
    private readonly ISupplierCatalogService _suppliers;

    public CustomerActiveInSapRule(ICustomerCatalogService customers, ISupplierCatalogService suppliers)
    {
        _customers = customers;
        _suppliers = suppliers;
    }

    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.CustomerActiveInSap;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = [GenericImportModule.Sales, GenericImportModule.Purchase];
    public bool SeverityIsConfigurable => true;

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var cardCodes = rows.Where(r => r.BusinessPartnerCardCode is not null)
            .Select(r => r.BusinessPartnerCardCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (cardCodes.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }

        var activeStatus = module == GenericImportModule.Purchase
            ? await _suppliers.GetActiveStatusAsync(cardCodes, ct)
            : await _customers.GetActiveStatusAsync(cardCodes, ct);

        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var row in rows)
        {
            if (row.BusinessPartnerCardCode is { } cardCode && activeStatus.TryGetValue(cardCode, out var isActive) && !isActive)
            {
                result[row.RowNumber] = [$"El socio de negocio \"{cardCode}\" está inactivo en SAP."];
            }
        }
        return result;
    }
}
```

- [ ] **Step 4: Implementar `ItemActiveInSapRule`**

```csharp
// src/PortalSaas.Core/ImportacionGenerica/Reglas/ItemActiveInSapRule.cs
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>El ItemCode resuelto de la fila está Activo en SAP (OITM.validFor = 'Y').</summary>
public sealed class ItemActiveInSapRule : IGenericImportValidationRule
{
    private readonly IItemCatalogService _items;

    public ItemActiveInSapRule(IItemCatalogService items)
    {
        _items = items;
    }

    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.ItemActiveInSap;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = Enum.GetValues<GenericImportModule>();
    public bool SeverityIsConfigurable => true;

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var itemCodes = rows.Where(r => r.ItemCode is not null)
            .Select(r => r.ItemCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (itemCodes.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }

        var activeStatus = await _items.GetActiveStatusAsync(itemCodes, ct);

        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var row in rows)
        {
            if (row.ItemCode is { } itemCode && activeStatus.TryGetValue(itemCode, out var isActive) && !isActive)
            {
                result[row.RowNumber] = [$"El artículo \"{itemCode}\" está inactivo en SAP."];
            }
        }
        return result;
    }
}
```

- [ ] **Step 5: Correr y confirmar que pasan**

Run: `dotnet test "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\tests\PortalSaas.Core.Tests" --filter "FullyQualifiedName~GenericImportValidationRuleEngineTests"`
Expected: PASS (4/4 acumulado).

- [ ] **Step 6: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Core/ImportacionGenerica/Reglas" "Portal SaaS - Core/tests"
git commit -m "$(cat <<'EOF'
feat(importacion-generica): reglas CustomerActiveInSap / ItemActiveInSap

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: Reglas `PriceVsFixedList` / `PriceVsCustomerList`

**Files:**
- Create: `src/PortalSaas.Core/ImportacionGenerica/Reglas/PriceVsFixedListRule.cs`
- Create: `src/PortalSaas.Core/ImportacionGenerica/Reglas/PriceVsCustomerListRule.cs`
- Modify: `tests/.../Reglas/GenericImportValidationRuleEngineTests.cs`

**Interfaces:**
- Consumes: `IPriceListService.GetPricesAsync(IReadOnlyCollection<string> itemCodes, int priceList, CancellationToken ct)`,
  `IBusinessPartnerDefaultsService.GetAsync(string cardCode, CancellationToken ct)` (ya existen, sin cambios).
- Produces: `PriceVsFixedListRule`, `PriceVsCustomerListRule`.

- [ ] **Step 1: Escribir los tests que fallan**

```csharp
    private sealed class FakePriceListService : PortalSaas.Abstractions.Contratos.IPriceListService
    {
        private readonly IReadOnlyDictionary<string, decimal> _prices;
        public FakePriceListService(IReadOnlyDictionary<string, decimal> prices) => _prices = prices;
        public Task<decimal?> GetPriceAsync(string itemCode, int priceList, CancellationToken ct = default) => Task.FromResult(_prices.TryGetValue(itemCode, out var p) ? p : (decimal?)null);
        public Task<IReadOnlyDictionary<string, decimal>> GetPricesAsync(IReadOnlyCollection<string> itemCodes, int priceList, CancellationToken ct = default) => Task.FromResult(_prices);
        public Task<IReadOnlyList<PriceListOptionDto>> ListAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PriceListOptionDto>>([]);
    }

    private sealed class FakeBusinessPartnerDefaultsService : PortalSaas.Abstractions.Contratos.IBusinessPartnerDefaultsService
    {
        private readonly int? _priceListCode;
        public FakeBusinessPartnerDefaultsService(int? priceListCode) => _priceListCode = priceListCode;
        public Task<BusinessPartnerDefaultsDto?> GetAsync(string cardCode, CancellationToken ct = default) =>
            Task.FromResult<BusinessPartnerDefaultsDto?>(new BusinessPartnerDefaultsDto { CardCode = cardCode, PriceListCode = _priceListCode });
    }

    private static GenericImportRowDto RowWithPrice(int rowNumber, string itemCode, decimal unitPrice, string cardCode = "C001") => new()
    {
        RowNumber = rowNumber,
        GroupingKey = "G1",
        IsValid = true,
        ItemCode = itemCode,
        UnitPrice = unitPrice,
        BusinessPartnerCardCode = cardCode,
    };

    [Fact]
    public async Task PriceVsFixedListRule_marca_precio_fuera_de_tolerancia()
    {
        var rule = new PriceVsFixedListRule(new FakePriceListService(new Dictionary<string, decimal> { ["I001"] = 100m }));
        var rows = new[] { RowWithPrice(1, "I001", 100m), RowWithPrice(2, "I001", 80m) };
        var parameters = new Dictionary<string, object?> { ["priceListNum"] = 1, ["tolerancePercent"] = 5m };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, parameters, CancellationToken.None);

        Assert.False(result.ContainsKey(1));
        Assert.True(result.ContainsKey(2));
    }

    [Fact]
    public async Task PriceVsCustomerListRule_usa_la_lista_asignada_al_cliente()
    {
        var rule = new PriceVsCustomerListRule(
            new FakePriceListService(new Dictionary<string, decimal> { ["I001"] = 200m }),
            new FakeBusinessPartnerDefaultsService(priceListCode: 2));
        var rows = new[] { RowWithPrice(1, "I001", 200m), RowWithPrice(2, "I001", 150m) };
        var parameters = new Dictionary<string, object?> { ["tolerancePercent"] = 0m };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, parameters, CancellationToken.None);

        Assert.False(result.ContainsKey(1));
        Assert.True(result.ContainsKey(2));
    }

    [Fact]
    public async Task PriceVsCustomerListRule_sin_lista_asignada_no_valida_nada()
    {
        var rule = new PriceVsCustomerListRule(
            new FakePriceListService(new Dictionary<string, decimal> { ["I001"] = 200m }),
            new FakeBusinessPartnerDefaultsService(priceListCode: null));
        var rows = new[] { RowWithPrice(1, "I001", 1m) };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?> { ["tolerancePercent"] = 0m }, CancellationToken.None);

        Assert.Empty(result);
    }
```

- [ ] **Step 2: Correr y confirmar que falla**

Run: `dotnet test "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\tests\PortalSaas.Core.Tests" --filter "FullyQualifiedName~GenericImportValidationRuleEngineTests"`
Expected: FAIL -- las 2 clases no existen.

- [ ] **Step 3: Implementar `PriceVsFixedListRule`**

```csharp
// src/PortalSaas.Core/ImportacionGenerica/Reglas/PriceVsFixedListRule.cs
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>
/// UnitPrice de la fila vs. el precio del artículo en una lista de precio FIJA
/// (parámetro "priceListNum"), con tolerancia "tolerancePercent" (% sobre el precio de
/// lista, en cualquier dirección). Solo Artículo con precio -- Servicio/Inventario no
/// tienen ItemCode+UnitPrice juntos en el mismo sentido, se filtran igual acá.
/// </summary>
public sealed class PriceVsFixedListRule : IGenericImportValidationRule
{
    private readonly IPriceListService _priceList;

    public PriceVsFixedListRule(IPriceListService priceList)
    {
        _priceList = priceList;
    }

    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.PriceVsFixedList;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = [GenericImportModule.Sales, GenericImportModule.Purchase];
    public bool SeverityIsConfigurable => true;

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var priceListNum = Convert.ToInt32(parameters["priceListNum"]);
        var tolerancePercent = parameters.TryGetValue("tolerancePercent", out var t) && t is not null ? Convert.ToDecimal(t) : 0m;

        var itemsWithPrice = rows.Where(r => r.ItemCode is not null && r.UnitPrice is not null).ToList();
        var itemCodes = itemsWithPrice.Select(r => r.ItemCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (itemCodes.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }

        var listPrices = await _priceList.GetPricesAsync(itemCodes, priceListNum, ct);

        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var row in itemsWithPrice)
        {
            if (!listPrices.TryGetValue(row.ItemCode!, out var listPrice))
            {
                continue;
            }

            var tolerancia = listPrice * (tolerancePercent / 100m);
            if (Math.Abs(row.UnitPrice!.Value - listPrice) > tolerancia)
            {
                result[row.RowNumber] = [$"Precio importado ({row.UnitPrice:N2}) distinto al de la lista {priceListNum} ({listPrice:N2})."];
            }
        }
        return result;
    }
}
```

- [ ] **Step 4: Implementar `PriceVsCustomerListRule`**

```csharp
// src/PortalSaas.Core/ImportacionGenerica/Reglas/PriceVsCustomerListRule.cs
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>
/// Igual que PriceVsFixedListRule pero la lista de referencia es la asignada al
/// CardCode de cada fila (OCRD.ListNum, vía IBusinessPartnerDefaultsService) -- sin
/// lista asignada, esa fila simplemente no se valida (no es un error, el cliente no
/// tiene lista de precio configurada en SAP).
/// </summary>
public sealed class PriceVsCustomerListRule : IGenericImportValidationRule
{
    private readonly IPriceListService _priceList;
    private readonly IBusinessPartnerDefaultsService _partnerDefaults;

    public PriceVsCustomerListRule(IPriceListService priceList, IBusinessPartnerDefaultsService partnerDefaults)
    {
        _priceList = priceList;
        _partnerDefaults = partnerDefaults;
    }

    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.PriceVsCustomerList;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = [GenericImportModule.Sales, GenericImportModule.Purchase];
    public bool SeverityIsConfigurable => true;

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var tolerancePercent = parameters.TryGetValue("tolerancePercent", out var t) && t is not null ? Convert.ToDecimal(t) : 0m;

        var itemsWithPrice = rows.Where(r => r.ItemCode is not null && r.UnitPrice is not null && r.BusinessPartnerCardCode is not null).ToList();
        if (itemsWithPrice.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }

        var priceListByCardCode = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        foreach (var cardCode in itemsWithPrice.Select(r => r.BusinessPartnerCardCode!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var defaults = await _partnerDefaults.GetAsync(cardCode, ct);
            priceListByCardCode[cardCode] = defaults?.PriceListCode;
        }

        var itemCodes = itemsWithPrice.Select(r => r.ItemCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var pricesByList = new Dictionary<int, IReadOnlyDictionary<string, decimal>>();
        foreach (var priceListNum in priceListByCardCode.Values.Where(v => v is not null).Select(v => v!.Value).Distinct())
        {
            pricesByList[priceListNum] = await _priceList.GetPricesAsync(itemCodes, priceListNum, ct);
        }

        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var row in itemsWithPrice)
        {
            var priceListNum = priceListByCardCode.GetValueOrDefault(row.BusinessPartnerCardCode!);
            if (priceListNum is null || !pricesByList.TryGetValue(priceListNum.Value, out var prices) || !prices.TryGetValue(row.ItemCode!, out var listPrice))
            {
                continue;
            }

            var tolerancia = listPrice * (tolerancePercent / 100m);
            if (Math.Abs(row.UnitPrice!.Value - listPrice) > tolerancia)
            {
                result[row.RowNumber] = [$"Precio importado ({row.UnitPrice:N2}) distinto al de la lista del cliente {priceListNum} ({listPrice:N2})."];
            }
        }
        return result;
    }
}
```

- [ ] **Step 5: Correr y confirmar que pasan**

Run: `dotnet test "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\tests\PortalSaas.Core.Tests" --filter "FullyQualifiedName~GenericImportValidationRuleEngineTests"`
Expected: PASS (7/7 acumulado).

- [ ] **Step 6: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Core/ImportacionGenerica/Reglas" "Portal SaaS - Core/tests"
git commit -m "$(cat <<'EOF'
feat(importacion-generica): reglas PriceVsFixedList / PriceVsCustomerList

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 8: Regla `StockAvailable`

**Files:**
- Create: `src/PortalSaas.Core/ImportacionGenerica/Reglas/StockAvailableRule.cs`
- Modify: `tests/.../Reglas/GenericImportValidationRuleEngineTests.cs`

**Interfaces:**
- Consumes: `IItemStockService.GetAvailableStockAsync` (Task 4).
- Produces: `StockAvailableRule`.

- [ ] **Step 1: Escribir los tests que fallan**

```csharp
    private sealed class FakeItemStockService : PortalSaas.Abstractions.Contratos.IItemStockService
    {
        private readonly IReadOnlyDictionary<(string, string), decimal> _available;
        public FakeItemStockService(IReadOnlyDictionary<(string, string), decimal> available) => _available = available;
        public Task<IReadOnlyList<WarehouseStockDto>> GetStockByItemAsync(string itemCode, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WarehouseStockDto>>([]);
        public Task<IReadOnlyDictionary<(string ItemCode, string WhsCode), decimal>> GetAvailableStockAsync(IReadOnlyList<(string ItemCode, string WhsCode)> pairs, CancellationToken ct = default) => Task.FromResult(_available);
    }

    private static GenericImportRowDto RowWithStock(int rowNumber, string itemCode, string warehouse, decimal quantity) => new()
    {
        RowNumber = rowNumber,
        GroupingKey = "G1",
        IsValid = true,
        ItemCode = itemCode,
        Warehouse = warehouse,
        Quantity = quantity,
    };

    [Fact]
    public async Task StockAvailableRule_suma_demanda_de_varios_documentos_del_mismo_archivo()
    {
        var rule = new StockAvailableRule(new FakeItemStockService(new Dictionary<(string, string), decimal> { [("I001", "01")] = 100m }));
        var rows = new[]
        {
            RowWithStock(1, "I001", "01", 60m),
            RowWithStock(2, "I001", "01", 60m), // suma 120 > 100 disponible
        };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?> { ["tolerancePercent"] = 0m }, CancellationToken.None);

        Assert.True(result.ContainsKey(1));
        Assert.True(result.ContainsKey(2));
    }

    [Fact]
    public async Task StockAvailableRule_valida_cada_bodega_de_forma_independiente()
    {
        var rule = new StockAvailableRule(new FakeItemStockService(new Dictionary<(string, string), decimal>
        {
            [("I001", "01")] = 10m,
            [("I001", "02")] = 100m,
        }));
        var rows = new[]
        {
            RowWithStock(1, "I001", "01", 50m), // no alcanza en 01
            RowWithStock(2, "I001", "02", 50m), // sí alcanza en 02
        };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?> { ["tolerancePercent"] = 0m }, CancellationToken.None);

        Assert.True(result.ContainsKey(1));
        Assert.False(result.ContainsKey(2));
    }

    [Fact]
    public async Task StockAvailableRule_respeta_la_tolerancia()
    {
        var rule = new StockAvailableRule(new FakeItemStockService(new Dictionary<(string, string), decimal> { [("I001", "01")] = 100m }));
        var rows = new[] { RowWithStock(1, "I001", "01", 105m) }; // 5% de más

        var conTolerancia = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?> { ["tolerancePercent"] = 10m }, CancellationToken.None);
        var sinTolerancia = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?> { ["tolerancePercent"] = 0m }, CancellationToken.None);

        Assert.False(conTolerancia.ContainsKey(1));
        Assert.True(sinTolerancia.ContainsKey(1));
    }

    [Fact]
    public async Task StockAvailableRule_usa_SourceWarehouse_en_Inventario()
    {
        var rule = new StockAvailableRule(new FakeItemStockService(new Dictionary<(string, string), decimal> { [("I001", "01")] = 10m }));
        var row = new GenericImportRowDto { RowNumber = 1, GroupingKey = "G1", IsValid = true, ItemCode = "I001", SourceWarehouse = "01", Quantity = 50m };

        var result = await rule.ValidateAsync([row], GenericImportModule.Inventory, new Dictionary<string, object?> { ["tolerancePercent"] = 0m }, CancellationToken.None);

        Assert.True(result.ContainsKey(1));
    }
```

- [ ] **Step 2: Correr y confirmar que falla**

Run: `dotnet test "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\tests\PortalSaas.Core.Tests" --filter "FullyQualifiedName~GenericImportValidationRuleEngineTests"`
Expected: FAIL -- `StockAvailableRule` no existe.

- [ ] **Step 3: Implementar**

```csharp
// src/PortalSaas.Core/ImportacionGenerica/Reglas/StockAvailableRule.cs
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>
/// Demanda agregada de TODO el archivo (suma de Quantity de todas las filas, de
/// TODOS los documentos) por (ItemCode, Bodega) vs. disponible real (OnHand -
/// IsCommited). Bodega = Warehouse en Venta/Compra, SourceWarehouse en Inventario
/// (el que efectivamente descuenta stock -- DestinationWarehouse no se valida).
/// "tolerancePercent" se aplica sobre la DEMANDA (no sobre el precio, a diferencia de
/// las reglas de precio) -- ej. 10% permite que la demanda supere el disponible hasta
/// en un 10% sin marcar la fila.
/// </summary>
public sealed class StockAvailableRule : IGenericImportValidationRule
{
    private readonly IItemStockService _stock;

    public StockAvailableRule(IItemStockService stock)
    {
        _stock = stock;
    }

    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.StockAvailable;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = Enum.GetValues<GenericImportModule>();
    public bool SeverityIsConfigurable => true;

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var tolerancePercent = parameters.TryGetValue("tolerancePercent", out var t) && t is not null ? Convert.ToDecimal(t) : 0m;

        string? ResolveWarehouse(GenericImportRowDto row) => module == GenericImportModule.Inventory ? row.SourceWarehouse : row.Warehouse;

        var relevantRows = rows.Where(r => r.ItemCode is not null && ResolveWarehouse(r) is not null && r.Quantity is not null).ToList();
        if (relevantRows.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }

        var demandaPorPar = relevantRows
            .GroupBy(r => (ItemCode: r.ItemCode!, Warehouse: ResolveWarehouse(r)!), TupleComparer.Instance)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity!.Value), TupleComparer.Instance);

        var disponible = await _stock.GetAvailableStockAsync(
            demandaPorPar.Keys.Select(k => (k.ItemCode, WhsCode: k.Warehouse)).ToList(), ct);

        var pares_con_deficit = new HashSet<(string ItemCode, string Warehouse)>(TupleComparer.Instance);
        foreach (var (clave, demanda) in demandaPorPar)
        {
            var stockDisponible = disponible.GetValueOrDefault((clave.ItemCode, clave.Warehouse), 0m);
            var tolerancia = demanda * (tolerancePercent / 100m);
            if (demanda > stockDisponible + tolerancia)
            {
                pares_con_deficit.Add(clave);
            }
        }

        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var row in relevantRows)
        {
            var clave = (ItemCode: row.ItemCode!, Warehouse: ResolveWarehouse(row)!);
            if (!pares_con_deficit.Contains(clave))
            {
                continue;
            }

            var demanda = demandaPorPar[clave];
            var stockDisponible = disponible.GetValueOrDefault((clave.ItemCode, clave.Warehouse), 0m);
            result[row.RowNumber] =
            [
                $"Demanda total de \"{clave.ItemCode}\" en bodega \"{clave.Warehouse}\": {demanda:N2} -- disponible: {stockDisponible:N2} (faltan {demanda - stockDisponible:N2}).",
            ];
        }
        return result;
    }

    private sealed class TupleComparer : IEqualityComparer<(string ItemCode, string Warehouse)>
    {
        public static readonly TupleComparer Instance = new();
        public bool Equals((string ItemCode, string Warehouse) x, (string ItemCode, string Warehouse) y) =>
            string.Equals(x.ItemCode, y.ItemCode, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Warehouse, y.Warehouse, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string ItemCode, string Warehouse) obj) =>
            HashCode.Combine(obj.ItemCode.ToUpperInvariant(), obj.Warehouse.ToUpperInvariant());
    }
}
```

- [ ] **Step 4: Correr y confirmar que pasan**

Run: `dotnet test "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\tests\PortalSaas.Core.Tests" --filter "FullyQualifiedName~GenericImportValidationRuleEngineTests"`
Expected: PASS (11/11 acumulado).

- [ ] **Step 5: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Core/ImportacionGenerica/Reglas" "Portal SaaS - Core/tests"
git commit -m "$(cat <<'EOF'
feat(importacion-generica): regla StockAvailable (demanda agregada de lote por artículo-bodega)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 9: Regla `CustomerBranchValid`

**Files:**
- Create: `src/PortalSaas.Core/ImportacionGenerica/Reglas/CustomerBranchValidRule.cs`
- Modify: `tests/.../Reglas/GenericImportValidationRuleEngineTests.cs`

**Interfaces:**
- Consumes: `ICustomerShipToAddressService.GetShipToAddressCodesAsync` (Task 4).
- Produces: `CustomerBranchValidRule`.

- [ ] **Step 1: Escribir el test que falla**

```csharp
    private sealed class FakeCustomerShipToAddressService : PortalSaas.Abstractions.Contratos.ICustomerShipToAddressService
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _addressesByCardCode;
        public FakeCustomerShipToAddressService(IReadOnlyDictionary<string, IReadOnlyList<string>> addressesByCardCode) => _addressesByCardCode = addressesByCardCode;
        public Task<IReadOnlyList<string>> GetShipToAddressCodesAsync(string cardCode, CancellationToken ct = default) =>
            Task.FromResult(_addressesByCardCode.GetValueOrDefault(cardCode, []));
    }

    private static GenericImportRowDto RowWithBranch(int rowNumber, string cardCode, string branch) => new()
    {
        RowNumber = rowNumber,
        GroupingKey = "G1",
        IsValid = true,
        BusinessPartnerCardCode = cardCode,
        Branch = branch,
    };

    [Fact]
    public async Task CustomerBranchValidRule_marca_sucursal_que_no_pertenece_al_cliente()
    {
        var rule = new CustomerBranchValidRule(new FakeCustomerShipToAddressService(
            new Dictionary<string, IReadOnlyList<string>> { ["C001"] = ["SUC-CENTRAL", "SUC-NORTE"] }));
        var rows = new[] { RowWithBranch(1, "C001", "SUC-CENTRAL"), RowWithBranch(2, "C001", "SUC-INEXISTENTE") };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?>(), CancellationToken.None);

        Assert.False(result.ContainsKey(1));
        Assert.True(result.ContainsKey(2));
    }
```

- [ ] **Step 2: Correr y confirmar que falla**

Run: `dotnet test "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\tests\PortalSaas.Core.Tests" --filter "FullyQualifiedName~GenericImportValidationRuleEngineTests"`
Expected: FAIL.

- [ ] **Step 3: Implementar**

```csharp
// src/PortalSaas.Core/ImportacionGenerica/Reglas/CustomerBranchValidRule.cs
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>La Sucursal (Branch) de la fila corresponde a una dirección de despacho (CRD1, AddressType='S') del CardCode de esa fila.</summary>
public sealed class CustomerBranchValidRule : IGenericImportValidationRule
{
    private readonly ICustomerShipToAddressService _shipToAddresses;

    public CustomerBranchValidRule(ICustomerShipToAddressService shipToAddresses)
    {
        _shipToAddresses = shipToAddresses;
    }

    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.CustomerBranchValid;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = [GenericImportModule.Sales, GenericImportModule.Purchase];
    public bool SeverityIsConfigurable => true;

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var relevantRows = rows.Where(r => r.BusinessPartnerCardCode is not null && !string.IsNullOrWhiteSpace(r.Branch)).ToList();
        if (relevantRows.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }

        var addressesByCardCode = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var cardCode in relevantRows.Select(r => r.BusinessPartnerCardCode!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            addressesByCardCode[cardCode] = await _shipToAddresses.GetShipToAddressCodesAsync(cardCode, ct);
        }

        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var row in relevantRows)
        {
            var addresses = addressesByCardCode.GetValueOrDefault(row.BusinessPartnerCardCode!, []);
            if (!addresses.Contains(row.Branch!, StringComparer.OrdinalIgnoreCase))
            {
                result[row.RowNumber] = [$"La sucursal \"{row.Branch}\" no pertenece al cliente \"{row.BusinessPartnerCardCode}\"."];
            }
        }
        return result;
    }
}
```

- [ ] **Step 4: Correr y confirmar que pasan**

Run: `dotnet test "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\tests\PortalSaas.Core.Tests" --filter "FullyQualifiedName~GenericImportValidationRuleEngineTests"`
Expected: PASS (12/12 acumulado).

- [ ] **Step 5: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Core/ImportacionGenerica/Reglas" "Portal SaaS - Core/tests"
git commit -m "$(cat <<'EOF'
feat(importacion-generica): regla CustomerBranchValid

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 10: Motor orquestador `GenericImportValidationRuleEngine`

**Files:**
- Create: `src/PortalSaas.Core/ImportacionGenerica/Reglas/GenericImportValidationRuleEngine.cs`
- Modify: `tests/.../Reglas/GenericImportValidationRuleEngineTests.cs`

**Interfaces:**
- Consumes: `IGenericImportValidationRule` (todas las implementaciones anteriores),
  `GenericImportValidationRuleAssignmentDto`.
- Produces:
  ```csharp
  public interface IGenericImportValidationRuleEngine
  {
      Task<IReadOnlyList<GenericImportRowDto>> ApplyAsync(
          IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
          IReadOnlyList<GenericImportValidationRuleAssignmentDto> assignments, CancellationToken ct = default);
  }
  ```

- [ ] **Step 1: Escribir los tests que fallan**

```csharp
    [Fact]
    public async Task ApplyAsync_reglas_estructurales_siempre_corren_como_Block()
    {
        var engine = new GenericImportValidationRuleEngine([new PositiveQuantityRule(), new ValidDiscountPercentRule()]);
        var rows = new[] { Row(1, quantity: -1) };

        var result = await engine.ApplyAsync(rows, GenericImportModule.Sales, [], CancellationToken.None);

        var row = Assert.Single(result);
        Assert.False(row.IsValid);
        Assert.Contains("Cantidad debe ser mayor a 0.", row.Errors);
        Assert.Empty(row.Warnings);
    }

    [Fact]
    public async Task ApplyAsync_Severity_Warning_no_afecta_IsValid()
    {
        var engine = new GenericImportValidationRuleEngine(
        [
            new PositiveQuantityRule(),
            new ValidDiscountPercentRule(),
            new ItemActiveInSapRule(new FakeItemCatalogService(new Dictionary<string, bool> { ["I001"] = false })),
        ]);
        var rows = new[] { RowWithPartner(1, "C001", "I001") with { Quantity = 1 } };
        var assignments = new[]
        {
            new GenericImportValidationRuleAssignmentDto(1, GenericImportValidationRuleType.ItemActiveInSap,
                GenericImportValidationSeverity.Warning, true, new Dictionary<string, object?>()),
        };

        var result = await engine.ApplyAsync(rows, GenericImportModule.Sales, assignments, CancellationToken.None);

        var row = Assert.Single(result);
        Assert.True(row.IsValid);
        Assert.Empty(row.Errors);
        Assert.Contains(row.Warnings, w => w.Contains("inactivo"));
    }

    [Fact]
    public async Task ApplyAsync_Severity_Block_marca_IsValid_false()
    {
        var engine = new GenericImportValidationRuleEngine(
        [
            new PositiveQuantityRule(),
            new ValidDiscountPercentRule(),
            new ItemActiveInSapRule(new FakeItemCatalogService(new Dictionary<string, bool> { ["I001"] = false })),
        ]);
        var rows = new[] { RowWithPartner(1, "C001", "I001") with { Quantity = 1 } };
        var assignments = new[]
        {
            new GenericImportValidationRuleAssignmentDto(1, GenericImportValidationRuleType.ItemActiveInSap,
                GenericImportValidationSeverity.Block, true, new Dictionary<string, object?>()),
        };

        var result = await engine.ApplyAsync(rows, GenericImportModule.Sales, assignments, CancellationToken.None);

        var row = Assert.Single(result);
        Assert.False(row.IsValid);
        Assert.Contains(row.Errors, e => e.Contains("inactivo"));
    }

    [Fact]
    public async Task ApplyAsync_regla_inactiva_no_corre()
    {
        var engine = new GenericImportValidationRuleEngine(
        [
            new PositiveQuantityRule(),
            new ValidDiscountPercentRule(),
            new ItemActiveInSapRule(new FakeItemCatalogService(new Dictionary<string, bool> { ["I001"] = false })),
        ]);
        var rows = new[] { RowWithPartner(1, "C001", "I001") with { Quantity = 1 } };
        var assignments = new[]
        {
            new GenericImportValidationRuleAssignmentDto(1, GenericImportValidationRuleType.ItemActiveInSap,
                GenericImportValidationSeverity.Block, false, new Dictionary<string, object?>()),
        };

        var result = await engine.ApplyAsync(rows, GenericImportModule.Sales, assignments, CancellationToken.None);

        Assert.True(Assert.Single(result).IsValid);
    }

    [Fact]
    public async Task ApplyAsync_regla_no_aplicable_al_modulo_no_corre_aunque_este_activa()
    {
        // ValidDiscountPercentRule no aplica a Inventario (ApplicableModules = Sales/Purchase)
        var engine = new GenericImportValidationRuleEngine([new PositiveQuantityRule(), new ValidDiscountPercentRule()]);
        var row = new GenericImportRowDto { RowNumber = 1, GroupingKey = "G1", IsValid = true, Quantity = 1, DiscountPercent = 999 };

        var result = await engine.ApplyAsync([row], GenericImportModule.Inventory, [], CancellationToken.None);

        Assert.True(Assert.Single(result).IsValid);
    }
```

- [ ] **Step 2: Correr y confirmar que falla**

Run: `dotnet test "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\tests\PortalSaas.Core.Tests" --filter "FullyQualifiedName~GenericImportValidationRuleEngineTests"`
Expected: FAIL -- `GenericImportValidationRuleEngine`/`IGenericImportValidationRuleEngine` no existen.

- [ ] **Step 3: Implementar**

```csharp
// src/PortalSaas.Core/ImportacionGenerica/Reglas/GenericImportValidationRuleEngine.cs
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

public interface IGenericImportValidationRuleEngine
{
    /// <summary>
    /// Corre las reglas estructurales fijas (siempre, Severity = Block) + las
    /// configurables activas de `assignments` (respetando su Severity), y devuelve las
    /// filas con Errors/Warnings/IsValid actualizados. Una regla no aplicable al
    /// `module` recibido no corre aunque esté en `assignments` -- defensa en
    /// profundidad, el motor no confía ciegamente en la configuración guardada.
    /// </summary>
    Task<IReadOnlyList<GenericImportRowDto>> ApplyAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyList<GenericImportValidationRuleAssignmentDto> assignments, CancellationToken ct = default);
}

/// <summary>Ver IGenericImportValidationRuleEngine. Recibe TODAS las implementaciones de IGenericImportValidationRule vía DI (IEnumerable, ver Program.cs).</summary>
public sealed class GenericImportValidationRuleEngine : IGenericImportValidationRuleEngine
{
    private readonly IReadOnlyList<IGenericImportValidationRule> _allRules;

    public GenericImportValidationRuleEngine(IEnumerable<IGenericImportValidationRule> allRules)
    {
        _allRules = allRules.ToList();
    }

    public async Task<IReadOnlyList<GenericImportRowDto>> ApplyAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyList<GenericImportValidationRuleAssignmentDto> assignments, CancellationToken ct = default)
    {
        var errorsByRow = new Dictionary<int, List<string>>();
        var warningsByRow = new Dictionary<int, List<string>>();

        void AddMessages(IReadOnlyDictionary<int, IReadOnlyList<string>> messages, bool isBlock)
        {
            var target = isBlock ? errorsByRow : warningsByRow;
            foreach (var (rowNumber, list) in messages)
            {
                if (!target.TryGetValue(rowNumber, out var bucket))
                {
                    bucket = [];
                    target[rowNumber] = bucket;
                }
                bucket.AddRange(list);
            }
        }

        foreach (var rule in _allRules.Where(r => !r.SeverityIsConfigurable && r.ApplicableModules.Contains(module)))
        {
            var messages = await rule.ValidateAsync(rows, module, new Dictionary<string, object?>(), ct);
            AddMessages(messages, isBlock: true);
        }

        foreach (var assignment in assignments.Where(a => a.IsActive))
        {
            var rule = _allRules.FirstOrDefault(r => r.SeverityIsConfigurable && r.RuleType == assignment.RuleType);
            if (rule is null || !rule.ApplicableModules.Contains(module))
            {
                continue;
            }

            var messages = await rule.ValidateAsync(rows, module, assignment.Parameters, ct);
            AddMessages(messages, isBlock: assignment.Severity == GenericImportValidationSeverity.Block);
        }

        return rows.Select(row =>
        {
            var newErrors = errorsByRow.TryGetValue(row.RowNumber, out var errs) ? row.Errors.Concat(errs).ToList() : row.Errors;
            var newWarnings = warningsByRow.TryGetValue(row.RowNumber, out var warns) ? (IReadOnlyList<string>)warns : row.Warnings;
            return newErrors == row.Errors && newWarnings == row.Warnings
                ? row
                : row with { Errors = newErrors, Warnings = newWarnings, IsValid = newErrors.Count == 0 };
        }).ToList();
    }
}
```

- [ ] **Step 4: Correr y confirmar que pasan**

Run: `dotnet test "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\tests\PortalSaas.Core.Tests" --filter "FullyQualifiedName~GenericImportValidationRuleEngineTests"`
Expected: PASS (17/17 acumulado).

- [ ] **Step 5: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Core/ImportacionGenerica/Reglas" "Portal SaaS - Core/tests"
git commit -m "$(cat <<'EOF'
feat(importacion-generica): GenericImportValidationRuleEngine (orquestador)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 11: Integrar el motor en `GenericImportService` + DI

**Files:**
- Modify: `src/PortalSaas.Core/ImportacionGenerica/GenericImportService.cs`
- Modify: `src/PortalSaas.Host/Program.cs`

**Interfaces:**
- Consumes: `IGenericImportValidationRuleEngine.ApplyAsync` (Task 10).
- Produces: `GenericImportService.ProcessFileAsync` corre el motor antes de armar
  `GenericImportResultDto`.

- [ ] **Step 1: Sacar `BuiltInRules` y el `using` viejo**

En `GenericImportService.cs`, borrar el bloque (líneas 50-54 actuales):

```csharp
    private static readonly IReadOnlyList<IGenericImportValidationRule> BuiltInRules =
    [
        new PositiveQuantityRule(),
        new ValidDiscountPercentRule(),
    ];
```

Y en `ProcessRow` (cerca de la línea 970-973 actuales), borrar:

```csharp
        foreach (var rule in BuiltInRules)
        {
            errors.AddRange(rule.Validate(row));
        }
```

(Dejar `return row with { IsValid = errors.Count == 0, Errors = errors };` tal cual --
las 2 reglas fijas ahora corren desde el motor nuevo, no acá.)

Agregar el `using` del namespace nuevo al tope del archivo:

```csharp
using PortalSaas.Core.ImportacionGenerica.Reglas;
```

- [ ] **Step 2: Inyectar el motor en el constructor**

Agregar el campo y el parámetro de constructor (junto a `_progress`/`_logger`):

```csharp
    private readonly IGenericImportValidationRuleEngine _validationEngine;
```

```csharp
    public GenericImportService(IGenericImportConfigService config, IGenericImportUserFieldService userFieldsCatalog,
        IBusinessPartnerDefaultsService partnerDefaults, IItemCrossReferenceService crossReference, IItemCatalogService items,
        IWarehouseCatalogService warehouses, IGeneralLedgerAccountCatalogService accounts, ICostCenterCatalogService costCenters,
        IPriceListService priceList, ICustomerCatalogService customers, ISupplierCatalogService suppliers,
        ISalesDocumentService sales, IPurchaseDocumentService purchase, IInventoryDocumentService inventory,
        IGenericImportProgressStore progress, IGenericImportValidationRuleEngine validationEngine, ILogger<GenericImportService> logger)
    {
        // ... asignaciones existentes sin cambios ...
        _validationEngine = validationEngine;
        _logger = logger;
    }
```

- [ ] **Step 3: Correr el motor en `ProcessFileAsync`**

Reemplazar el bloque final de `ProcessFileAsync` (líneas 230-239 actuales):

```csharp
        var rows = rawRows.Select(r => ProcessRow(r, config, parameters.Module, parameters.LineType, parameters.BusinessPartnerCardCode,
            validBusinessPartners, crossReferenceBySocio, items, warehouses, accounts, costCenters, dimension2, dimension3,
            userFieldsCatalog, systemPrices)).ToList();

        rows = (await _validationEngine.ApplyAsync(rows, parameters.Module, config.ValidationRules, ct)).ToList();

        var documents = rows
            .GroupBy(r => r.GroupingKey)
            .Select(g => new GenericImportDocumentDto { GroupingKey = g.Key, Rows = g.ToList() })
            .ToList();

        return new GenericImportResultDto { HasValidConfig = true, Documents = documents };
```

- [ ] **Step 4: Compilar `PortalSaas.Core`**

Run: `dotnet build "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\src\PortalSaas.Core\PortalSaas.Core.csproj"`
Expected: 0 errores.

- [ ] **Step 5: Registrar todo en DI**

En `src/PortalSaas.Host/Program.cs`, agregar el `using` del namespace nuevo (junto a
los demás `using PortalSaas.Core...`), y después de la línea
`builder.Services.AddScoped<IGenericImportService, GenericImportService>();` (línea
253 actual):

```csharp
builder.Services.AddScoped<IGenericImportValidationRule, PositiveQuantityRule>();
builder.Services.AddScoped<IGenericImportValidationRule, ValidDiscountPercentRule>();
builder.Services.AddScoped<IGenericImportValidationRule, CustomerActiveInSapRule>();
builder.Services.AddScoped<IGenericImportValidationRule, ItemActiveInSapRule>();
builder.Services.AddScoped<IGenericImportValidationRule, PriceVsFixedListRule>();
builder.Services.AddScoped<IGenericImportValidationRule, PriceVsCustomerListRule>();
builder.Services.AddScoped<IGenericImportValidationRule, StockAvailableRule>();
builder.Services.AddScoped<IGenericImportValidationRule, CustomerBranchValidRule>();
builder.Services.AddScoped<IGenericImportValidationRuleEngine, GenericImportValidationRuleEngine>();
```

(El orden de registro no importa -- `GenericImportValidationRuleEngine` recibe las 8
vía `IEnumerable<IGenericImportValidationRule>`, ASP.NET Core DI resuelve todas las
registradas bajo esa interfaz automáticamente.)

- [ ] **Step 6: Compilar y correr TODOS los tests**

Run: `dotnet build "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\PortalSaas.sln"`
Expected: 0 errores/0 advertencias.

Run: `dotnet test "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\tests\PortalSaas.Core.Tests"`
Expected: todos los tests en verde (los preexistentes + los nuevos de esta feature).

- [ ] **Step 7: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Core/ImportacionGenerica/GenericImportService.cs" "Portal SaaS - Core/src/PortalSaas.Host/Program.cs"
git commit -m "$(cat <<'EOF'
feat(importacion-generica): integra el motor de reglas en ProcessFileAsync

Reemplaza el loop viejo de BuiltInRules -- las 2 reglas estructurales +
las 6 configurables corren desde GenericImportValidationRuleEngine, después
de que ProcessRow ya resolvió cada fila contra los catálogos SAP.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 12: `GenerateValidationReportAsync` (reporte .xlsx)

**Files:**
- Modify: `src/PortalSaas.Abstractions/Contratos/IGenericImportService.cs`
- Modify: `src/PortalSaas.Core/ImportacionGenerica/GenericImportService.cs`

**Interfaces:**
- Produces: `IGenericImportService.GenerateValidationReportAsync(GenericImportParametersDto parameters, IReadOnlyList<GenericImportDocumentDto> documents, CancellationToken ct = default) -> Task<byte[]>`.

Sin test dedicado (mismo criterio que `GenerateFileWithErrorsAsync`/`GenerateTemplateAsync`,
generación de Excel verificada manualmente/E2E, no con xUnit -- ningún método de esa
familia lo tiene hoy en este archivo).

- [ ] **Step 1: Agregar la firma a la interfaz**

En `IGenericImportService.cs`, después de `GenerateFileWithErrorsAsync`:

```csharp
    /// <summary>
    /// Reporte de validación PRE-carga -- distinto del reporte de resultado post-carga
    /// (ver docs/superpowers/specs/2026-09-06-reporte-resultado-importacion-generica-
    /// design.md). Se genera a partir de la vista previa (ProcessFileAsync), disponible
    /// sin haber confirmado nada -- hoja "Detalle" (una fila por línea, con Errores y
    /// Advertencias) + hoja "Stock por artículo-bodega" (una fila por cada par
    /// (ItemCode, Bodega) marcado por la regla StockAvailable).
    /// </summary>
    Task<byte[]> GenerateValidationReportAsync(GenericImportParametersDto parameters,
        IReadOnlyList<GenericImportDocumentDto> documents, CancellationToken ct = default);
```

- [ ] **Step 2: Implementar en `GenericImportService`**

Agregar después de `GenerateFileWithErrorsAsync`:

```csharp
    public async Task<byte[]> GenerateValidationReportAsync(GenericImportParametersDto parameters,
        IReadOnlyList<GenericImportDocumentDto> documents, CancellationToken ct = default)
    {
        var config = await _config.ResolveAsync(parameters.Module, parameters.DocumentType, parameters.LineType,
            parameters.BusinessPartnerCardCode, ct);
        var userFieldsCatalog = (await _userFieldsCatalog.ListAsync(parameters.Module, ct)).ToDictionary(f => f.Id);

        using var workbook = new XLWorkbook();

        var detalle = workbook.Worksheets.Add("Detalle");
        var mappedFields = config?.Fields.Where(f => !string.IsNullOrEmpty(f.ExcelColumn)).ToList() ?? [];
        foreach (var field in mappedFields)
        {
            var index = ColumnLetterToIndex(field.ExcelColumn!);
            detalle.Cell(1, index + 1).Value = FieldLabel(field, userFieldsCatalog);
        }
        var errorsColumnIndex = (mappedFields.Count > 0 ? mappedFields.Max(f => ColumnLetterToIndex(f.ExcelColumn!)) : -1) + 1;
        detalle.Cell(1, errorsColumnIndex + 1).Value = "Errores";
        detalle.Cell(1, errorsColumnIndex + 2).Value = "Advertencias";

        var allRows = documents.SelectMany(d => d.Rows).OrderBy(r => r.RowNumber).ToList();
        var rowIndex = 1;
        foreach (var row in allRows)
        {
            rowIndex++;
            foreach (var field in mappedFields)
            {
                var index = ColumnLetterToIndex(field.ExcelColumn!);
                detalle.Cell(rowIndex, index + 1).Value = FieldValue(row, field, userFieldsCatalog) ?? string.Empty;
            }
            detalle.Cell(rowIndex, errorsColumnIndex + 1).Value = string.Join("; ", row.Errors);
            detalle.Cell(rowIndex, errorsColumnIndex + 2).Value = string.Join("; ", row.Warnings);

            if (!row.IsValid)
            {
                detalle.Range(rowIndex, 1, rowIndex, errorsColumnIndex + 2).Style.Fill.BackgroundColor = XLColor.FromArgb(255, 214, 214);
            }
            else if (row.Warnings.Count > 0)
            {
                detalle.Range(rowIndex, 1, rowIndex, errorsColumnIndex + 2).Style.Fill.BackgroundColor = XLColor.FromArgb(255, 243, 205);
            }
        }
        detalle.Columns().AdjustToContents();

        var stockSheet = workbook.Worksheets.Add("Stock por artículo-bodega");
        stockSheet.Cell(1, 1).Value = "Artículo";
        stockSheet.Cell(1, 2).Value = "Bodega";
        stockSheet.Cell(1, 3).Value = "Advertencia";

        var stockWarningRows = allRows
            .SelectMany(r => r.Warnings.Where(w => w.StartsWith("Demanda total de")).Select(w => (r.ItemCode, r.Warehouse ?? r.SourceWarehouse, w)))
            .Distinct()
            .ToList();
        var stockRowIndex = 1;
        foreach (var (itemCode, warehouse, warning) in stockWarningRows)
        {
            stockRowIndex++;
            stockSheet.Cell(stockRowIndex, 1).Value = itemCode;
            stockSheet.Cell(stockRowIndex, 2).Value = warehouse;
            stockSheet.Cell(stockRowIndex, 3).Value = warning;
        }
        stockSheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
```

(`FieldLabel`/`FieldValue`/`ColumnLetterToIndex` ya existen como métodos privados en
el archivo -- reusados tal cual, sin cambios. El filtro `w.StartsWith("Demanda total de")`
sobre el texto del mensaje es frágil pero suficiente para esta primera entrega -- si
se necesita algo más robusto, `StockAvailableRule` podría exponer un DTO estructurado
en vez de solo texto; queda fuera de alcance de esta tarea, anotado para una iteración
futura si hace falta.)

- [ ] **Step 3: Compilar**

Run: `dotnet build "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\PortalSaas.sln"`
Expected: 0 errores.

- [ ] **Step 4: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IGenericImportService.cs" "Portal SaaS - Core/src/PortalSaas.Core/ImportacionGenerica/GenericImportService.cs"
git commit -m "$(cat <<'EOF'
feat(importacion-generica): GenerateValidationReportAsync (reporte pre-carga)

Hoja Detalle (Errores + Advertencias por línea) + hoja de stock por
artículo-bodega. Distinto del reporte de resultado post-carga (spec
2026-09-06) -- este corre sobre la vista previa, sin necesitar Confirmar.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 13: UI de configuración por Formato (`Pages/Configuracion`)

**Files:**
- Modify: `plugins/Modulo.ImportacionGenerica/Pages/Configuracion/Index.cshtml.cs`
- Modify: `plugins/Modulo.ImportacionGenerica/Pages/Configuracion/Index.cshtml`

**Interfaces:**
- Consumes: `IGenericImportConfigService.SaveValidationRulesAsync` (Task 3),
  `GenericImportValidationRuleAssignmentDto`/`GenericImportValidationRuleType`/`GenericImportValidationSeverity` (Task 1).

- [ ] **Step 1: Agregar el `InputModel` de reglas en el codebehind**

En `Index.cshtml.cs`, dentro de `InputModel` (junto a `UserFieldMappings`), agregar:

```csharp
        public List<ValidationRuleInput> ValidationRules { get; set; } = [];
```

Y una clase nueva al final del archivo (junto a `UserFieldMappingInput`):

```csharp
    public sealed class ValidationRuleInput
    {
        public GenericImportValidationRuleType RuleType { get; set; }
        public bool IsActive { get; set; }
        public GenericImportValidationSeverity Severity { get; set; } = GenericImportValidationSeverity.Warning;
        public int? PriceListNum { get; set; }
        public decimal? TolerancePercent { get; set; }
    }
```

- [ ] **Step 2: Poblar el catálogo fijo de 6 reglas configurables**

Agregar una propiedad de solo lectura con los 6 tipos, mismo criterio que
`Model.LineTypes`/`Model.PriceSources`:

```csharp
    public static readonly IReadOnlyList<GenericImportValidationRuleType> ConfigurableRuleTypes =
    [
        GenericImportValidationRuleType.CustomerActiveInSap,
        GenericImportValidationRuleType.ItemActiveInSap,
        GenericImportValidationRuleType.PriceVsFixedList,
        GenericImportValidationRuleType.PriceVsCustomerList,
        GenericImportValidationRuleType.StockAvailable,
        GenericImportValidationRuleType.CustomerBranchValid,
    ];
```

- [ ] **Step 3: Cargar/guardar en `OnGetAsync`/`OnPostSaveAsync`**

En `MapToInput` (donde ya se arma `Input.Fields`/`Input.UserFieldMappings` desde un
`GenericImportConfigDto` existente), agregar:

```csharp
        input.ValidationRules = ConfigurableRuleTypes.Select(type =>
        {
            var existing = config.ValidationRules.FirstOrDefault(r => r.RuleType == type);
            return new ValidationRuleInput
            {
                RuleType = type,
                IsActive = existing?.IsActive ?? false,
                Severity = existing?.Severity ?? GenericImportValidationSeverity.Warning,
                PriceListNum = existing?.Parameters.TryGetValue("priceListNum", out var pl) == true ? Convert.ToInt32(pl) : null,
                TolerancePercent = existing?.Parameters.TryGetValue("tolerancePercent", out var tp) == true ? Convert.ToDecimal(tp) : null,
            };
        }).ToList();
```

Y cuando `Input.ValidationRules.Count == 0` (nueva configuración sin cargar todavía,
mismo criterio que `LoadUserFieldOptionsAsync` con `Input.Fields`), poblarla con el
mismo bloque pero sin `existing` (todas `IsActive = false`).

En `OnPostSaveAsync`, después de guardar `fields`/antes del `return RedirectToPage()`:

```csharp
            var validationRules = Input.ValidationRules.Where(r => r.IsActive).Select(r =>
            {
                var parameters = new Dictionary<string, object?>();
                if (r.RuleType is GenericImportValidationRuleType.PriceVsFixedList)
                {
                    parameters["priceListNum"] = r.PriceListNum;
                }
                if (r.RuleType is GenericImportValidationRuleType.PriceVsFixedList or GenericImportValidationRuleType.PriceVsCustomerList or GenericImportValidationRuleType.StockAvailable)
                {
                    parameters["tolerancePercent"] = r.TolerancePercent ?? 0m;
                }
                return new GenericImportValidationRuleAssignmentDto(0, r.RuleType, r.Severity, true, parameters);
            }).ToList();

            var configId = EditId ?? /* el id recién creado por CreateAsync, ver más abajo */;
            await _configs.SaveValidationRulesAsync(configId, validationRules, ct);
```

(Ajustar el punto exacto de inserción leyendo el `OnPostSaveAsync` real antes de
tocarlo -- el `configId` para el caso "creación nueva" sale del valor devuelto por
`_configs.CreateAsync(...)`, ya capturado en una variable local en el código existente
-- reusar esa variable en vez de re-declarar `configId`.)

- [ ] **Step 4: Sección "Reglas de validación" en el `.cshtml`**

Después de la tabla de "Campos de usuario" (antes del botón "Crear"/"Guardar" final),
agregar:

```html
        <h5 class="mt-4">Reglas de validación</h5>
        <p class="text-muted small">Corren antes de confirmar la carga a SAP -- nunca bloquean si están en modo Alerta.</p>
        <table class="table table-sm">
            <thead>
                <tr>
                    <th>Regla</th>
                    <th>Activa</th>
                    <th>Severidad</th>
                    <th>Parámetros</th>
                </tr>
            </thead>
            <tbody>
                @for (var i = 0; i < Model.Input.ValidationRules.Count; i++)
                {
                    var rule = Model.Input.ValidationRules[i];
                    <tr>
                        <td>
                            @RuleLabel(rule.RuleType)
                            <input type="hidden" name="Input.ValidationRules[@i].RuleType" value="@rule.RuleType" />
                        </td>
                        <td>
                            <input type="checkbox" name="Input.ValidationRules[@i].IsActive" value="true" class="form-check-input" checked="@rule.IsActive" data-toggle-params="params-@i" />
                            <input type="hidden" name="Input.ValidationRules[@i].IsActive" value="false" />
                        </td>
                        <td>
                            <select name="Input.ValidationRules[@i].Severity" class="form-select form-select-sm">
                                <option value="@GenericImportValidationSeverity.Warning" selected="@(rule.Severity == GenericImportValidationSeverity.Warning)">Alerta</option>
                                <option value="@GenericImportValidationSeverity.Block" selected="@(rule.Severity == GenericImportValidationSeverity.Block)">Bloqueante</option>
                            </select>
                        </td>
                        <td id="params-@i" style="@(rule.IsActive ? "" : "display:none")">
                            @if (rule.RuleType == PortalSaas.Abstractions.Modelos.GenericImportValidationRuleType.PriceVsFixedList)
                            {
                                <label class="form-label small">N° Lista de precio</label>
                                <input type="number" name="Input.ValidationRules[@i].PriceListNum" value="@rule.PriceListNum" class="form-control form-control-sm" style="max-width:100px" />
                            }
                            @if (rule.RuleType is PortalSaas.Abstractions.Modelos.GenericImportValidationRuleType.PriceVsFixedList
                                or PortalSaas.Abstractions.Modelos.GenericImportValidationRuleType.PriceVsCustomerList
                                or PortalSaas.Abstractions.Modelos.GenericImportValidationRuleType.StockAvailable)
                            {
                                <label class="form-label small">Tolerancia %</label>
                                <input type="number" step="0.01" name="Input.ValidationRules[@i].TolerancePercent" value="@rule.TolerancePercent" class="form-control form-control-sm" style="max-width:100px" />
                            }
                        </td>
                    </tr>
                }
            </tbody>
        </table>
```

(El primer `@if (rule.RuleType == GenericImportLogicalField.PriceVsFixedList) { }` de
arriba es un error de copiado -- BORRARLO, quedó de un tipo equivocado (`GenericImportLogicalField`
no `GenericImportValidationRuleType`); dejar solo los dos bloques `@if` correctos que
le siguen.)

Agregar el helper `RuleLabel` como función local al final del `.cshtml` (mismo patrón
que cualquier `@functions` ya usado en otras páginas del plugin, o simplemente un
`switch` inline si el proyecto no usa `@functions` en este archivo -- confirmar
mirando el resto del `.cshtml` antes de elegir):

```csharp
@functions {
    string RuleLabel(PortalSaas.Abstractions.Modelos.GenericImportValidationRuleType type) => type switch
    {
        PortalSaas.Abstractions.Modelos.GenericImportValidationRuleType.CustomerActiveInSap => "Cliente/Proveedor activo en SAP",
        PortalSaas.Abstractions.Modelos.GenericImportValidationRuleType.ItemActiveInSap => "Artículo activo en SAP",
        PortalSaas.Abstractions.Modelos.GenericImportValidationRuleType.PriceVsFixedList => "Precio vs. lista fija",
        PortalSaas.Abstractions.Modelos.GenericImportValidationRuleType.PriceVsCustomerList => "Precio vs. lista del cliente",
        PortalSaas.Abstractions.Modelos.GenericImportValidationRuleType.StockAvailable => "Stock disponible (agregado del lote)",
        PortalSaas.Abstractions.Modelos.GenericImportValidationRuleType.CustomerBranchValid => "Sucursal pertenece al cliente",
        _ => type.ToString(),
    };
}
```

Y el JS de toggle (junto al resto de `<script>` de la página):

```html
<script>
    document.querySelectorAll('input[data-toggle-params]').forEach(function (checkbox) {
        checkbox.addEventListener('change', function () {
            document.getElementById(checkbox.dataset.toggleParams).style.display = checkbox.checked ? '' : 'none';
        });
    });
</script>
```

La validación "no dos reglas de precio activas a la vez" ya la hace
`SaveValidationRulesAsync` server-side (Task 3, `InvalidOperationException` ->
`ErrorMessage` visible en la página vía el `catch` que ya tiene `OnPostSaveAsync`) --
sin duplicarla en JS, un intento inválido simplemente recarga con el mensaje de error
real.

- [ ] **Step 5: Compilar y probar manualmente**

Run: `dotnet build "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\PortalSaas.sln"`
Expected: 0 errores.

Levantar el Host (`build-all.ps1` o `dotnet run --project src/PortalSaas.Host`),
entrar a `/importacion-generica/configuracion`, editar una configuración existente,
activar "Stock disponible" en Alerta, guardar, reabrir la edición y confirmar que
quedó activa y con la severidad correcta.

- [ ] **Step 6: Commit**

```bash
git add "Portal SaaS - Core/plugins/Modulo.ImportacionGenerica/Pages/Configuracion"
git commit -m "$(cat <<'EOF'
feat(importacion-generica): UI de configuración de reglas de validación por Formato

Sección nueva en Configuración de Importación Genérica -- 6 reglas
configurables (activa/severidad/parámetros), las 2 estructurales no
aparecen acá (siempre activas).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 14: UI de vista previa (advertencias) + reporte descargable

**Files:**
- Modify: `plugins/Modulo.ImportacionGenerica/Pages/Importar/Index.cshtml.cs`
- Modify: `plugins/Modulo.ImportacionGenerica/Pages/Importar/Index.cshtml`

**Interfaces:**
- Consumes: `GenericImportRowDto.Warnings` (Task 1), `IGenericImportService.GenerateValidationReportAsync` (Task 12).

- [ ] **Step 1: Handler de descarga en el codebehind**

En `Index.cshtml.cs`, después de `OnPostDownloadWithErrorsAsync`:

```csharp
    public async Task<IActionResult> OnPostDownloadValidationReportAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(Input.Base64File))
        {
            ModelState.AddModelError(string.Empty, "Volvé a procesar el archivo antes de descargar el reporte.");
            await LoadCreatableDocumentTypesAsync(ct);
            await ResolveBusinessPartnerFromFileAsync(ct);
            return Page();
        }

        var parameters = BuildParameters();
        var bytes = Convert.FromBase64String(Input.Base64File);
        using var stream = new MemoryStream(bytes);
        var preview = await _importService.ProcessFileAsync(parameters, stream, ct);

        var fileBytes = await _importService.GenerateValidationReportAsync(parameters, preview.Documents, ct);
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ReporteValidacion_{Input.Module}_{parameters.DocumentType}.xlsx");
    }
```

- [ ] **Step 2: Botón + form en el `.cshtml`**

Buscar el bloque `@if (Model.PreviewResult.Documents.Any(d => !d.CanCreate))` (donde
vive el form de "Descargar con errores") y agregar, ANTES de ese `@if` (el reporte de
validación aplica siempre que haya vista previa, no solo cuando hay errores):

```html
<form method="post" asp-page-handler="DownloadValidationReport" asp-antiforgery="true" class="mt-2 d-inline-block">
    <input type="hidden" asp-for="Input.Module" />
    <input type="hidden" asp-for="Input.SalesDocumentType" />
    <input type="hidden" asp-for="Input.PurchaseDocumentType" />
    <input type="hidden" asp-for="Input.InventoryDocumentType" />
    <input type="hidden" asp-for="Input.LineType" />
    <input type="hidden" asp-for="Input.BusinessPartnerCardCode" />
    <input type="hidden" asp-for="Input.Base64File" />
    <button type="submit" class="btn btn-outline-secondary">Descargar reporte de validación</button>
</form>
```

(Copiar exactamente el set de hidden fields del form de "DownloadWithErrors" ya
existente en el archivo -- deben coincidir con `BuildParameters()`.)

- [ ] **Step 3: Columna de advertencias en la tabla de detalle**

En la tabla de cada documento (`<table class="table table-sm mt-2">`, con columnas
`Fila | Artículo | Cantidad | Errores`), agregar una columna `Advertencias` después de
`Errores`:

```html
                            <tr>
                                <th>Fila</th>
                                <th>Artículo</th>
                                <th>Cantidad</th>
                                <th>Errores</th>
                                <th>Advertencias</th>
                            </tr>
```

Y en el `@foreach (var row in document.Rows)`, agregar la celda correspondiente
(buscar la celda de Errores existente, ej. `<td>@string.Join("; ", row.Errors)</td>`,
y agregar justo después):

```html
                                    <td class="text-warning">@string.Join("; ", row.Warnings)</td>
```

- [ ] **Step 4: Contador de advertencias en el resumen del documento**

En el `<summary>` de cada `<details>`, después del conteo de filas/errores existente,
agregar (mismo patrón que el resto de esa línea con `@if`):

```html
                        @{
                            var advertenciasCount = document.Rows.Sum(r => r.Warnings.Count);
                        }
                        @if (advertenciasCount > 0)
                        {
                            <text> · @advertenciasCount advertencia(s)</text>
                        }
```

- [ ] **Step 5: Compilar y probar manualmente**

Run: `dotnet build "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\PortalSaas.sln"`
Expected: 0 errores.

Con el Host levantado: activar `StockAvailable` en Alerta para un Formato real,
procesar un archivo cuya demanda supere el disponible, confirmar que la vista previa
muestra la advertencia en ámbar (no bloquea "Confirmar creación en SAP"), y que
"Descargar reporte de validación" baja un `.xlsx` con las 2 hojas.

- [ ] **Step 6: Commit**

```bash
git add "Portal SaaS - Core/plugins/Modulo.ImportacionGenerica/Pages/Importar"
git commit -m "$(cat <<'EOF'
feat(importacion-generica): advertencias en vista previa + reporte de validación descargable

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 15: Verificación final

**Files:** ninguno nuevo -- solo verificación end-to-end.

- [ ] **Step 1: Build completo**

Run: `dotnet build "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\PortalSaas.sln"`
Expected: 0 advertencias / 0 errores.

- [ ] **Step 2: Suite completa de tests**

Run: `dotnet test "C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Core\tests\PortalSaas.Core.Tests"`
Expected: todos en verde (preexistentes + los ~19 nuevos de esta feature).

- [ ] **Step 3: Recompilar y relevantar con `build-all.ps1`**

Run (PowerShell): `& "C:\PROYECTOS\Proyecto Portal Web-Company\build-all.ps1"`
Expected: detiene procesos viejos, recompila plugins externos + Core, levanta Central
(6001) y Comercial Depor (6002), ambos respondiendo OK.

- [ ] **Step 4: E2E contra SAP real -- `StockAvailable`**

En Comercial Depor (6002): en `/importacion-generica/configuracion`, activar
`StockAvailable` en modo Alerta para un Formato de Venta real. Armar (o usar) un
archivo cuya demanda agregada de un artículo en una bodega real supere el disponible.
Procesar, confirmar que aparece la advertencia (ámbar, no bloquea), confirmar que
"Confirmar creación en SAP" igual crea el documento, y que el reporte descargable
trae el detalle correcto en la hoja "Stock por artículo-bodega".

- [ ] **Step 5: E2E contra SAP real -- `PriceVsCustomerList`**

Activar `PriceVsCustomerList` para el mismo Formato, con un cliente real que tenga
lista de precio asignada distinta al precio del archivo de prueba. Confirmar que la
advertencia aparece con el precio esperado correcto.

- [ ] **Step 6: `graphify update .`**

Run: `graphify update .` (desde la raíz del repo, actualiza el grafo de conocimiento
con los archivos nuevos/modificados de esta entrega).

- [ ] **Step 7: Commit final (si `graphify update` generó cambios)**

```bash
git add "Portal SaaS - Core/graphify-out"
git commit -m "$(cat <<'EOF'
chore: actualiza graphify tras el motor de reglas de validación de importación genérica

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
```
