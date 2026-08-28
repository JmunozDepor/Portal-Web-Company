# Migración Wms — Ronda D: SAP → WMS (Picking) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Completar la 4ª entidad SAP→WMS del legado (Picking/Listas de Picking liberadas) sobre el mismo motor de 2 etapas ya construido en Ronda C.

**Architecture:** Etapa 1 (Bajada): `SapDocumentConnector.PullAsync` gana `TipoEntidad="Picking"` — consulta `PickLists` liberadas, agrupa líneas por documento base distinto, consulta cada documento base una sola vez, y un `IIntegrationEntityWriter` nuevo hace upsert en 2 tablas de staging nuevas (hdr+dtl). Etapa 2 (Subida): un `IIntegrationEntityReader` nuevo lee ese staging, y `WmsCloudConnector` gana una rama nueva (`"Order"` → `ListOfOrders`).

**Tech Stack:** .NET 8, EF Core (Postgres/SQL Server dual), B1SLayer vía `ISapConnectionProvider`/`ISapSession` (`GetAllAsync<T>`, ya paginado internamente), `System.Xml.Linq`.

## Global Constraints

- Multi-repo: Tasks 1, 2, 4, 5 viven en `Portal SaaS - Plugins/Modulo.Wms`; Task 3 vive en `Portal SaaS - Core`.
- **A diferencia de Ronda C, esta ronda NO necesita una tarea de cableado del `IntegrationSyncHostedService`** — la rama `Bajada`/`Subida` ya es genérica (resuelve reader/writer/connector por `EntidadNegocio`/`Tipo`, ver Ronda C Task 4, ya mergeada), así que una entidad nueva solo necesita su propio reader/writer/rama de connector, sin tocar el orquestador.
- `IntegrationDefinition.ConectorConfigCifrado` de la `IntegrationDefinition` de Bajada de esta entidad declara `{"TipoEntidad":"Picking"}` (mismo patrón que `Item`/`Store`/`InboundTraslado` de Ronda C) — fuera del alcance de este plan crear esa fila (se siembra manualmente, mismo criterio que Ronda C).
- Nombres de tabla/columna nuevos en `snake_case`, sin `HasColumnType` (dual-motor-safe), `HasPrecision` explícito en columnas `decimal` (mismo criterio ya exigido y aplicado en Ronda C Task 1 — `Quantity` de detalle usa `HasPrecision(18, 4)`, mismo valor).
- Comentarios y mensajes de excepción en español.
- **Mismo criterio de resync ya corregido en la revisión final de Ronda C**: la condición de resync de header debe incluir `Status == ErrorWms` (sin importar fecha) desde el primer intento — NO repetir el bug encontrado ahí (Item/Store no lo tenían al principio, tuvo que agregarse en un fix wave). El writer de esta ronda se escribe YA con ese criterio incluido.
- **No asumir nombres de recurso/campo de Service Layer sin confirmar** — cada task que los necesita trae un Step 1 explícito de verificación. En particular: el nombre/valor exacto del campo de estado "liberada" en el recurso `PickLists` de Service Layer NO está confirmado contra código real de este repo ni contra documentación verificada en esta sesión — es el mayor riesgo de esta ronda, ver Task 3 Step 1.
- Vocabulario de campo XML para `order`/`order_hdr`/`order_dtl` confirmado contra el DDL real del legado (`010_stg_sap_order_hdr.sql`/`011_stg_sap_order_dtl.sql`) y el mapeo real de `SP_DEP_WMS_PROCESS_PICKING` (`014_sp_dep_wms_integration_trigger.sql`), ambos en `C:\PROYECTOS\WMS_Suite\db\provisioning\hana\` — ver la tabla completa en el spec (`docs/superpowers/specs/2026-08-16-wms-ronda-d-picking-design.md`).

---

### Task 1: Entidades de staging + configuración EF + migraciones (`Modulo.Wms`)

**Files:**
- Create: `src/Modulo.Wms/Models/WmsSapStageOrder.cs` (contiene `WmsSapStageOrderHdr` y `WmsSapStageOrderDtl`)
- Modify: `src/Modulo.Wms/Data/WmsDbContext.cs` (agregar 2 `DbSet` + configuración Fluent API)
- Create: migraciones Postgres y SQL Server

**Interfaces:**
- Produces: `WmsSapStageOrderHdr { LineId (long PK), CompanyId (Guid), OrderNbr (string, clave natural), OrderType (string), PickListAbsEntry (int), BaseObjectType (int), BaseEntry (int), CardCode (string), CardName (string), CustomerPoNbr (string?), OrdDate (DateTime?), ExpDate (DateTime?), ReqShipDate (DateTime?), ShipToCode (string?), SourceUpdateDate (DateTime), Status (WmsSapStageStatus), RetryCount (int), ErrorMsg (string?), CreatedAt (DateTimeOffset), SyncedAt (DateTimeOffset?) }` + `WmsSapStageOrderDtl { LineId (long PK), ParentId (long FK cascade), ItemCode (string), Quantity (decimal), WhsCode (string?), LineNum (int), SeqNbr (int) }` — consumidos por Task 2 (writer) y Task 4 (reader).

- [ ] **Step 1: Crear las 2 clases de modelo**

`WmsSapStageStatus` ya existe (definido en `WmsSapStageItem.cs`, Ronda C) — no lo redefinas.

```csharp
// src/Modulo.Wms/Models/WmsSapStageOrder.cs
namespace Modulo.Wms.Models;

