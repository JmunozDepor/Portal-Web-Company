# Migración Wms — Ronda C: SAP → WMS (Artículos, Tiendas, Traslados) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Completar la dirección Bajada del motor genérico de integración para que Artículos, Tiendas y Traslados fluyan de SAP hacia Oracle WMS Cloud, cerrando el ciclo bidireccional del piloto (Ronda B ya confirma el Traslado de vuelta).

**Architecture:** Dos etapas por entidad, sobre el motor genérico existente. Etapa 1 (Bajada): `SapDocumentConnector.PullAsync` consulta Service Layer con `$filter` OData replicando el criterio de negocio del legado, y un `IIntegrationEntityWriter` nuevo por entidad escribe en staging local (`Modulo.Wms`). Etapa 2 (Subida): un `IIntegrationEntityReader` nuevo por entidad lee ese staging, y un `WmsCloudConnector` nuevo postea el XML real a Oracle WMS Cloud.

**Tech Stack:** .NET 8, EF Core (Postgres/SQL Server dual), B1SLayer vía `ISapConnectionProvider`/`ISapSession` (ya existente), `System.Text.Json`, `System.Xml.Linq`.

## Global Constraints

- Multi-repo: Tasks 1, 2, 5 viven en `Portal SaaS - Plugins/Modulo.Wms`; Tasks 3, 4 viven en `Portal SaaS - Core`; Task 6 vive en `Modulo.Wms`.
- **Corrección de diseño respecto al spec** (`docs/superpowers/specs/2026-08-15-wms-ronda-c-sap-a-wms-design.md`): el spec proponía un cursor `MAX(SourceUpdateDate)` consultado por `SapDocumentConnector.PullAsync` contra la tabla de staging antes del GET a SAP. Eso es arquitectónicamente imposible: `SapDocumentConnector` vive en `PortalSaas.Core`, que NO puede referenciar el `WmsDbContext` de un plugin (`Modulo.Wms`) — regla dura del proyecto, los plugins solo son consumidos vía `PortalSaas.Abstractions`, nunca al revés. **Corrección**: `PullAsync` consulta SAP filtrando SOLO por el criterio de negocio (sin rango de fecha) cada ciclo — son conjuntos acotados por naturaleza (ítems/tiendas/traslados marcados explícitamente para WMS, no el catálogo completo). La deduplicación/resync pasa al `IIntegrationEntityWriter`: hace upsert por clave natural (`ItemCode`/`CardCode`/`DocEntry`) contra la tabla de staging — si no existe, inserta `Pendiente`; si existe y ya fue procesado (`ProcesadoWms`) pero `SourceUpdateDate` de SAP es más nueva que la guardada, vuelve a `Pendiente` (resync); si existe y sigue `Pendiente`, no duplica. El comportamiento observable para el usuario final es idéntico al spec (nada se pierde, nada se duplica); solo cambia dónde vive la lógica de "qué es nuevo".
- Multi-tenant: todo por `CompanyId`, nunca compartido entre compañías — mismo criterio que Rondas 0/A/B.
- `IntegrationDefinition.ConectorConfigCifrado` por cada `IntegrationDefinition` de Bajada declara `{"TipoEntidad":"Item"|"Store"|"InboundTraslado"}` — un valor por definición, 3 definiciones de Bajada en total (fuera del alcance de este plan crear esas 3 filas de `IntegrationDefinition`; se siembran manualmente o por script aparte cuando se despliegue el piloto, mismo criterio que las definiciones existentes de Ronda B).
- Nombres de tabla/columna nuevos en `snake_case` (mismo criterio EF Fluent API que `wms_oracle_inbound_stage`/`wms_oracle_stage_slsh`), sin `HasColumnType` (dual-motor-safe Postgres/SQL Server).
- Comentarios y mensajes de excepción en español, siguiendo el estilo del resto del código ya escrito en estas rondas.
- **No asumir nombres de recurso/campo de Service Layer ni el vocabulario de campo esperado por Oracle WMS Cloud** — cada task que los necesita trae un Step 1 explícito de verificación contra el código legado real (`C:\PROYECTOS\WMS_Suite`), con instrucciones concretas de qué grep correr y cómo adaptar si el resultado difiere de lo asumido acá.

---

### Task 1: Entidades de staging + configuración EF + migraciones (`Modulo.Wms`)

**Files:**
- Create: `src/Modulo.Wms/Models/WmsSapStageItem.cs`
- Create: `src/Modulo.Wms/Models/WmsSapStageStore.cs`
- Create: `src/Modulo.Wms/Models/WmsSapStageInbound.cs` (contiene `WmsSapStageInboundHdr` y `WmsSapStageInboundDtl`)
- Modify: `src/Modulo.Wms/Data/WmsDbContext.cs` (agregar 4 `DbSet` + configuración Fluent API)
- Create: migraciones Postgres y SQL Server (mismo patrón que Ronda A — ver Step 5)

**Interfaces:**
- Produces: `WmsSapStageItem { LineId (long PK), CompanyId (Guid), ItemCode (string), ItemName (string), BarCode (string?), SourceUpdateDate (DateTime), Status (enum WmsSapStageStatus), RetryCount (int), ErrorMsg (string?), CreatedAt (DateTimeOffset), SyncedAt (DateTimeOffset?) }`. `WmsSapStageStore` con la misma forma técnica, campos de negocio `CardCode (string)`, `CardName (string)`, `Street (string?)`, `City (string?)`, `ZipCode (string?)`. `WmsSapStageInboundHdr { LineId (long PK), CompanyId (Guid), SapDocEntry (int), ShipmentType (string), SourceUpdateDate (DateTime), Status, RetryCount, ErrorMsg, CreatedAt, SyncedAt }` + `WmsSapStageInboundDtl { LineId (long PK), ParentId (long FK cascade a WmsSapStageInboundHdr.LineId), ItemCode (string), Quantity (decimal), WhsCode (string), LineNum (int) }` — consumidos por Task 2 (writers) y Task 5 (readers).

- [ ] **Step 1: Crear el enum y las 3 clases de modelo**

```csharp
// src/Modulo.Wms/Models/WmsSapStageItem.cs
namespace Modulo.Wms.Models;

public enum WmsSapStageStatus { Pendiente, ProcesadoWms, ErrorWms }

public class WmsSapStageItem
{
    public long LineId { get; set; }
    public Guid CompanyId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? BarCode { get; set; }
    public DateTime SourceUpdateDate { get; set; }
    public WmsSapStageStatus Status { get; set; } = WmsSapStageStatus.Pendiente;
    public int RetryCount { get; set; }
    public string? ErrorMsg { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SyncedAt { get; set; }
}
```

```csharp
// src/Modulo.Wms/Models/WmsSapStageStore.cs
namespace Modulo.Wms.Models;

public class WmsSapStageStore
{
    public long LineId { get; set; }
    public Guid CompanyId { get; set; }
    public string CardCode { get; set; } = string.Empty;
    public string CardName { get; set; } = string.Empty;
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? ZipCode { get; set; }
    public DateTime SourceUpdateDate { get; set; }
    public WmsSapStageStatus Status { get; set; } = WmsSapStageStatus.Pendiente;
    public int RetryCount { get; set; }
    public string? ErrorMsg { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SyncedAt { get; set; }
}
```

```csharp
// src/Modulo.Wms/Models/WmsSapStageInbound.cs
namespace Modulo.Wms.Models;

public class WmsSapStageInboundHdr
{
    public long LineId { get; set; }
    public Guid CompanyId { get; set; }
    public int SapDocEntry { get; set; }
    public string ShipmentType { get; set; } = string.Empty;
    public DateTime SourceUpdateDate { get; set; }
    public WmsSapStageStatus Status { get; set; } = WmsSapStageStatus.Pendiente;
    public int RetryCount { get; set; }
    public string? ErrorMsg { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SyncedAt { get; set; }
}

public class WmsSapStageInboundDtl
{
    public long LineId { get; set; }
    public long ParentId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string WhsCode { get; set; } = string.Empty;
    public int LineNum { get; set; }
}
```

- [ ] **Step 2: Registrar los 4 `DbSet` en `WmsDbContext`**

Abrir `src/Modulo.Wms/Data/WmsDbContext.cs`, agregar junto a los `DbSet` existentes (línea ~32-33):

```csharp
public DbSet<WmsSapStageItem> WmsSapStageItems => Set<WmsSapStageItem>();
public DbSet<WmsSapStageStore> WmsSapStageStores => Set<WmsSapStageStore>();
public DbSet<WmsSapStageInboundHdr> WmsSapStageInboundHdrs => Set<WmsSapStageInboundHdr>();
public DbSet<WmsSapStageInboundDtl> WmsSapStageInboundDtls => Set<WmsSapStageInboundDtl>();
```

- [ ] **Step 3: Fluent API en `OnModelCreating`**

Agregar dentro de `OnModelCreating`, siguiendo el patrón exacto de `wms_oracle_inbound_stage`/`wms_oracle_stage_slsh` (mismo archivo, líneas 84-115 aprox. — leerlas primero para copiar el estilo exacto):

