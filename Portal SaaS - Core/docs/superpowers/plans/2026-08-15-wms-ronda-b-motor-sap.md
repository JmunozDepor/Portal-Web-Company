# Migración Wms — Ronda B (Motor + SAP) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cerrar el ciclo WMS→SAP del piloto: leer `wms_oracle_stage_slsh` (producido por Ronda A), mapearlo a un `StockTransfer` real de SAP vía Copy-From, y marcar cada fila como procesada o con error.

**Architecture:** Copy-From (`BaseType`/`BaseEntry`/`BaseLine`) agregado a los 3 motores genéricos de documento (paridad). Nuevo `WmsSlshInventoryReader` en el plugin `Modulo.Wms` implementa `IIntegrationEntityReader` (extendida con un método de ack). `SapDocumentConnector` (rama `Inventory`) construye el `InventoryDocumentDto` real. `IntegrationSyncHostedService` deja de usar el hueco hardcodeado y cablea reader→mapeo→conector→ack.

**Tech Stack:** .NET 8, EF Core 8, xUnit + EF Core InMemory, SAP Business One Service Layer (Copy-From estándar: `BaseType`/`BaseEntry`/`BaseLine` por línea).

## Global Constraints

- Copy-From se agrega a los 3 motores (`SalesDocumentLineDto`/`PurchaseDocumentLineDto`/`InventoryDocumentLineDto` + sus `Sap*Line` internos) aunque solo Inventario tenga consumidor real en esta ronda — decisión de paridad ya tomada.
- `InventoryDocumentLineDto.FromWarehouseCode`/`ToWarehouseCode` pasan de `string` a `string?` — SAP los resuelve del documento base en un Copy-From, no hace falta enviarlos. Cambio retrocompatible (los llamadores existentes que ya pasan un string concreto siguen funcionando sin cambios).
- `IIntegrationEntityWriter` (dirección Bajada) NO se toca — sigue fuera de alcance.
- El mecanismo real de Copy-From en SAP B1 Service Layer es `BaseType`/`BaseEntry`/`BaseLine` por línea del documento — NO usar el patrón `DocumentReferences` del sistema legado (confirmado sospechoso/no estándar durante el diseño).
- `IntegrationRecord.Fields` puede contener un campo `"Lineas"` con `List<IntegrationRecord>` anidado — primer precedente de anidado en este tipo, documentarlo con un comentario en el código que lo produce/consume.
- `_StagingLineIds` (prefijo `_`) es metadata interna del reader — el motor genérico nunca la interpreta, solo la transporta de vuelta en `MarcarProcesadoAsync`.
- Tests: xUnit + `UseInMemoryDatabase(Guid.NewGuid().ToString())`, sin mocking framework, siguiendo el patrón ya establecido en `tests/PortalSaas.Core.Tests/` y `tests/Modulo.Wms.Tests/`.
- Multi-repo: Task 1, 2, 5 viven en `Portal SaaS - Core`; Task 3 vive en `Modulo.Wms` (`Portal SaaS - Plugins`); Task 4 vive en `Portal SaaS - Core` (`SapDocumentConnector` está en `PortalSaas.Core`).

---

## File Structure

**Modificados en `Portal SaaS - Core`:**
- `src/PortalSaas.Abstractions/Modelos/InventoryDocumentDto.cs` — `BaseType`/`BaseEntry`/`BaseLine` en `InventoryDocumentLineDto`, `FromWarehouseCode`/`ToWarehouseCode` a nullable.
- `src/PortalSaas.Abstractions/Modelos/SalesDocumentDto.cs` (o donde viva `SalesDocumentLineDto`) — mismos 3 campos.
- `src/PortalSaas.Abstractions/Modelos/PurchaseDocumentDto.cs` (o equivalente) — mismos 3 campos.
- `src/PortalSaas.Core/Inventario/SapInventoryDocumentModels.cs` — `BaseType`/`BaseEntry`/`BaseLine` en `SapInventoryDocumentLine`.
- `src/PortalSaas.Core/Ventas/SapSalesDocumentModels.cs` — mismos 3 campos en `SapSalesDocumentLine`.
- `src/PortalSaas.Core/Compras/SapPurchaseDocumentModels.cs` — mismos 3 campos en `SapPurchaseDocumentLine`.
- `src/PortalSaas.Core/Inventario/InventoryDocumentService.cs` — `MapLine` puebla los 3 campos nuevos.
- `src/PortalSaas.Core/Ventas/SalesDocumentService.cs` — `MapLine` puebla los 3 campos nuevos.
- `src/PortalSaas.Core/Compras/PurchaseDocumentService.cs` — `MapLine` puebla los 3 campos nuevos.
- `src/PortalSaas.Abstractions/Contratos/Integraciones/IIntegrationEntityReader.cs` — nuevo método `MarcarProcesadoAsync`.
- `src/PortalSaas.Core/Integraciones/SapDocumentConnector.cs` — rama `Inventory` implementada, `foreach` con aislamiento por registro.
- `src/PortalSaas.Integrations/IntegrationSyncHostedService.cs` — rama `Subida` cableada al reader real.

**Nuevos/modificados en `Modulo.Wms` (`Portal SaaS - Plugins/Modulo.Wms`):**
- `src/Modulo.Wms/Services/WmsSlshInventoryReader.cs` (nuevo)
- `src/Modulo.Wms/ModuloWms.cs` — registrar `IIntegrationEntityReader` → `WmsSlshInventoryReader`.
- `tests/Modulo.Wms.Tests/Services/WmsSlshInventoryReaderTests.cs` (nuevo)