public class WmsSapStageOrderHdr
{
    public long LineId { get; set; }
    public Guid CompanyId { get; set; }
    public string OrderNbr { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public int PickListAbsEntry { get; set; }
    public int BaseObjectType { get; set; }
    public int BaseEntry { get; set; }
    public string CardCode { get; set; } = string.Empty;
    public string CardName { get; set; } = string.Empty;
    public string? CustomerPoNbr { get; set; }
    public DateTime? OrdDate { get; set; }
    public DateTime? ExpDate { get; set; }
    public DateTime? ReqShipDate { get; set; }
    public string? ShipToCode { get; set; }
    public DateTime SourceUpdateDate { get; set; }
    public WmsSapStageStatus Status { get; set; } = WmsSapStageStatus.Pendiente;
    public int RetryCount { get; set; }
    public string? ErrorMsg { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SyncedAt { get; set; }
}

public class WmsSapStageOrderDtl
{
    public long LineId { get; set; }
    public long ParentId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? WhsCode { get; set; }
    public int LineNum { get; set; }
    public int SeqNbr { get; set; }
}
```

- [ ] **Step 2: Registrar los 2 `DbSet` en `WmsDbContext`**

Junto a los `DbSet` de `WmsSapStageInboundHdr`/`Dtl` (línea ~36-37):

```csharp
public DbSet<WmsSapStageOrderHdr> WmsSapStageOrderHdrs => Set<WmsSapStageOrderHdr>();
public DbSet<WmsSapStageOrderDtl> WmsSapStageOrderDtls => Set<WmsSapStageOrderDtl>();
```

- [ ] **Step 3: Fluent API en `OnModelCreating`**

Mismo estilo exacto que `WmsSapStageInboundHdr`/`Dtl` (líneas ~356-386 de `WmsDbContext.cs` — leerlas primero para copiar el estilo, incluido `HasPrecision(18, 4)` en `Quantity`):

```csharp
modelBuilder.Entity<WmsSapStageOrderHdr>(entity =>
{
    entity.ToTable("wms_sap_stage_order_hdr");
    entity.HasKey(e => e.LineId);
    entity.Property(e => e.LineId).HasColumnName("line_id");
    entity.Property(e => e.CompanyId).HasColumnName("company_id");
    entity.Property(e => e.OrderNbr).HasColumnName("order_nbr").HasMaxLength(50);
    entity.Property(e => e.OrderType).HasColumnName("order_type").HasMaxLength(20);
    entity.Property(e => e.PickListAbsEntry).HasColumnName("pick_list_abs_entry");
    entity.Property(e => e.BaseObjectType).HasColumnName("base_object_type");
    entity.Property(e => e.BaseEntry).HasColumnName("base_entry");
    entity.Property(e => e.CardCode).HasColumnName("card_code").HasMaxLength(50);
    entity.Property(e => e.CardName).HasColumnName("card_name").HasMaxLength(200);
    entity.Property(e => e.CustomerPoNbr).HasColumnName("customer_po_nbr").HasMaxLength(50);
    entity.Property(e => e.OrdDate).HasColumnName("ord_date");
    entity.Property(e => e.ExpDate).HasColumnName("exp_date");
    entity.Property(e => e.ReqShipDate).HasColumnName("req_ship_date");
    entity.Property(e => e.ShipToCode).HasColumnName("ship_to_code").HasMaxLength(20);
    entity.Property(e => e.SourceUpdateDate).HasColumnName("source_update_date");
    entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
    entity.Property(e => e.RetryCount).HasColumnName("retry_count");
    entity.Property(e => e.ErrorMsg).HasColumnName("error_msg").HasMaxLength(500);
    entity.Property(e => e.CreatedAt).HasColumnName("created_at");
    entity.Property(e => e.SyncedAt).HasColumnName("synced_at");
    entity.HasIndex(e => new { e.CompanyId, e.OrderNbr }).IsUnique().HasDatabaseName("ix_wms_sap_stage_order_hdr_company_ordernbr");
    entity.HasIndex(e => e.Status).HasDatabaseName("ix_wms_sap_stage_order_hdr_status");
});

modelBuilder.Entity<WmsSapStageOrderDtl>(entity =>
{
    entity.ToTable("wms_sap_stage_order_dtl");
    entity.HasKey(e => e.LineId);
    entity.Property(e => e.LineId).HasColumnName("line_id");
    entity.Property(e => e.ParentId).HasColumnName("parent_id");
    entity.Property(e => e.ItemCode).HasColumnName("item_code").HasMaxLength(50);
    entity.Property(e => e.Quantity).HasColumnName("quantity").HasPrecision(18, 4);
    entity.Property(e => e.WhsCode).HasColumnName("whs_code").HasMaxLength(20);
    entity.Property(e => e.LineNum).HasColumnName("line_num");
    entity.Property(e => e.SeqNbr).HasColumnName("seq_nbr");
    entity.HasOne<WmsSapStageOrderHdr>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Cascade);
});
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Modulo.Wms/Modulo.Wms.csproj` (recrear la junction cross-repo si hace falta, mismo procedimiento de siempre).
Expected: compila sin errores.

- [ ] **Step 5: Generar migraciones Postgres y SQL Server**

Mismo comando/patrón exacto usado para `AddWmsSapStaging` de Ronda C (confirmar el comando real revisando esa migración antes de correr el nuevo).

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj` (test de humo, sin tests nuevos todavía).
Expected: todos los tests existentes en verde.

- [ ] **Step 6: Commit**

```bash
git add src/Modulo.Wms/Models/WmsSapStageOrder.cs src/Modulo.Wms/Data/WmsDbContext.cs
git add [rutas de migraciones generadas en Step 5]
git commit -m "feat: entidades de staging para Picking (Ronda D)"
```

---

### Task 2: `WmsSapStageOrderWriter` (`Modulo.Wms`)

**Files:**
- Create: `src/Modulo.Wms/Services/WmsSapStageOrderWriter.cs`
- Modify: `src/Modulo.Wms/ModuloWms.cs` (registrar en DI)
- Test: `tests/Modulo.Wms.Tests/Services/WmsSapStageOrderWriterTests.cs`

**Interfaces:**
- Consumes: `IIntegrationEntityWriter` (ya existe). `WmsSapStageOrderHdr`/`Dtl` (Task 1).
- Produces: `EntidadNegocio => "SapWms.Order"` — usado por Task 3 vía el orquestador genérico (sin tarea de cableado nueva, ver Global Constraints).

**Contrato de campos esperado en cada `IntegrationRecord` recibido** (producido por `SapDocumentConnector.PullAsync`, Task 3): `Fields["OrderNbr"]` (string), `["OrderType"]` (string), `["PickListAbsEntry"]` (int), `["BaseObjectType"]` (int), `["BaseEntry"]` (int), `["CardCode"]` (string), `["CardName"]` (string), `["CustomerPoNbr"]` (string?), `["OrdDate"]` (DateTime?), `["ExpDate"]` (DateTime?), `["ReqShipDate"]` (DateTime?), `["ShipToCode"]` (string?), `["SourceUpdateDate"]` (DateTime), `["Lineas"]` (`List<IntegrationRecord>`, cada una con `["ItemCode"]` (string), `["Quantity"]` (decimal), `["WhsCode"]` (string?), `["LineNum"]` (int), `["SeqNbr"]` (int)).

- [ ] **Step 1: Tests — nuevo, ya-pendiente-no-duplica, resync desde ProcesadoWms, resync desde ErrorWms**

```csharp
// tests/Modulo.Wms.Tests/Services/WmsSapStageOrderWriterTests.cs
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSapStageOrderWriterTests
{
    private static WmsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(opciones);
    }

    private static IntegrationRecord CrearRegistroOrden(string orderNbr, DateTime sourceUpdateDate, string itemCode = "ITM001") =>
        new(new Dictionary<string, object?>
        {
            ["OrderNbr"] = orderNbr,
            ["OrderType"] = "VTA",
            ["PickListAbsEntry"] = 100,
            ["BaseObjectType"] = 17,
            ["BaseEntry"] = 500,
            ["CardCode"] = "C001",
            ["CardName"] = "Cliente de prueba",
            ["CustomerPoNbr"] = "PO-1",
            ["OrdDate"] = sourceUpdateDate,
            ["ExpDate"] = (DateTime?)null,
            ["ReqShipDate"] = (DateTime?)null,
            ["ShipToCode"] = "SHIP1",
            ["SourceUpdateDate"] = sourceUpdateDate,
            ["Lineas"] = new List<IntegrationRecord>
            {
                new(new Dictionary<string, object?>
                {
                    ["ItemCode"] = itemCode,
                    ["Quantity"] = 5m,
                    ["WhsCode"] = "01",
                    ["LineNum"] = 0,
                    ["SeqNbr"] = 1,
                }),
            },
        });

    [Fact]
    public async Task EscribirAsync_OrdenNueva_InsertaCabeceraYDetalle()
    {
        var contexto = CrearContexto();
        var writer = new WmsSapStageOrderWriter(contexto);
        var companyId = Guid.NewGuid();

        await writer.EscribirAsync(companyId, [CrearRegistroOrden("VTA-1001", new DateTime(2026, 8, 16))], CancellationToken.None);

        var hdr = Assert.Single(contexto.WmsSapStageOrderHdrs);
        Assert.Equal("VTA-1001", hdr.OrderNbr);
        Assert.Equal(WmsSapStageStatus.Pendiente, hdr.Status);

        var dtl = Assert.Single(contexto.WmsSapStageOrderDtls);
        Assert.Equal(hdr.LineId, dtl.ParentId);
        Assert.Equal("ITM001", dtl.ItemCode);
    }

    [Fact]
    public async Task EscribirAsync_OrdenYaPendienteSinCambios_NoDuplicaDetalle()
    {
        var contexto = CrearContexto();
        var writer = new WmsSapStageOrderWriter(contexto);
        var companyId = Guid.NewGuid();
        var fecha = new DateTime(2026, 8, 16);

        await writer.EscribirAsync(companyId, [CrearRegistroOrden("VTA-1001", fecha)], CancellationToken.None);
        await writer.EscribirAsync(companyId, [CrearRegistroOrden("VTA-1001", fecha)], CancellationToken.None);

        Assert.Single(contexto.WmsSapStageOrderHdrs);
        Assert.Single(contexto.WmsSapStageOrderDtls);
    }

    [Fact]
    public async Task EscribirAsync_OrdenProcesadaConCambioEnSap_VuelveAPendienteYReemplazaDetalle()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdrExistente = new WmsSapStageOrderHdr
        {
            CompanyId = companyId,
            OrderNbr = "VTA-1001",
            OrderType = "VTA",
            CardCode = "C001",
            CardName = "Cliente de prueba",
            SourceUpdateDate = new DateTime(2026, 8, 10),
            Status = WmsSapStageStatus.ProcesadoWms,
        };
        contexto.WmsSapStageOrderHdrs.Add(hdrExistente);
        await contexto.SaveChangesAsync();
        contexto.WmsSapStageOrderDtls.Add(new WmsSapStageOrderDtl { ParentId = hdrExistente.LineId, ItemCode = "ITM_VIEJO", Quantity = 1m, LineNum = 0, SeqNbr = 1 });
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageOrderWriter(contexto);
        await writer.EscribirAsync(companyId, [CrearRegistroOrden("VTA-1001", new DateTime(2026, 8, 16))], CancellationToken.None);

        var hdr = Assert.Single(contexto.WmsSapStageOrderHdrs);
        Assert.Equal(WmsSapStageStatus.Pendiente, hdr.Status);

        var dtl = Assert.Single(contexto.WmsSapStageOrderDtls);
        Assert.Equal("ITM001", dtl.ItemCode);
    }

    [Fact]
    public async Task EscribirAsync_OrdenEnErrorWms_VuelveAPendienteYLimpiaError()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdrExistente = new WmsSapStageOrderHdr
        {
            CompanyId = companyId,
            OrderNbr = "VTA-1001",
            OrderType = "VTA",
            CardCode = "C001",
            CardName = "Cliente de prueba",
            SourceUpdateDate = new DateTime(2026, 8, 16),
            Status = WmsSapStageStatus.ErrorWms,
            ErrorMsg = "Rechazado por WMS",
        };
        contexto.WmsSapStageOrderHdrs.Add(hdrExistente);
        await contexto.SaveChangesAsync();

        var writer = new WmsSapStageOrderWriter(contexto);
        await writer.EscribirAsync(companyId, [CrearRegistroOrden("VTA-1001", new DateTime(2026, 8, 16))], CancellationToken.None);

        var hdr = Assert.Single(contexto.WmsSapStageOrderHdrs);
        Assert.Equal(WmsSapStageStatus.Pendiente, hdr.Status);
        Assert.Null(hdr.ErrorMsg);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsSapStageOrderWriterTests`
Expected: FAIL — clase no existe.

- [ ] **Step 3: Implementar `WmsSapStageOrderWriter`**

Copia EXACTA del patrón ya corregido de `WmsSapStageInboundWriter.cs` (leerlo primero, es la referencia — incluye el `insertarDetalle`/`esResync`/`debeResincronizar` con `ErrorWms` ya cubierto desde el día 1, no como un fix posterior):

```csharp
// src/Modulo.Wms/Services/WmsSapStageOrderWriter.cs
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Escritor del motor genérico (IIntegrationEntityWriter) para Picking (Listas de
/// Picking liberadas en SAP) que llegan desde SAP. Mismo criterio de upsert por clave
/// natural (CompanyId, OrderNbr) y mismo tratamiento de detalle que
/// WmsSapStageInboundWriter (Ronda C) -- incluido el resync desde ErrorWms desde el
/// primer intento (bug real encontrado y corregido recién en la revisión final de
/// Ronda C para las otras 3 entidades, no se repite acá).
/// </summary>
public class WmsSapStageOrderWriter : IIntegrationEntityWriter
{
    private readonly WmsDbContext _contexto;

    public WmsSapStageOrderWriter(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Order";

    public async Task EscribirAsync(Guid companyId, IReadOnlyList<IntegrationRecord> registros, CancellationToken cancellationToken)
    {
        foreach (var registro in registros)
        {
            var orderNbr = (string)registro["OrderNbr"]!;
            var sourceUpdateDate = (DateTime)registro["SourceUpdateDate"]!;
            var lineas = (List<IntegrationRecord>)registro["Lineas"]!;

            var hdr = await _contexto.WmsSapStageOrderHdrs
                .FirstOrDefaultAsync(f => f.CompanyId == companyId && f.OrderNbr == orderNbr, cancellationToken);

            bool insertarDetalle;

            if (hdr is null)
            {
                hdr = new WmsSapStageOrderHdr
                {
                    CompanyId = companyId,
                    OrderNbr = orderNbr,
                    OrderType = (string)registro["OrderType"]!,
                    PickListAbsEntry = (int)registro["PickListAbsEntry"]!,
                    BaseObjectType = (int)registro["BaseObjectType"]!,
                    BaseEntry = (int)registro["BaseEntry"]!,
                    CardCode = (string)registro["CardCode"]!,
                    CardName = (string)registro["CardName"]!,
                    CustomerPoNbr = (string?)registro["CustomerPoNbr"],
                    OrdDate = (DateTime?)registro["OrdDate"],
                    ExpDate = (DateTime?)registro["ExpDate"],
                    ReqShipDate = (DateTime?)registro["ReqShipDate"],
                    ShipToCode = (string?)registro["ShipToCode"],
                    SourceUpdateDate = sourceUpdateDate,
                    Status = WmsSapStageStatus.Pendiente,
                };
                _contexto.WmsSapStageOrderHdrs.Add(hdr);
                await _contexto.SaveChangesAsync(cancellationToken);
                insertarDetalle = true;
            }
            else
            {
                var debeResincronizar = (hdr.Status == WmsSapStageStatus.ProcesadoWms && sourceUpdateDate > hdr.SourceUpdateDate)
                    || hdr.Status == WmsSapStageStatus.ErrorWms;
                var esResync = debeResincronizar || hdr.Status == WmsSapStageStatus.Pendiente;

                if (debeResincronizar)
                {
                    hdr.Status = WmsSapStageStatus.Pendiente;
                    hdr.ErrorMsg = null;
                }

                hdr.OrderType = (string)registro["OrderType"]!;
                hdr.PickListAbsEntry = (int)registro["PickListAbsEntry"]!;
                hdr.BaseObjectType = (int)registro["BaseObjectType"]!;
                hdr.BaseEntry = (int)registro["BaseEntry"]!;
                hdr.CardCode = (string)registro["CardCode"]!;
                hdr.CardName = (string)registro["CardName"]!;
                hdr.CustomerPoNbr = (string?)registro["CustomerPoNbr"];
                hdr.OrdDate = (DateTime?)registro["OrdDate"];
                hdr.ExpDate = (DateTime?)registro["ExpDate"];
                hdr.ReqShipDate = (DateTime?)registro["ReqShipDate"];
                hdr.ShipToCode = (string?)registro["ShipToCode"];
                hdr.SourceUpdateDate = sourceUpdateDate;

                if (esResync)
                {
                    var detalleExistente = await _contexto.WmsSapStageOrderDtls
                        .Where(d => d.ParentId == hdr.LineId)
                        .ToListAsync(cancellationToken);
                    _contexto.WmsSapStageOrderDtls.RemoveRange(detalleExistente);
                    await _contexto.SaveChangesAsync(cancellationToken);
                }

                insertarDetalle = esResync;
            }

            if (insertarDetalle)
            {
                foreach (var linea in lineas)
                {
                    _contexto.WmsSapStageOrderDtls.Add(new WmsSapStageOrderDtl
                    {
                        ParentId = hdr.LineId,
                        ItemCode = (string)linea["ItemCode"]!,
                        Quantity = (decimal)linea["Quantity"]!,
                        WhsCode = (string?)linea["WhsCode"],
                        LineNum = (int)linea["LineNum"]!,
                        SeqNbr = (int)linea["SeqNbr"]!,
                    });
                }
            }

            await _contexto.SaveChangesAsync(cancellationToken);
        }
    }
}
```

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsSapStageOrderWriterTests`
Expected: PASS (4/4).

- [ ] **Step 5: Registrar en DI**

En `src/Modulo.Wms/ModuloWms.cs`, junto a `services.AddScoped<IIntegrationEntityWriter, WmsSapStageInboundWriter>();`:

```csharp
services.AddScoped<IIntegrationEntityWriter, WmsSapStageOrderWriter>();
```

- [ ] **Step 6: Build + test completo del proyecto**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj`
Expected: todos los tests en verde.

- [ ] **Step 7: Commit**

```bash
git add src/Modulo.Wms/Services/WmsSapStageOrderWriter.cs src/Modulo.Wms/ModuloWms.cs tests/Modulo.Wms.Tests/Services/WmsSapStageOrderWriterTests.cs
git commit -m "feat: writer del motor de integración para staging de Picking (Ronda D)"
```

---

### Task 3: `SapDocumentConnector.PullAsync` — rama `Picking` (`Portal SaaS - Core`)

**Files:**
- Modify: `src/PortalSaas.Core/Integraciones/SapDocumentConnector.cs`
- Test: `tests/PortalSaas.Core.Tests/Integraciones/SapDocumentConnectorTests.cs` (archivo ya existe)

**Interfaces:**
- Consumes: `ISapSession.GetAllAsync<T>(recurso, filtroOData, expandOData, ct)` (ya existe, ya usado por `LeerItemsAsync`/`LeerStoresAsync`/`LeerTrasladosAsync` en el mismo archivo — copiar el patrón exacto).
- Produces: `PullAsync` con `TipoEntidad="Picking"` retorna `IReadOnlyList<IntegrationRecord>` con la forma exacta documentada en Task 2 (contrato de campos).

**Verificación previa obligatoria (Step 1) — no asumir nombre/valor del campo de estado en `PickLists`:**

- [ ] **Step 1: Confirmar el campo/valor real de "Liberada" en el recurso `PickLists` de Service Layer**

El SP legado filtra `OPKL.Status = 'R'` (columna nativa de la tabla HANA `OPKL`) — pero el recurso `PickLists` de Service Layer puede exponer el campo con otro nombre/tipo (Service Layer suele usar nombres tipo `PascalCase` y valores de enum con prefijo, ej. `"psReleased"`/similar, no la letra cruda `'R'` de la tabla nativa). Antes de escribir código:
1. Buscar en el repo si ya existe algún GET contra `PickLists` (`grep -rn "PickLists" src/PortalSaas.Core/`). Si existe, usar exactamente ese nombre/valor.
2. Si no existe (probable, es la primera vez que este repo toca este recurso), usar como mejor esfuerzo el nombre de campo `Status` con el valor de enum `"psReleased"` (convención de Service Layer para el estado "Liberado" de recursos con máquina de estados de picking/entrega, análoga a `bo_ShipTo` ya usado en `BusinessPartners.BPAddresses.AddressType` de Ronda C) — pero **documentarlo explícitamente como riesgo NO confirmado** en el comentario del código y en el mensaje de commit de este task, igual criterio que Ronda C dejó documentado para `Items`/`BusinessPartners`.
3. `PickListsLines` (vía `$expand=PickListsLines`) trae, entre otros, `BaseObjectType` (int), `OrderEntry` (int, `DocEntry` del documento base), `OrderLine` (int, número de línea del documento base), `ReleasedQuantity` (decimal) — estos 4 sí son nombres estándar de Service Layer para `PickListsLines`, usarlos tal cual.

- [ ] **Step 2: Test — `PullAsync` con `TipoEntidad="Picking"` agrupa por documento base y arma el `IntegrationRecord` correcto**

Revisar primero cómo están armados los fakes de `ISapSession` en `SapDocumentConnectorTests.cs` (ya usados para `Item`/`Store`/`InboundTraslado`) antes de escribir uno nuevo — reusar el mismo patrón. El fake para este test necesita devolver: (a) una lista de `SapWmsPickListRow` (con sus `PickListsLines`) al primer `GetAllAsync` (recurso `PickLists`), y (b) una lista de `SapWmsOrderBaseRow` al segundo `GetAllAsync` (recurso `Orders`, ya que el `BaseObjectType` de prueba es `17`) — el fake debe distinguir por el nombre de `recurso` recibido en cada llamada.

```csharp
[Fact]
public async Task PullAsync_TipoEntidadPicking_AgrupaPorDocumentoBaseYArmaRegistro()
{
    var pickList = new SapWmsPickListRow
    {
        AbsEntry = 100,
        PickDate = new DateTime(2026, 8, 16),
        UpdateDate = new DateTime(2026, 8, 16),
        Owner = new SapWmsPickListOwnerRow { CardCode = "C001", CardName = "Cliente de prueba" },
        U_NX_order_type = "VTA",
        PickListsLines = new List<SapWmsPickListLineRow>
        {
            new() { BaseObjectType = 17, OrderEntry = 500, OrderLine = 0, ReleasedQuantity = 5m },
        },
    };
    var ordenBase = new SapWmsOrderBaseRow
    {
        DocEntry = 500,
        CardCode = "C001",
        CardName = "Cliente de prueba",
        NumAtCard = "PO-1",
        CancelDate = null,
        DocDueDate = null,
        ShipToCode = "SHIP1",
        DocumentLines = new List<SapWmsOrderBaseLineRow> { new() { LineNum = 0, ItemCode = "ITM001", WarehouseCode = "01" } },
    };

    var sesionFalsa = new SapSessionFalsaParaPicking(
        pickLists: new List<SapWmsPickListRow> { pickList },
        ordenes: new List<SapWmsOrderBaseRow> { ordenBase });
    var proveedorFalso = new SapConnectionProviderFalso(sesionFalsa);
    var conector = new SapDocumentConnector(_salesFalso, _purchaseFalso, _inventoryFalso, proveedorFalso);

    var config = """{"TipoEntidad":"Picking"}""";
    var resultado = await conector.PullAsync(config, CancellationToken.None);

    var registro = Assert.Single(resultado);
    Assert.Equal("C001", registro["CardCode"]);
    Assert.Equal(500, registro["BaseEntry"]);
    var lineas = (List<IntegrationRecord>)registro["Lineas"]!;
    var linea = Assert.Single(lineas);
    Assert.Equal("ITM001", linea["ItemCode"]);
    Assert.Equal(5m, linea["Quantity"]);
}
```

(Nombres exactos de las clases fake `SapSessionFalsaParaPicking` — confirmar contra el patrón real ya usado por `SapSessionFalsaParaItems`/similares del mismo archivo, ver Ronda C Task 3.)

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter PullAsync_TipoEntidadPicking_AgrupaPorDocumentoBaseYArmaRegistro`
Expected: FAIL — `TipoEntidad "Picking"` no soportado todavía.

- [ ] **Step 3: Agregar la rama `"Picking"` al `switch` de `PullAsync`**

```csharp
return config.TipoEntidad switch
{
    "Item" => await LeerItemsAsync(session, cancellationToken),
    "Store" => await LeerStoresAsync(session, cancellationToken),
    "InboundTraslado" => await LeerTrasladosAsync(session, cancellationToken),
    "Picking" => await LeerPickingAsync(session, cancellationToken),
    _ => throw new InvalidOperationException($"TipoEntidad '{config.TipoEntidad}' no soportado en PullAsync."),
};
```

- [ ] **Step 4: Implementar `LeerPickingAsync` con agrupación por documento base**

```csharp
private static async Task<IReadOnlyList<IntegrationRecord>> LeerPickingAsync(ISapSession session, CancellationToken ct)
{
    // Ver Step 1: "Status eq 'psReleased'" es mejor esfuerzo, NO confirmado contra
    // Service Layer real -- verificar contra un ambiente SAP real antes de producción.
    var filtro = "Status eq 'psReleased'";
    var pickLists = await session.GetAllAsync<SapWmsPickListRow>("PickLists", filtro, "PickListsLines", ct);

    // PickListsLines no trae ItemCode/almacén -- solo BaseObjectType/OrderEntry/OrderLine.
    // Se agrupa por documento base distinto (BaseObjectType, OrderEntry) para consultar
    // cada documento base UNA sola vez, no una vez por línea (Global Constraints del plan).
    var lineasPorDocumentoBase = pickLists
        .SelectMany(pl => pl.PickListsLines ?? [], (pl, linea) => (PickList: pl, Linea: linea))
        .GroupBy(x => (x.Linea.BaseObjectType, x.Linea.OrderEntry));

    var documentosBaseCache = new Dictionary<(int Tipo, int DocEntry), SapWmsOrderBaseRow?>();
    var registros = new List<IntegrationRecord>();

    foreach (var grupo in lineasPorDocumentoBase)
    {
        var (baseObjectType, orderEntry) = grupo.Key;
        var clave = (baseObjectType, orderEntry);

        if (!documentosBaseCache.TryGetValue(clave, out var documentoBase))
        {
            var recurso = baseObjectType switch
            {
                17 => "Orders",
                13 => "Invoices",
                1250000001 => "InventoryTransferRequests",
                _ => null,
            };

            documentoBase = recurso is null
                ? null
                : await session.GetAsync<SapWmsOrderBaseRow>($"{recurso}({orderEntry})", ct: ct);
            documentosBaseCache[clave] = documentoBase;
        }

        if (documentoBase is null)
        {
            continue;
        }

        var pickList = grupo.First().PickList;
        var lineasDelGrupo = grupo.ToList();

        var lineas = lineasDelGrupo
            .Select(x =>
            {
                var lineaBase = documentoBase.DocumentLines?.FirstOrDefault(l => l.LineNum == x.Linea.OrderLine);
                return new IntegrationRecord(new Dictionary<string, object?>
                {
                    ["ItemCode"] = lineaBase?.ItemCode,
                    ["Quantity"] = x.Linea.ReleasedQuantity,
                    ["WhsCode"] = lineaBase?.WarehouseCode,
                    ["LineNum"] = x.Linea.OrderLine,
                    ["SeqNbr"] = x.Linea.OrderLine,
                });
            })
            .Where(l => l["ItemCode"] is not null)
            .ToList();

        if (lineas.Count == 0)
        {
            continue;
        }

        var sufijoOrderNbr = baseObjectType == 17 ? $"-{pickList.AbsEntry}" : string.Empty;
        var orderNbr = $"{pickList.U_NX_order_type}{documentoBase.DocEntry}{sufijoOrderNbr}";

        registros.Add(new IntegrationRecord(new Dictionary<string, object?>
        {
            ["OrderNbr"] = orderNbr,
            ["OrderType"] = pickList.U_NX_order_type,
            ["PickListAbsEntry"] = pickList.AbsEntry,
            ["BaseObjectType"] = baseObjectType,
            ["BaseEntry"] = orderEntry,
            ["CardCode"] = documentoBase.CardCode,
            ["CardName"] = documentoBase.CardName,
            ["CustomerPoNbr"] = documentoBase.NumAtCard,
            ["OrdDate"] = pickList.PickDate,
            ["ExpDate"] = documentoBase.CancelDate,
            ["ReqShipDate"] = documentoBase.DocDueDate,
            ["ShipToCode"] = documentoBase.ShipToCode,
            ["SourceUpdateDate"] = pickList.UpdateDate,
            ["Lineas"] = lineas,
        }));
    }

    return registros;
}
```

Agrega las clases de modelo nuevas (mismo estilo POCO plano, sin decorar, que `SapWmsItemRow`/`SapWmsTrasladoRow`):

```csharp
internal sealed class SapWmsPickListRow
{
    public int AbsEntry { get; set; }
    public DateTime PickDate { get; set; }
    public DateTime UpdateDate { get; set; }
    public string U_NX_order_type { get; set; } = string.Empty;
    public List<SapWmsPickListLineRow>? PickListsLines { get; set; }
}

internal sealed class SapWmsPickListLineRow
{
    public int BaseObjectType { get; set; }
    public int OrderEntry { get; set; }
    public int OrderLine { get; set; }
    public decimal ReleasedQuantity { get; set; }
}

internal sealed class SapWmsOrderBaseRow
{
    public int DocEntry { get; set; }
    public string CardCode { get; set; } = string.Empty;
    public string CardName { get; set; } = string.Empty;
    public string? NumAtCard { get; set; }
    public DateTime? CancelDate { get; set; }
    public DateTime? DocDueDate { get; set; }
    public string? ShipToCode { get; set; }
    public List<SapWmsOrderBaseLineRow>? DocumentLines { get; set; }
}

internal sealed class SapWmsOrderBaseLineRow
{
    public int LineNum { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string? WarehouseCode { get; set; }
}
```

**Nota sobre `SapWmsPickListRow.Owner`/`U_NX_order_type`**: en el test de referencia del Step 2 aparece un `Owner` con `CardCode`/`CardName` — pero el diseño real (arriba) toma `CardCode`/`CardName` del **documento base** (`SapWmsOrderBaseRow`), no de `PickLists` directamente (el legado los saca de `OCRD`/`T4` vía join con el documento base, no de la Lista de Picking). Ajusta el test del Step 2 para que use `documentoBase.CardCode`/`CardName` como fuente de verdad al armar las aserciones, y quita el campo `Owner` de `SapWmsPickListRow` si no se termina usando en la implementación real — prioriza que el código de este Step 4 sea internamente consistente sobre igualar el test ilustrativo al pie de la letra.

- [ ] **Step 5: Correr y verificar que pasa**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter PullAsync_TipoEntidadPicking_AgrupaPorDocumentoBaseYArmaRegistro`
Expected: PASS.

- [ ] **Step 6: Test adicional — 2 líneas del mismo documento base disparan una sola consulta**

```csharp
[Fact]
public async Task PullAsync_TipoEntidadPicking_DosLineasDelMismoDocumentoBase_ConsultaUnaSolaVez()
{
    // Arrange: 1 PickList con 2 PickListsLines, ambas con BaseObjectType=17 y
    // OrderEntry=500 (mismo documento base, líneas 0 y 1).
    // Act: PullAsync.
    // Assert: sesionFalsa.LlamadasAOrders == 1 (contador expuesto por el fake), y el
    // IntegrationRecord resultante trae 2 líneas en Fields["Lineas"].
}
```

Ajusta `SapSessionFalsaParaPicking` para exponer un contador de llamadas por recurso (`Dictionary<string, int>` o similar) y verificar que el agrupamiento realmente evita consultas repetidas — este es el comportamiento central que motivó la decisión de diseño de esta ronda, debe quedar probado explícitamente, no solo implementado.

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter SapDocumentConnectorTests`
Expected: todos los tests de este archivo en verde.

- [ ] **Step 7: Build completo + suite completa**

Run: `dotnet build PortalSaas.sln` — Expected: 0/0.
Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj` — Expected: todo en verde.

- [ ] **Step 8: Commit**

```bash
git add src/PortalSaas.Core/Integraciones/SapDocumentConnector.cs tests/PortalSaas.Core.Tests/Integraciones/SapDocumentConnectorTests.cs
git commit -m "feat: PullAsync rama Picking en SapDocumentConnector (Ronda D) -- Status='psReleased' NO confirmado contra Service Layer real"
```

---

### Task 4: `WmsSapStageOrderReader` (`Modulo.Wms`)

**Files:**
- Create: `src/Modulo.Wms/Services/WmsSapStageOrderReader.cs`
- Modify: `src/Modulo.Wms/ModuloWms.cs` (registrar en DI)
- Test: `tests/Modulo.Wms.Tests/Services/WmsSapStageOrderReaderTests.cs`

**Interfaces:**
- Consumes: `IIntegrationEntityReader` (ya existe). `WmsSapStageOrderHdr`/`Dtl` (Task 1).
- Produces: `EntidadNegocio => "SapWms.Order.Subida"`. `Fields["TipoDocumento"] = "Order"` — consumido por Task 5.

- [ ] **Step 1: Test**

Mismo patrón EXACTO que `WmsSapStageInboundReaderTests.cs` (Ronda C) — 3 casos: filtra por `Pendiente`+`CompanyId` con líneas anidadas, `MarcarProcesadoAsync` éxito, `MarcarProcesadoAsync` error.

```csharp
// tests/Modulo.Wms.Tests/Services/WmsSapStageOrderReaderTests.cs
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos.Integraciones;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSapStageOrderReaderTests
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
        var hdr = new WmsSapStageOrderHdr { CompanyId = companyId, OrderNbr = "VTA-1001", OrderType = "VTA", CardCode = "C001", CardName = "Cliente de prueba", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageOrderHdrs.Add(hdr);
        await contexto.SaveChangesAsync();
        contexto.WmsSapStageOrderDtls.Add(new WmsSapStageOrderDtl { ParentId = hdr.LineId, ItemCode = "ITM001", Quantity = 5m, LineNum = 0, SeqNbr = 1 });
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageOrderReader(contexto);
        var resultado = await reader.LeerPendientesAsync(companyId, CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal("Order", registro["TipoDocumento"]);
        Assert.Equal("VTA-1001", registro["OrderNbr"]);
        var lineas = (List<IntegrationRecord>)registro["Lineas"]!;
        var linea = Assert.Single(lineas);
        Assert.Equal("ITM001", linea["ItemCode"]);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_Exito_ActualizaStatusYSyncedAt()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdr = new WmsSapStageOrderHdr { CompanyId = companyId, OrderNbr = "VTA-1001", OrderType = "VTA", CardCode = "C001", CardName = "Cliente de prueba", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageOrderHdrs.Add(hdr);
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageOrderReader(contexto);
        var registro = (await reader.LeerPendientesAsync(companyId, CancellationToken.None)).Single();

        await reader.MarcarProcesadoAsync(companyId, registro, exito: true, mensajeError: null, CancellationToken.None);

        var actualizada = await contexto.WmsSapStageOrderHdrs.SingleAsync();
        Assert.Equal(WmsSapStageStatus.ProcesadoWms, actualizada.Status);
        Assert.NotNull(actualizada.SyncedAt);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_Error_ActualizaStatusYErrorMsg()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var hdr = new WmsSapStageOrderHdr { CompanyId = companyId, OrderNbr = "VTA-1001", OrderType = "VTA", CardCode = "C001", CardName = "Cliente de prueba", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageOrderHdrs.Add(hdr);
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageOrderReader(contexto);
        var registro = (await reader.LeerPendientesAsync(companyId, CancellationToken.None)).Single();

        await reader.MarcarProcesadoAsync(companyId, registro, exito: false, mensajeError: "Rechazado por WMS", CancellationToken.None);

        var actualizada = await contexto.WmsSapStageOrderHdrs.SingleAsync();
        Assert.Equal(WmsSapStageStatus.ErrorWms, actualizada.Status);
        Assert.Equal("Rechazado por WMS", actualizada.ErrorMsg);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsSapStageOrderReaderTests`
Expected: FAIL.

- [ ] **Step 3: Implementar `WmsSapStageOrderReader`**

Copia exacta del patrón de `WmsSapStageInboundReader.cs` (Ronda C, ya en este repo):

```csharp
// src/Modulo.Wms/Services/WmsSapStageOrderReader.cs
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Lector del motor genérico (IIntegrationEntityReader) para el staging de Picking
/// pendiente de enviar a Oracle WMS Cloud (etapa Subida de la Ronda D). Mismo patrón
/// exacto que WmsSapStageInboundReader (Ronda C) -- cada WmsSapStageOrderHdr ya es una
/// unidad, el detalle se anida bajo "Lineas" sin agrupación adicional.
/// </summary>
public class WmsSapStageOrderReader : IIntegrationEntityReader
{
    private readonly WmsDbContext _contexto;

    public WmsSapStageOrderReader(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Order.Subida";

    public async Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var hdrs = await _contexto.WmsSapStageOrderHdrs
            .Where(h => h.CompanyId == companyId && h.Status == WmsSapStageStatus.Pendiente)
            .ToListAsync(cancellationToken);

        var registros = new List<IntegrationRecord>();
        foreach (var hdr in hdrs)
        {
            var detalle = await _contexto.WmsSapStageOrderDtls
                .Where(d => d.ParentId == hdr.LineId)
                .OrderBy(d => d.LineNum)
                .ToListAsync(cancellationToken);

            registros.Add(new IntegrationRecord(new Dictionary<string, object?>
            {
                ["TipoDocumento"] = "Order",
                ["OrderNbr"] = hdr.OrderNbr,
                ["OrderType"] = hdr.OrderType,
                ["OrdDate"] = hdr.OrdDate,
                ["ExpDate"] = hdr.ExpDate,
                ["ReqShipDate"] = hdr.ReqShipDate,
                ["CustomerPoNbr"] = hdr.CustomerPoNbr,
                ["ShipToCode"] = hdr.ShipToCode,
                ["Lineas"] = detalle
                    .Select(d => new IntegrationRecord(new Dictionary<string, object?>
                    {
                        ["ItemCode"] = d.ItemCode,
                        ["Quantity"] = d.Quantity,
                        ["LineNum"] = d.LineNum,
                        ["SeqNbr"] = d.SeqNbr,
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

        var filas = await _contexto.WmsSapStageOrderHdrs
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

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsSapStageOrderReaderTests`
Expected: PASS (3/3).

- [ ] **Step 5: Registrar en DI**

```csharp
services.AddScoped<IIntegrationEntityReader, WmsSapStageOrderReader>();
```

- [ ] **Step 6: Build + test completo**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj`
Expected: todos los tests en verde.

- [ ] **Step 7: Commit**

```bash
git add src/Modulo.Wms/Services/WmsSapStageOrderReader.cs src/Modulo.Wms/ModuloWms.cs tests/Modulo.Wms.Tests/Services/WmsSapStageOrderReaderTests.cs
git commit -m "feat: reader del motor de integración para etapa Subida de Picking (Ronda D)"
```

---

### Task 5: `WmsCloudConnector` — rama `Order` (`Modulo.Wms`)

**Files:**
- Modify: `src/Modulo.Wms/Services/WmsCloudConnector.cs`
- Test: `tests/Modulo.Wms.Tests/Services/WmsCloudConnectorTests.cs` (archivo ya existe)

**Interfaces:**
- Consumes: `Fields["TipoDocumento"] = "Order"` (Task 4).
- Produces: hoja de esta cadena, mismo `IIntegrationConnector` ya cableado genéricamente por el orquestador.

- [ ] **Step 1: Test — `PushAsync` con `TipoDocumento="Order"` arma `ListOfOrders` con cabecera+detalle anidados**

```csharp
[Fact]
public async Task PushAsync_ConTipoDocumentoOrder_PosteaXmlConCabeceraYDetalleAnidados()
{
    var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK);
    var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
    var conector = new WmsCloudConnector(httpClient, NullLogger<WmsCloudConnector>.Instance);

