# Batching real + reconciliación de resultado (Subida SAP→WMS) - Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `WmsCloudConnector`'s one-XML-per-record sends with real batching (configurable batch size), and add a reconciliation mechanism (two background services querying Oracle's LGFAPI) that resolves each record's real final status, since a successful `init_stage_interface` POST only confirms receipt, not business validation.

**Architecture:** A new small contract in `PortalSaas.Abstractions` (`IIntegrationConnectorConfigService`) lets the `Modulo.Wms` plugin read its own `IntegrationDefinition` config (implemented in `PortalSaas.Integrations`, which already references `PortalSaasDbContext`) without violating the plugin-isolation rule. `WmsCloudConnector` batches sends and moves successfully-POSTed records to a new intermediate `Enviado` status instead of `ProcesadoWms`. Two new `BackgroundService`s in `Modulo.Wms` poll Oracle's LGFAPI REST endpoint per record and resolve `Enviado` → `ProcesadoWms`/`ErrorWms`, recording results in a new `wms_oracle_export_validations` table.

**Tech Stack:** .NET 8, ASP.NET Core Razor Pages, EF Core 8 (dual engine: Npgsql + SqlServer), xUnit + fakes (no EF InMemory for DI-dependent background services, per the `WmsSlshStageParser` precedent in this repo).

## Global Constraints