```csharp
modelBuilder.Entity<WmsSapStageItem>(entity =>
{
    entity.ToTable("wms_sap_stage_item");
    entity.HasKey(e => e.LineId);
    entity.Property(e => e.LineId).HasColumnName("line_id");
    entity.Property(e => e.CompanyId).HasColumnName("company_id");
    entity.Property(e => e.ItemCode).HasColumnName("item_code").HasMaxLength(50);
    entity.Property(e => e.ItemName).HasColumnName("item_name").HasMaxLength(200);
    entity.Property(e => e.BarCode).HasColumnName("bar_code").HasMaxLength(50);
    entity.Property(e => e.SourceUpdateDate).HasColumnName("source_update_date");
    entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
    entity.Property(e => e.RetryCount).HasColumnName("retry_count");
    entity.Property(e => e.ErrorMsg).HasColumnName("error_msg").HasMaxLength(500);
    entity.Property(e => e.CreatedAt).HasColumnName("created_at");
    entity.Property(e => e.SyncedAt).HasColumnName("synced_at");
    entity.HasIndex(e => new { e.CompanyId, e.ItemCode }).IsUnique().HasDatabaseName("ix_wms_sap_stage_item_company_itemcode");
    entity.HasIndex(e => e.Status).HasDatabaseName("ix_wms_sap_stage_item_status");
});

modelBuilder.Entity<WmsSapStageStore>(entity =>
{
    entity.ToTable("wms_sap_stage_store");
    entity.HasKey(e => e.LineId);
    entity.Property(e => e.LineId).HasColumnName("line_id");
    entity.Property(e => e.CompanyId).HasColumnName("company_id");
    entity.Property(e => e.CardCode).HasColumnName("card_code").HasMaxLength(50);
    entity.Property(e => e.CardName).HasColumnName("card_name").HasMaxLength(200);
    entity.Property(e => e.Street).HasColumnName("street").HasMaxLength(200);
    entity.Property(e => e.City).HasColumnName("city").HasMaxLength(100);
    entity.Property(e => e.ZipCode).HasColumnName("zip_code").HasMaxLength(20);
    entity.Property(e => e.SourceUpdateDate).HasColumnName("source_update_date");
    entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
    entity.Property(e => e.RetryCount).HasColumnName("retry_count");
    entity.Property(e => e.ErrorMsg).HasColumnName("error_msg").HasMaxLength(500);
    entity.Property(e => e.CreatedAt).HasColumnName("created_at");
    entity.Property(e => e.SyncedAt).HasColumnName("synced_at");
    entity.HasIndex(e => new { e.CompanyId, e.CardCode }).IsUnique().HasDatabaseName("ix_wms_sap_stage_store_company_cardcode");
    entity.HasIndex(e => e.Status).HasDatabaseName("ix_wms_sap_stage_store_status");
});

modelBuilder.Entity<WmsSapStageInboundHdr>(entity =>
{
    entity.ToTable("wms_sap_stage_inbound_hdr");
    entity.HasKey(e => e.LineId);
    entity.Property(e => e.LineId).HasColumnName("line_id");
    entity.Property(e => e.CompanyId).HasColumnName("company_id");
    entity.Property(e => e.SapDocEntry).HasColumnName("sap_doc_entry");
    entity.Property(e => e.ShipmentType).HasColumnName("shipment_type").HasMaxLength(50);
    entity.Property(e => e.SourceUpdateDate).HasColumnName("source_update_date");
    entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
    entity.Property(e => e.RetryCount).HasColumnName("retry_count");
    entity.Property(e => e.ErrorMsg).HasColumnName("error_msg").HasMaxLength(500);
    entity.Property(e => e.CreatedAt).HasColumnName("created_at");
    entity.Property(e => e.SyncedAt).HasColumnName("synced_at");
    entity.HasIndex(e => new { e.CompanyId, e.SapDocEntry }).IsUnique().HasDatabaseName("ix_wms_sap_stage_inbound_hdr_company_docentry");
    entity.HasIndex(e => e.Status).HasDatabaseName("ix_wms_sap_stage_inbound_hdr_status");
});

modelBuilder.Entity<WmsSapStageInboundDtl>(entity =>
{
    entity.ToTable("wms_sap_stage_inbound_dtl");
    entity.HasKey(e => e.LineId);
    entity.Property(e => e.LineId).HasColumnName("line_id");
    entity.Property(e => e.ParentId).HasColumnName("parent_id");
    entity.Property(e => e.ItemCode).HasColumnName("item_code").HasMaxLength(50);
    entity.Property(e => e.Quantity).HasColumnName("quantity");
    entity.Property(e => e.WhsCode).HasColumnName("whs_code").HasMaxLength(20);
    entity.Property(e => e.LineNum).HasColumnName("line_num");
    entity.HasOne<WmsSapStageInboundHdr>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Cascade);
});
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Modulo.Wms/Modulo.Wms.csproj` (recrear la junction cross-repo si hace falta, mismo procedimiento que rondas anteriores).
Expected: compila sin errores.

- [ ] **Step 5: Generar migraciones Postgres y SQL Server**