    var registro = new IntegrationRecord(new Dictionary<string, object?>
    {
        ["TipoDocumento"] = "Order",
        ["OrderNbr"] = "VTA-1001",
        ["OrderType"] = "VTA",
        ["OrdDate"] = new DateTime(2026, 8, 16),
        ["ExpDate"] = (DateTime?)null,
        ["ReqShipDate"] = (DateTime?)null,
        ["CustomerPoNbr"] = "PO-1",
        ["ShipToCode"] = "SHIP1",
        ["Lineas"] = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["ItemCode"] = "ITM001", ["Quantity"] = 5m, ["LineNum"] = 0, ["SeqNbr"] = 1 }),
        },
    });

    var config = """{"ApiUrl":"https://wms.example.com/init_stage_interface","Usuario":"wmsuser","Clave":"wmspass","ClientEnvCode":"CLI01","ParentCompanyCode":"COMP01"}""";
    var resultado = await conector.PushAsync(config, [registro], CancellationToken.None);

    Assert.True(resultado[0].Exito);
    Assert.Contains("ListOfOrders", handlerFalso.UltimoContenido);
    Assert.Contains("VTA-1001", handlerFalso.UltimoContenido);
    Assert.Contains("order_dtl", handlerFalso.UltimoContenido);
}
```

(Reusar la clase fake `HttpHandlerFalso` ya existente en `WmsCloudConnectorTests.cs`, mismo patrón que los tests de `Item`/`IbShipment`.)

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsCloudConnectorTests`
Expected: FAIL — `TipoDocumento="Order"` no soportado todavía.