---

### Task 1: Copy-From en los 3 motores genéricos

**Files:**
- Modify: `src/PortalSaas.Abstractions/Modelos/InventoryDocumentDto.cs`, `SalesDocumentDto.cs`, `PurchaseDocumentDto.cs` (o los archivos reales donde vivan `InventoryDocumentLineDto`/`SalesDocumentLineDto`/`PurchaseDocumentLineDto` — confirmar ruta exacta en el Step 1)
- Modify: `src/PortalSaas.Core/Inventario/SapInventoryDocumentModels.cs`, `src/PortalSaas.Core/Ventas/SapSalesDocumentModels.cs`, `src/PortalSaas.Core/Compras/SapPurchaseDocumentModels.cs`
- Modify: `src/PortalSaas.Core/Inventario/InventoryDocumentService.cs`, `src/PortalSaas.Core/Ventas/SalesDocumentService.cs`, `src/PortalSaas.Core/Compras/PurchaseDocumentService.cs`
- Test: `tests/PortalSaas.Core.Tests/Inventario/InventoryDocumentServiceMapLineTests.cs` (nuevo, o agregar a un archivo de test existente de `InventoryDocumentService` si ya existe — confirmar en Step 1)

**Interfaces:**
- Consumes: nada de tareas anteriores.
- Produces: `InventoryDocumentLineDto` con `BaseType`/`BaseEntry`/`BaseLine` (`int?`) y `FromWarehouseCode`/`ToWarehouseCode` como `string?` — consumido por `SapDocumentConnector` (Task 4). `SalesDocumentLineDto`/`PurchaseDocumentLineDto` con los mismos 3 campos nuevos, sin consumidor en esta ronda (solo paridad).

- [ ] **Step 1: Localizar los archivos reales y confirmar la forma exacta de las clases a modificar**

Run: `grep -rn "class InventoryDocumentLineDto\|record InventoryDocumentLineDto" "src/PortalSaas.Abstractions/"` (y lo mismo para `SalesDocumentLineDto`/`PurchaseDocumentLineDto`, y para `SapInventoryDocumentLine`/`SapSalesDocumentLine`/`SapPurchaseDocumentLine` en `src/PortalSaas.Core/`)
Expected: confirma la ruta exacta de cada archivo (puede diferir levemente de la ruta propuesta arriba) y si son `record`/`class`, y si usan parámetros posicionales (como `InventoryDocumentDto`, que es un `record` con constructor posicional) o propiedades con `{ get; set; }` (como `SapInventoryDocumentLine`, que es una `class` mutable). Ajustar los Steps siguientes según lo que se encuentre — el patrón real ya confirmado para `InventoryDocumentLineDto`/`SapInventoryDocumentLine` es:

```csharp
// InventoryDocumentLineDto es un record con constructor posicional:
public sealed record InventoryDocumentLineDto(
    string ItemCode, string? Description, decimal Quantity,
    string? FromWarehouseCode, string? ToWarehouseCode,   // <- ya no string, ahora string?
    int? BaseType = null, int? BaseEntry = null, int? BaseLine = null,
    IReadOnlyDictionary<string, object?>? AdditionalFields = null);

// SapInventoryDocumentLine es una class interna mutable:
internal sealed class SapInventoryDocumentLine
{
    public int? LineNum { get; set; }
    public string ItemCode { get; set; } = null!;
    public decimal Quantity { get; set; }
    public string? WarehouseCode { get; set; }        // también pasa a nullable
    public string? FromWarehouseCode { get; set; }     // también pasa a nullable
    public int? BaseType { get; set; }
    public int? BaseEntry { get; set; }
    public int? BaseLine { get; set; }
    public IReadOnlyDictionary<string, object?>? AdditionalFields { get; set; }
}
```

Nota: `SapInventoryDocumentLine.WarehouseCode` (el destino) también debe pasar a `string?` junto con `FromWarehouseCode`, por el mismo motivo (Copy-From no necesita ninguno de los dos).

- [ ] **Step 2: Aplicar el cambio a `InventoryDocumentLineDto` y `SapInventoryDocumentLine`**

Según lo confirmado en Step 1: agregar `BaseType`/`BaseEntry`/`BaseLine` (`int?`, default `null` si el tipo lo permite) a ambas clases, y cambiar `FromWarehouseCode`/`ToWarehouseCode` (DTO) / `FromWarehouseCode`/`WarehouseCode` (Sap-model) de `string` a `string?`.

- [ ] **Step 3: Actualizar `InventoryDocumentService.MapLine`**

Ubicar (ya confirmado, `src/PortalSaas.Core/Inventario/InventoryDocumentService.cs:187-194`):

```csharp
private static SapInventoryDocumentLine MapLine(InventoryDocumentLineDto line) => new()
{
    ItemCode = line.ItemCode,
    Quantity = line.Quantity,
    WarehouseCode = line.ToWarehouseCode,
    FromWarehouseCode = line.FromWarehouseCode,
    AdditionalFields = line.AdditionalFields,
};
```

Agregar las 3 líneas nuevas:

```csharp
private static SapInventoryDocumentLine MapLine(InventoryDocumentLineDto line) => new()
{
    ItemCode = line.ItemCode,
    Quantity = line.Quantity,
    WarehouseCode = line.ToWarehouseCode,
    FromWarehouseCode = line.FromWarehouseCode,
    BaseType = line.BaseType,
    BaseEntry = line.BaseEntry,
    BaseLine = line.BaseLine,
    AdditionalFields = line.AdditionalFields,
};
```

- [ ] **Step 4: Escribir el test que falla**