Mismo patrón que Ronda A Task 1 — dos migraciones, una por proveedor. Confirmar el comando exacto leyendo cómo se generaron `wms_oracle_inbound_stage`/`wms_oracle_stage_slsh` (buscar en el historial de migraciones del proyecto, carpeta `Migrations/` de `Modulo.Wms` o donde corresponda según el proveedor activo del entorno de desarrollo) antes de correr el comando — no asumir el nombre exacto del `DbContext` de diseño ni el startup project sin confirmarlo.

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj` (test de humo — ningún test nuevo todavía, solo confirmar que nada existente se rompió).
Expected: todos los tests existentes en verde.

- [ ] **Step 6: Commit**

```bash
git add src/Modulo.Wms/Models/WmsSapStageItem.cs src/Modulo.Wms/Models/WmsSapStageStore.cs src/Modulo.Wms/Models/WmsSapStageInbound.cs src/Modulo.Wms/Data/WmsDbContext.cs
git add [rutas de migraciones generadas en Step 5]
git commit -m "feat: entidades de staging SAP->WMS (Item, Store, Traslado)"
```

---

### Task 2: `IIntegrationEntityWriter` × 3 (`Modulo.Wms`)

**Files:**
- Create: `src/Modulo.Wms/Services/WmsSapStageItemWriter.cs`
- Create: `src/Modulo.Wms/Services/WmsSapStageStoreWriter.cs`
- Create: `src/Modulo.Wms/Services/WmsSapStageInboundWriter.cs`
- Modify: `src/Modulo.Wms/ModuloWms.cs` (registrar los 3 en DI)
- Test: `tests/Modulo.Wms.Tests/Services/WmsSapStageItemWriterTests.cs`
- Test: `tests/Modulo.Wms.Tests/Services/WmsSapStageStoreWriterTests.cs`
- Test: `tests/Modulo.Wms.Tests/Services/WmsSapStageInboundWriterTests.cs`

**Interfaces:**
- Consumes: `IIntegrationEntityWriter { string EntidadNegocio; Task EscribirAsync(Guid companyId, IReadOnlyList<IntegrationRecord> registros, CancellationToken cancellationToken); }` (`PortalSaas.Abstractions/Contratos/Integraciones/IIntegrationEntityWriter.cs`, ya existe, confirmado por lectura directa). `WmsSapStageItem`/`WmsSapStageStore`/`WmsSapStageInboundHdr`/`WmsSapStageInboundDtl` (Task 1).
- Produces: `WmsSapStageItemWriter.EntidadNegocio => "SapWms.Item"`, `WmsSapStageStoreWriter.EntidadNegocio => "SapWms.Store"`, `WmsSapStageInboundWriter.EntidadNegocio => "SapWms.Traslado"` — usados por Task 4 (cableado del motor) para resolver el writer correcto por `IntegrationDefinition.EntidadNegocio`.

**Contrato de campos esperado en cada `IntegrationRecord` recibido** (producido por `SapDocumentConnector.PullAsync`, Task 3 — documentado acá para que el writer y el connector coincidan sin que ninguno lea el código del otro):
- Item: `Fields["ItemCode"]` (string), `Fields["ItemName"]` (string), `Fields["BarCode"]` (string?), `Fields["SourceUpdateDate"]` (DateTime).
- Store: `Fields["CardCode"]`, `Fields["CardName"]`, `Fields["Street"]` (string?), `Fields["City"]` (string?), `Fields["ZipCode"]` (string?), `Fields["SourceUpdateDate"]`.
- Traslado: `Fields["SapDocEntry"]` (int), `Fields["ShipmentType"]` (string), `Fields["SourceUpdateDate"]`, `Fields["Lineas"]` (`List<IntegrationRecord>`, cada una con `Fields["ItemCode"]`, `Fields["Quantity"]` (decimal), `Fields["WhsCode"]` (string), `Fields["LineNum"]` (int)).

- [ ] **Step 1: Test de `WmsSapStageItemWriter` — inserta fila nueva**

```csharp
// tests/Modulo.Wms.Tests/Services/WmsSapStageItemWriterTests.cs
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSapStageItemWriterTests
{
    private static WmsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(opciones);
    }

    [Fact]
    public async Task EscribirAsync_ItemNuevo_InsertaPendiente()
    {
        var contexto = CrearContexto();
        var writer = new WmsSapStageItemWriter(contexto);
        var companyId = Guid.NewGuid();

        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["ItemCode"] = "ITM001",
            ["ItemName"] = "Artículo de prueba",
            ["BarCode"] = "7801234567890",
            ["SourceUpdateDate"] = new DateTime(2026, 8, 15),
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        var fila = Assert.Single(contexto.WmsSapStageItems);
        Assert.Equal("ITM001", fila.ItemCode);
        Assert.Equal(WmsSapStageStatus.Pendiente, fila.Status);
        Assert.Equal(companyId, fila.CompanyId);
    }

    [Fact]
    public async Task EscribirAsync_ItemYaPendienteSinCambios_NoDuplica()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        contexto.WmsSapStageItems.Add(new WmsSapStageItem
        {
            CompanyId = companyId,
            ItemCode = "ITM001",
            ItemName = "Artículo de prueba",
            SourceUpdateDate = new DateTime(2026, 8, 15),
            Status = WmsSapStageStatus.Pendiente,
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageItemWriter(contexto);
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["ItemCode"] = "ITM001",
            ["ItemName"] = "Artículo de prueba",
            ["BarCode"] = null,
            ["SourceUpdateDate"] = new DateTime(2026, 8, 15),
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        Assert.Single(contexto.WmsSapStageItems);
    }

    [Fact]
    public async Task EscribirAsync_ItemYaProcesadoConCambioEnSap_VuelveAPendiente()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        contexto.WmsSapStageItems.Add(new WmsSapStageItem
        {
            CompanyId = companyId,
            ItemCode = "ITM001",
            ItemName = "Nombre viejo",
            SourceUpdateDate = new DateTime(2026, 8, 10),
            Status = WmsSapStageStatus.ProcesadoWms,
            SyncedAt = DateTimeOffset.UtcNow,
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageItemWriter(contexto);
        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["ItemCode"] = "ITM001",
            ["ItemName"] = "Nombre nuevo",
            ["BarCode"] = null,
            ["SourceUpdateDate"] = new DateTime(2026, 8, 15),
        });

        await writer.EscribirAsync(companyId, [registro], CancellationToken.None);

        var fila = Assert.Single(contexto.WmsSapStageItems);
        Assert.Equal(WmsSapStageStatus.Pendiente, fila.Status);
        Assert.Equal("Nombre nuevo", fila.ItemName);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsSapStageItemWriterTests`
Expected: FAIL — `WmsSapStageItemWriter` no existe todavía.

- [ ] **Step 3: Implementar `WmsSapStageItemWriter`**

```csharp
// src/Modulo.Wms/Services/WmsSapStageItemWriter.cs
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Escritor del motor genérico de integración (IIntegrationEntityWriter) para
/// Artículos que llegan desde SAP (dirección Bajada) hacia el staging local -- upsert
/// por (CompanyId, ItemCode): si no existe, inserta Pendiente; si existe y sigue
/// Pendiente, no duplica; si existe y ya fue ProcesadoWms pero SAP tiene una versión
/// más nueva (SourceUpdateDate), vuelve a Pendiente para resync. Reemplaza el cursor
/// de fecha que el connector no puede consultar (ver Global Constraints del plan).
/// </summary>
public class WmsSapStageItemWriter : IIntegrationEntityWriter
{
    private readonly WmsDbContext _contexto;

    public WmsSapStageItemWriter(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Item";

    public async Task EscribirAsync(Guid companyId, IReadOnlyList<IntegrationRecord> registros, CancellationToken cancellationToken)
    {
        foreach (var registro in registros)
        {
            var itemCode = (string)registro["ItemCode"]!;
            var sourceUpdateDate = (DateTime)registro["SourceUpdateDate"]!;

            var existente = await _contexto.WmsSapStageItems
                .FirstOrDefaultAsync(f => f.CompanyId == companyId && f.ItemCode == itemCode, cancellationToken);

            if (existente is null)
            {
                _contexto.WmsSapStageItems.Add(new WmsSapStageItem
                {
                    CompanyId = companyId,
                    ItemCode = itemCode,
                    ItemName = (string)registro["ItemName"]!,
                    BarCode = (string?)registro["BarCode"],
                    SourceUpdateDate = sourceUpdateDate,
                    Status = WmsSapStageStatus.Pendiente,
                });
                continue;
            }

            if (existente.Status == WmsSapStageStatus.ProcesadoWms && sourceUpdateDate > existente.SourceUpdateDate)
            {
                existente.Status = WmsSapStageStatus.Pendiente;
                existente.ErrorMsg = null;
            }

            existente.ItemName = (string)registro["ItemName"]!;
            existente.BarCode = (string?)registro["BarCode"];
            existente.SourceUpdateDate = sourceUpdateDate;
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsSapStageItemWriterTests`
Expected: PASS (3/3).

- [ ] **Step 5: Repetir Steps 1-4 para `WmsSapStageStoreWriter`**

Mismo patrón exacto que `WmsSapStageItemWriter`, sobre `WmsSapStageStore`/`WmsSapStageStores`, clave natural `(CompanyId, CardCode)`, `EntidadNegocio => "SapWms.Store"`, leyendo `Fields["CardCode"]`/`Fields["CardName"]`/`Fields["Street"]`/`Fields["City"]`/`Fields["ZipCode"]`/`Fields["SourceUpdateDate"]`. Escribir el archivo de test análogo (`WmsSapStageStoreWriterTests.cs`, mismos 3 casos: nuevo, ya pendiente sin cambios, ya procesado con cambio) antes de implementar.

- [ ] **Step 6: `WmsSapStageInboundWriter` — cabecera + detalle**

Test primero (`tests/Modulo.Wms.Tests/Services/WmsSapStageInboundWriterTests.cs`):

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSapStageInboundWriterTests
{
    private static WmsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(opciones);
    }

    private static IntegrationRecord CrearRegistroTraslado(int docEntry, DateTime sourceUpdateDate) =>
        new(new Dictionary<string, object?>
        {
            ["SapDocEntry"] = docEntry,
            ["ShipmentType"] = "TRASLADO_ESTANDAR",
            ["SourceUpdateDate"] = sourceUpdateDate,
            ["Lineas"] = new List<IntegrationRecord>
            {
                new(new Dictionary<string, object?>
                {
                    ["ItemCode"] = "ITM001",
                    ["Quantity"] = 10m,
                    ["WhsCode"] = "01",
                    ["LineNum"] = 0,
                }),
            },
        });

    [Fact]
    public async Task EscribirAsync_TrasladoNuevo_InsertaCabeceraYDetalle()
    {
        var contexto = CrearContexto();
        var writer = new WmsSapStageInboundWriter(contexto);
        var companyId = Guid.NewGuid();

        await writer.EscribirAsync(companyId, [CrearRegistroTraslado(500123, new DateTime(2026, 8, 15))], CancellationToken.None);

        var hdr = Assert.Single(contexto.WmsSapStageInboundHdrs);
        Assert.Equal(500123, hdr.SapDocEntry);
        Assert.Equal(WmsSapStageStatus.Pendiente, hdr.Status);

        var dtl = Assert.Single(contexto.WmsSapStageInboundDtls);
        Assert.Equal(hdr.LineId, dtl.ParentId);
        Assert.Equal("ITM001", dtl.ItemCode);
        Assert.Equal(10m, dtl.Quantity);
    }

    [Fact]
    public async Task EscribirAsync_TrasladoYaProcesadoConCambioEnSap_VuelveAPendienteYReemplazaDetalle()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdrExistente = new WmsSapStageInboundHdr
        {
            CompanyId = companyId,
            SapDocEntry = 500123,
            ShipmentType = "TRASLADO_ESTANDAR",
            SourceUpdateDate = new DateTime(2026, 8, 10),
            Status = WmsSapStageStatus.ProcesadoWms,
        };
        contexto.WmsSapStageInboundHdrs.Add(hdrExistente);
        await contexto.SaveChangesAsync();
        contexto.WmsSapStageInboundDtls.Add(new WmsSapStageInboundDtl
        {
            ParentId = hdrExistente.LineId,
            ItemCode = "ITM_VIEJO",
            Quantity = 1m,
            WhsCode = "01",
            LineNum = 0,
        });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageInboundWriter(contexto);
        await writer.EscribirAsync(companyId, [CrearRegistroTraslado(500123, new DateTime(2026, 8, 15))], CancellationToken.None);

        var hdr = Assert.Single(contexto.WmsSapStageInboundHdrs);
        Assert.Equal(WmsSapStageStatus.Pendiente, hdr.Status);

        var dtl = Assert.Single(contexto.WmsSapStageInboundDtls);
        Assert.Equal("ITM001", dtl.ItemCode);
    }
}
```

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsSapStageInboundWriterTests` — Expected: FAIL (clase no existe).

Implementación:

```csharp
// src/Modulo.Wms/Services/WmsSapStageInboundWriter.cs
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Escritor del motor genérico (IIntegrationEntityWriter) para Traslados (Solicitudes
/// de Traslado, OWTQ) que llegan desde SAP. Mismo criterio de upsert por clave natural
/// que WmsSapStageItemWriter (ver ese archivo), sobre (CompanyId, SapDocEntry). El
/// detalle se reemplaza completo en cada resync -- más simple que un diff línea por
/// línea, y el volumen por traslado es chico (mismo criterio que
/// ItemCrossReferenceService.SyncAsync).
/// </summary>
public class WmsSapStageInboundWriter : IIntegrationEntityWriter
{
    private readonly WmsDbContext _contexto;

    public WmsSapStageInboundWriter(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Traslado";

    public async Task EscribirAsync(Guid companyId, IReadOnlyList<IntegrationRecord> registros, CancellationToken cancellationToken)
    {
        foreach (var registro in registros)
        {
            var docEntry = (int)registro["SapDocEntry"]!;
            var sourceUpdateDate = (DateTime)registro["SourceUpdateDate"]!;
            var lineas = (List<IntegrationRecord>)registro["Lineas"]!;

            var hdr = await _contexto.WmsSapStageInboundHdrs
                .FirstOrDefaultAsync(f => f.CompanyId == companyId && f.SapDocEntry == docEntry, cancellationToken);

            if (hdr is null)
            {
                hdr = new WmsSapStageInboundHdr
                {
                    CompanyId = companyId,
                    SapDocEntry = docEntry,
                    ShipmentType = (string)registro["ShipmentType"]!,
                    SourceUpdateDate = sourceUpdateDate,
                    Status = WmsSapStageStatus.Pendiente,
                };
                _contexto.WmsSapStageInboundHdrs.Add(hdr);
                await _contexto.SaveChangesAsync(cancellationToken);
            }
            else
            {
                var debeResincronizar = hdr.Status == WmsSapStageStatus.ProcesadoWms && sourceUpdateDate > hdr.SourceUpdateDate;
                var esResync = debeResincronizar || hdr.Status == WmsSapStageStatus.Pendiente;

                if (debeResincronizar)
                {
                    hdr.Status = WmsSapStageStatus.Pendiente;
                    hdr.ErrorMsg = null;
                }

                hdr.ShipmentType = (string)registro["ShipmentType"]!;
                hdr.SourceUpdateDate = sourceUpdateDate;

                if (esResync)
                {
                    var detalleExistente = await _contexto.WmsSapStageInboundDtls
                        .Where(d => d.ParentId == hdr.LineId)
                        .ToListAsync(cancellationToken);
                    _contexto.WmsSapStageInboundDtls.RemoveRange(detalleExistente);
                    await _contexto.SaveChangesAsync(cancellationToken);
                }
            }

            foreach (var linea in lineas)
            {
                _contexto.WmsSapStageInboundDtls.Add(new WmsSapStageInboundDtl
                {
                    ParentId = hdr.LineId,
                    ItemCode = (string)linea["ItemCode"]!,
                    Quantity = (decimal)linea["Quantity"]!,
                    WhsCode = (string)linea["WhsCode"]!,
                    LineNum = (int)linea["LineNum"]!,
                });
            }

            await _contexto.SaveChangesAsync(cancellationToken);
        }
    }
}
```

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsSapStageInboundWriterTests` — Expected: PASS (2/2).

- [ ] **Step 7: Registrar los 3 writers en DI**

En `src/Modulo.Wms/ModuloWms.cs`, método `RegisterServices`, junto a `services.AddScoped<IIntegrationEntityReader, WmsSlshInventoryReader>();`:

```csharp
services.AddScoped<IIntegrationEntityWriter, WmsSapStageItemWriter>();
services.AddScoped<IIntegrationEntityWriter, WmsSapStageStoreWriter>();
services.AddScoped<IIntegrationEntityWriter, WmsSapStageInboundWriter>();
```

- [ ] **Step 8: Build + test completo del proyecto**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj`
Expected: todos los tests en verde, incluidos los preexistentes.

- [ ] **Step 9: Commit**

```bash
git add src/Modulo.Wms/Services/WmsSapStageItemWriter.cs src/Modulo.Wms/Services/WmsSapStageStoreWriter.cs src/Modulo.Wms/Services/WmsSapStageInboundWriter.cs src/Modulo.Wms/ModuloWms.cs tests/Modulo.Wms.Tests/Services/WmsSapStageItemWriterTests.cs tests/Modulo.Wms.Tests/Services/WmsSapStageStoreWriterTests.cs tests/Modulo.Wms.Tests/Services/WmsSapStageInboundWriterTests.cs
git commit -m "feat: writers del motor de integración para staging SAP->WMS (Item, Store, Traslado)"
```

---

### Task 3: `SapDocumentConnector.PullAsync` completo (`Portal SaaS - Core`)

**Files:**
- Modify: `src/PortalSaas.Core/Integraciones/SapDocumentConnector.cs`
- Test: `tests/PortalSaas.Core.Tests/Integraciones/SapDocumentConnectorTests.cs` (agregar casos, archivo ya existe)

**Interfaces:**
- Consumes: `ISapConnectionProvider.GetConnectionAsync(ct)` → `ISapSession` (`PortalSaas.Abstractions/Contratos/ISapConnectionProvider.cs`, ya existe). `ISapSession.GetAsync<T>(recurso, filtroOData, expandOData, ct)` (mismo archivo). `ODataFilterHelper.Eq(campo, valor)`/`.Escape(valor)` (`PortalSaas.Core/Sap/ODataFilterHelper.cs`, `internal`, ya existe).
- Produces: `PullAsync` retorna `IReadOnlyList<IntegrationRecord>` con la forma exacta documentada en Task 2 (contrato de campos por entidad) — Task 2 (writers) ya está escrito contra esa forma.

**Verificación previa obligatoria (Step 1) — no asumir nombres de recurso/campo de Service Layer:**

- [ ] **Step 1: Confirmar nombres reales de recurso y campo en Service Layer**

Los nombres de recurso asumidos (`Items`, `BusinessPartners`, `InventoryTransferRequests`) son los estándar documentados de SAP B1 Service Layer, pero **no están verificados contra un ambiente real**. Antes de escribir código:
1. Buscar en el repo si ya existe algún GET contra `Items`/`BusinessPartners`/`InventoryTransferRequests` (`grep -rn "\"Items\"\|\"BusinessPartners\"\|\"InventoryTransferRequests\"" src/PortalSaas.Core/`). Si existe, usar exactamente esos nombres.
2. Si no existe, mantener los nombres estándar (`Items`, `BusinessPartners`, `InventoryTransferRequests`) — son el nombre de recurso oficial de la documentación pública de SAP B1 Service Layer, no un dato específico de este proyecto.
3. Para la dirección de envío (Ship-to) de `BusinessPartners`: el campo es la colección anidada `BPAddresses` (`AddressType = "bo_ShipTo"` en Service Layer, no `"S"` como en la tabla HANA `CRD1` — son vocabularios distintos, HANA vs Service Layer). Usar `$expand=BPAddresses` y filtrar la colección resultante en memoria por `AddressType == "bo_ShipTo"` (Service Layer no permite `$filter` dentro de una colección anidada).
4. Documentar en el mensaje de commit de este task cualquier nombre que haya tenido que ajustarse respecto a lo asumido acá.

- [ ] **Step 2: Test — `PullAsync` con `TipoEntidad="Item"` arma el filtro correcto y mapea los campos**

Abrir `tests/PortalSaas.Core.Tests/Integraciones/SapDocumentConnectorTests.cs`, revisar cómo se fakea `ISapSession`/`ISapConnectionProvider` en los tests existentes de este archivo (o de `SalesDocumentServiceTests`/`ItemCrossReferenceServiceTests` si no hay precedente acá) antes de escribir el fake nuevo — reusar el mismo patrón de fake, no inventar uno paralelo. Agregar:

```csharp
[Fact]
public async Task PullAsync_TipoEntidadItem_ConsultaServiceLayerYMapeaCampos()
{
    var sesionFalsa = new SapSessionFalsaParaItems(new List<SapWmsItemRow>
    {
        new() { ItemCode = "ITM001", ItemName = "Artículo de prueba", CodeBars = "7801234567890", UpdateDate = new DateTime(2026, 8, 15) },
    });
    var proveedorFalso = new SapConnectionProviderFalso(sesionFalsa);
    var conector = new SapDocumentConnector(_salesFalso, _purchaseFalso, _inventoryFalso, proveedorFalso);

    var config = """{"TipoEntidad":"Item"}""";
    var resultado = await conector.PullAsync(config, CancellationToken.None);

    var registro = Assert.Single(resultado);
    Assert.Equal("ITM001", registro["ItemCode"]);
    Assert.Equal("Artículo de prueba", registro["ItemName"]);
    Assert.Equal("7801234567890", registro["BarCode"]);
}
```

(Nombres exactos de las clases fake `SapSessionFalsaParaItems`/`SapConnectionProviderFalso` y de los campos inyectados en `_salesFalso`/`_purchaseFalso`/`_inventoryFalso` del constructor existente de `SapDocumentConnector` — confirmar contra el archivo real de test antes de escribir, ya tiene fakes para los otros 3 servicios inyectados en el constructor actual.)

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter PullAsync_TipoEntidadItem_ConsultaServiceLayerYMapeaCampos`
Expected: FAIL — `PullAsync` sigue lanzando `NotSupportedException`, y el constructor no acepta `ISapConnectionProvider` todavía.

- [ ] **Step 3: Agregar `ISapConnectionProvider` al constructor de `SapDocumentConnector`**

```csharp
private readonly ISapConnectionProvider _sapConnectionProvider;

public SapDocumentConnector(
    ISalesDocumentService salesDocumentService,
    IPurchaseDocumentService purchaseDocumentService,
    IInventoryDocumentService inventoryDocumentService,
    ISapConnectionProvider sapConnectionProvider)
{
    _salesDocumentService = salesDocumentService;
    _purchaseDocumentService = purchaseDocumentService;
    _inventoryDocumentService = inventoryDocumentService;
    _sapConnectionProvider = sapConnectionProvider;
}
```

- [ ] **Step 4: Implementar `PullAsync`**

```csharp
private sealed record SapWmsOutboundConfig(string TipoEntidad);

public async Task<IReadOnlyList<IntegrationRecord>> PullAsync(
    string conectorConfigJson,
    CancellationToken cancellationToken)
{
    var config = System.Text.Json.JsonSerializer.Deserialize<SapWmsOutboundConfig>(conectorConfigJson)
        ?? throw new InvalidOperationException("Config de conector Sap (Bajada) inválida o vacía.");

    var session = await _sapConnectionProvider.GetConnectionAsync(cancellationToken);

    return config.TipoEntidad switch
    {
        "Item" => await LeerItemsAsync(session, cancellationToken),
        "Store" => await LeerStoresAsync(session, cancellationToken),
        "InboundTraslado" => await LeerTrasladosAsync(session, cancellationToken),
        _ => throw new InvalidOperationException($"TipoEntidad '{config.TipoEntidad}' no soportado en PullAsync."),
    };
}

private static async Task<IReadOnlyList<IntegrationRecord>> LeerItemsAsync(ISapSession session, CancellationToken ct)
{
    var filtro = "U_NX_EnviarWMS eq 'Y' and InvntItem eq 'tYES'";
    var filas = await session.GetAsync<List<SapWmsItemRow>>("Items", filtro, ct: ct) ?? [];

    return filas
        .Where(f => !string.IsNullOrWhiteSpace(f.CodeBars) && f.CodeBars != "0")
        .Select(f => new IntegrationRecord(new Dictionary<string, object?>
        {
            ["ItemCode"] = f.ItemCode,
            ["ItemName"] = f.ItemName,
            ["BarCode"] = f.CodeBars,
            ["SourceUpdateDate"] = f.UpdateDate,
        }))
        .ToList();
}

private static async Task<IReadOnlyList<IntegrationRecord>> LeerStoresAsync(ISapSession session, CancellationToken ct)
{
    var filtro = "U_NX_EnviarWMS eq 'Y'";
    var filas = await session.GetAsync<List<SapWmsStoreRow>>("BusinessPartners", filtro, "BPAddresses", ct) ?? [];

    var registros = new List<IntegrationRecord>();
    foreach (var fila in filas)
    {
        var direccionEnvio = fila.BPAddresses?.FirstOrDefault(a => a.AddressType == "bo_ShipTo");
        if (direccionEnvio is null)
        {
            continue;
        }

        registros.Add(new IntegrationRecord(new Dictionary<string, object?>
        {
            ["CardCode"] = fila.CardCode,
            ["CardName"] = fila.CardName,
            ["Street"] = direccionEnvio.Street,
            ["City"] = direccionEnvio.City,
            ["ZipCode"] = direccionEnvio.ZipCode,
            ["SourceUpdateDate"] = fila.UpdateDate,
        }));
    }

    return registros;
}

private static async Task<IReadOnlyList<IntegrationRecord>> LeerTrasladosAsync(ISapSession session, CancellationToken ct)
{
    var filtro = "(U_NX_WMS_SEND eq 'Y' or U_NX_WMS_SEND eq 'EN PROCESO ENVIO WMS' or U_NX_WMS_SEND eq 'EN PROCESO RE-ENVIO WMS') and U_NX_shipment_type ne ''";
    var filas = await session.GetAsync<List<SapWmsTrasladoRow>>("InventoryTransferRequests", filtro, "StockTransferLines", ct) ?? [];

    return filas
        .Select(f => new IntegrationRecord(new Dictionary<string, object?>
        {
            ["SapDocEntry"] = f.DocEntry,
            ["ShipmentType"] = f.U_NX_shipment_type,
            ["SourceUpdateDate"] = f.UpdateDate,
            ["Lineas"] = (f.StockTransferLines ?? [])
                .Where(l => l.Quantity != 0)
                .Select((l, indice) => new IntegrationRecord(new Dictionary<string, object?>
                {
                    ["ItemCode"] = l.ItemCode,
                    ["Quantity"] = l.Quantity,
                    ["WhsCode"] = l.WarehouseCode,
                    ["LineNum"] = indice,
                }))
                .ToList(),
        }))
        .ToList();
}
```

Agregar las clases de modelo (mismo estilo que `SapAlternateCatNum` — POCO plano, nombres PascalCase idénticos a Service Layer, sin decorar):

```csharp
// Junto a las demás clases de modelo Sap del connector, o en un archivo nuevo SapWmsOutboundModels.cs
internal sealed class SapWmsItemRow
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? CodeBars { get; set; }
    public DateTime UpdateDate { get; set; }
}

internal sealed class SapWmsStoreRow
{
    public string CardCode { get; set; } = string.Empty;
    public string CardName { get; set; } = string.Empty;
    public DateTime UpdateDate { get; set; }
    public List<SapWmsBpAddressRow>? BPAddresses { get; set; }
}

internal sealed class SapWmsBpAddressRow
{
    public string AddressType { get; set; } = string.Empty;
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? ZipCode { get; set; }
}

internal sealed class SapWmsTrasladoRow
{
    public int DocEntry { get; set; }
    public string U_NX_shipment_type { get; set; } = string.Empty;
    public DateTime UpdateDate { get; set; }
    public List<SapWmsTrasladoLineaRow>? StockTransferLines { get; set; }
}

internal sealed class SapWmsTrasladoLineaRow
{
    public string ItemCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
}
```

- [ ] **Step 5: Correr y verificar que pasa**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter PullAsync_TipoEntidadItem_ConsultaServiceLayerYMapeaCampos`
Expected: PASS.

- [ ] **Step 6: Agregar los 2 tests restantes (`Store`, `InboundTraslado`)** siguiendo el mismo patrón — fake de `ISapSession` que devuelve una lista de `SapWmsStoreRow`/`SapWmsTrasladoRow`, aserciones sobre los campos mapeados, incluyendo el caso de una línea con `Quantity == 0` (debe excluirse) y una dirección sin `AddressType == "bo_ShipTo"` (debe excluir esa tienda completa).

- [ ] **Step 7: Test — `TipoEntidad` desconocido lanza excepción clara**

```csharp
[Fact]
public async Task PullAsync_TipoEntidadDesconocido_LanzaExcepcionClara()
{
    var conector = new SapDocumentConnector(_salesFalso, _purchaseFalso, _inventoryFalso, _proveedorFalsoVacio);
    var config = """{"TipoEntidad":"Desconocido"}""";

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => conector.PullAsync(config, CancellationToken.None));
    Assert.Contains("Desconocido", ex.Message);
}
```

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter SapDocumentConnectorTests`
Expected: todos los tests de `SapDocumentConnectorTests` en verde (nuevos + preexistentes).

- [ ] **Step 8: Build completo + suite completa**

Run: `dotnet build PortalSaas.sln` — Expected: 0 warnings/0 errores.
Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj` — Expected: todo en verde.

- [ ] **Step 9: Commit**

```bash
git add src/PortalSaas.Core/Integraciones/SapDocumentConnector.cs tests/PortalSaas.Core.Tests/Integraciones/SapDocumentConnectorTests.cs
git commit -m "feat: PullAsync real en SapDocumentConnector (Item, Store, Traslado) via Service Layer"
```

---

### Task 4: Cableado de la rama `Bajada` en `IntegrationSyncHostedService` (`Portal SaaS - Core`)

**Files:**
- Modify: `src/PortalSaas.Integrations/IntegrationSyncHostedService.cs`
- Test: `tests/PortalSaas.Core.Tests/Integraciones/IntegrationSyncHostedServiceTests.cs` (archivo ya existe, agregar casos)

**Interfaces:**
- Consumes: `IIntegrationEntityWriter.EscribirAsync(Guid, IReadOnlyList<IntegrationRecord>, CancellationToken)` (Task 2). `IIntegrationConnector.PullAsync` (Task 3).
- Produces: rama `Bajada` funcional — hoja de esta cadena para la etapa 1, sin consumidores posteriores dentro de este plan.

- [ ] **Step 1: Leer el método actual completo**

Abrir `src/PortalSaas.Integrations/IntegrationSyncHostedService.cs`, confirmar la firma exacta de `EjecutarCicloAsync`/`EjecutarIntegracionAsync` tras los cambios de Ronda B (ya resuelve `writers`? — no, hoy solo resuelve `readers`/`conectores`, hay que agregar `writers` al mismo punto). Confirmar también dónde vive el `override.Set(definicion.CompanyId)` agregado en el fix de Ronda B (debe seguir corriendo ANTES de resolver `writers`, igual que ya corre antes de `readers`/`conectores` — mismo motivo: el `WmsDbContext` de los writers de Task 2 depende de `ICurrentCompanyAccessor.HasCompany`/`.CompanyId`).

- [ ] **Step 2: Test — ciclo `Bajada` exitoso escribe vía el writer correcto**

```csharp
[Fact]
public async Task EjecutarCicloAsync_DireccionBajadaConWriterYPullExitoso_EscribeYRegistraLog()
{
    // Preparar: 1 IntegrationDefinition con Direccion=Bajada, EntidadNegocio="SapWms.Item",
    // un ConectorFalso cuyo PullAsync retorna 2 IntegrationRecord, y un WriterFalso
    // (clase de test nueva, análoga a ReaderFalso ya existente en este archivo) que
    // registra los registros recibidos en una lista pública para assertar.
    // Confirmar el nombre exacto de la clase fake existente para IIntegrationConnector
    // en este archivo (ConectorFalso o similar) antes de reusarla.

    var writerFalso = new WriterFalso(entidadNegocio: "SapWms.Item");
    // ... arrange completo siguiendo el patrón de EjecutarCicloAsync_ConReaderYPushExitoso_...
    // (Ronda B, mismo archivo) pero con Direccion.Bajada, un conector cuyo PullAsync
    // retorna registros fijos, y sin reader/mapeoServicio de por medio.

    await servicio.EjecutarCicloAsync(CancellationToken.None);

    Assert.Equal(2, writerFalso.RegistrosRecibidos.Count);
}
```

(Completar el arrange exacto leyendo primero cómo está armado `EjecutarCicloAsync_ConReaderYPushExitoso_...` en este mismo archivo — mismo nivel de detalle de mocks de `PortalSaasDbContext`/`ISecretoCifradoService`/`ICurrentCompanyOverride`, ya resueltos en Ronda B, reusar tal cual.)

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter EjecutarCicloAsync_DireccionBajadaConWriterYPullExitoso_EscribeYRegistraLog`
Expected: FAIL — la rama `Bajada` sigue lanzando `NotSupportedException`.

- [ ] **Step 3: Implementar la rama `Bajada`**

Reemplazar el bloque actual (`if (definicion.Direccion is IntegrationDireccion.Bajada or IntegrationDireccion.Ambas) { throw ... }`) por:

```csharp
if (definicion.Direccion is IntegrationDireccion.Ambas)
{
    throw new NotSupportedException(
        $"La dirección 'Ambas' todavía no está implementada para la integración '{definicion.Nombre}'.");
}

if (definicion.Direccion is IntegrationDireccion.Bajada)
{
    var conectorConfigJson = DescifrarConfigConector(secretoServicio, definicion);

    var writer = writers.FirstOrDefault(w => w.EntidadNegocio == definicion.EntidadNegocio)
        ?? throw new InvalidOperationException($"No hay writer registrado para entidad '{definicion.EntidadNegocio}'.");

    var registrosExternos = await conector.PullAsync(conectorConfigJson, cancellationToken);
    await writer.EscribirAsync(definicion.CompanyId, registrosExternos, cancellationToken);

    log.RegistrosProcesados = registrosExternos.Count;
}
```

Agregar la resolución de `writers` junto a `readers` en `EjecutarCicloAsync` (mismo punto, después de fijar `ICurrentCompanyOverride`):

```csharp
var writers = scope.ServiceProvider.GetServices<IIntegrationEntityWriter>().ToList();
```

Y pasarlo como parámetro nuevo a `EjecutarIntegracionAsync` (misma forma que `readers`).

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter EjecutarCicloAsync_DireccionBajadaConWriterYPullExitoso_EscribeYRegistraLog`
Expected: PASS.

- [ ] **Step 5: Test — sin writer registrado para la entidad lanza excepción clara**

```csharp
[Fact]
public async Task EjecutarCicloAsync_DireccionBajadaSinWriterRegistrado_LanzaYRegistraError()
{
    // IntegrationDefinition Direccion=Bajada, EntidadNegocio="Entidad.Inexistente", sin
    // ningún WriterFalso registrado en el scope de test.
    await servicio.EjecutarCicloAsync(CancellationToken.None);
    // Assert: el log persistido para esa definición tiene Resultado=Error y
    // DetalleError contiene "No hay writer registrado".
}
```

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter IntegrationSyncHostedServiceTests`
Expected: todos los tests de este archivo en verde (nuevos + preexistentes, incluidos los de Ronda B).

- [ ] **Step 6: Build completo + suite completa**

Run: `dotnet build PortalSaas.sln` — Expected: 0/0.
Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj` — Expected: todo en verde.

- [ ] **Step 7: Commit**

```bash
git add src/PortalSaas.Integrations/IntegrationSyncHostedService.cs tests/PortalSaas.Core.Tests/Integraciones/IntegrationSyncHostedServiceTests.cs
git commit -m "feat: cablear la dirección Bajada en IntegrationSyncHostedService"
```

---

### Task 5: `IIntegrationEntityReader` × 3 para el staging SAP→WMS (`Modulo.Wms`)

**Files:**
- Create: `src/Modulo.Wms/Services/WmsSapStageItemReader.cs`
- Create: `src/Modulo.Wms/Services/WmsSapStageStoreReader.cs`
- Create: `src/Modulo.Wms/Services/WmsSapStageInboundReader.cs`
- Modify: `src/Modulo.Wms/ModuloWms.cs` (registrar los 3 en DI)
- Test: `tests/Modulo.Wms.Tests/Services/WmsSapStageItemReaderTests.cs`
- Test: `tests/Modulo.Wms.Tests/Services/WmsSapStageStoreReaderTests.cs`
- Test: `tests/Modulo.Wms.Tests/Services/WmsSapStageInboundReaderTests.cs`

**Interfaces:**
- Consumes: `IIntegrationEntityReader` (ya existe, con `MarcarProcesadoAsync` de Ronda B). `WmsSapStageItem`/`WmsSapStageStore`/`WmsSapStageInboundHdr`/`WmsSapStageInboundDtl` (Task 1).
- Produces: `WmsSapStageItemReader.EntidadNegocio => "SapWms.Item.Subida"`, `WmsSapStageStoreReader.EntidadNegocio => "SapWms.Store.Subida"`, `WmsSapStageInboundReader.EntidadNegocio => "SapWms.Traslado.Subida"` (sufijo `.Subida` para distinguir de la `EntidadNegocio` de Bajada de la misma entidad de negocio — son 2 `IntegrationDefinition` distintas por entidad, una por dirección, y el motor resuelve reader/writer por coincidencia exacta de `EntidadNegocio`). `Fields["TipoDocumento"]` = `"Item"`/`"Store"`/`"IbShipment"` en cada `IntegrationRecord` producido — consumido por Task 6 (`WmsCloudConnector`) para saber qué lista XML armar.

- [ ] **Step 1: Test de `WmsSapStageItemReader`**

```csharp
// tests/Modulo.Wms.Tests/Services/WmsSapStageItemReaderTests.cs
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSapStageItemReaderTests
{
    private static WmsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(opciones);
    }

    [Fact]
    public async Task LeerPendientesAsync_SoloTraePendientesDeLaCompania()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var otraCompanyId = Guid.NewGuid();

        contexto.WmsSapStageItems.AddRange(
            new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Pendiente },
            new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM002", ItemName = "B", Status = WmsSapStageStatus.ProcesadoWms },
            new WmsSapStageItem { CompanyId = otraCompanyId, ItemCode = "ITM003", ItemName = "C", Status = WmsSapStageStatus.Pendiente });
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageItemReader(contexto);
        var resultado = await reader.LeerPendientesAsync(companyId, CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal("Item", registro["TipoDocumento"]);
        Assert.Equal("ITM001", registro["ItemCode"]);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_Exito_ActualizaStatusYSyncedAt()
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
        Assert.Equal(WmsSapStageStatus.ProcesadoWms, actualizada.Status);
        Assert.NotNull(actualizada.SyncedAt);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_Error_ActualizaStatusYErrorMsg()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var fila = new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageItems.Add(fila);
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageItemReader(contexto);
        var registro = (await reader.LeerPendientesAsync(companyId, CancellationToken.None)).Single();

        await reader.MarcarProcesadoAsync(companyId, registro, exito: false, mensajeError: "Rechazado por WMS", CancellationToken.None);

        var actualizada = await contexto.WmsSapStageItems.SingleAsync();
        Assert.Equal(WmsSapStageStatus.ErrorWms, actualizada.Status);
        Assert.Equal("Rechazado por WMS", actualizada.ErrorMsg);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsSapStageItemReaderTests`
Expected: FAIL — clase no existe.

- [ ] **Step 3: Implementar `WmsSapStageItemReader`**

```csharp
// src/Modulo.Wms/Services/WmsSapStageItemReader.cs
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Lector del motor genérico (IIntegrationEntityReader) para el staging de Artículos
/// pendientes de enviar a Oracle WMS Cloud (etapa Subida de la Ronda C). Usa el
/// LineId de la propia fila como "_StagingLineIds" de una sola posición -- mismo
/// campo interno que WmsSlshInventoryReader (Ronda B), reutilizado acá aunque el
/// grupo siempre tenga tamaño 1 (no hay agrupación como en Traslados).
/// </summary>
public class WmsSapStageItemReader : IIntegrationEntityReader
{
    private readonly WmsDbContext _contexto;

    public WmsSapStageItemReader(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Item.Subida";

    public async Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var filas = await _contexto.WmsSapStageItems
            .Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Pendiente)
            .ToListAsync(cancellationToken);

        return filas
            .Select(f => new IntegrationRecord(new Dictionary<string, object?>
            {
                ["TipoDocumento"] = "Item",
                ["ItemCode"] = f.ItemCode,
                ["ItemName"] = f.ItemName,
                ["BarCode"] = f.BarCode,
                ["_StagingLineIds"] = new List<long> { f.LineId },
            }))
            .ToList();
    }

    public async Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken)
    {
        var idsDeLinea = (List<long>)registro["_StagingLineIds"]!;

        var filas = await _contexto.WmsSapStageItems
            .Where(f => idsDeLinea.Contains(f.LineId))
            .ToListAsync(cancellationToken);

        foreach (var fila in filas)
        {
            fila.Status = exito ? WmsSapStageStatus.ProcesadoWms : WmsSapStageStatus.ErrorWms;
            fila.ErrorMsg = exito ? null : mensajeError;
            fila.SyncedAt = exito ? DateTimeOffset.UtcNow : fila.SyncedAt;
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsSapStageItemReaderTests`
Expected: PASS (3/3).

- [ ] **Step 5: Repetir Steps 1-4 para `WmsSapStageStoreReader`**

Mismo patrón exacto, `EntidadNegocio => "SapWms.Store.Subida"`, `Fields["TipoDocumento"] = "Store"`, campos `CardCode`/`CardName`/`Street`/`City`/`ZipCode`.

- [ ] **Step 6: `WmsSapStageInboundReader` — agrupación por cabecera (no hay agrupación real, cada `WmsSapStageInboundHdr` ya es una unidad; el detalle se anida)**

Test:

```csharp
// tests/Modulo.Wms.Tests/Services/WmsSapStageInboundReaderTests.cs
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSapStageInboundReaderTests
{
    private static WmsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(opciones);
    }

    [Fact]
    public async Task LeerPendientesAsync_ArmaCabeceraConLineasAnidadas()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdr = new WmsSapStageInboundHdr { CompanyId = companyId, SapDocEntry = 500123, ShipmentType = "TRASLADO_ESTANDAR", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageInboundHdrs.Add(hdr);
        await contexto.SaveChangesAsync();
        contexto.WmsSapStageInboundDtls.Add(new WmsSapStageInboundDtl { ParentId = hdr.LineId, ItemCode = "ITM001", Quantity = 10m, WhsCode = "01", LineNum = 0 });
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageInboundReader(contexto);
        var resultado = await reader.LeerPendientesAsync(companyId, CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal("IbShipment", registro["TipoDocumento"]);
        Assert.Equal(500123, registro["SapDocEntry"]);
        var lineas = (List<IntegrationRecord>)registro["Lineas"]!;
        var linea = Assert.Single(lineas);
        Assert.Equal("ITM001", linea["ItemCode"]);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_Error_ActualizaSoloLaCabecera()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdr = new WmsSapStageInboundHdr { CompanyId = companyId, SapDocEntry = 500123, ShipmentType = "TRASLADO_ESTANDAR", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageInboundHdrs.Add(hdr);
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageInboundReader(contexto);
        var registro = (await reader.LeerPendientesAsync(companyId, CancellationToken.None)).Single();

        await reader.MarcarProcesadoAsync(companyId, registro, exito: false, mensajeError: "BaseEntry inválido", CancellationToken.None);

        var actualizada = await contexto.WmsSapStageInboundHdrs.SingleAsync();
        Assert.Equal(WmsSapStageStatus.ErrorWms, actualizada.Status);
        Assert.Equal("BaseEntry inválido", actualizada.ErrorMsg);
    }
}
```

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsSapStageInboundReaderTests` — Expected: FAIL.

Implementación:

```csharp
// src/Modulo.Wms/Services/WmsSapStageInboundReader.cs
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

public class WmsSapStageInboundReader : IIntegrationEntityReader
{
    private readonly WmsDbContext _contexto;

    public WmsSapStageInboundReader(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Traslado.Subida";

    public async Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var hdrs = await _contexto.WmsSapStageInboundHdrs
            .Where(h => h.CompanyId == companyId && h.Status == WmsSapStageStatus.Pendiente)
            .ToListAsync(cancellationToken);

        var registros = new List<IntegrationRecord>();
        foreach (var hdr in hdrs)
        {
            var detalle = await _contexto.WmsSapStageInboundDtls
                .Where(d => d.ParentId == hdr.LineId)
                .OrderBy(d => d.LineNum)
                .ToListAsync(cancellationToken);

            registros.Add(new IntegrationRecord(new Dictionary<string, object?>
            {
                ["TipoDocumento"] = "IbShipment",
                ["SapDocEntry"] = hdr.SapDocEntry,
                ["ShipmentType"] = hdr.ShipmentType,
                ["Lineas"] = detalle
                    .Select(d => new IntegrationRecord(new Dictionary<string, object?>
                    {
                        ["ItemCode"] = d.ItemCode,
                        ["Quantity"] = d.Quantity,
                        ["WhsCode"] = d.WhsCode,
                        ["LineNum"] = d.LineNum,
                    }))
                    .ToList(),
                ["_StagingLineIds"] = new List<long> { hdr.LineId },
            }));
        }

        return registros;
    }

    public async Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken)
    {
        var idsDeLinea = (List<long>)registro["_StagingLineIds"]!;

        var filas = await _contexto.WmsSapStageInboundHdrs
            .Where(f => idsDeLinea.Contains(f.LineId))
            .ToListAsync(cancellationToken);

        foreach (var fila in filas)
        {
            fila.Status = exito ? WmsSapStageStatus.ProcesadoWms : WmsSapStageStatus.ErrorWms;
            fila.ErrorMsg = exito ? null : mensajeError;
            fila.SyncedAt = exito ? DateTimeOffset.UtcNow : fila.SyncedAt;
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }
}
```

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsSapStageInboundReaderTests` — Expected: PASS (2/2).

- [ ] **Step 7: Registrar los 3 readers en DI**

En `ModuloWms.cs`, junto a los writers de Task 2:

```csharp
services.AddScoped<IIntegrationEntityReader, WmsSapStageItemReader>();
services.AddScoped<IIntegrationEntityReader, WmsSapStageStoreReader>();
services.AddScoped<IIntegrationEntityReader, WmsSapStageInboundReader>();
```

- [ ] **Step 8: Build + test completo del proyecto**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj`
Expected: todos los tests en verde.

- [ ] **Step 9: Commit**

```bash
git add src/Modulo.Wms/Services/WmsSapStageItemReader.cs src/Modulo.Wms/Services/WmsSapStageStoreReader.cs src/Modulo.Wms/Services/WmsSapStageInboundReader.cs src/Modulo.Wms/ModuloWms.cs tests/Modulo.Wms.Tests/Services/WmsSapStageItemReaderTests.cs tests/Modulo.Wms.Tests/Services/WmsSapStageStoreReaderTests.cs tests/Modulo.Wms.Tests/Services/WmsSapStageInboundReaderTests.cs
git commit -m "feat: readers del motor de integración para etapa Subida SAP->WMS (Item, Store, Traslado)"
```

---

### Task 6: `WmsCloudConnector` — POST real a Oracle WMS Cloud (`Modulo.Wms`)

**Files:**
- Create: `src/Modulo.Wms/Services/WmsCloudConnector.cs`
- Modify: `src/Modulo.Wms/ModuloWms.cs` (registrar `IIntegrationConnector`, agregar `HttpClient` con nombre si hace falta)
- Test: `tests/Modulo.Wms.Tests/Services/WmsCloudConnectorTests.cs`

**Interfaces:**
- Consumes: `IIntegrationConnector { string Tipo; Task<IReadOnlyList<IntegrationRecord>> PullAsync(...); Task<IReadOnlyList<IntegrationPushResult>> PushAsync(string conectorConfigJson, IReadOnlyList<IntegrationRecord> registros, CancellationToken); }` (`PortalSaas.Abstractions/Contratos/Integraciones/IIntegrationConnector.cs`, firma ya confirmada — `PushAsync` retorna `IntegrationPushResult` por registro, mismo contrato que `SapDocumentConnector` desde el fix de Ronda B). `Fields["TipoDocumento"]` = `"Item"`/`"Store"`/`"IbShipment"` (Task 5).
- Produces: hoja de esta cadena — consumido indirectamente por `IntegrationSyncHostedService` (ya cableado desde Ronda B, sin cambios necesarios acá porque `IIntegrationConnector` no cambia de firma).

**Verificación previa obligatoria (Step 1) — no asumir el vocabulario de campo que Oracle WMS Cloud espera:**

- [ ] **Step 1: Confirmar el vocabulario de campo real esperado por Oracle WMS Cloud**

El legado arma cada nodo XML dinámicamente a partir de los nombres de columna de sus propias tablas `STG_SAP_ITEM`/`STG_SAP_STORE`/`STG_SAP_IB_SHIPMENT_HDR`/`STG_SAP_IB_SHIPMENT_DTL` (`new XElement(kvp.Key.ToLower(), valor)`, confirmado en `WmsOutbound_ItemProcessor.cs`/`WmsOutbound_StoreProcessor.cs`/`WmsOutbound_ShipmentProcessor.cs`, método `MapFields` o equivalente inline) — es decir, esos nombres de columna SON el vocabulario que Oracle WMS Cloud espera recibir. Antes de escribir el builder de XML:
1. Buscar el DDL real de esas 4 tablas en `C:\PROYECTOS\WMS_Suite\db\provisioning\hana\` (`grep -rln "STG_SAP_ITEM\|STG_SAP_STORE\|STG_SAP_IB_SHIPMENT" C:\PROYECTOS\WMS_Suite\db\provisioning\hana\`) y extraer los nombres de columna reales.
2. Mapear cada campo del `IntegrationRecord` (`ItemCode`/`ItemName`/`BarCode` para Item, etc.) al nombre de columna equivalente encontrado en el DDL — si no coincide 1:1 con lo asumido en Task 1/3, usar el nombre real del DDL como nombre de nodo XML (el nombre de la COLUMNA de staging nuestra puede seguir siendo el que ya se definió en Task 1, esto solo afecta el nombre del NODO XML de salida, que se arma explícito en este task, no reflejado dinámicamente como hace el legado).
3. Si el DDL no está disponible o los nombres no son inequívocos, usar los nombres de campo ya usados en `IntegrationRecord` (`item_code`, `item_name`, `bar_code` en minúsculas snake_case, siguiendo la convención `kvp.Key.ToLower()` del legado) como mejor esfuerzo, y dejarlo anotado explícitamente en el mensaje de commit de este task como riesgo pendiente de validar contra un ambiente real.

- [ ] **Step 2: Test — `PushAsync` arma el XML de `Item` y hace POST form-urlencoded**

```csharp
// tests/Modulo.Wms.Tests/Services/WmsCloudConnectorTests.cs
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsCloudConnectorTests
{
    private sealed class HttpHandlerFalso : HttpMessageHandler
    {
        public HttpRequestMessage? UltimaRequest { get; private set; }
        public string? UltimoContenido { get; private set; }
        private readonly HttpStatusCode _statusCode;

        public HttpHandlerFalso(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UltimaRequest = request;
            UltimoContenido = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_statusCode) { Content = new StringContent("{}") };
        }
    }

    [Fact]
    public async Task PushAsync_ConTipoDocumentoItem_PosteaXmlConNodoItem()
    {
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var conector = new WmsCloudConnector(httpClient, NullLogger<WmsCloudConnector>.Instance);

        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["TipoDocumento"] = "Item",
            ["ItemCode"] = "ITM001",
            ["ItemName"] = "Artículo de prueba",
            ["BarCode"] = "7801234567890",
        });

        var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01"}""";
        var resultado = await conector.PushAsync(config, [registro], CancellationToken.None);

        Assert.Single(resultado);
        Assert.True(resultado[0].Exito);
        Assert.Contains("ListOfItems", handlerFalso.UltimoContenido);
        Assert.Contains("ITM001", handlerFalso.UltimoContenido);
    }

    [Fact]
    public async Task PushAsync_WmsRespondeError_MarcaRegistroComoFallido()
    {
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.BadRequest);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var conector = new WmsCloudConnector(httpClient, NullLogger<WmsCloudConnector>.Instance);

        var registro = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["TipoDocumento"] = "Item",
            ["ItemCode"] = "ITM001",
            ["ItemName"] = "Artículo de prueba",
            ["BarCode"] = "7801234567890",
        });

        var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01"}""";
        var resultado = await conector.PushAsync(config, [registro], CancellationToken.None);

        Assert.Single(resultado);
        Assert.False(resultado[0].Exito);
        Assert.NotNull(resultado[0].MensajeError);
    }
}
```

- [ ] **Step 3: Correr y verificar que falla**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsCloudConnectorTests`
Expected: FAIL — `WmsCloudConnector` no existe.

- [ ] **Step 4: Implementar `WmsCloudConnector`**

```csharp
// src/Modulo.Wms/Services/WmsCloudConnector.cs
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Conector del motor genérico (IIntegrationConnector) que postea documentos reales a
/// Oracle WMS Cloud (LogFire), mismo formato XML descubierto en el legado
/// (WmsApiService.SendXmlWithResponseAsync, C:\PROYECTOS\WMS_Suite): POST
/// form-urlencoded con "xml_data" y auth Basic. La estructura raíz (LgfData/Header) es
/// fija; la lista (ListOfItems/ListOfStores/ListOfIbShipments) depende de
/// Fields["TipoDocumento"] de cada registro -- un lote nunca mezcla tipos porque cada
/// IntegrationDefinition de Subida es de una sola entidad (ver Task 5).
/// </summary>
public class WmsCloudConnector : IIntegrationConnector
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WmsCloudConnector> _logger;

    public WmsCloudConnector(HttpClient httpClient, ILogger<WmsCloudConnector> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string Tipo => "WmsCloud";

    public Task<IReadOnlyList<IntegrationRecord>> PullAsync(string conectorConfigJson, CancellationToken cancellationToken) =>
        throw new NotSupportedException("WmsCloudConnector.PullAsync no está implementado -- este conector solo envía (Subida), nunca lee de Oracle WMS Cloud.");

    public async Task<IReadOnlyList<IntegrationPushResult>> PushAsync(
        string conectorConfigJson,
        IReadOnlyList<IntegrationRecord> registros,
        CancellationToken cancellationToken)
    {
        if (registros.Count == 0)
        {
            return Array.Empty<IntegrationPushResult>();
        }

        var config = JsonSerializer.Deserialize<WmsCloudConfig>(conectorConfigJson)
            ?? throw new InvalidOperationException("Config de conector WmsCloud inválida o vacía.");

        var resultados = new List<IntegrationPushResult>();
        var errores = new List<Exception>();

        foreach (var registro in registros)
        {
            try
            {
                var xml = ArmarXml(registro, config);
                await EnviarAsync(xml, config, cancellationToken);
                resultados.Add(new IntegrationPushResult(registro, Exito: true, MensajeError: null));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando documento a Oracle WMS Cloud");
                errores.Add(ex);
                resultados.Add(new IntegrationPushResult(registro, Exito: false, MensajeError: ex.Message));
            }
        }

        if (errores.Count == registros.Count)
        {
            throw new AggregateException("Todos los documentos del lote fallaron al enviarse a Oracle WMS Cloud.", errores);
        }

        return resultados;
    }

    private static XDocument ArmarXml(IntegrationRecord registro, WmsCloudConfig config)
    {
        var tipoDocumento = (string)registro["TipoDocumento"]!;
        var (entity, nombreLista, nombreItem) = tipoDocumento switch
        {
            "Item" => ("item", "ListOfItems", "item"),
            "Store" => ("store", "ListOfStores", "store"),
            "IbShipment" => ("ib_shipment", "ListOfIbShipments", "ib_shipment"),
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

        var nodoItem = tipoDocumento switch
        {
            "Item" => new XElement(nombreItem,
                new XElement("item_alternate_code", registro["ItemCode"]),
                new XElement("description", registro["ItemName"]),
                new XElement("bar_code", registro["BarCode"])),
            "Store" => new XElement(nombreItem,
                new XElement("code", registro["CardCode"]),
                new XElement("name", registro["CardName"]),
                new XElement("parent_company_id", config.ParentCompanyCode)),
            "IbShipment" => ArmarNodoIbShipment(registro),
            _ => throw new InvalidOperationException($"TipoDocumento '{tipoDocumento}' no soportado en WmsCloudConnector."),
        };

        return new XDocument(new XElement("LgfData", header, new XElement(nombreLista, nodoItem)));
    }

    private static XElement ArmarNodoIbShipment(IntegrationRecord registro)
    {
        var lineas = (List<IntegrationRecord>)registro["Lineas"]!;
        var hdr = new XElement("ib_shipment_hdr",
            new XElement("shipment_nbr", registro["SapDocEntry"]),
            new XElement("shipment_type", registro["ShipmentType"]));

        var detalles = lineas.Select(l => new XElement("ib_shipment_dtl",
            new XElement("seq_nbr", l["LineNum"]),
            new XElement("item_alternate_code", l["ItemCode"]),
            new XElement("expected_qty", l["Quantity"]),
            new XElement("dest_facility_code", l["WhsCode"])));

        return new XElement("ib_shipment", hdr, detalles);
    }

    private async Task EnviarAsync(XDocument xml, WmsCloudConfig config, CancellationToken cancellationToken)
    {
        var authToken = Encoding.UTF8.GetBytes($"{config.Usuario}:{config.Clave}");
        using var request = new HttpRequestMessage(HttpMethod.Post, config.ApiUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["xml_data"] = xml.ToString() }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var cuerpo = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Oracle WMS Cloud respondió {(int)response.StatusCode}: {cuerpo}");
        }
    }

    private sealed record WmsCloudConfig(string ApiUrl, string Usuario, string Clave, string ClientEnvCode, string ParentCompanyCode);
}
```

- [ ] **Step 5: Correr y verificar que pasa**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsCloudConnectorTests`
Expected: PASS (2/2).

- [ ] **Step 6: Test — `PushAsync` con `IbShipment` arma cabecera + detalle anidado**

```csharp
[Fact]
public async Task PushAsync_ConTipoDocumentoIbShipment_PosteaXmlConDetalleAnidado()
{
    var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK);
    var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
    var conector = new WmsCloudConnector(httpClient, NullLogger<WmsCloudConnector>.Instance);

    var registro = new IntegrationRecord(new Dictionary<string, object?>
    {
        ["TipoDocumento"] = "IbShipment",
        ["SapDocEntry"] = 500123,
        ["ShipmentType"] = "TRASLADO_ESTANDAR",
        ["Lineas"] = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["ItemCode"] = "ITM001", ["Quantity"] = 10m, ["WhsCode"] = "01", ["LineNum"] = 0 }),
        },
    });

    var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01"}""";
    var resultado = await conector.PushAsync(config, [registro], CancellationToken.None);

    Assert.True(resultado[0].Exito);
    Assert.Contains("500123", handlerFalso.UltimoContenido);
    Assert.Contains("ib_shipment_dtl", handlerFalso.UltimoContenido);
}
```

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsCloudConnectorTests`
Expected: PASS (3/3).