- [ ] **Step 3: Agregar la rama `"Order"` a `WmsCloudConnector`**

En `ArmarXml`, extender el `switch` de `(entity, nombreLista, nombreItem)`:

```csharp
var (entity, nombreLista, nombreItem) = tipoDocumento switch
{
    "Item" => ("item", "ListOfItems", "item"),
    "Store" => ("store", "ListOfStores", "store"),
    "IbShipment" => ("ib_shipment", "ListOfIbShipments", "ib_shipment"),
    "Order" => ("order", "ListOfOrders", "order"),
    _ => throw new InvalidOperationException($"TipoDocumento '{tipoDocumento}' no soportado en WmsCloudConnector."),
};
```

Y el `switch` de `nodoItem`:

```csharp
"Order" => ArmarNodoOrder(registro),
```

Nuevo método (mismo estilo que `ArmarNodoIbShipment`, vocabulario de nodo confirmado contra el DDL real del legado — ver el spec de esta ronda para la tabla completa):

```csharp
private static XElement ArmarNodoOrder(IntegrationRecord registro)
{
    var lineas = (List<IntegrationRecord>)registro["Lineas"]!;
    var hdr = new XElement("order_hdr",
        new XElement("order_nbr", registro["OrderNbr"]),
        new XElement("order_type", registro["OrderType"]),
        new XElement("ord_date", registro["OrdDate"]),
        new XElement("exp_date", registro["ExpDate"]),
        new XElement("req_ship_date", registro["ReqShipDate"]),
        new XElement("ref_nbr", registro["CustomerPoNbr"]),
        new XElement("dest_dept_nbr", registro["ShipToCode"]),
        new XElement("priority", "1"));

    var detalles = lineas.Select(l => new XElement("order_dtl",
        new XElement("order_nbr", registro["OrderNbr"]),
        new XElement("seq_nbr", l["SeqNbr"]),
        new XElement("item_alternate_code", l["ItemCode"]),
        new XElement("ord_qty", l["Quantity"])));

    return new XElement("order", hdr, detalles);
}
```