```csharp
using PortalSaas.Abstractions.Modelos;
using Xunit;

namespace PortalSaas.Core.Tests.Inventario;

public class InventoryDocumentServiceMapLineTests
{
    [Fact]
    public void InventoryDocumentLineDto_AceptaCamposDeCopyFrom()
    {
        var linea = new InventoryDocumentLineDto(
            ItemCode: "ITEM-001",
            Description: null,
            Quantity: 10,
            FromWarehouseCode: null,
            ToWarehouseCode: null,
            BaseType: 1250000001,
            BaseEntry: 42,
            BaseLine: 0);

        Assert.Equal(1250000001, linea.BaseType);
        Assert.Equal(42, linea.BaseEntry);
        Assert.Equal(0, linea.BaseLine);
        Assert.Null(linea.FromWarehouseCode);
        Assert.Null(linea.ToWarehouseCode);
    }
}
```

Ajustar el orden/nombres de los parámetros posicionales del `record` exactamente como quedó confirmado en Step 1 (el orden importa en un constructor posicional de C#).

- [ ] **Step 5: Ejecutar el test y verificar que falla**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter InventoryDocumentServiceMapLineTests`
Expected: FAIL — `InventoryDocumentLineDto` no acepta todavía `BaseType`/`BaseEntry`/`BaseLine`, o `FromWarehouseCode`/`ToWarehouseCode` no son nullable.

- [ ] **Step 6: Aplicar Steps 2-3 (si no se hizo ya) y verificar que el test pasa**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter InventoryDocumentServiceMapLineTests`
Expected: PASS.

- [ ] **Step 7: Replicar el mismo cambio (sin test nuevo, es paridad sin consumidor) en `SalesDocumentLineDto`/`SapSalesDocumentLine`/`SalesDocumentService.MapLine` y `PurchaseDocumentLineDto`/`SapPurchaseDocumentLine`/`PurchaseDocumentService.MapLine`**

Mismo patrón: agregar `BaseType`/`BaseEntry`/`BaseLine` (`int?`) a la línea DTO y a la línea Sap-model correspondiente, poblar en el `MapLine` respectivo. NO cambiar `WarehouseCode`/`AccountCode` de estos dos motores a nullable — ya son nullable hoy (confirmado en la investigación: `SapSalesDocumentLine.WarehouseCode`/`AccountCode` ya son `string?`), ese cambio solo aplicaba a Inventario.

- [ ] **Step 8: Compilar la solución completa**

Run: `dotnet build PortalSaas.sln`
Expected: Build succeeded, 0 warnings, 0 errors. Si `FromWarehouseCode`/`ToWarehouseCode` ahora nullable rompe algún llamador existente de `InventoryDocumentLineDto` que asumía no-nullable (poco probable en C#, pasar de `string` a `string?` no rompe callers que ya pasan un valor concreto, pero si algún código hace `.Length`/etc. sobre el valor sin chequeo de null, el compilador puede advertir con nullable warnings) — resolver cualquier warning de nulabilidad que aparezca en el código existente de `Modulo.Ventas|Compras|Inventario` (los plugins de digitación directa), agregando un chequeo de null razonable si hace falta, sin cambiar su comportamiento funcional.

- [ ] **Step 9: Ejecutar toda la suite de tests**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj`
Expected: todos los tests pasan (los anteriores + el nuevo de este task).

- [ ] **Step 10: Commit**

```bash
git add src/PortalSaas.Abstractions/Modelos/ src/PortalSaas.Core/Inventario/ src/PortalSaas.Core/Ventas/ src/PortalSaas.Core/Compras/ tests/PortalSaas.Core.Tests/Inventario/InventoryDocumentServiceMapLineTests.cs
git commit -m "feat: agregar soporte Copy-From (BaseType/BaseEntry/BaseLine) a los 3 motores genéricos"
```

---

### Task 2: Ack en `IIntegrationEntityReader`

**Files:**
- Modify: `src/PortalSaas.Abstractions/Contratos/Integraciones/IIntegrationEntityReader.cs`

**Interfaces:**
- Consumes: nada de tareas anteriores.
- Produces: `IIntegrationEntityReader.MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken)` — implementado por `WmsSlshInventoryReader` (Task 3), llamado por `IntegrationSyncHostedService` (Task 5).

- [ ] **Step 1: Agregar el método a la interfaz**

```csharp
namespace PortalSaas.Abstractions.Contratos.Integraciones;

public interface IIntegrationEntityReader
{
    string EntidadNegocio { get; }
    Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken);
    Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Compilar `PortalSaas.Abstractions`**

Run: `dotnet build src/PortalSaas.Abstractions/PortalSaas.Abstractions.csproj`
Expected: Build succeeded, 0 warnings, 0 errors (no debería haber ningún implementador de `IIntegrationEntityReader` todavía en este repo, así que agregar un método nuevo no rompe nada existente — confirmar con `grep -rn "IIntegrationEntityReader" --include=*.cs .` que no hay implementadores fuera de este archivo antes de continuar).

- [ ] **Step 3: Commit**

```bash
git add src/PortalSaas.Abstractions/Contratos/Integraciones/IIntegrationEntityReader.cs
git commit -m "feat: agregar MarcarProcesadoAsync a IIntegrationEntityReader"
```

---

### Task 3: `WmsSlshInventoryReader` (plugin `Modulo.Wms`)

**Files:**
- Create: `src/Modulo.Wms/Services/WmsSlshInventoryReader.cs`
- Modify: `src/Modulo.Wms/ModuloWms.cs`
- Test: `tests/Modulo.Wms.Tests/Services/WmsSlshInventoryReaderTests.cs`

**Interfaces:**
- Consumes: `IIntegrationEntityReader` (Task 2, cruza el repo — `Modulo.Wms.csproj` ya referencia `PortalSaas.Abstractions`, confirmado en rondas anteriores); `WmsOracleStageSlsh`, `WmsOracleInboundStage`, `WmsDbContext` (ya existentes de Ronda A).
- Produces: `WmsSlshInventoryReader : IIntegrationEntityReader`, `EntidadNegocio = "Wms.ConfirmacionTraslado"` — consumido por `IntegrationSyncHostedService` (Task 5) vía `GetServices<IIntegrationEntityReader>()`.

**Nota multi-repo**: trabajar desde el worktree/checkout de `Modulo.Wms`; si se usa un worktree anidado, puede requerir la junction local documentada en Ronda A para resolver la referencia a `PortalSaas.Abstractions` — recrearla si hace falta.

- [ ] **Step 1: Escribir el test que falla**

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSlshInventoryReaderTests
{
    private static WmsDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(options);
    }

    private static async Task<long> SembrarInboundStageAsync(WmsDbContext contexto, Guid companyId)
    {
        var stage = new WmsOracleInboundStage
        {
            CompanyId = companyId,
            TipoDoc = "SLSH",
            Formato = WmsInboundFormato.Xml,
            NombreArchivo = "test.xml",
            HashArchivo = Guid.NewGuid().ToString(),
            Contenido = "<xml/>",
            Estado = WmsInboundEstado.Aplanado,
        };
        contexto.WmsOracleInboundStages.Add(stage);
        await contexto.SaveChangesAsync();
        return stage.Id;
    }

    [Fact]
    public async Task LeerPendientesAsync_AgrupaLineasPorDocumentoBase()
    {
        await using var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var parentId = await SembrarInboundStageAsync(contexto, companyId);

        contexto.WmsOracleStageSlsh.Add(new WmsOracleStageSlsh
        {
            ParentId = parentId,
            Status = WmsSlshStatus.Pendiente,
            order_hdr_cust_field_4 = "42",
            order_dtl_cust_number_2 = "0",
            item_part_a = "ITEM-A",
            shipped_qty = "10",
        });
        contexto.WmsOracleStageSlsh.Add(new WmsOracleStageSlsh
        {
            ParentId = parentId,
            Status = WmsSlshStatus.Pendiente,
            order_hdr_cust_field_4 = "42",
            order_dtl_cust_number_2 = "1",
            item_part_a = "ITEM-B",
            shipped_qty = "5",
        });
        await contexto.SaveChangesAsync();

        var reader = new WmsSlshInventoryReader(contexto);
        var registros = await reader.LeerPendientesAsync(companyId, CancellationToken.None);

        Assert.Single(registros);
        var lineas = (List<IntegrationRecord>)registros[0]["Lineas"]!;
        Assert.Equal(2, lineas.Count);
        Assert.Equal("ITEM-A", lineas[0]["ItemCode"]);
        Assert.Equal(42, lineas[0]["BaseEntry"]);
    }

    [Fact]
    public async Task LeerPendientesAsync_SoloTraeFilasDeLaCompaniaPedida()
    {
        await using var contexto = CrearContexto();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var parentA = await SembrarInboundStageAsync(contexto, companyA);
        var parentB = await SembrarInboundStageAsync(contexto, companyB);

        contexto.WmsOracleStageSlsh.Add(new WmsOracleStageSlsh { ParentId = parentA, Status = WmsSlshStatus.Pendiente, order_hdr_cust_field_4 = "1", item_part_a = "A" });
        contexto.WmsOracleStageSlsh.Add(new WmsOracleStageSlsh { ParentId = parentB, Status = WmsSlshStatus.Pendiente, order_hdr_cust_field_4 = "2", item_part_a = "B" });
        await contexto.SaveChangesAsync();

        var reader = new WmsSlshInventoryReader(contexto);
        var registros = await reader.LeerPendientesAsync(companyA, CancellationToken.None);

        Assert.Single(registros);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_ConExito_ActualizaStatusAProcesadoSap()
    {
        await using var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var parentId = await SembrarInboundStageAsync(contexto, companyId);
        var fila = new WmsOracleStageSlsh { ParentId = parentId, Status = WmsSlshStatus.Pendiente, order_hdr_cust_field_4 = "42", item_part_a = "A" };
        contexto.WmsOracleStageSlsh.Add(fila);
        await contexto.SaveChangesAsync();

        var reader = new WmsSlshInventoryReader(contexto);
        var registros = await reader.LeerPendientesAsync(companyId, CancellationToken.None);
        await reader.MarcarProcesadoAsync(companyId, registros[0], exito: true, mensajeError: null, CancellationToken.None);

        var filaActualizada = await contexto.WmsOracleStageSlsh.FirstAsync(f => f.LineId == fila.LineId);
        Assert.Equal(WmsSlshStatus.ProcesadoSap, filaActualizada.Status);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_ConError_ActualizaStatusAErrorSapConMensaje()
    {
        await using var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var parentId = await SembrarInboundStageAsync(contexto, companyId);
        var fila = new WmsOracleStageSlsh { ParentId = parentId, Status = WmsSlshStatus.Pendiente, order_hdr_cust_field_4 = "42", item_part_a = "A" };
        contexto.WmsOracleStageSlsh.Add(fila);
        await contexto.SaveChangesAsync();

        var reader = new WmsSlshInventoryReader(contexto);
        var registros = await reader.LeerPendientesAsync(companyId, CancellationToken.None);
        await reader.MarcarProcesadoAsync(companyId, registros[0], exito: false, mensajeError: "SAP rechazó el documento", CancellationToken.None);

        var filaActualizada = await contexto.WmsOracleStageSlsh.FirstAsync(f => f.LineId == fila.LineId);
        Assert.Equal(WmsSlshStatus.ErrorSap, filaActualizada.Status);
        Assert.Equal("SAP rechazó el documento", filaActualizada.ErrorMsg);
    }
}
```

- [ ] **Step 2: Ejecutar el test y verificar que falla**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsSlshInventoryReaderTests`
Expected: FAIL — `WmsSlshInventoryReader` no existe.

- [ ] **Step 3: Implementar `WmsSlshInventoryReader`**

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

public class WmsSlshInventoryReader : IIntegrationEntityReader
{
    private readonly WmsDbContext _contexto;

    public WmsSlshInventoryReader(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "Wms.ConfirmacionTraslado";

    public async Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var filas = await (
            from linea in _contexto.WmsOracleStageSlsh
            join stage in _contexto.WmsOracleInboundStages on linea.ParentId equals stage.Id
            where stage.CompanyId == companyId && linea.Status == WmsSlshStatus.Pendiente
            select linea
        ).ToListAsync(cancellationToken);

        var grupos = filas.GroupBy(f => f.order_hdr_cust_field_4);
        var registros = new List<IntegrationRecord>();

        foreach (var grupo in grupos)
        {
            var lineas = new List<IntegrationRecord>();
            var idsDeLinea = new List<long>();

            foreach (var fila in grupo)
            {
                idsDeLinea.Add(fila.LineId);
                lineas.Add(new IntegrationRecord(new Dictionary<string, object?>
                {
                    ["ItemCode"] = fila.item_part_a,
                    ["Quantity"] = fila.shipped_qty,
                    ["BaseType"] = 1250000001,
                    ["BaseEntry"] = ParseIntOrNull(fila.order_hdr_cust_field_4),
                    ["BaseLine"] = ParseIntOrNull(fila.order_dtl_cust_number_2),
                }));
            }

            registros.Add(new IntegrationRecord(new Dictionary<string, object?>
            {
                ["TipoDocumento"] = "Inventory",
                ["Lineas"] = lineas,
                ["_StagingLineIds"] = idsDeLinea,
            }));
        }

        return registros;
    }

    public async Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken)
    {
        var idsDeLinea = (List<long>)registro["_StagingLineIds"]!;

        var filas = await _contexto.WmsOracleStageSlsh
            .Where(f => idsDeLinea.Contains(f.LineId))
            .ToListAsync(cancellationToken);

        foreach (var fila in filas)
        {
            fila.Status = exito ? WmsSlshStatus.ProcesadoSap : WmsSlshStatus.ErrorSap;
            fila.ErrorMsg = exito ? null : mensajeError;
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }

    private static int? ParseIntOrNull(string? valor) => int.TryParse(valor, out var resultado) ? resultado : null;
}
```

- [ ] **Step 4: Ejecutar el test y verificar que pasa**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsSlshInventoryReaderTests`
Expected: PASS, 4/4.

- [ ] **Step 5: Registrar en `ModuloWms.RegisterServices`**

```csharp
services.AddScoped<IIntegrationEntityReader, WmsSlshInventoryReader>();
```

Agregar `using PortalSaas.Abstractions.Contratos.Integraciones;` si no está presente.

- [ ] **Step 6: Compilar y ejecutar toda la suite de tests del plugin**

Run: `dotnet build src/Modulo.Wms/Modulo.Wms.csproj && dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj`
Expected: Build succeeded, todos los tests pasan.

- [ ] **Step 7: Commit**

```bash
git add src/Modulo.Wms/Services/WmsSlshInventoryReader.cs src/Modulo.Wms/ModuloWms.cs tests/Modulo.Wms.Tests/Services/WmsSlshInventoryReaderTests.cs
git commit -m "feat: agregar WmsSlshInventoryReader"
```

---

### Task 4: Mapeo real en `SapDocumentConnector` (rama `Inventory`)

**Files:**
- Modify: `src/PortalSaas.Core/Integraciones/SapDocumentConnector.cs`
- Test: `tests/PortalSaas.Core.Tests/Integraciones/SapDocumentConnectorTests.cs` (agregar casos nuevos al archivo existente de la ronda base)

**Interfaces:**
- Consumes: `IntegrationRecord` con `Fields["Lineas"]` (`List<IntegrationRecord>`, Task 3); `IInventoryDocumentService.CreateAsync`, `InventoryDocumentDto`/`InventoryDocumentLineDto` (Task 1); `InventoryDocumentType.StockTransfer` (ya existente).
- Produces: `SapDocumentConnector.PushAsync` con la rama `Inventory` funcional — hoja de esta cadena, consumida por `IntegrationSyncHostedService` (Task 5) de forma indirecta (a través de la interfaz `IIntegrationConnector`, sin cambio de firma).

- [ ] **Step 1: Leer el archivo actual para confirmar el `switch` exacto de `PushAsync`**

Run: `cat "src/PortalSaas.Core/Integraciones/SapDocumentConnector.cs"` (o abrir el archivo)
Expected: confirma el `foreach (var registro in registros)` y el `switch (tipoDocumento)` con la rama `case "Inventory":` lanzando `NotSupportedException` — este task reemplaza SOLO esa rama, sin tocar `Sales`/`Purchase`/`default`/`PullAsync`.

- [ ] **Step 2: Escribir los tests que fallan**

Agregar al archivo de test existente (`tests/PortalSaas.Core.Tests/Integraciones/SapDocumentConnectorTests.cs`), usando los mismos fakes de servicios ya definidos ahí (`SalesDocumentServiceFalso`/`PurchaseDocumentServiceFalso`/`InventoryDocumentServiceFalso` de la ronda base — para este test, `InventoryDocumentServiceFalso` necesita dejar de lanzar `InvalidOperationException` en `CreateAsync` y en cambio registrar la llamada):

```csharp
[Fact]
public async Task PushAsync_ConTipoInventoryYLineas_LlamaCreateAsyncConStockTransfer()
{
    InventoryDocumentType? tipoUsado = null;
    InventoryDocumentDto? dtoUsado = null;
    var inventoryServiceFalso = new InventoryDocumentServiceCapturador((tipo, usuario, dto, ct) =>
    {
        tipoUsado = tipo;
        dtoUsado = dto;
        return Task.FromResult(999);
    });
    var conector = new SapDocumentConnector(new SalesDocumentServiceFalso(), new PurchaseDocumentServiceFalso(), inventoryServiceFalso);

    var lineas = new List<IntegrationRecord>
    {
        new(new Dictionary<string, object?> { ["ItemCode"] = "ITEM-A", ["Quantity"] = 10m, ["BaseType"] = 1250000001, ["BaseEntry"] = 42, ["BaseLine"] = 0 }),
    };
    var registros = new List<IntegrationRecord>
    {
        new(new Dictionary<string, object?> { ["TipoDocumento"] = "Inventory", ["Lineas"] = lineas }),
    };

    await conector.PushAsync("{}", registros, CancellationToken.None);

    Assert.Equal(InventoryDocumentType.StockTransfer, tipoUsado);
    Assert.NotNull(dtoUsado);
    Assert.Single(dtoUsado!.Lines);
    Assert.Equal("ITEM-A", dtoUsado.Lines[0].ItemCode);
    Assert.Equal(42, dtoUsado.Lines[0].BaseEntry);
}
```

Definir el fake capturador (agregarlo junto a los otros fakes del mismo archivo):

```csharp
private sealed class InventoryDocumentServiceCapturador : IInventoryDocumentService
{
    private readonly Func<InventoryDocumentType, string, InventoryDocumentDto, CancellationToken, Task<int>> _onCreate;

    public InventoryDocumentServiceCapturador(Func<InventoryDocumentType, string, InventoryDocumentDto, CancellationToken, Task<int>> onCreate)
    {
        _onCreate = onCreate;
    }

    public Task<bool> CanCreateAsync(InventoryDocumentType type, CancellationToken ct = default) => Task.FromResult(true);
    public Task<int> CreateAsync(InventoryDocumentType type, string portalUsername, InventoryDocumentDto document, CancellationToken ct = default)
        => _onCreate(type, portalUsername, document, ct);
}
```

- [ ] **Step 3: Ejecutar el test y verificar que falla**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter PushAsync_ConTipoInventoryYLineas_LlamaCreateAsyncConStockTransfer`
Expected: FAIL — la rama `Inventory` todavía lanza `NotSupportedException`.

- [ ] **Step 4: Implementar la rama `Inventory` de `PushAsync`**

Reemplazar SOLO el `case "Inventory":` (dejar `Sales`/`Purchase`/`default` intactos):

```csharp
case "Inventory":
{
    var lineasRaw = (List<IntegrationRecord>)(registro["Lineas"] ?? new List<IntegrationRecord>());
    var lineasDto = lineasRaw.Select(l => new InventoryDocumentLineDto(
        ItemCode: (string)l["ItemCode"]!,
        Description: null,
        Quantity: Convert.ToDecimal(l["Quantity"]),
        FromWarehouseCode: null,
        ToWarehouseCode: null,
        BaseType: (int?)l["BaseType"],
        BaseEntry: (int?)l["BaseEntry"],
        BaseLine: (int?)l["BaseLine"])).ToList();

    var dto = new InventoryDocumentDto(
        DocDate: DateOnly.FromDateTime(DateTime.UtcNow),
        Comments: null,
        Lines: lineasDto);

    await _inventoryDocumentService.CreateAsync(InventoryDocumentType.StockTransfer, "wms-integration", dto, cancellationToken);
    break;
}
```

Ajustar el orden/nombres exactos de los parámetros posicionales de `InventoryDocumentDto`/`InventoryDocumentLineDto` según lo confirmado en Task 1 Step 1 si difiere de lo mostrado aquí.

- [ ] **Step 5: Ejecutar el test y verificar que pasa**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter PushAsync_ConTipoInventoryYLineas_LlamaCreateAsyncConStockTransfer`
Expected: PASS.

- [ ] **Step 6: Aislamiento por registro — envolver el `foreach` de `PushAsync`**

Ubicar el `foreach (var registro in registros)` de `PushAsync` y envolver el cuerpo del `switch` en un try/catch que acumule errores por registro en vez de abortar todo el método al primer fallo:

```csharp
var errores = new List<Exception>();
foreach (var registro in registros)
{
    try
    {
        // ... switch existente (Sales/Purchase/Inventory/default) ...
    }
    catch (Exception ex)
    {
        errores.Add(ex);
    }
}

if (errores.Count == registros.Count && errores.Count > 0)
{
    throw new AggregateException("Todos los registros del lote fallaron.", errores);
}
```

Nota: esto significa que `IntegrationSyncHostedService` (Task 5) debe iterar sus propios `MarcarProcesadoAsync` fuera de este método, decidiendo éxito/error por registro según si `PushAsync` lanzó excepción global o no — como `PushAsync` no distingue qué registro específico fallo de cuáles no (solo lanza si TODOS fallaron), Task 5 trata el resultado como "todo o nada" en esta primera versión: si `PushAsync` no lanza, todos los registros del lote se marcan `exito: true`; si lanza, todos se marcan `exito: false` con el mensaje de la excepción. Esto es una simplificación consciente — el aislamiento fino por registro individual (saber cuál de varios documentos falló específicamente) queda para una iteración futura si el volumen del piloto lo justifica.

- [ ] **Step 7: Ejecutar toda la suite de tests**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj`
Expected: todos los tests pasan, incluidos los existentes de `SapDocumentConnectorTests` (verificar que el aislamiento por registro del Step 6 no rompe los tests de `Sales`/`Purchase`/`default` ya existentes, que hoy esperan que la excepción se propague directo — si algún test existente falla por este cambio, ajustarlo para reflejar el nuevo comportamiento "todo o nada" en vez de "single record throws immediately", sin cambiar la intención original del test).

- [ ] **Step 8: Commit**

```bash
git add src/PortalSaas.Core/Integraciones/SapDocumentConnector.cs tests/PortalSaas.Core.Tests/Integraciones/SapDocumentConnectorTests.cs
git commit -m "feat: implementar mapeo real Inventory en SapDocumentConnector"
```

---

### Task 5: Cableado en `IntegrationSyncHostedService`

**Files:**
- Modify: `src/PortalSaas.Integrations/IntegrationSyncHostedService.cs`
- Test: `tests/PortalSaas.Core.Tests/Integraciones/IntegrationSyncHostedServiceTests.cs` (agregar caso al archivo existente)

**Interfaces:**
- Consumes: `IIntegrationEntityReader` (Task 2, 3), `IIntegrationFieldMappingService.MapToExternalAsync` (ya existente), `IIntegrationConnector.PushAsync` (Task 4).
- Produces: ciclo de sincronización funcional de punta a punta — no produce nada consumido por otra tarea, es el final de la cadena.

- [ ] **Step 1: Leer el archivo actual para confirmar el bloque exacto a reemplazar**

Run: `grep -n "registrosExternos\|La lectura de pendientes" "src/PortalSaas.Integrations/IntegrationSyncHostedService.cs"`
Expected: confirma la ubicación exacta (línea ~130, dentro de `EjecutarIntegracionAsync`, rama `if (definicion.Direccion is IntegrationDireccion.Subida)`).

- [ ] **Step 2: Escribir el test que falla**

Agregar al archivo de test existente, siguiendo el mismo patrón de `ConectorFalso` ya definido ahí — agregar un `ReaderFalso`:

```csharp
private class ReaderFalso : IIntegrationEntityReader
{
    private readonly List<IntegrationRecord> _registros;
    public List<(Guid CompanyId, bool Exito)> LlamadasDeAck { get; } = new();

    public ReaderFalso(List<IntegrationRecord> registros)
    {
        _registros = registros;
    }

    public string EntidadNegocio => "Wms.ConfirmacionTraslado";

    public Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<IntegrationRecord>>(_registros);

    public Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken)
    {
        LlamadasDeAck.Add((companyId, exito));
        return Task.CompletedTask;
    }
}

[Fact]
public async Task EjecutarCicloAsync_ConReaderYPushExitoso_LlamaAckConExitoTrue()
{
    var dbName = Guid.NewGuid().ToString();
    var conectorFalso = new ConectorFalso();
    var registroDePrueba = new IntegrationRecord(new Dictionary<string, object?> { ["TipoDocumento"] = "Inventory" });
    var readerFalso = new ReaderFalso(new List<IntegrationRecord> { registroDePrueba });

    var services = new ServiceCollection();
    services.AddDbContext<PortalSaasDbContext>(o => o.UseInMemoryDatabase(dbName));
    services.AddSingleton<IIntegrationConnector>(conectorFalso);
    services.AddSingleton<IIntegrationEntityReader>(readerFalso);
    services.AddScoped<IIntegrationFieldMappingService, IntegrationFieldMappingService>();
    services.AddSingleton<Microsoft.Extensions.Logging.ILogger<IntegrationSyncHostedService>>(NullLogger<IntegrationSyncHostedService>.Instance);
    var proveedor = services.BuildServiceProvider();

    var definicion = new IntegrationDefinition
    {
        Nombre = "Test", ModuloOrigen = "Wms", EntidadNegocio = "Wms.ConfirmacionTraslado",
        ConectorTipo = IntegrationConectorTipo.Sap, ConectorConfigCifrado = "{}",
        Direccion = IntegrationDireccion.Subida, Activo = true,
        NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1),
    };

    using (var scope = proveedor.CreateScope())
    {
        var contexto = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
        contexto.IntegrationDefinitions.Add(definicion);
        await contexto.SaveChangesAsync();
    }

    var servicio = new IntegrationSyncHostedService(proveedor.GetRequiredService<IServiceScopeFactory>(), NullLogger<IntegrationSyncHostedService>.Instance);
    await servicio.EjecutarCicloAsync(CancellationToken.None);

    Assert.Single(readerFalso.LlamadasDeAck);
    Assert.True(readerFalso.LlamadasDeAck[0].Exito);
    Assert.True(conectorFalso.PushLlamado);
}
```

- [ ] **Step 3: Ejecutar el test y verificar que falla**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter EjecutarCicloAsync_ConReaderYPushExitoso_LlamaAckConExitoTrue`
Expected: FAIL — el reader nunca se resuelve/llama todavía (`registrosExternos` sigue hardcodeado vacío).