- [ ] **Step 7: Registrar `WmsCloudConnector` en DI**

En `ModuloWms.cs`, junto al resto de `RegisterServices` — usar `AddHttpClient` (named o tipado) para que el `HttpClient` se gestione vía `IHttpClientFactory` (evita el problema clásico de socket exhaustion de crear `HttpClient` directo):

```csharp
services.AddHttpClient<IIntegrationConnector, WmsCloudConnector>();
```

(Si el proyecto ya registra otros `IIntegrationConnector` de forma distinta — confirmar contra cómo se registró `SapDocumentConnector` en `Portal SaaS - Core` antes de elegir el método exacto; si `AddHttpClient<TService,TImplementation>` no aplica limpiamente porque `IIntegrationConnector` ya tiene otro registro con el mismo tipo de interfaz para `SapDocumentConnector`, usar `AddHttpClient("WmsCloud")` con un `IHttpClientFactory` inyectado en el constructor de `WmsCloudConnector` en vez de `HttpClient` directo, y ajustar el constructor/tests de este task en consecuencia.)

- [ ] **Step 8: Build + test completo del proyecto**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj`
Expected: todos los tests en verde.

- [ ] **Step 9: Commit**

```bash
git add src/Modulo.Wms/Services/WmsCloudConnector.cs src/Modulo.Wms/ModuloWms.cs tests/Modulo.Wms.Tests/Services/WmsCloudConnectorTests.cs
git commit -m "feat: WmsCloudConnector -- POST real a Oracle WMS Cloud (Item, Store, Traslado)"
```

---

## Self-Review (registrado acá, no repetir en ejecución)

**1. Cobertura del spec:** Filtro de negocio por entidad (spec sección "Contexto") → Task 3 Step 4 (`$filter` OData por entidad). Etapa 1 Bajada (staging local) → Tasks 1, 2, 3, 4. Etapa 2 Subida (WMS real) → Tasks 5, 6. Cierre del círculo (`DocEntry` == `BaseEntry` de Ronda B) → documentado en Task 1 (`WmsSapStageInboundHdr.SapDocEntry`) y Task 3 (`Fields["SapDocEntry"]`), verificable end-to-end solo contra un ambiente real (fuera de alcance de este plan, ver spec "Riesgos y supuestos explícitos").

**2. Placeholder scan:** sin "TBD"/"TODO". Las verificaciones marcadas como "confirmar contra el código real" (Task 3 Step 1, Task 6 Step 1, Task 4 Step 1) traen instrucciones concretas de qué comando correr y cómo adaptar el resultado — no son placeholders, son el mismo patrón de verificación ya usado en Ronda A/B (ej. Ronda B Task 1 Step 1).

**3. Consistencia de tipos:** `IntegrationRecord.Fields` con las claves `ItemCode`/`ItemName`/`BarCode` (Item), `CardCode`/`CardName`/`Street`/`City`/`ZipCode` (Store), `SapDocEntry`/`ShipmentType`/`Lineas`/`LineNum`/`WhsCode`/`Quantity` (Traslado) se usan con el mismo nombre exacto en Task 2 (writer, consumidor), Task 3 (connector Bajada, productor), Task 5 (reader, productor de la 2ª etapa) y Task 6 (`WmsCloudConnector`, consumidor de la 2ª etapa) — verificado cruzando las 4 tasks. `WmsSapStageStatus` (enum) con los mismos 3 valores (`Pendiente`/`ProcesadoWms`/`ErrorWms`) en Task 1 (definición), Task 2 (writer) y Task 5 (reader).

**4. Riesgo señalado explícitamente:** Task 3 Step 1 y Task 6 Step 1 avisan que los nombres de recurso/campo de Service Layer y el vocabulario de campo de Oracle WMS Cloud no están confirmados contra un ambiente real — mismo criterio que Ronda B Task 1 Step 1 (no asumir código ilustrativo reconstruido sin confirmar). La corrección de arquitectura del cursor (Global Constraints) está documentada con su razón, no dejada sin explicar.