Actualiza también el comentario XML-doc de la clase (bloque al inicio del archivo) para documentar el vocabulario de `order`/`order_hdr`/`order_dtl` confirmado, mismo criterio que ya se hizo para Item/Store/IbShipment.

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsCloudConnectorTests`
Expected: PASS (4/4 — 3 preexistentes + 1 nuevo).

- [ ] **Step 5: Build + test completo**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj`
Expected: todos los tests en verde.

- [ ] **Step 6: Commit**

```bash
git add src/Modulo.Wms/Services/WmsCloudConnector.cs tests/Modulo.Wms.Tests/Services/WmsCloudConnectorTests.cs
git commit -m "feat: rama Order en WmsCloudConnector (Ronda D)"
```

---

## Self-Review (registrado acá, no repetir en ejecución)

**1. Cobertura del spec:** filtro de negocio (`OPKL.Status='R'` → `PickLists Status='psReleased'`, no confirmado) → Task 3. Agrupación por documento base distinto (decisión de arquitectura del spec) → Task 3 Step 4 + Step 6 (test dedicado del comportamiento). Staging hdr+dtl → Tasks 1, 2. Etapa Subida + vocabulario XML confirmado contra DDL real → Tasks 4, 5. Resync desde `ErrorWms` desde el primer intento (lección de Ronda C) → Task 2, incluido desde el diseño, no como fix posterior.