- [ ] **Step 4: Implementar el cableado**

Reemplazar el bloque hardcodeado (ubicado en Step 1) por:

```csharp
if (definicion.Direccion is IntegrationDireccion.Subida)
{
    var conectorConfigJson = DescifrarConfigConector(secretoServicio, definicion);

    var readers = scope.ServiceProvider.GetServices<IIntegrationEntityReader>().ToList();
    var reader = readers.FirstOrDefault(r => r.EntidadNegocio == definicion.EntidadNegocio)
        ?? throw new InvalidOperationException($"No hay reader registrado para entidad '{definicion.EntidadNegocio}'.");

    var registrosLocales = await reader.LeerPendientesAsync(definicion.CompanyId, cancellationToken);
    var registrosMapeados = new List<IntegrationRecord>();
    foreach (var registroLocal in registrosLocales)
    {
        registrosMapeados.Add(await mapeoServicio.MapToExternalAsync(definicion.Id, registroLocal));
    }

    Exception? excepcionDePush = null;
    try
    {
        await conector.PushAsync(conectorConfigJson, registrosMapeados, cancellationToken);
    }
    catch (Exception ex)
    {
        excepcionDePush = ex;
    }

    foreach (var registroLocal in registrosLocales)
    {
        await reader.MarcarProcesadoAsync(definicion.CompanyId, registroLocal, exito: excepcionDePush is null, mensajeError: excepcionDePush?.Message, cancellationToken);
    }

    if (excepcionDePush is not null)
    {
        throw excepcionDePush;
    }

    log.RegistrosProcesados = registrosLocales.Count;
}
```