- Plugin references only `PortalSaas.Abstractions`, never `PortalSaas.Core`/`PortalSaas.Data`/`PortalSaas.Host` directly — the new `IIntegrationConnectorConfigService` contract exists specifically so `Modulo.Wms` never needs a direct reference to `PortalSaasDbContext`.
- `CompanyId` always mandatory, no Organization-level fallback — `wms_oracle_export_validations` and every new query filters by it.
- DB naming convention: snake_case, plural or descriptive singular table names matching existing `wms_sap_stage_*` style, `id`/`company_id` columns.
- Any `BackgroundService` resolving `WmsDbContext` (or any plugin-scoped service depending on `ICurrentCompanyAccessor`) MUST set `ICurrentCompanyOverride` in its own DI scope before resolving that service — see `WmsSlshStageParser.cs` (already fixed this exact bug this session) as the reference pattern. Do not repeat that bug.
- Migrations: every new/changed table needs a migration in BOTH `Modulo.Wms.Migrations.Postgres` and `Modulo.Wms.Migrations.SqlServer`, generated via real `dotnet ef migrations add` (never hand-written) — see this session's precedent in the Rendiciones plan for why hand-written migrations are unacceptable.
- Stop any locally-running `PortalSaas.Host` process before rebuilding (`PublicarComoPlugin`/`AfterTargets="Build"` MSBuild targets fail with MSB3027 file-lock otherwise) — ask the user to confirm before any build step.
- `WmsSapStageStatus` is stored via `.HasConversion<string>().HasMaxLength(20)` in `WmsDbContext` (see `WmsSapStageItem`'s mapping) — adding the new `Enviado` value needs NO migration (the column is already a string, "Enviado" fits in 20 chars); only the new table (Task 4) needs migrations.
- No response-body parsing changes to `WmsCloudConnector.EnviarAsync`'s existing 2xx/non-2xx check — this plan does not change how transport-level failures are detected, only what happens to a record once transport succeeds.

---

## File Structure

New files:
- `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IIntegrationConnectorConfigService.cs`
- `Portal SaaS - Core/src/PortalSaas.Integrations/IntegrationConnectorConfigService.cs`
- `Portal SaaS - Core/tests/PortalSaas.Core.Tests/Integrations/IntegrationConnectorConfigServiceTests.cs`
- `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsExportValidation.cs`
- `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/IWmsValidationApiClient.cs`
- `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsValidationApiClient.cs`
- `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsStageErrorReconciler.cs`
- `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsExistsReconciler.cs`
- `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsValidationApiClientTests.cs`
- `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsStageErrorReconcilerTests.cs`
- `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsExistsReconcilerTests.cs`
- Migration files under both `Modulo.Wms.Migrations.Postgres/Migrations/` and `Modulo.Wms.Migrations.SqlServer/Migrations/` (generated, not hand-written).

Modified files:
- `Portal SaaS - Core/src/PortalSaas.Host/Program.cs` — register `IIntegrationConnectorConfigService`.
- `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsSapStageItem.cs` — `WmsSapStageStatus` gains `Enviado`.
- `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSapStageItemReader.cs`, `WmsSapStageStoreReader.cs`, `WmsSapStageOrderReader.cs`, `WmsSapStageInboundReader.cs` — success now sets `Enviado`, not `ProcesadoWms`.
- `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsCloudConnector.cs` — batching via `Chunk(config.BatchSize)`, `WmsCloudConfig` gains `BatchSize`/`LgfApiBaseUrl`.
- `Portal SaaS - Core/src/PortalSaas.Host/Pages/Admin/Integraciones/Nuevo.cshtml(.cs)` — expose `BatchSize`/`LgfApiBaseUrl` in the WmsCloud config block.
- `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Data/WmsDbContext.cs` — `DbSet<WmsExportValidation>` + mapping.
- `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs` — register `IWmsValidationApiClient`, `WmsStageErrorReconciler`, `WmsExistsReconciler`.
- `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsCloudConnectorTests.cs` — batching test, update existing assertions for the `Enviado` status where relevant (none of the existing tests assert on `Status`, only on HTTP payload — confirm no change needed beyond what Task 3 specifies).

---

### Task 1: `IIntegrationConnectorConfigService` contract + implementation

**Files:**
- Create: `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IIntegrationConnectorConfigService.cs`
- Create: `Portal SaaS - Core/src/PortalSaas.Integrations/IntegrationConnectorConfigService.cs`
- Modify: `Portal SaaS - Core/src/PortalSaas.Host/Program.cs`
- Test: `Portal SaaS - Core/tests/PortalSaas.Core.Tests/Integrations/IntegrationConnectorConfigServiceTests.cs`

**Interfaces:**
- Produces: `IIntegrationConnectorConfigService.GetDecryptedConfigAsync(Guid companyId, string moduloOrigen, string conectorTipo, CancellationToken ct = default)` returning `Task<string?>` (decrypted JSON config, or null if no active matching `IntegrationDefinition` exists) — consumed by Task 6 and Task 7.

- [ ] **Step 1: Write the failing test**

Create `Portal SaaS - Core/tests/PortalSaas.Core.Tests/Integrations/IntegrationConnectorConfigServiceTests.cs`. Follow the EF Core InMemory `PortalSaasDbContext` setup pattern already used elsewhere in this test project (check any existing test file in `tests/PortalSaas.Core.Tests/` for the exact `DbContextOptionsBuilder<PortalSaasDbContext>().UseInMemoryDatabase(...)` incantation if one exists; otherwise use this directly):

```csharp
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities.Integraciones;
using PortalSaas.Integrations;
using Xunit;

namespace PortalSaas.Core.Tests.Integrations;

public class IntegrationConnectorConfigServiceTests
{
    private sealed class FakeSecretoCifradoService : PortalSaas.Abstractions.Contratos.ISecretoCifradoService
    {
        public string Encrypt(string plainText) => $"ENC[{plainText}]";
        public string Decrypt(string cipherText) => cipherText.Replace("ENC[", "").TrimEnd(']');
    }

    private static PortalSaasDbContext CrearContexto(string dbName)
    {
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new PortalSaasDbContext(options);
    }

    [Fact]
    public async Task GetDecryptedConfigAsync_ConDefinicionActivaQueMatchea_DevuelveConfigDescifrada()
    {
        var companyId = Guid.NewGuid();
        await using var db = CrearContexto(nameof(GetDecryptedConfigAsync_ConDefinicionActivaQueMatchea_DevuelveConfigDescifrada));
        var secreto = new FakeSecretoCifradoService();

        db.IntegrationDefinitions.Add(new IntegrationDefinition
        {
            CompanyId = companyId,
            Nombre = "WMS - Items (Subida WMS)",
            ModuloOrigen = "Wms",
            EntidadNegocio = "SapWms.Item.Subida",
            ConectorTipo = IntegrationConectorTipo.WmsCloud,
            ConectorConfigCifrado = secreto.Encrypt("""{"ApiUrl":"https://x"}"""),
            Direccion = IntegrationDireccion.Subida,
            Activo = true,
        });
        await db.SaveChangesAsync();

        var sut = new IntegrationConnectorConfigService(db, secreto);
        var resultado = await sut.GetDecryptedConfigAsync(companyId, "Wms", "WmsCloud", CancellationToken.None);

        Assert.Equal("""{"ApiUrl":"https://x"}""", resultado);
    }

    [Fact]
    public async Task GetDecryptedConfigAsync_SinDefinicionActivaQueMatchee_DevuelveNull()
    {
        var companyId = Guid.NewGuid();
        await using var db = CrearContexto(nameof(GetDecryptedConfigAsync_SinDefinicionActivaQueMatchee_DevuelveNull));
        var secreto = new FakeSecretoCifradoService();

        db.IntegrationDefinitions.Add(new IntegrationDefinition
        {
            CompanyId = companyId,
            Nombre = "WMS - Items (Subida WMS)",
            ModuloOrigen = "Wms",
            EntidadNegocio = "SapWms.Item.Subida",
            ConectorTipo = IntegrationConectorTipo.WmsCloud,
            ConectorConfigCifrado = secreto.Encrypt("{}"),
            Direccion = IntegrationDireccion.Subida,
            Activo = false, // inactiva -- no debe matchear
        });
        await db.SaveChangesAsync();

        var sut = new IntegrationConnectorConfigService(db, secreto);
        var resultado = await sut.GetDecryptedConfigAsync(companyId, "Wms", "WmsCloud", CancellationToken.None);

        Assert.Null(resultado);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter IntegrationConnectorConfigServiceTests`
Expected: FAIL to compile — `IntegrationConnectorConfigService`/`IIntegrationConnectorConfigService` don't exist yet.

- [ ] **Step 3: Create the contract**

Create `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IIntegrationConnectorConfigService.cs`:

```csharp
namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Permite a un plugin leer la config (ya descifrada) de una IntegrationDefinition
/// activa sin referenciar PortalSaas.Core/PortalSaas.Data directamente -- regla dura
/// del proyecto. Pensado para BackgroundService de un plugin que necesitan la config
/// de un conector (ej. credenciales de un endpoint externo) fuera de un ciclo del
/// motor de integración genérico (que ya resuelve esto internamente vía
/// IntegrationSyncHostedService, sin necesitar este contrato).
/// </summary>
public interface IIntegrationConnectorConfigService
{
    /// <summary>Config JSON descifrada de la primera IntegrationDefinition activa que
    /// matchea (companyId, moduloOrigen, conectorTipo) -- null si no hay ninguna.</summary>
    Task<string?> GetDecryptedConfigAsync(Guid companyId, string moduloOrigen, string conectorTipo, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement it**

Create `Portal SaaS - Core/src/PortalSaas.Integrations/IntegrationConnectorConfigService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities.Integraciones;

namespace PortalSaas.Integrations;

public class IntegrationConnectorConfigService : IIntegrationConnectorConfigService
{
    private readonly PortalSaasDbContext _contexto;
    private readonly ISecretoCifradoService _secretoCifradoService;

    public IntegrationConnectorConfigService(PortalSaasDbContext contexto, ISecretoCifradoService secretoCifradoService)
    {
        _contexto = contexto;
        _secretoCifradoService = secretoCifradoService;
    }

    public async Task<string?> GetDecryptedConfigAsync(Guid companyId, string moduloOrigen, string conectorTipo, CancellationToken ct = default)
    {
        if (!Enum.TryParse<IntegrationConectorTipo>(conectorTipo, out var tipoEnum))
        {
            return null;
        }

        var definicion = await _contexto.IntegrationDefinitions
            .Where(d => d.CompanyId == companyId && d.ModuloOrigen == moduloOrigen && d.ConectorTipo == tipoEnum && d.Activo)
            .FirstOrDefaultAsync(ct);

        if (definicion is null || string.IsNullOrEmpty(definicion.ConectorConfigCifrado))
        {
            return null;
        }

        return _secretoCifradoService.Decrypt(definicion.ConectorConfigCifrado);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter IntegrationConnectorConfigServiceTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Register in DI**

Edit `Portal SaaS - Core/src/PortalSaas.Host/Program.cs`, next to the existing `builder.Services.AddScoped<IIntegrationFieldMappingService, IntegrationFieldMappingService>();` line, add:

```csharp
builder.Services.AddScoped<IIntegrationConnectorConfigService, IntegrationConnectorConfigService>();
```

- [ ] **Step 7: Build to verify**

Ask the user to confirm the local `PortalSaas.Host` is stopped, then:
```bash
dotnet build "Portal SaaS - Core/src/PortalSaas.Host/PortalSaas.Host.csproj" -c Release
```
Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add "src/PortalSaas.Abstractions/Contratos/IIntegrationConnectorConfigService.cs" "src/PortalSaas.Integrations/IntegrationConnectorConfigService.cs" "src/PortalSaas.Host/Program.cs" "tests/PortalSaas.Core.Tests/Integrations/IntegrationConnectorConfigServiceTests.cs"
git commit -m "feat: IIntegrationConnectorConfigService -- config de conector sin cruzar la frontera plugin/Core"
```

---

### Task 2: `WmsSapStageStatus` gana `Enviado`; los 4 readers de Subida lo usan

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsSapStageItem.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSapStageItemReader.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSapStageStoreReader.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSapStageOrderReader.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSapStageInboundReader.cs`
- Test: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsSapStageItemReaderTests.cs` (existing file, extend)

**Interfaces:**
- Produces: `WmsSapStageStatus { Pendiente, Enviado, ProcesadoWms, ErrorWms }` — consumed by Task 3 (connector's batching no longer decides final status), Task 6, Task 7 (reconcilers query rows with `Status == Enviado`).

- [ ] **Step 1: Write the failing test**

Open `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsSapStageItemReaderTests.cs` and add a test confirming success now sets `Enviado`, not `ProcesadoWms`:

```csharp
[Fact]
public async Task MarcarProcesadoAsync_Exito_DejaLaFilaEnEnviado_NoEnProcesadoWms()
{
    var contexto = CrearContexto();
    var companyId = Guid.NewGuid();
    var fila = new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Pendiente };
    contexto.WmsSapStageItems.Add(fila);
    await contexto.SaveChangesAsync();

    var reader = new WmsSapStageItemReader(contexto);
    var registro = (await reader.LeerPendientesAsync(companyId, CancellationToken.None)).Single();

    await reader.MarcarProcesadoAsync(companyId, registro, exito: true, mensajeError: null, CancellationToken.None);

    var actualizada = await contexto.WmsSapStageItems.SingleAsync();
    Assert.Equal(WmsSapStageStatus.Enviado, actualizada.Status);
}
```

Also update (do NOT delete) the existing test `MarcarProcesadoAsync_Exito_ActualizaStatusYSyncedAt` in the same file — its assertion `Assert.Equal(WmsSapStageStatus.ProcesadoWms, actualizada.Status);` must change to `Assert.Equal(WmsSapStageStatus.Enviado, actualizada.Status);` (the new test above is otherwise a near-duplicate — keep only one; rename the existing test to `MarcarProcesadoAsync_Exito_DejaLaFilaEnEnviado_NoEnProcesadoWms` and update its assertion in place instead of adding a second test, to avoid duplication. Do NOT add both.).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsSapStageItemReaderTests`
Expected: FAIL — actual status is still `ProcesadoWms`.

- [ ] **Step 3: Add `Enviado` to the enum**

Edit `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsSapStageItem.cs`:

```csharp
public enum WmsSapStageStatus { Pendiente, Enviado, ProcesadoWms, ErrorWms }
```

- [ ] **Step 4: Update the 4 readers**

In each of `WmsSapStageItemReader.cs`, `WmsSapStageStoreReader.cs`, `WmsSapStageOrderReader.cs`, `WmsSapStageInboundReader.cs`, inside `MarcarProcesadoAsync`, change:

```csharp
fila.Status = exito ? WmsSapStageStatus.ProcesadoWms : WmsSapStageStatus.ErrorWms;
```
to:
```csharp
fila.Status = exito ? WmsSapStageStatus.Enviado : WmsSapStageStatus.ErrorWms;
```

`ErrorMsg`/`SyncedAt` assignment lines directly below stay unchanged in all 4 files — `SyncedAt` still only gets set on `exito: true` (it now means "sent successfully", the reconcilers in Task 6/7 will further update it or leave it, per those tasks).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests"`
Expected: ALL tests pass (this changes shared enum/behavior — run the full suite, not just the filtered one, to catch any other test asserting `ProcesadoWms` after a successful `MarcarProcesadoAsync` call in the Store/Order/Inbound reader test files too — if any exist, update them the same way as Step 1).

- [ ] **Step 6: Build to verify**

```bash
dotnet build "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Modulo.Wms.csproj" -c Release
```
Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/Modulo.Wms/Models/WmsSapStageItem.cs src/Modulo.Wms/Services/WmsSapStage*Reader.cs tests/Modulo.Wms.Tests/Services/*ReaderTests.cs
git commit -m "feat: WmsSapStageStatus.Enviado -- exito de envio ya no implica ProcesadoWms"
```

---

### Task 3: Batching real en `WmsCloudConnector` + config `BatchSize`/`LgfApiBaseUrl`

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsCloudConnector.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsCloudConnectorTests.cs`
- Modify: `Portal SaaS - Core/src/PortalSaas.Host/Pages/Admin/Integraciones/Nuevo.cshtml`
- Modify: `Portal SaaS - Core/src/PortalSaas.Host/Pages/Admin/Integraciones/Nuevo.cshtml.cs`

**Interfaces:**
- Consumes: nothing new from earlier tasks.
- Produces: `WmsCloudConnector.WmsCloudConfig` gains `int BatchSize = 50` and `string? LgfApiBaseUrl = null` — consumed by Task 6/7 (which read `LgfApiBaseUrl` via `IIntegrationConnectorConfigService`, deserializing the same JSON shape).

- [ ] **Step 1: Write the failing test**

Add to `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsCloudConnectorTests.cs`:

```csharp
[Fact]
public async Task PushAsync_ConVariosRegistrosYBatchSizeMenorQueLaCantidad_HaceVariosPosts()
{
    var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK);
    var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
    var conector = CrearConector(httpClient);

    var registros = Enumerable.Range(1, 5).Select(i => new IntegrationRecord(new Dictionary<string, object?>
    {
        ["TipoDocumento"] = "Item",
        ["ItemCode"] = $"ITM{i:000}",
        ["ItemName"] = $"Articulo {i}",
        ["BarCode"] = "780000000000" + i,
    })).ToList();

    var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01","BatchSize":2}""";
    var resultado = await conector.PushAsync(config, registros, CancellationToken.None);

    Assert.Equal(5, resultado.Count);
    Assert.All(resultado, r => Assert.True(r.Exito));
    // BatchSize=2 sobre 5 registros -> 3 POSTs (2+2+1). El handler falso solo guarda
    // el ULTIMO request/contenido, así que se cuenta a través de un contador propio.
    Assert.Equal(3, handlerFalso.CantidadDeRequests);
}

[Fact]
public async Task PushAsync_ConVariosRegistrosYBatchSizeSuficiente_HaceUnSoloPostConVariosNodos()
{
    var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK);
    var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
    var conector = CrearConector(httpClient);

    var registros = Enumerable.Range(1, 3).Select(i => new IntegrationRecord(new Dictionary<string, object?>
    {
        ["TipoDocumento"] = "Item",
        ["ItemCode"] = $"ITM{i:000}",
        ["ItemName"] = $"Articulo {i}",
        ["BarCode"] = "780000000000" + i,
    })).ToList();

    var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01","BatchSize":50}""";
    var resultado = await conector.PushAsync(config, registros, CancellationToken.None);

    Assert.Equal(3, resultado.Count);
    Assert.Equal(1, handlerFalso.CantidadDeRequests);
    var xmlDecodificado = Uri.UnescapeDataString(handlerFalso.UltimoContenido!.Replace('+', ' '));
    Assert.Contains("ITM001", xmlDecodificado);
    Assert.Contains("ITM002", xmlDecodificado);
    Assert.Contains("ITM003", xmlDecodificado);
}
```

Also add a `CantidadDeRequests` counter to `HttpHandlerFalso` in the same file (increment it inside `SendAsync`, next to where `UltimaRequest`/`UltimoContenido` are set):

```csharp
public int CantidadDeRequests { get; private set; }
```
and inside `SendAsync`, add `CantidadDeRequests++;` as its first line.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsCloudConnectorTests`
Expected: FAIL — today `CantidadDeRequests` would be 5 (one per record) for the first test, not 3; and the second test's single-POST XML wouldn't contain all 3 item codes together (or `BatchSize` isn't a recognized config field yet, causing a JSON deserialization no-op since `record` types ignore unknown properties by default — the count assertions are what actually fail).

- [ ] **Step 3: Add `BatchSize`/`LgfApiBaseUrl` to the config record**

Edit `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsCloudConnector.cs`, the `WmsCloudConfig` record:

```csharp
private sealed record WmsCloudConfig(string ApiUrl, string Usuario, string Clave, string ClientEnvCode, string ParentCompanyCode, int BatchSize = 50, string? LgfApiBaseUrl = null);
```

- [ ] **Step 4: Batch in `PushAsync`**

Replace the `foreach (var registro in registros) { ... }` loop body in `PushAsync` with a loop over chunks. The existing loop currently looks like:

```csharp
foreach (var registro in registros)
{
    try
    {
        var xml = ArmarXml(registro, config, mapeos);
        await EnviarAsync(xml, config, cancellationToken);
        resultados.Add(new IntegrationPushResult(registro, Exito: true, MensajeError: null));
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error enviando documento a Oracle WMS Cloud");
        resultados.Add(new IntegrationPushResult(registro, Exito: false, MensajeError: ex.Message));
    }
}
```

Replace with:

```csharp
var batchSize = config.BatchSize > 0 ? config.BatchSize : 50;
foreach (var lote in registros.Chunk(batchSize))
{
    try
    {
        var xml = ArmarXmlLote(lote, config, mapeos);
        await EnviarAsync(xml, config, cancellationToken);
        foreach (var registro in lote)
        {
            resultados.Add(new IntegrationPushResult(registro, Exito: true, MensajeError: null));
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error enviando lote de {Cantidad} documento(s) a Oracle WMS Cloud", lote.Length);
        foreach (var registro in lote)
        {
            resultados.Add(new IntegrationPushResult(registro, Exito: false, MensajeError: ex.Message));
        }
    }
}
```

- [ ] **Step 5: Rename/adapt `ArmarXml` to `ArmarXmlLote`, building N nodes**

Replace the existing `ArmarXml` method (single-record) with `ArmarXmlLote` (multi-record). All node-shape logic (`ArmarNodoIbShipment`, `ArmarNodoOrder`, `CampoXml`, item/store inline construction) stays exactly as Task from the previous session left it — only the outer wrapping changes from "one node" to "N nodes of the same `TipoDocumento`, assumed homogeneous within a chunk since a Subida `IntegrationDefinition` is always single-entity":

```csharp
private static XDocument ArmarXmlLote(IReadOnlyList<IntegrationRecord> lote, WmsCloudConfig config, IReadOnlyDictionary<(string MapperKey, string FieldName), string> mapeos)
{
    var tipoDocumento = (string)lote[0]["TipoDocumento"]!;
    var (entity, nombreLista, nombreItem) = tipoDocumento switch
    {
        "Item" => ("item", "ListOfItems", "item"),
        "Store" => ("store", "ListOfStores", "store"),
        "IbShipment" => ("ib_shipment", "ListOfIbShipments", "ib_shipment"),
        "Order" => ("order", "ListOfOrders", "order"),
        _ => throw new InvalidOperationException($"TipoDocumento '{tipoDocumento}' no soportado en WmsCloudConnector."),
    };

    var header = new XElement("Header",
        new XElement("DocumentVersion", "24D"),
        new XElement("OriginSystem", "LogFire"),
        new XElement("ClientEnvCode", config.ClientEnvCode),
        new XElement("ParentCompanyCode", config.ParentCompanyCode),
        new XElement("Entity", entity),
        new XElement("TimeStamp", DateTime.UtcNow.ToString("O")),
        new XElement("MessageId", Guid.NewGuid().ToString()));

    var nodos = lote.Select(registro => tipoDocumento switch
    {
        "Item" => new XElement(nombreItem,
            CampoXml(mapeos, "SAPWMS_ITEM", "item_alternate_code", registro, registro["ItemCode"]),
            CampoXml(mapeos, "SAPWMS_ITEM", "description", registro, registro["ItemName"]),
            CampoXml(mapeos, "SAPWMS_ITEM", "barcode", registro, registro["BarCode"])),
        "Store" => new XElement(nombreItem,
            CampoXml(mapeos, "SAPWMS_STORE", "code", registro, registro["CardCode"]),
            CampoXml(mapeos, "SAPWMS_STORE", "name", registro, registro["CardName"]),
            CampoXml(mapeos, "SAPWMS_STORE", "parent_company_id", registro, config.ParentCompanyCode)),
        "IbShipment" => ArmarNodoIbShipment(registro, mapeos),
        "Order" => ArmarNodoOrder(registro, mapeos),
        _ => throw new InvalidOperationException($"TipoDocumento '{tipoDocumento}' no soportado en WmsCloudConnector."),
    });

    return new XDocument(new XElement("LgfData", header, new XElement(nombreLista, nodos)));
}
```

Note this is a straight lift of the existing per-record switch expression body, just applied via `.Select` over `lote` instead of once over a single `registro`, and `new XElement(nombreLista, nodoItem)` becomes `new XElement(nombreLista, nodos)` (an `XElement` constructor accepts an `IEnumerable<XElement>` as content directly — no extra code needed for that part). Do not change `ArmarNodoIbShipment`, `ArmarNodoOrder`, or `CampoXml` — their signatures already take a single `IntegrationRecord`, which is what `.Select` calls them with per-item.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsCloudConnectorTests`
Expected: PASS (all, including the two new ones).

- [ ] **Step 7: Expose `BatchSize`/`LgfApiBaseUrl` in `Admin/Integraciones/Nuevo`**

Edit `Portal SaaS - Core/src/PortalSaas.Host/Pages/Admin/Integraciones/Nuevo.cshtml.cs`. In the private `WmsCloudConfigInput` record, add the two fields:

```csharp
private sealed record WmsCloudConfigInput(string ApiUrl, string Usuario, string Clave, string ClientEnvCode, string ParentCompanyCode, int BatchSize = 50, string? LgfApiBaseUrl = null);
```

In `InputModel`, add:
```csharp
[Display(Name = "Tamaño de lote (BatchSize)")]
public int BatchSize { get; set; } = 50;

[Display(Name = "URL base de LGFAPI (consulta de status, opcional)")]
public string? LgfApiBaseUrl { get; set; }
```

In `OnGetAsync`'s WmsCloud precarga block, add after `Input.ParentCompanyCode = configActual.ParentCompanyCode;`:
```csharp
Input.BatchSize = configActual.BatchSize;
Input.LgfApiBaseUrl = configActual.LgfApiBaseUrl;
```

In `ArmarConfigCifradaAsync`'s WmsCloud branch, change the `WmsCloudConfigInput` construction call to pass the two new fields:
```csharp
var json = JsonSerializer.Serialize(new WmsCloudConfigInput(
    Input.ApiUrl!.Trim(), Input.Usuario!.Trim(), clave ?? string.Empty, Input.ClientEnvCode!.Trim(), Input.ParentCompanyCode!.Trim(),
    Input.BatchSize > 0 ? Input.BatchSize : 50, string.IsNullOrWhiteSpace(Input.LgfApiBaseUrl) ? null : Input.LgfApiBaseUrl.Trim()));
```

Edit `Portal SaaS - Core/src/PortalSaas.Host/Pages/Admin/Integraciones/Nuevo.cshtml`, inside `<div id="bloque-config-wmscloud">`, after the `Input.ParentCompanyCode` field block, add:

```html
<div class="col-md-3">
    <label asp-for="Input.BatchSize" class="form-label"></label>
    <input asp-for="Input.BatchSize" class="form-control" type="number" min="1" />
</div>
<div class="col-md-9">
    <label asp-for="Input.LgfApiBaseUrl" class="form-label"></label>
    <input asp-for="Input.LgfApiBaseUrl" class="form-control" placeholder="Ej. https://ta11.wms.ocs.oraclecloud.com/cd_test/wms/lgfapi/v10/entity/" />
</div>
```

- [ ] **Step 8: Build both repos to verify**

Ask the user to confirm the local `PortalSaas.Host` is stopped, then:
```bash
dotnet build "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Modulo.Wms.csproj" -c Release
dotnet build "Portal SaaS - Core/src/PortalSaas.Host/PortalSaas.Host.csproj" -c Release
```
Expected: 0 errors on both.

- [ ] **Step 9: Commit (two commits, one per repo)**

```bash
# in Modulo.Wms
git add src/Modulo.Wms/Services/WmsCloudConnector.cs tests/Modulo.Wms.Tests/Services/WmsCloudConnectorTests.cs
git commit -m "feat: WmsCloudConnector arma lotes reales (BatchSize) en vez de un XML por registro"

# in Portal SaaS - Core
git add src/PortalSaas.Host/Pages/Admin/Integraciones/Nuevo.cshtml src/PortalSaas.Host/Pages/Admin/Integraciones/Nuevo.cshtml.cs
git commit -m "feat: expone BatchSize/LgfApiBaseUrl en Admin/Integraciones/Nuevo (conector WmsCloud)"
```

---

### Task 4: Tabla `wms_oracle_export_validations`

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsExportValidation.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Data/WmsDbContext.cs`
- Create: migration `AddWmsExportValidations` in both migrations projects

**Interfaces:**
- Produces: `WmsExportValidation { Id (long), CompanyId (Guid), TipoDoc (string), Clave (string), EnviadoEn (DateTimeOffset), WmsStatusId (int?), WmsStatusDesc (string?), WmsErrorMsg (string?), ValidadoEn (DateTimeOffset?), Intentos (int) }`, `WmsDbContext.WmsExportValidations` (`DbSet<WmsExportValidation>`) — consumed by Task 6 and Task 7.

- [ ] **Step 1: Create the model**

Create `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsExportValidation.cs`:

```csharp
namespace Modulo.Wms.Models;

/// <summary>
/// Reconciliación del resultado real de un documento enviado a Oracle WMS Cloud --
/// init_stage_interface (WmsCloudConnector) solo confirma recepción, no validación de
/// negocio. Espejo de STG_WMS_VALIDATION del legado (WMS_Suite), con company_id real
/// en vez de aislamiento por schema. Ver
/// docs/superpowers/specs/2026-08-17-batching-y-reconciliacion-wms-design.md.
/// </summary>
public class WmsExportValidation
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public string TipoDoc { get; set; } = string.Empty;
    public string Clave { get; set; } = string.Empty;
    public DateTimeOffset EnviadoEn { get; set; } = DateTimeOffset.UtcNow;
    public int? WmsStatusId { get; set; }
    public string? WmsStatusDesc { get; set; }
    public string? WmsErrorMsg { get; set; }
    public DateTimeOffset? ValidadoEn { get; set; }
    public int Intentos { get; set; }
}
```

- [ ] **Step 2: Map it in `WmsDbContext`**

Edit `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Data/WmsDbContext.cs`. Add the `DbSet` next to the others (near line 39):

```csharp
public DbSet<WmsExportValidation> WmsExportValidations => Set<WmsExportValidation>();
```

Add to `OnModelCreating` (follow the exact style of the `WmsSapStageItem`/`WmsSapStageStore` blocks already in the file):

```csharp
modelBuilder.Entity<WmsExportValidation>(entity =>
{
    entity.ToTable("wms_oracle_export_validations");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasColumnName("id");
    entity.Property(e => e.CompanyId).HasColumnName("company_id");
    entity.Property(e => e.TipoDoc).HasColumnName("tipo_doc").HasMaxLength(20);
    entity.Property(e => e.Clave).HasColumnName("clave").HasMaxLength(100);
    entity.Property(e => e.EnviadoEn).HasColumnName("enviado_en");
    entity.Property(e => e.WmsStatusId).HasColumnName("wms_status_id");
    entity.Property(e => e.WmsStatusDesc).HasColumnName("wms_status_desc").HasMaxLength(100);
    entity.Property(e => e.WmsErrorMsg).HasColumnName("wms_error_msg").HasMaxLength(500);
    entity.Property(e => e.ValidadoEn).HasColumnName("validado_en");
    entity.Property(e => e.Intentos).HasColumnName("intentos");
    entity.HasIndex(e => new { e.CompanyId, e.TipoDoc, e.Clave }).IsUnique().HasDatabaseName("ix_wms_oracle_export_validations_company_tipodoc_clave");
});
```

- [ ] **Step 3: Generate both migrations**

Ask the user to confirm the local `PortalSaas.Host` is stopped. From the `Modulo.Wms` repo root:

```bash
dotnet ef migrations add AddWmsExportValidations --project src/Modulo.Wms.Migrations.Postgres --startup-project src/Modulo.Wms.Migrations.Postgres
dotnet ef migrations add AddWmsExportValidations --project src/Modulo.Wms.Migrations.SqlServer --startup-project src/Modulo.Wms.Migrations.SqlServer
```

Expected: new migration files creating `wms_oracle_export_validations` with the unique index, in both projects.

- [ ] **Step 4: Build to verify**

```bash
dotnet build "src/Modulo.Wms/Modulo.Wms.csproj" -c Release
dotnet build src/Modulo.Wms.Migrations.Postgres -c Release
dotnet build src/Modulo.Wms.Migrations.SqlServer -c Release
```
Expected: 0 errors on all three.

- [ ] **Step 5: Commit**

```bash
git add src/Modulo.Wms/Models/WmsExportValidation.cs src/Modulo.Wms/Data/WmsDbContext.cs src/Modulo.Wms.Migrations.Postgres/Migrations src/Modulo.Wms.Migrations.SqlServer/Migrations
git commit -m "feat: tabla wms_oracle_export_validations para reconciliacion de resultado real"
```

---

### Task 5: `IWmsValidationApiClient` / `WmsValidationApiClient` (cliente LGFAPI)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/IWmsValidationApiClient.cs`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsValidationApiClient.cs`
- Test: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsValidationApiClientTests.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs`

**Interfaces:**
- Produces: `IWmsValidationApiClient.CheckStageRecordAsync(string lgfApiBaseUrl, string usuario, string clave, string entity, string keyField, string keyValue, string? companyCode, bool filtrarPorUrl, CancellationToken ct)` returning `Task<WmsStageCheckResult>`, `WmsStageCheckResult(bool Found, int? StatusId, string? ErrorMessage)` — consumed by Task 6 and Task 7.

- [ ] **Step 1: Write the failing test**

Create `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsValidationApiClientTests.cs`. Follow the same `HttpMessageHandler` fake pattern as `WmsCloudConnectorTests.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsValidationApiClientTests
{
    private sealed class HttpHandlerFalso : HttpMessageHandler
    {
        public string? UltimaUrl { get; private set; }
        private readonly HttpStatusCode _statusCode;
        private readonly string _cuerpo;

        public HttpHandlerFalso(HttpStatusCode statusCode, string cuerpo)
        {
            _statusCode = statusCode;
            _cuerpo = cuerpo;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UltimaUrl = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(_statusCode) { Content = new StringContent(_cuerpo) });
        }
    }

    [Fact]
    public async Task CheckStageRecordAsync_EncuentraRegistroQueMatcheaCompania_DevuelveFoundConStatusId()
    {
        var cuerpo = """{"result_count":1,"next_page":null,"results":[{"status_id":90,"error_message":null,"company_code":"COMP01"}]}""";
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK, cuerpo);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var sut = new WmsValidationApiClient(httpClient, NullLogger<WmsValidationApiClient>.Instance);

        var resultado = await sut.CheckStageRecordAsync(
            "https://wms.example.com/lgfapi/v10/entity/", "wmsuser", "wmspass",
            "item", "item_alternate_code", "ITM001", "COMP01", filtrarPorUrl: true, CancellationToken.None);

        Assert.True(resultado.Found);
        Assert.Equal(90, resultado.StatusId);
        Assert.Contains("item_alternate_code=ITM001", handlerFalso.UltimaUrl);
        Assert.Contains("company_code=COMP01", handlerFalso.UltimaUrl);
    }

    [Fact]
    public async Task CheckStageRecordAsync_SinResultados_DevuelveNotFound()
    {
        var cuerpo = """{"result_count":0,"next_page":null,"results":[]}""";
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK, cuerpo);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var sut = new WmsValidationApiClient(httpClient, NullLogger<WmsValidationApiClient>.Instance);

        var resultado = await sut.CheckStageRecordAsync(
            "https://wms.example.com/lgfapi/v10/entity/", "wmsuser", "wmspass",
            "item", "item_alternate_code", "ITM999", "COMP01", filtrarPorUrl: true, CancellationToken.None);

        Assert.False(resultado.Found);
        Assert.Null(resultado.StatusId);
    }

    [Fact]
    public async Task CheckStageRecordAsync_ResultadoDeOtraCompania_NoMatchea_DevuelveNotFound()
    {
        var cuerpo = """{"result_count":1,"next_page":null,"results":[{"status_id":90,"error_message":null,"company_code":"OTRA"}]}""";
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK, cuerpo);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var sut = new WmsValidationApiClient(httpClient, NullLogger<WmsValidationApiClient>.Instance);

        var resultado = await sut.CheckStageRecordAsync(
            "https://wms.example.com/lgfapi/v10/entity/", "wmsuser", "wmspass",
            "item", "item_alternate_code", "ITM001", "COMP01", filtrarPorUrl: true, CancellationToken.None);

        Assert.False(resultado.Found);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsValidationApiClientTests`
Expected: FAIL to compile — types don't exist yet.

- [ ] **Step 3: Create the interface**

Create `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/IWmsValidationApiClient.cs`:

```csharp
namespace Modulo.Wms.Services;

public interface IWmsValidationApiClient
{
    Task<WmsStageCheckResult> CheckStageRecordAsync(
        string lgfApiBaseUrl, string usuario, string clave,
        string entity, string keyField, string keyValue, string? companyCode,
        bool filtrarPorUrl, CancellationToken ct = default);
}

public sealed record WmsStageCheckResult(bool Found, int? StatusId, string? ErrorMessage);
```

- [ ] **Step 4: Implement it**

Create `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsValidationApiClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Modulo.Wms.Services;

/// <summary>
/// Cliente de LGFAPI (Oracle WMS Cloud, endpoint de consulta de status -- distinto de
/// init_stage_interface, que solo confirma recepción). Puerto directo de
/// WmsValidationApiClient del legado (WMS_Suite, fuera de este repo), mismo formato de
/// consulta/respuesta -- no rediseñado. Ver
/// docs/superpowers/specs/2026-08-17-batching-y-reconciliacion-wms-design.md.
/// </summary>
public class WmsValidationApiClient : IWmsValidationApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WmsValidationApiClient> _logger;

    public WmsValidationApiClient(HttpClient httpClient, ILogger<WmsValidationApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<WmsStageCheckResult> CheckStageRecordAsync(
        string lgfApiBaseUrl, string usuario, string clave,
        string entity, string keyField, string keyValue, string? companyCode,
        bool filtrarPorUrl, CancellationToken ct = default)
    {
        var baseUrl = lgfApiBaseUrl.EndsWith('/') ? lgfApiBaseUrl : lgfApiBaseUrl + "/";
        var url = $"{baseUrl}{entity}?{keyField}={Uri.EscapeDataString(keyValue)}";
        if (filtrarPorUrl && !string.IsNullOrWhiteSpace(companyCode))
        {
            url += $"&company_code={Uri.EscapeDataString(companyCode)}";
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var authToken = Encoding.UTF8.GetBytes($"{usuario}:{clave}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

            var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WMS LGFAPI [{Entity}] respondió {Status} para {KeyField}={KeyValue}: {Body}", entity, response.StatusCode, keyField, keyValue, body);
                return new WmsStageCheckResult(Found: false, StatusId: null, ErrorMessage: null);
            }

            var parsed = JsonSerializer.Deserialize<LgfApiListResponse>(body);
            if (parsed is null || parsed.Results.Count == 0)
            {
                return new WmsStageCheckResult(Found: false, StatusId: null, ErrorMessage: null);
            }

            foreach (var fila in parsed.Results)
            {
                if (!MatchesCompany(fila, companyCode))
                {
                    continue;
                }

                return new WmsStageCheckResult(
                    Found: true,
                    StatusId: ReadIntOrNull(fila, "status_id"),
                    ErrorMessage: ReadStringOrNull(fila, "error_message"));
            }

            return new WmsStageCheckResult(Found: false, StatusId: null, ErrorMessage: null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error consultando WMS LGFAPI [{Entity}] para {KeyField}={KeyValue}", entity, keyField, keyValue);
            return new WmsStageCheckResult(Found: false, StatusId: null, ErrorMessage: null);
        }
    }

    private static bool MatchesCompany(JsonElement fila, string? companyCode)
    {
        if (string.IsNullOrWhiteSpace(companyCode))
        {
            return true;
        }

        foreach (var campo in new[] { "company_code", "company_id", "parent_company_id" })
        {
            var valor = ReadStringOrNull(fila, campo);
            if (valor is not null && string.Equals(valor, companyCode, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string? ReadStringOrNull(JsonElement fila, string campo) =>
        fila.TryGetProperty(campo, out var valor) && valor.ValueKind != JsonValueKind.Null ? valor.ToString() : null;

    private static int? ReadIntOrNull(JsonElement fila, string campo) =>
        fila.TryGetProperty(campo, out var valor) && valor.ValueKind == JsonValueKind.Number ? valor.GetInt32() : null;

    private sealed class LgfApiListResponse
    {
        [JsonPropertyName("result_count")]
        public int ResultCount { get; set; }
        [JsonPropertyName("next_page")]
        public string? NextPage { get; set; }
        [JsonPropertyName("results")]
        public List<JsonElement> Results { get; set; } = [];
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsValidationApiClientTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Register in DI**

Edit `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs`, `RegisterServices`, next to the existing `services.AddHttpClient<IIntegrationConnector, WmsCloudConnector>();` line, add:

```csharp
services.AddHttpClient<IWmsValidationApiClient, WmsValidationApiClient>();
```

- [ ] **Step 7: Build to verify**

```bash
dotnet build "src/Modulo.Wms/Modulo.Wms.csproj" -c Release
```
Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/Modulo.Wms/Services/IWmsValidationApiClient.cs src/Modulo.Wms/Services/WmsValidationApiClient.cs src/Modulo.Wms/ModuloWms.cs tests/Modulo.Wms.Tests/Services/WmsValidationApiClientTests.cs
git commit -m "feat: WmsValidationApiClient -- cliente LGFAPI (consulta de status real en Oracle WMS Cloud)"
```

---

### Task 6: `WmsStageErrorReconciler` (detector de rechazos)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsStageErrorReconciler.cs`
- Test: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsStageErrorReconcilerTests.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs`

**Interfaces:**
- Consumes: `IExternalDatabaseConnectionService.ListActiveCompanyIdsAsync("Wms", ct)` (existing), `ICurrentCompanyOverride.Set(companyId)` (existing, same pattern as `WmsSlshStageParser`), `IIntegrationConnectorConfigService.GetDecryptedConfigAsync(companyId, "Wms", "WmsCloud", ct)` (Task 1), `IWmsValidationApiClient.CheckStageRecordAsync(...)` (Task 5), `WmsDbContext.WmsSapStageItems`/`Stores`/`OrderHdrs`/`InboundHdrs` filtered `Status == Enviado` (Task 2), `WmsDbContext.WmsExportValidations` (Task 4).
- Produces: nothing consumed by later tasks — this is a terminal `BackgroundService`.

- [ ] **Step 1: Write the failing test**

Create `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsStageErrorReconcilerTests.cs`. This follows the exact real-DI-container pattern established this session in `WmsSlshStageParserTests.cs` — read that file first for the fakes (`FakeCurrentCompanyOverride`, `FakeCurrentCompanyAccessor`, `FakeExternalDatabaseConnectionService`, `BuildProvider` helper using `UseInMemoryDatabase` + the `HasCompany` guard) and reuse the identical shape, adding two more fakes:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsStageErrorReconcilerTests
{
    private sealed class FakeCurrentCompanyOverride : ICurrentCompanyOverride
    {
        public Guid? CompanyId { get; private set; }
        public void Set(Guid companyId) => CompanyId = companyId;
        public void Clear() => CompanyId = null;
    }

    private sealed class FakeCurrentCompanyAccessor : ICurrentCompanyAccessor
    {
        private readonly ICurrentCompanyOverride _override;
        public FakeCurrentCompanyAccessor(ICurrentCompanyOverride @override) => _override = @override;
        public Guid CompanyId => _override.CompanyId ?? throw new InvalidOperationException("No hay compañía activa.");
        public string Code => "TEST";
        public string Database => "test";
        public string ServiceLayerUrl => "https://test";
        public string Country => "CL";
        public bool HasCompany => _override.CompanyId is not null;
    }

    private sealed class FakeExternalDatabaseConnectionService : IExternalDatabaseConnectionService
    {
        private readonly IReadOnlyList<ModuleCompanyDto> _companias;
        public FakeExternalDatabaseConnectionService(IReadOnlyList<ModuleCompanyDto> companias) => _companias = companias;
        public Task<ExternalDatabaseConnection> ResolveConnectionAsync(string moduleCode, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ModuleCompanyDto>> ListActiveCompanyIdsAsync(string moduleCode, CancellationToken ct = default) => Task.FromResult(_companias);
    }

    private sealed class FakeIntegrationConnectorConfigService : IIntegrationConnectorConfigService
    {
        private readonly string? _config;
        public FakeIntegrationConnectorConfigService(string? config) => _config = config;
        public Task<string?> GetDecryptedConfigAsync(Guid companyId, string moduloOrigen, string conectorTipo, CancellationToken ct = default) => Task.FromResult(_config);
    }

    private sealed class FakeWmsValidationApiClient : IWmsValidationApiClient
    {
        private readonly WmsStageCheckResult _resultado;
        public FakeWmsValidationApiClient(WmsStageCheckResult resultado) => _resultado = resultado;
        public Task<WmsStageCheckResult> CheckStageRecordAsync(string lgfApiBaseUrl, string usuario, string clave, string entity, string keyField, string keyValue, string? companyCode, bool filtrarPorUrl, CancellationToken ct = default)
            => Task.FromResult(_resultado);
    }

    private static ServiceProvider BuildProvider(string dbName, IReadOnlyList<ModuleCompanyDto> companiasActivas, string? configJson, WmsStageCheckResult resultadoLgfApi)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentCompanyOverride, FakeCurrentCompanyOverride>();
        services.AddScoped<ICurrentCompanyAccessor, FakeCurrentCompanyAccessor>();
        services.AddSingleton<IExternalDatabaseConnectionService>(new FakeExternalDatabaseConnectionService(companiasActivas));
        services.AddSingleton<IIntegrationConnectorConfigService>(new FakeIntegrationConnectorConfigService(configJson));
        services.AddSingleton<IWmsValidationApiClient>(new FakeWmsValidationApiClient(resultadoLgfApi));
        services.AddLogging();
        services.AddSingleton(NullLogger<WmsStageErrorReconciler>.Instance);

        services.AddDbContext<WmsDbContext>((sp, options) =>
        {
            var companyAccessor = sp.GetRequiredService<ICurrentCompanyAccessor>();
            if (!companyAccessor.HasCompany)
            {
                throw new InvalidOperationException("Modulo.Wms requiere una compañía activa en la sesión -- seleccioná una compañía antes de continuar.");
            }
            options.UseInMemoryDatabase(dbName);
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task EjecutarCicloAsync_RegistroEnviadoConStatus101_LoMarcaErrorWms()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var configJson = """{"ApiUrl":"https://x","Usuario":"u","Clave":"p","ClientEnvCode":"cli","ParentCompanyCode":"COMP01","LgfApiBaseUrl":"https://x/lgfapi/v10/entity/"}""";
        var resultadoLgfApi = new WmsStageCheckResult(Found: true, StatusId: 101, ErrorMessage: "Item duplicado");

        await using var provider = BuildProvider(dbName, [new(companyId, Guid.NewGuid())], configJson, resultadoLgfApi);

        var seedOptions = new DbContextOptionsBuilder<WmsDbContext>().UseInMemoryDatabase(dbName).Options;
        await using (var seedContext = new WmsDbContext(seedOptions))
        {
            seedContext.WmsSapStageItems.Add(new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Enviado });
            await seedContext.SaveChangesAsync();
        }

        var reconciler = new WmsStageErrorReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WmsStageErrorReconciler>.Instance);

        await reconciler.EjecutarCicloAsync(CancellationToken.None);

        await using var verifyContext = new WmsDbContext(seedOptions);
        var fila = await verifyContext.WmsSapStageItems.SingleAsync();
        Assert.Equal(WmsSapStageStatus.ErrorWms, fila.Status);
        Assert.Equal("Item duplicado", fila.ErrorMsg);

        var validacion = await verifyContext.WmsExportValidations.SingleAsync();
        Assert.Equal(101, validacion.WmsStatusId);
    }

    [Fact]
    public async Task EjecutarCicloAsync_SinConfigParaLaCompania_NoRompeYNoTocaElRegistro()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var resultadoLgfApi = new WmsStageCheckResult(Found: false, StatusId: null, ErrorMessage: null);

        await using var provider = BuildProvider(dbName, [new(companyId, Guid.NewGuid())], configJson: null, resultadoLgfApi);

        var seedOptions = new DbContextOptionsBuilder<WmsDbContext>().UseInMemoryDatabase(dbName).Options;
        await using (var seedContext = new WmsDbContext(seedOptions))
        {
            seedContext.WmsSapStageItems.Add(new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Enviado });
            await seedContext.SaveChangesAsync();
        }

        var reconciler = new WmsStageErrorReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WmsStageErrorReconciler>.Instance);

        await reconciler.EjecutarCicloAsync(CancellationToken.None);

        await using var verifyContext = new WmsDbContext(seedOptions);
        var fila = await verifyContext.WmsSapStageItems.SingleAsync();
        Assert.Equal(WmsSapStageStatus.Enviado, fila.Status);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsStageErrorReconcilerTests`
Expected: FAIL to compile — `WmsStageErrorReconciler` doesn't exist yet.

- [ ] **Step 3: Implement `WmsStageErrorReconciler`**

Create `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsStageErrorReconciler.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Services;

/// <summary>
/// Detecta rechazos explícitos de Oracle WMS Cloud para registros en estado Enviado --
/// consulta LGFAPI (entidades stage_*) filtrando status_id=101 (Failed, terminal, sin
/// reintentos). Puerto directo de WmsOutbound_StageErrorProcessor del legado
/// (WMS_Suite, fuera de este repo). Mismo patrón de scoping que WmsSlshStageParser
/// (ICurrentCompanyOverride fijado antes de resolver WmsDbContext -- ver ese archivo
/// para el porqué, ya corregido en este repo).
/// </summary>
public sealed class WmsStageErrorReconciler : BackgroundService
{
    private static readonly TimeSpan IntervaloCiclo = TimeSpan.FromSeconds(60);
    private const int StatusIdRechazado = 101;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WmsStageErrorReconciler> _logger;

    public WmsStageErrorReconciler(IServiceScopeFactory scopeFactory, ILogger<WmsStageErrorReconciler> logger)
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
                await EjecutarCicloAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado en el ciclo de detección de rechazos WMS");
            }

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
        List<PortalSaas.Abstractions.Modelos.ModuleCompanyDto> companias;
        using (var scope = _scopeFactory.CreateScope())
        {
            var externalDb = scope.ServiceProvider.GetRequiredService<IExternalDatabaseConnectionService>();
            companias = (await externalDb.ListActiveCompanyIdsAsync("Wms", cancellationToken)).ToList();
        }

        foreach (var compania in companias)
        {
            await ProcesarCompaniaAsync(compania.CompanyId, cancellationToken);
        }
    }

    private async Task ProcesarCompaniaAsync(Guid companyId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IIntegrationConnectorConfigService>();
        var configJson = await configService.GetDecryptedConfigAsync(companyId, "Wms", "WmsCloud", cancellationToken);
        if (configJson is null)
        {
            return;
        }

        var config = System.Text.Json.JsonSerializer.Deserialize<WmsCloudConfigParaReconciliacion>(configJson);
        if (config is null || string.IsNullOrWhiteSpace(config.LgfApiBaseUrl))
        {
            return;
        }

        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var validador = scope.ServiceProvider.GetRequiredService<IWmsValidationApiClient>();

        await ProcesarEntidadAsync(contexto, validador, config, companyId, "Item", "stage_item", "item_alternate_code",
            contexto.WmsSapStageItems.Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Enviado).ToList,
            f => f.ItemCode, (f, msg) => { f.Status = WmsSapStageStatus.ErrorWms; f.ErrorMsg = msg; }, cancellationToken);

        await ProcesarEntidadAsync(contexto, validador, config, companyId, "Store", "stage_store", "code",
            contexto.WmsSapStageStores.Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Enviado).ToList,
            f => f.CardCode, (f, msg) => { f.Status = WmsSapStageStatus.ErrorWms; f.ErrorMsg = msg; }, cancellationToken);

        await ProcesarEntidadAsync(contexto, validador, config, companyId, "Order", "stage_order_hdr", "order_nbr",
            contexto.WmsSapStageOrderHdrs.Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Enviado).ToList,
            f => f.OrderNbr, (f, msg) => { f.Status = WmsSapStageStatus.ErrorWms; f.ErrorMsg = msg; }, cancellationToken);

        await ProcesarEntidadAsync(contexto, validador, config, companyId, "IbShipment", "stage_ib_shipment", "shipment_nbr",
            contexto.WmsSapStageInboundHdrs.Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Enviado).ToList,
            f => f.SapDocEntry.ToString(), (f, msg) => { f.Status = WmsSapStageStatus.ErrorWms; f.ErrorMsg = msg; }, cancellationToken);
    }

    private async Task ProcesarEntidadAsync<TFila>(
        WmsDbContext contexto, IWmsValidationApiClient validador, WmsCloudConfigParaReconciliacion config, Guid companyId,
        string tipoDoc, string entidadStage, string keyField,
        Func<List<TFila>> obtenerPendientes, Func<TFila, string> obtenerClave, Action<TFila, string?> marcarError,
        CancellationToken cancellationToken)
        where TFila : class
    {
        var pendientes = obtenerPendientes();
        foreach (var fila in pendientes)
        {
            var clave = obtenerClave(fila);
            var resultado = await validador.CheckStageRecordAsync(
                config.LgfApiBaseUrl!, config.Usuario, config.Clave, entidadStage, keyField, clave, config.ParentCompanyCode,
                filtrarPorUrl: true, cancellationToken);

            if (!resultado.Found || resultado.StatusId != StatusIdRechazado)
            {
                continue;
            }

            marcarError(fila, resultado.ErrorMessage);

            var validacion = await contexto.WmsExportValidations
                .FirstOrDefaultAsync(v => v.CompanyId == companyId && v.TipoDoc == tipoDoc && v.Clave == clave, cancellationToken);
            if (validacion is null)
            {
                validacion = new Models.WmsExportValidation { CompanyId = companyId, TipoDoc = tipoDoc, Clave = clave };
                contexto.WmsExportValidations.Add(validacion);
            }
            validacion.WmsStatusId = resultado.StatusId;
            validacion.WmsErrorMsg = resultado.ErrorMessage;
            validacion.ValidadoEn = DateTimeOffset.UtcNow;
        }

        await contexto.SaveChangesAsync(cancellationToken);
    }

    private sealed record WmsCloudConfigParaReconciliacion(string Usuario, string Clave, string ParentCompanyCode, string? LgfApiBaseUrl);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsStageErrorReconcilerTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Register as hosted service**

Edit `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs`, next to `services.AddHostedService<WmsSlshStageParser>();`, add:

```csharp
services.AddHostedService<WmsStageErrorReconciler>();
```

- [ ] **Step 6: Build to verify**

```bash
dotnet build "src/Modulo.Wms/Modulo.Wms.csproj" -c Release
```
Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/Modulo.Wms/Services/WmsStageErrorReconciler.cs src/Modulo.Wms/ModuloWms.cs tests/Modulo.Wms.Tests/Services/WmsStageErrorReconcilerTests.cs
git commit -m "feat: WmsStageErrorReconciler -- detecta rechazos reales (status_id=101) via LGFAPI"
```

---

### Task 7: `WmsExistsReconciler` (confirmador de éxito real)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsExistsReconciler.cs`
- Test: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsExistsReconcilerTests.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs`

**Interfaces:**
- Consumes: same as Task 6, plus the final-entity names (`item`, `facility`, `order_hdr`, `ib_shipment` — NOT the `stage_*` ones, since Oracle removes a record from `stage_*` once it finishes processing it, per the legacy precedent this ports).
- Produces: nothing consumed by later tasks — terminal `BackgroundService`.

- [ ] **Step 1: Write the failing test**

Create `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsExistsReconcilerTests.cs`. Reuse the exact same fakes as `WmsStageErrorReconcilerTests.cs` (copy the private fake classes and `BuildProvider` helper verbatim, adjusted for `WmsExistsReconciler`'s own logger type):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsExistsReconcilerTests
{
    private sealed class FakeCurrentCompanyOverride : ICurrentCompanyOverride
    {
        public Guid? CompanyId { get; private set; }
        public void Set(Guid companyId) => CompanyId = companyId;
        public void Clear() => CompanyId = null;
    }

    private sealed class FakeCurrentCompanyAccessor : ICurrentCompanyAccessor
    {
        private readonly ICurrentCompanyOverride _override;
        public FakeCurrentCompanyAccessor(ICurrentCompanyOverride @override) => _override = @override;
        public Guid CompanyId => _override.CompanyId ?? throw new InvalidOperationException("No hay compañía activa.");
        public string Code => "TEST";
        public string Database => "test";
        public string ServiceLayerUrl => "https://test";
        public string Country => "CL";
        public bool HasCompany => _override.CompanyId is not null;
    }

    private sealed class FakeExternalDatabaseConnectionService : IExternalDatabaseConnectionService
    {
        private readonly IReadOnlyList<ModuleCompanyDto> _companias;
        public FakeExternalDatabaseConnectionService(IReadOnlyList<ModuleCompanyDto> companias) => _companias = companias;
        public Task<ExternalDatabaseConnection> ResolveConnectionAsync(string moduleCode, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ModuleCompanyDto>> ListActiveCompanyIdsAsync(string moduleCode, CancellationToken ct = default) => Task.FromResult(_companias);
    }

    private sealed class FakeIntegrationConnectorConfigService : IIntegrationConnectorConfigService
    {
        private readonly string? _config;
        public FakeIntegrationConnectorConfigService(string? config) => _config = config;
        public Task<string?> GetDecryptedConfigAsync(Guid companyId, string moduloOrigen, string conectorTipo, CancellationToken ct = default) => Task.FromResult(_config);
    }

    private sealed class FakeWmsValidationApiClient : IWmsValidationApiClient
    {
        private readonly WmsStageCheckResult _resultado;
        public FakeWmsValidationApiClient(WmsStageCheckResult resultado) => _resultado = resultado;
        public Task<WmsStageCheckResult> CheckStageRecordAsync(string lgfApiBaseUrl, string usuario, string clave, string entity, string keyField, string keyValue, string? companyCode, bool filtrarPorUrl, CancellationToken ct = default)
            => Task.FromResult(_resultado);
    }

    private static ServiceProvider BuildProvider(string dbName, IReadOnlyList<ModuleCompanyDto> companiasActivas, string? configJson, WmsStageCheckResult resultadoLgfApi)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentCompanyOverride, FakeCurrentCompanyOverride>();
        services.AddScoped<ICurrentCompanyAccessor, FakeCurrentCompanyAccessor>();
        services.AddSingleton<IExternalDatabaseConnectionService>(new FakeExternalDatabaseConnectionService(companiasActivas));
        services.AddSingleton<IIntegrationConnectorConfigService>(new FakeIntegrationConnectorConfigService(configJson));
        services.AddSingleton<IWmsValidationApiClient>(new FakeWmsValidationApiClient(resultadoLgfApi));
        services.AddLogging();
        services.AddSingleton(NullLogger<WmsExistsReconciler>.Instance);

        services.AddDbContext<WmsDbContext>((sp, options) =>
        {
            var companyAccessor = sp.GetRequiredService<ICurrentCompanyAccessor>();
            if (!companyAccessor.HasCompany)
            {
                throw new InvalidOperationException("Modulo.Wms requiere una compañía activa en la sesión -- seleccioná una compañía antes de continuar.");
            }
            options.UseInMemoryDatabase(dbName);
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task EjecutarCicloAsync_RegistroEncontradoEnEntidadFinal_LoMarcaProcesadoWms()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var configJson = """{"ApiUrl":"https://x","Usuario":"u","Clave":"p","ClientEnvCode":"cli","ParentCompanyCode":"COMP01","LgfApiBaseUrl":"https://x/lgfapi/v10/entity/"}""";
        var resultadoLgfApi = new WmsStageCheckResult(Found: true, StatusId: 90, ErrorMessage: null);

        await using var provider = BuildProvider(dbName, [new(companyId, Guid.NewGuid())], configJson, resultadoLgfApi);

        var seedOptions = new DbContextOptionsBuilder<WmsDbContext>().UseInMemoryDatabase(dbName).Options;
        await using (var seedContext = new WmsDbContext(seedOptions))
        {
            seedContext.WmsSapStageItems.Add(new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Enviado });
            await seedContext.SaveChangesAsync();
        }

        var reconciler = new WmsExistsReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WmsExistsReconciler>.Instance);

        await reconciler.EjecutarCicloAsync(CancellationToken.None);

        await using var verifyContext = new WmsDbContext(seedOptions);
        var fila = await verifyContext.WmsSapStageItems.SingleAsync();
        Assert.Equal(WmsSapStageStatus.ProcesadoWms, fila.Status);
        Assert.NotNull(fila.SyncedAt);
    }

    [Fact]
    public async Task EjecutarCicloAsync_NoEncontradoTrasVeinteIntentos_LoMarcaErrorWms()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var configJson = """{"ApiUrl":"https://x","Usuario":"u","Clave":"p","ClientEnvCode":"cli","ParentCompanyCode":"COMP01","LgfApiBaseUrl":"https://x/lgfapi/v10/entity/"}""";
        var resultadoLgfApi = new WmsStageCheckResult(Found: false, StatusId: null, ErrorMessage: null);

        await using var provider = BuildProvider(dbName, [new(companyId, Guid.NewGuid())], configJson, resultadoLgfApi);

        var seedOptions = new DbContextOptionsBuilder<WmsDbContext>().UseInMemoryDatabase(dbName).Options;
        await using (var seedContext = new WmsDbContext(seedOptions))
        {
            seedContext.WmsSapStageItems.Add(new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Enviado });
            seedContext.WmsExportValidations.Add(new WmsExportValidation { CompanyId = companyId, TipoDoc = "Item", Clave = "ITM001", Intentos = 19 });
            await seedContext.SaveChangesAsync();
        }

        var reconciler = new WmsExistsReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WmsExistsReconciler>.Instance);

        await reconciler.EjecutarCicloAsync(CancellationToken.None);

        await using var verifyContext = new WmsDbContext(seedOptions);
        var fila = await verifyContext.WmsSapStageItems.SingleAsync();
        Assert.Equal(WmsSapStageStatus.ErrorWms, fila.Status);

        var validacion = await verifyContext.WmsExportValidations.SingleAsync();
        Assert.Equal(20, validacion.Intentos);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsExistsReconcilerTests`
Expected: FAIL to compile — `WmsExistsReconciler` doesn't exist yet.

- [ ] **Step 3: Implement `WmsExistsReconciler`**

Create `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsExistsReconciler.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Services;

/// <summary>
/// Confirma el éxito real de un registro en estado Enviado consultando la entidad
/// FINAL en LGFAPI (item/facility/order_hdr/ib_shipment, no las stage_* -- Oracle saca
/// el registro de stage_* una vez que termina de procesarlo). Puerto directo de
/// WmsOutbound_ExistsProcessor del legado (WMS_Suite, fuera de este repo). Reintenta
/// hasta MaxIntentos veces (mismo tope que el legado) antes de dar error definitivo.
/// Mismo patrón de scoping que WmsSlshStageParser.
/// </summary>
public sealed class WmsExistsReconciler : BackgroundService
{
    private static readonly TimeSpan IntervaloCiclo = TimeSpan.FromSeconds(300);
    private const int MaxIntentos = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WmsExistsReconciler> _logger;

    public WmsExistsReconciler(IServiceScopeFactory scopeFactory, ILogger<WmsExistsReconciler> logger)
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
                await EjecutarCicloAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado en el ciclo de confirmación de éxito WMS");
            }

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
        List<PortalSaas.Abstractions.Modelos.ModuleCompanyDto> companias;
        using (var scope = _scopeFactory.CreateScope())
        {
            var externalDb = scope.ServiceProvider.GetRequiredService<IExternalDatabaseConnectionService>();
            companias = (await externalDb.ListActiveCompanyIdsAsync("Wms", cancellationToken)).ToList();
        }

        foreach (var compania in companias)
        {
            await ProcesarCompaniaAsync(compania.CompanyId, cancellationToken);
        }
    }

    private async Task ProcesarCompaniaAsync(Guid companyId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IIntegrationConnectorConfigService>();
        var configJson = await configService.GetDecryptedConfigAsync(companyId, "Wms", "WmsCloud", cancellationToken);
        if (configJson is null)
        {
            return;
        }

        var config = System.Text.Json.JsonSerializer.Deserialize<WmsCloudConfigParaReconciliacion>(configJson);
        if (config is null || string.IsNullOrWhiteSpace(config.LgfApiBaseUrl))
        {
            return;
        }

        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var validador = scope.ServiceProvider.GetRequiredService<IWmsValidationApiClient>();

        await ProcesarEntidadAsync(contexto, validador, config, companyId, "Item", "item", "item_alternate_code",
            contexto.WmsSapStageItems.Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Enviado).ToList,
            f => f.ItemCode, (f) => { f.Status = WmsSapStageStatus.ProcesadoWms; f.SyncedAt = DateTimeOffset.UtcNow; },
            (f) => f.Status = WmsSapStageStatus.ErrorWms, cancellationToken);

        await ProcesarEntidadAsync(contexto, validador, config, companyId, "Store", "facility", "code",
            contexto.WmsSapStageStores.Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Enviado).ToList,
            f => f.CardCode, (f) => { f.Status = WmsSapStageStatus.ProcesadoWms; f.SyncedAt = DateTimeOffset.UtcNow; },
            (f) => f.Status = WmsSapStageStatus.ErrorWms, cancellationToken);

        await ProcesarEntidadAsync(contexto, validador, config, companyId, "Order", "order_hdr", "order_nbr",
            contexto.WmsSapStageOrderHdrs.Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Enviado).ToList,
            f => f.OrderNbr, (f) => { f.Status = WmsSapStageStatus.ProcesadoWms; f.SyncedAt = DateTimeOffset.UtcNow; },
            (f) => f.Status = WmsSapStageStatus.ErrorWms, cancellationToken);

        await ProcesarEntidadAsync(contexto, validador, config, companyId, "IbShipment", "ib_shipment", "shipment_nbr",
            contexto.WmsSapStageInboundHdrs.Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Enviado).ToList,
            f => f.SapDocEntry.ToString(), (f) => { f.Status = WmsSapStageStatus.ProcesadoWms; f.SyncedAt = DateTimeOffset.UtcNow; },
            (f) => f.Status = WmsSapStageStatus.ErrorWms, cancellationToken);
    }

    private async Task ProcesarEntidadAsync<TFila>(
        WmsDbContext contexto, IWmsValidationApiClient validador, WmsCloudConfigParaReconciliacion config, Guid companyId,
        string tipoDoc, string entidadFinal, string keyField,
        Func<List<TFila>> obtenerPendientes, Func<TFila, string> obtenerClave,
        Action<TFila> marcarConfirmado, Action<TFila> marcarErrorDefinitivo,
        CancellationToken cancellationToken)
        where TFila : class
    {
        var pendientes = obtenerPendientes();
        foreach (var fila in pendientes)
        {
            var clave = obtenerClave(fila);
            var resultado = await validador.CheckStageRecordAsync(
                config.LgfApiBaseUrl!, config.Usuario, config.Clave, entidadFinal, keyField, clave, config.ParentCompanyCode,
                filtrarPorUrl: false, cancellationToken);

            var validacion = await contexto.WmsExportValidations
                .FirstOrDefaultAsync(v => v.CompanyId == companyId && v.TipoDoc == tipoDoc && v.Clave == clave, cancellationToken);
            if (validacion is null)
            {
                validacion = new WmsExportValidation { CompanyId = companyId, TipoDoc = tipoDoc, Clave = clave };
                contexto.WmsExportValidations.Add(validacion);
            }

            if (resultado.Found)
            {
                marcarConfirmado(fila);
                validacion.WmsStatusId = resultado.StatusId;
                validacion.ValidadoEn = DateTimeOffset.UtcNow;
                continue;
            }

            validacion.Intentos++;
            if (validacion.Intentos >= MaxIntentos)
            {
                marcarErrorDefinitivo(fila);
                validacion.WmsErrorMsg = $"No confirmado en Oracle WMS Cloud tras {validacion.Intentos} intentos.";
                validacion.ValidadoEn = DateTimeOffset.UtcNow;
            }
        }

        await contexto.SaveChangesAsync(cancellationToken);
    }

    private sealed record WmsCloudConfigParaReconciliacion(string Usuario, string Clave, string ParentCompanyCode, string? LgfApiBaseUrl);
}
```

**Note on `filtrarPorUrl: false`**: final entities (`item`/`facility`/`order_hdr`/`ib_shipment`) may contain rows from other companies with the same business key, so filtering happens client-side inside `WmsValidationApiClient.MatchesCompany` (Task 5) rather than via the URL's `company_code` query param — this mirrors the legacy comment about final entities specifically (see the design spec).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsExistsReconcilerTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Register as hosted service**

Edit `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs`, next to `services.AddHostedService<WmsStageErrorReconciler>();`, add:

```csharp
services.AddHostedService<WmsExistsReconciler>();
```

- [ ] **Step 6: Run the FULL test suite and build both repos**

```bash
dotnet build "src/Modulo.Wms/Modulo.Wms.csproj" -c Release
dotnet test "tests/Modulo.Wms.Tests"
```
Expected: 0 build errors, all tests pass.

Also confirm the Core repo still builds (Task 1/3 touched it):
```bash
dotnet build "Portal SaaS - Core/src/PortalSaas.Host/PortalSaas.Host.csproj" -c Release
```
Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/Modulo.Wms/Services/WmsExistsReconciler.cs src/Modulo.Wms/ModuloWms.cs tests/Modulo.Wms.Tests/Services/WmsExistsReconcilerTests.cs
git commit -m "feat: WmsExistsReconciler -- confirma exito real en entidad final via LGFAPI, tope 20 intentos"
```

---

## Self-Review Notes

- **Spec coverage**: contract for cross-repo config access (Task 1), `Enviado` intermediate status (Task 2), real batching + config fields (Task 3), reconciliation table (Task 4), LGFAPI client (Task 5), both reconciliation workers (Task 6, Task 7) — every section of the design spec maps to a task. The spec's explicit "fuera de alcance" items (bulk paginated query, auto-resend on reconciliation error, Bajada batching, Estado del Servicio UI) are intentionally not tasked.
- **Placeholder scan**: none found — every step has literal code, exact file paths, and exact commands.
- **Type consistency**: `WmsStageCheckResult(bool Found, int? StatusId, string? ErrorMessage)` defined in Task 5, used identically in Task 6 and Task 7's fakes and real consumption. `IIntegrationConnectorConfigService.GetDecryptedConfigAsync(Guid, string, string, CancellationToken)` defined in Task 1, called identically (`companyId, "Wms", "WmsCloud", ct`) in Task 6 and Task 7. `WmsSapStageStatus.Enviado` introduced in Task 2, consumed by Task 3 (connector no longer sets `ProcesadoWms`), Task 6/7 (query filter and terminal transitions). `WmsExportValidation` fields match between Task 4's model and Task 6/7's usage (`WmsStatusId`, `WmsErrorMsg`, `ValidadoEn`, `Intentos`).
- **Cross-task duplication note**: Task 6 and Task 7 each define a private `WmsCloudConfigParaReconciliacion` record and duplicate the fake classes/`BuildProvider` helper in their test files. This is intentional YAGNI — extracting a shared base class for two files is not worth the indirection at this scale; flagged here so the implementer doesn't "fix" it by over-abstracting mid-task.