**2. Placeholder scan:** sin "TBD"/"TODO". El Step 1 de Task 3 (nombre/valor no confirmado) trae instrucción concreta de qué hacer y cómo documentarlo — mismo patrón ya usado en Ronda C, no es un placeholder.

**3. Consistencia de tipos:** claves de `IntegrationRecord` (`OrderNbr`/`OrderType`/`PickListAbsEntry`/`BaseObjectType`/`BaseEntry`/`CardCode`/`CardName`/`CustomerPoNbr`/`OrdDate`/`ExpDate`/`ReqShipDate`/`ShipToCode`/`SourceUpdateDate`/`Lineas` con `ItemCode`/`Quantity`/`WhsCode`/`LineNum`/`SeqNbr`) usadas con el mismo nombre exacto en Task 2 (writer, consumidor), Task 3 (connector Bajada, productor), Task 4 (reader, productor 2ª etapa) y Task 5 (`WmsCloudConnector`, consumidor 2ª etapa) — verificado cruzando las 4 tasks. `WmsSapStageStatus` reusa el enum ya existente de Ronda C, sin redefinir.

**4. Riesgo señalado explícitamente:** Task 3 Step 1 avisa que el campo/valor de estado "Liberada" en `PickLists` no está confirmado — mismo criterio que Ronda C. La nota de la Global Constraints sobre no repetir el bug de `ErrorWms` de Ronda C está aplicada desde el Step 3 de Task 2, no dejada para un fix posterior.