Nota: se relanza `excepcionDePush` después de hacer el ack (en vez de tragarla en silencio), para que el `catch` externo ya existente en `EjecutarIntegracionAsync` siga marcando `log.Resultado = Error` correctamente — el ack ocurre ANTES de relanzar, así que las filas de staging quedan marcadas aunque el ciclo completo termine en error.

- [ ] **Step 5: Ejecutar el test y verificar que pasa**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter EjecutarCicloAsync_ConReaderYPushExitoso_LlamaAckConExitoTrue`
Expected: PASS.

- [ ] **Step 6: Ejecutar toda la suite de tests**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj`
Expected: todos los tests pasan, incluidos los existentes de `IntegrationSyncHostedServiceTests` (verificar que el test original, que no configuraba ningún `IIntegrationEntityReader`, siga pasando — si `definicion.EntidadNegocio` queda vacío/no coincide con ningún reader registrado, el nuevo código lanza `InvalidOperationException`, lo cual el test original NO esperaba; si eso rompe el test existente, ajustar ese test para registrar un `ReaderFalso` también, o para usar una `EntidadNegocio` que coincida — no cambiar el comportamiento de producción para acomodar el test viejo).

- [ ] **Step 7: Commit**

```bash
git add src/PortalSaas.Integrations/IntegrationSyncHostedService.cs tests/PortalSaas.Core.Tests/Integraciones/IntegrationSyncHostedServiceTests.cs
git commit -m "feat: cablear IntegrationSyncHostedService al reader real (WmsSlshInventoryReader vía DI)"
```

---

## Self-Review

**1. Cobertura del spec:** Pieza 1 (Copy-From en 3 motores) → Task 1. Pieza 2 (ack en `IIntegrationEntityReader`) → Task 2. Pieza 3 (`WmsSlshInventoryReader`) → Task 3. Pieza 4 (mapeo real `SapDocumentConnector`) → Task 4. Pieza 5 (cableado orquestador) → Task 5. Criterio de éxito del spec (fila `Pendiente` → `POST` real con `BaseType`/`BaseEntry`/`BaseLine` → `ProcesadoSap`/`ErrorSap`) queda cubierto estructuralmente por los tests de las 5 tareas encadenadas, aunque la verificación contra un SAP real queda fuera de este plan (ninguna tarea prueba contra Service Layer real, todas usan fakes/InMemory — consistente con el resto de esta migración hasta ahora).

**2. Placeholder scan:** sin "TBD"/"TODO". Las simplificaciones conscientes (aislamiento "todo o nada" en vez de por-registro individual en Task 4 Step 6; `portalUsername` fijo `"wms-integration"` en Task 4 Step 4) están documentadas con su razón, no dejadas sin explicar.

**3. Consistencia de tipos:** `IIntegrationEntityReader.MarcarProcesadoAsync` (Task 2) tiene la misma firma exacta en `WmsSlshInventoryReader` (Task 3) y en la llamada desde `IntegrationSyncHostedService` (Task 5). `IntegrationRecord["Lineas"]`/`["_StagingLineIds"]` se producen en Task 3 y se consumen con los mismos nombres de clave en Task 4 (`"Lineas"`) y Task 5 (implícito, vía el mismo objeto pasado de un lado a otro sin reinterpretar sus claves). `InventoryDocumentLineDto`/`InventoryDocumentDto` (Task 1) se instancian con la misma forma en el test de Task 1 y en la implementación real de Task 4.

**4. Riesgo señalado explícitamente:** Task 1 Step 1 exige confirmar la forma real de los archivos antes de aplicar los cambios propuestos (no asumir ciegamente el código mostrado, que se reconstruyó por investigación, no por lectura directa de los archivos completos de Sales/Purchase). Task 4 Step 7 y Task 5 Step 6 avisan explícitamente que tests existentes podrían necesitar ajuste por el cambio de comportamiento, sin dejarlo como sorpresa para el implementador.
