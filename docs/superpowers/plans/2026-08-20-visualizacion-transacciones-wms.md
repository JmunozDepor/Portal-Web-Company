# Visualización de Transacciones WMS Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Portar a Modulo.Wms las pantallas operativas de visualización (Dashboard, transacciones por tipo, archivos WMS, confirmaciones) que existían en el legado `WmsPortal.Web`, agregando el backend de Confirmación Ingreso (SVSH) y el heartbeat de servicio que faltaban.

**Architecture:** Razor Pages dentro de `Modulo.Wms` (mismo patrón que `MapeoCampos`/`ConfiguracionServicio`), leyendo directamente de `WmsDbContext` vía servicios de solo-lectura nuevos. El backend SVSH replica exactamente el patrón ya probado de SLSH (`WmsOracleStageSvsh`/`WmsSvshXmlParser`/`WmsSvshStageParser`/`WmsSvshInventoryReader`). El heartbeat es un helper compartido llamado desde los 4 `BackgroundService` del plugin.

**Tech Stack:** .NET 8, ASP.NET Core Razor Pages, EF Core 8 (Npgsql + SqlServer, motor dual), xUnit.

## Global Constraints

- Nombres de tabla/columna: inglés, plural donde aplica, snake_case, `id` surrogate, `company_id` en toda tabla de negocio (docs/01-CONVENCION-NOMBRES-BD.md).
- Ninguna columna usa `HasColumnType` específico de un solo motor (dual Postgres/SqlServer).
- Todo `BackgroundService` que resuelve `WmsDbContext` debe fijar `ICurrentCompanyOverride` en su propio scope ANTES de resolver el contexto (no hay `HttpContext` en background).
- Nunca usar el nombre comercial legado ("Logfire") en código/UI/commits.
- Cantidades numéricas que llegan como `string` (staging WMS) se parsean con `CultureInfo.InvariantCulture`, nunca con la cultura por defecto del hilo.
- Cada `BackgroundService` nuevo/tocado debe seguir corriendo si una compañía falla (try/catch por compañía dentro del ciclo, no dejar que una tumbe a las demás).

---

## File Structure

**Nuevos (backend SVSH + heartbeat):**
- `src/Modulo.Wms/Models/WmsOracleStageSvsh.cs` — modelo de la fila aplanada.
- `src/Modulo.Wms/Models/WmsServiceHeartbeat.cs` — ya existe, sin cambios.
- `src/Modulo.Wms/Services/WmsSvshXmlParser.cs` — aplanado del XML de 3 niveles.
- `src/Modulo.Wms/Services/WmsSvshStageParser.cs` — `BackgroundService`.
- `src/Modulo.Wms/Services/WmsSvshInventoryReader.cs` — `IIntegrationEntityReader`.
- `src/Modulo.Wms/Services/WmsServiceHeartbeatRecorder.cs` + `IWmsServiceHeartbeatRecorder.cs` — helper de heartbeat.
- `src/Modulo.Wms/Data/WmsDbContext.cs` — agrega `DbSet` + mapeo de `WmsOracleStageSvsh`.
- Migraciones nuevas en `Modulo.Wms.Migrations.Postgres` y `.SqlServer`.
- Tests en `tests/Modulo.Wms.Tests/Services/`.

**Nuevos (páginas):**
- `src/Modulo.Wms/Services/IWmsDashboardService.cs` + `WmsDashboardService.cs`.
- `src/Modulo.Wms/Services/IWmsTransaccionService.cs` + `WmsTransaccionService.cs`.
- `src/Modulo.Wms/Services/IWmsConfirmacionService.cs` + `WmsConfirmacionService.cs`.
- `src/Modulo.Wms/Services/IWmsArchivoService.cs` + `WmsArchivoService.cs`.
- `src/Modulo.Wms/Models/WmsDashboardModels.cs`, `WmsTransaccionModels.cs`, `WmsConfirmacionModels.cs`, `WmsArchivoModels.cs`.
- `src/Modulo.Wms/Pages/Dashboard/Index.cshtml(.cs)`.
- `src/Modulo.Wms/Pages/Transacciones/Index.cshtml(.cs)`.
- `src/Modulo.Wms/Pages/Confirmaciones/Index.cshtml(.cs)`.
- `src/Modulo.Wms/Pages/ArchivosWms/Index.cshtml(.cs)`.
- `src/Modulo.Wms/Pages/EstadoServicio/Index.cshtml(.cs)`.
- `src/Modulo.Wms/ModuloWms.cs` — nuevo menú + registro de servicios/hosted service.

---

### Task 1: Modelo `WmsOracleStageSvsh` + mapeo EF

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsOracleStageSvsh.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Data/WmsDbContext.cs`
- Test: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Data/WmsDbContextSvshMappingTests.cs`

**Interfaces:**
- Produces: `enum WmsSvshStatus { Pendiente, ProcesadoSap, ErrorSap }`, clase `WmsOracleStageSvsh` con propiedades `LineId (long, PK)`, `ParentId (long)`, `Status (WmsSvshStatus)`, `ErrorMsg (string?)`, `RetryCount (int)`, `SapDocEntry (int?)`, `SapObject (int?)`, y columnas de negocio (ver Step 1) — usadas por Task 2 (parser), Task 3 (background service), Task 4 (reader).

- [ ] **Step 1: Crear el modelo**

```csharp
namespace Modulo.Wms.Models;

public enum WmsSvshStatus { Pendiente, ProcesadoSap, ErrorSap }

/// <summary>
/// Confirmación WMS -> SAP de recepción de un ASN/traslado/devolución --
/// aplanado del XML de Oracle WMS Cloud (Header -> ib_shipment ->
/// ib_shipment_hdr + ib_shipment_dtl[], ver SVSH_Processor.cs en WMS_Suite),
/// una fila por línea de detalle. Espejo del patrón WmsOracleStageSlsh.
/// </summary>
public class WmsOracleStageSvsh
{
    public long LineId { get; set; }
    public long ParentId { get; set; }
    public WmsSvshStatus Status { get; set; } = WmsSvshStatus.Pendiente;
    public string? ErrorMsg { get; set; }
    public int RetryCount { get; set; }
    public int? SapDocEntry { get; set; }
    public int? SapObject { get; set; }

    // Header global (Header) y header del shipment (ib_shipment_hdr).
    public string? DocumentVersion { get; set; }
    public string? OriginSystem { get; set; }
    public string? ClientEnvCode { get; set; }
    public string? ParentCompanyCode { get; set; }
    public string? Entity { get; set; }
    public string? TimeStamp { get; set; }
    public string? MessageId { get; set; }
    public string? shipment_nbr { get; set; }
    public string? manifest_nbr { get; set; }
    public string? load_nbr { get; set; }
    public string? facility_code { get; set; }
    public string? company_code { get; set; }
    public string? asn_nbr { get; set; }
    public string? carrier_code { get; set; }
    public string? trailer_nbr { get; set; }
    public string? seal_nbr { get; set; }
    public string? rcvd_date { get; set; }
    public string? rcvd_date_time { get; set; }
    public string? cust_nbr { get; set; }
    public string? vendor_nbr { get; set; }
    public string? order_nbr { get; set; }
    public string? customer_po_nbr { get; set; }

    // Detalle (ib_shipment_dtl) -- usados por el reader para decidir endpoint SAP y línea.
    public string? shipment_dtl_cust_field_1 { get; set; } // BaseType
    public string? shipment_dtl_cust_field_2 { get; set; } // BaseEntry
    public string? shipment_dtl_cust_field_3 { get; set; } // LineNum
    public string? item_part_a { get; set; }
    public string? item_part_b { get; set; }
    public string? item_alternate_code { get; set; }
    public string? received_qty { get; set; }
    public string? shipped_qty { get; set; }
    public string? shipped_uom { get; set; }
    public string? ib_lpn_nbr { get; set; }
    public string? batch_nbr { get; set; }
    public string? expiry_date { get; set; }
    public string? serial_nbr { get; set; }
    public string? line_nbr { get; set; }
    public string? seq_nbr { get; set; }
}
```

- [ ] **Step 2: Mapear en `WmsDbContext`**

Agregar el `DbSet` junto a los demás (después de la línea `WmsOracleStageSlsh`):

```csharp
    public DbSet<WmsOracleStageSvsh> WmsOracleStageSvsh => Set<WmsOracleStageSvsh>();
```

Y dentro de `OnModelCreating`, después del bloque `modelBuilder.Entity<WmsOracleStageSlsh>(...)`:

```csharp
        modelBuilder.Entity<WmsOracleStageSvsh>(entity =>
        {
            entity.ToTable("wms_oracle_stage_svsh");
            entity.HasKey(e => e.LineId);
            entity.Property(e => e.LineId).HasColumnName("line_id");
            entity.Property(e => e.ParentId).HasColumnName("parent_id");
            entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ErrorMsg).HasColumnName("error_msg").HasMaxLength(500);
            entity.Property(e => e.RetryCount).HasColumnName("retry_count");
            entity.Property(e => e.SapDocEntry).HasColumnName("sap_doc_entry");
            entity.Property(e => e.SapObject).HasColumnName("sap_object");
            entity.Property(e => e.DocumentVersion).HasColumnName("document_version").HasMaxLength(50);
            entity.Property(e => e.OriginSystem).HasColumnName("origin_system").HasMaxLength(50);
            entity.Property(e => e.ClientEnvCode).HasColumnName("client_env_code").HasMaxLength(50);
            entity.Property(e => e.ParentCompanyCode).HasColumnName("parent_company_code").HasMaxLength(50);
            entity.Property(e => e.Entity).HasColumnName("entity").HasMaxLength(50);
            entity.Property(e => e.TimeStamp).HasColumnName("time_stamp").HasMaxLength(50);
            entity.Property(e => e.MessageId).HasColumnName("message_id").HasMaxLength(50);
            entity.Property(e => e.shipment_nbr).HasColumnName("shipment_nbr").HasMaxLength(50);
            entity.Property(e => e.manifest_nbr).HasColumnName("manifest_nbr").HasMaxLength(50);
            entity.Property(e => e.load_nbr).HasColumnName("load_nbr").HasMaxLength(50);
            entity.Property(e => e.facility_code).HasColumnName("facility_code").HasMaxLength(50);
            entity.Property(e => e.company_code).HasColumnName("company_code").HasMaxLength(50);
            entity.Property(e => e.asn_nbr).HasColumnName("asn_nbr").HasMaxLength(50);
            entity.Property(e => e.carrier_code).HasColumnName("carrier_code").HasMaxLength(50);
            entity.Property(e => e.trailer_nbr).HasColumnName("trailer_nbr").HasMaxLength(50);
            entity.Property(e => e.seal_nbr).HasColumnName("seal_nbr").HasMaxLength(50);
            entity.Property(e => e.rcvd_date).HasColumnName("rcvd_date").HasMaxLength(50);
            entity.Property(e => e.rcvd_date_time).HasColumnName("rcvd_date_time").HasMaxLength(50);
            entity.Property(e => e.cust_nbr).HasColumnName("cust_nbr").HasMaxLength(100);
            entity.Property(e => e.vendor_nbr).HasColumnName("vendor_nbr").HasMaxLength(100);
            entity.Property(e => e.order_nbr).HasColumnName("order_nbr").HasMaxLength(50);
            entity.Property(e => e.customer_po_nbr).HasColumnName("customer_po_nbr").HasMaxLength(50);
            entity.Property(e => e.shipment_dtl_cust_field_1).HasColumnName("shipment_dtl_cust_field_1").HasMaxLength(200);
            entity.Property(e => e.shipment_dtl_cust_field_2).HasColumnName("shipment_dtl_cust_field_2").HasMaxLength(200);
            entity.Property(e => e.shipment_dtl_cust_field_3).HasColumnName("shipment_dtl_cust_field_3").HasMaxLength(200);
            entity.Property(e => e.item_part_a).HasColumnName("item_part_a").HasMaxLength(50);
            entity.Property(e => e.item_part_b).HasColumnName("item_part_b").HasMaxLength(50);
            entity.Property(e => e.item_alternate_code).HasColumnName("item_alternate_code").HasMaxLength(50);
            entity.Property(e => e.received_qty).HasColumnName("received_qty").HasMaxLength(50);
            entity.Property(e => e.shipped_qty).HasColumnName("shipped_qty").HasMaxLength(50);
            entity.Property(e => e.shipped_uom).HasColumnName("shipped_uom").HasMaxLength(50);
            entity.Property(e => e.ib_lpn_nbr).HasColumnName("ib_lpn_nbr").HasMaxLength(50);
            entity.Property(e => e.batch_nbr).HasColumnName("batch_nbr").HasMaxLength(50);
            entity.Property(e => e.expiry_date).HasColumnName("expiry_date").HasMaxLength(50);
            entity.Property(e => e.serial_nbr).HasColumnName("serial_nbr").HasMaxLength(50);
            entity.Property(e => e.line_nbr).HasColumnName("line_nbr").HasMaxLength(50);
            entity.Property(e => e.seq_nbr).HasColumnName("seq_nbr").HasMaxLength(50);
            entity.HasOne<WmsOracleInboundStage>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.ParentId).HasDatabaseName("ix_wms_oracle_stage_svsh_parent");
            entity.HasIndex(e => new { e.Status, e.RetryCount }).HasDatabaseName("ix_wms_oracle_stage_svsh_status_retry");
            entity.HasIndex(e => e.shipment_nbr).HasDatabaseName("ix_wms_oracle_stage_svsh_shipment");
        });
```

- [ ] **Step 3: Test de mapeo (falla primero)**

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Xunit;

namespace Modulo.Wms.Tests.Data;

public class WmsDbContextSvshMappingTests
{
    [Fact]
    public void WmsOracleStageSvsh_SeMapeaConTablaYColumnasEsperadas()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var contexto = new WmsDbContext(options);

        var entityType = contexto.Model.FindEntityType(typeof(WmsOracleStageSvsh));

        Assert.NotNull(entityType);
        Assert.Equal("wms_oracle_stage_svsh", entityType!.GetTableName());
        Assert.Equal("line_id", entityType.FindProperty(nameof(WmsOracleStageSvsh.LineId))!.GetColumnName());
        Assert.Equal("shipment_nbr", entityType.FindProperty(nameof(WmsOracleStageSvsh.shipment_nbr))!.GetColumnName());
    }
}
```

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsDbContextSvshMappingTests`
Esperado antes de Step 1/2: FAIL (no compila, `WmsOracleStageSvsh` no existe). Después de Step 1/2: PASS.

- [ ] **Step 4: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsOracleStageSvsh.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Data/WmsDbContext.cs" "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Data/WmsDbContextSvshMappingTests.cs"
git commit -m "feat(wms): agregar modelo y mapeo EF de WmsOracleStageSvsh"
```

---

### Task 2: `WmsSvshXmlParser`

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSvshXmlParser.cs`
- Test: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsSvshXmlParserTests.cs`

**Interfaces:**
- Consumes: `WmsOracleStageSvsh` (Task 1).
- Produces: `static class WmsSvshXmlParser { static IReadOnlyList<WmsOracleStageSvsh> Parse(string xmlContent) }` — usado por Task 3.

- [ ] **Step 1: Test que falla primero**

```csharp
using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSvshXmlParserTests
{
    private const string XmlDosShipmentsUnDetalleCadaUno = """
        <Message>
          <Header>
            <DocumentVersion>1.0</DocumentVersion>
            <OriginSystem>WMS</OriginSystem>
            <ClientEnvCode>CLIENT_TEST</ClientEnvCode>
          </Header>
          <ib_shipment>
            <ib_shipment_hdr>
              <shipment_nbr>ASN1001</shipment_nbr>
              <facility_code>BOD1</facility_code>
            </ib_shipment_hdr>
            <ib_shipment_dtl>
              <shipment_dtl_cust_field_1>1250000001</shipment_dtl_cust_field_1>
              <shipment_dtl_cust_field_2>555</shipment_dtl_cust_field_2>
              <item_part_a>ITEM-A</item_part_a>
              <received_qty>10</received_qty>
            </ib_shipment_dtl>
          </ib_shipment>
          <ib_shipment>
            <ib_shipment_hdr>
              <shipment_nbr>ASN1002</shipment_nbr>
              <facility_code>BOD1</facility_code>
            </ib_shipment_hdr>
            <ib_shipment_dtl>
              <shipment_dtl_cust_field_1>1250000001</shipment_dtl_cust_field_1>
              <shipment_dtl_cust_field_2>556</shipment_dtl_cust_field_2>
              <item_part_a>ITEM-B</item_part_a>
              <received_qty>5</received_qty>
            </ib_shipment_dtl>
          </ib_shipment>
        </Message>
        """;

    [Fact]
    public void Parse_DosShipments_DevuelveUnaFilaPorLineaDeDetalle()
    {
        var filas = WmsSvshXmlParser.Parse(XmlDosShipmentsUnDetalleCadaUno);

        Assert.Equal(2, filas.Count);
        Assert.Contains(filas, f => f.shipment_nbr == "ASN1001" && f.item_part_a == "ITEM-A" && f.received_qty == "10");
        Assert.Contains(filas, f => f.shipment_nbr == "ASN1002" && f.item_part_a == "ITEM-B" && f.received_qty == "5");
    }

    [Fact]
    public void Parse_CampoSoloEnHeaderGlobal_SePropagaATodasLasFilas()
    {
        var filas = WmsSvshXmlParser.Parse(XmlDosShipmentsUnDetalleCadaUno);

        Assert.All(filas, f => Assert.Equal("CLIENT_TEST", f.ClientEnvCode));
    }

    [Fact]
    public void Parse_CampoEnDetalleTienePrioridadSobreHeaderDelShipment()
    {
        const string xml = """
            <Message>
              <Header></Header>
              <ib_shipment>
                <ib_shipment_hdr>
                  <shipment_nbr>ASN-PRIORIDAD</shipment_nbr>
                </ib_shipment_hdr>
                <ib_shipment_dtl>
                  <shipment_nbr>ASN-DETALLE-GANA</shipment_nbr>
                  <item_part_a>ITEM-X</item_part_a>
                </ib_shipment_dtl>
              </ib_shipment>
            </Message>
            """;

        var filas = WmsSvshXmlParser.Parse(xml);

        Assert.Single(filas);
        Assert.Equal("ASN-DETALLE-GANA", filas[0].shipment_nbr);
    }

    [Fact]
    public void Parse_SinNodosIbShipmentDtl_DevuelveListaVacia()
    {
        const string xml = "<Message><Header></Header></Message>";

        var filas = WmsSvshXmlParser.Parse(xml);

        Assert.Empty(filas);
    }
}
```

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsSvshXmlParserTests`
Esperado: FAIL (no compila, `WmsSvshXmlParser` no existe).

- [ ] **Step 2: Implementación**

```csharp
using System.Reflection;
using System.Xml.Linq;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

/// <summary>
/// Aplanado del XML SVSH (confirmación de recepción WMS -> SAP), 3 niveles:
/// Header global -> ib_shipment -> ib_shipment_hdr + ib_shipment_dtl[]. Una
/// fila de salida por cada ib_shipment_dtl, resolviendo cada columna de
/// negocio con prioridad detalle > header del shipment > header global
/// (mismo criterio que SVSH_Processor.cs del legado). Espejo estructural de
/// WmsSlshXmlParser, adaptado a 3 niveles en vez de 1.
/// </summary>
public static class WmsSvshXmlParser
{
    private static readonly HashSet<string> ColumnasMetadata = new(StringComparer.OrdinalIgnoreCase)
    {
        "LineId", "ParentId", "Status", "ErrorMsg", "RetryCount", "SapDocEntry", "SapObject",
    };

    private static readonly PropertyInfo[] ColumnasDeNegocio = typeof(WmsOracleStageSvsh)
        .GetProperties()
        .Where(p => p.PropertyType == typeof(string) && !ColumnasMetadata.Contains(p.Name))
        .ToArray();

    public static IReadOnlyList<WmsOracleStageSvsh> Parse(string xmlContent)
    {
        var doc = XDocument.Parse(xmlContent);

        var headerFields = doc.Descendants().Where(x => x.Name.LocalName == "Header")
            .Elements().ToDictionary(e => e.Name.LocalName, e => e.Value, StringComparer.OrdinalIgnoreCase);

        var filas = new List<WmsOracleStageSvsh>();

        foreach (var shipment in doc.Descendants().Where(x => x.Name.LocalName == "ib_shipment"))
        {
            var shipmentHdrFields = shipment.Elements()
                .FirstOrDefault(x => x.Name.LocalName == "ib_shipment_hdr")?
                .Elements().ToDictionary(e => e.Name.LocalName, e => e.Value, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var detalle in shipment.Elements().Where(x => x.Name.LocalName == "ib_shipment_dtl"))
            {
                var detalleFields = detalle.Elements()
                    .ToDictionary(e => e.Name.LocalName, e => e.Value, StringComparer.OrdinalIgnoreCase);

                var fila = new WmsOracleStageSvsh();
                foreach (var columna in ColumnasDeNegocio)
                {
                    string? valor = null;
                    if (detalleFields.TryGetValue(columna.Name, out var detalleValor))
                    {
                        valor = detalleValor;
                    }
                    else if (shipmentHdrFields.TryGetValue(columna.Name, out var hdrValor))
                    {
                        valor = hdrValor;
                    }
                    else if (headerFields.TryGetValue(columna.Name, out var globalValor))
                    {
                        valor = globalValor;
                    }

                    if (valor is not null)
                    {
                        columna.SetValue(fila, valor);
                    }
                }

                filas.Add(fila);
            }
        }

        return filas;
    }
}
```

- [ ] **Step 3: Verificar que los tests pasan**

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsSvshXmlParserTests`
Esperado: 4/4 PASS.

- [ ] **Step 4: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSvshXmlParser.cs" "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsSvshXmlParserTests.cs"
git commit -m "feat(wms): agregar WmsSvshXmlParser (aplanado XML de confirmacion de ingreso)"
```

---

### Task 3: `WmsSvshStageParser` (BackgroundService)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSvshStageParser.cs`
- Test: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsSvshStageParserTests.cs`

**Interfaces:**
- Consumes: `WmsSvshXmlParser.Parse` (Task 2), `WmsOracleInboundStages` / `WmsOracleStageSvsh` (`WmsDbContext`, Task 1), `ICurrentCompanyOverride`, `IExternalDatabaseConnectionService.ListActiveCompanyIdsAsync`.
- Produces: `sealed class WmsSvshStageParser : BackgroundService` con método interno `internal Task EjecutarCicloAsync(CancellationToken)` — usado en Task 6 (heartbeat) y Task 7 (registro en `ModuloWms.cs`).

Es una copia estructural exacta de `WmsSlshStageParser.cs` (ver `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSlshStageParser.cs`), cambiando `TipoDoc == "SLSH"` por `"SVSH"`, `WmsSlshXmlParser` por `WmsSvshXmlParser`, y `contexto.WmsOracleStageSlsh` por `contexto.WmsOracleStageSvsh`.

- [ ] **Step 1: Test que falla primero (aislamiento por compañía + aplanado exitoso)**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using Modulo.Wms.Tests.TestHelpers;
using PortalSaas.Abstractions.Contratos;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSvshStageParserTests
{
    private const string XmlValido = """
        <Message>
          <Header></Header>
          <ib_shipment>
            <ib_shipment_hdr><shipment_nbr>ASN2001</shipment_nbr></ib_shipment_hdr>
            <ib_shipment_dtl>
              <shipment_dtl_cust_field_1>1250000001</shipment_dtl_cust_field_1>
              <shipment_dtl_cust_field_2>900</shipment_dtl_cust_field_2>
              <item_part_a>ITEM-Z</item_part_a>
              <received_qty>3</received_qty>
            </ib_shipment_dtl>
          </ib_shipment>
        </Message>
        """;

    [Fact]
    public async Task EjecutarCicloAsync_ArchivoSvshPendiente_SeAplanaYQuedaEnAplanado()
    {
        var companyId = Guid.NewGuid();
        var provider = WmsTestServiceProviderFactory.Crear(companyId, out var scopeFactory);

        using (var scope = scopeFactory.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
            var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
            contexto.WmsOracleInboundStages.Add(new WmsOracleInboundStage
            {
                CompanyId = companyId,
                TipoDoc = "SVSH",
                Formato = WmsInboundFormato.Xml,
                NombreArchivo = "svsh_test.xml",
                HashArchivo = "hash-svsh-1",
                Contenido = XmlValido,
                Estado = WmsInboundEstado.Pendiente,
            });
            await contexto.SaveChangesAsync();
        }

        var parser = new WmsSvshStageParser(scopeFactory, NullLogger<WmsSvshStageParser>.Instance);
        await parser.EjecutarCicloAsync(CancellationToken.None);

        using var verifyScope = scopeFactory.CreateScope();
        verifyScope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var verifyContexto = verifyScope.ServiceProvider.GetRequiredService<WmsDbContext>();

        var stage = await verifyContexto.WmsOracleInboundStages.SingleAsync();
        Assert.Equal(WmsInboundEstado.Aplanado, stage.Estado);

        var fila = await verifyContexto.WmsOracleStageSvsh.SingleAsync();
        Assert.Equal("ASN2001", fila.shipment_nbr);
        Assert.Equal("ITEM-Z", fila.item_part_a);
        Assert.Equal(WmsSvshStatus.Pendiente, fila.Status);
    }
}
```

> Nota: `WmsTestServiceProviderFactory` ya existe en `tests/Modulo.Wms.Tests/TestHelpers/` (usado por `WmsSlshStageParserTests`) — reutilizar tal cual, no crear uno nuevo.

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsSvshStageParserTests`
Esperado: FAIL (no compila, `WmsSvshStageParser` no existe).

- [ ] **Step 2: Implementación**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Services;

public sealed class WmsSvshStageParser : BackgroundService
{
    private static readonly TimeSpan IntervaloCiclo = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WmsSvshStageParser> _logger;

    public WmsSvshStageParser(IServiceScopeFactory scopeFactory, ILogger<WmsSvshStageParser> logger)
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
                _logger.LogError(ex, "Error inesperado ejecutando el ciclo de aplanado SVSH");
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
            try
            {
                await ProcesarCompaniaAsync(compania.CompanyId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando el ciclo SVSH de la compañía {CompanyId}", compania.CompanyId);
            }
        }
    }

    private async Task ProcesarCompaniaAsync(Guid companyId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();

        var pendientes = await contexto.WmsOracleInboundStages
            .Where(s => s.Estado == WmsInboundEstado.Pendiente && s.TipoDoc == "SVSH" && s.Formato == WmsInboundFormato.Xml)
            .ToListAsync(cancellationToken);

        foreach (var entry in pendientes)
        {
            try
            {
                var filas = WmsSvshXmlParser.Parse(entry.Contenido);

                if (filas.Count == 0)
                {
                    entry.Estado = WmsInboundEstado.ErrorEstructura;
                    entry.MensajeError = "No se encontraron nodos ib_shipment_dtl válidos.";
                }
                else
                {
                    foreach (var fila in filas)
                    {
                        fila.ParentId = entry.Id;
                        contexto.WmsOracleStageSvsh.Add(fila);
                    }
                    entry.Estado = WmsInboundEstado.Aplanado;
                }
            }
            catch (Exception ex)
            {
                entry.Estado = WmsInboundEstado.ErrorStaging;
                entry.MensajeError = ex.Message;
                _logger.LogError(ex, "Error aplanando el archivo {NombreArchivo}", entry.NombreArchivo);
            }
            finally
            {
                entry.ProcessedAt = DateTimeOffset.UtcNow;
            }

            try
            {
                await contexto.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error guardando el resultado del aplanado SVSH para el archivo {NombreArchivo}", entry.NombreArchivo);
                await RegistrarFalloDePersistenciaAsync(companyId, entry.Id, ex, cancellationToken);
            }
        }
    }

    private async Task RegistrarFalloDePersistenciaAsync(Guid companyId, long entryId, Exception fallo, CancellationToken cancellationToken)
    {
        const int MaxIntentos = 3;

        try
        {
            using var recoveryScope = _scopeFactory.CreateScope();
            recoveryScope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
            var recoveryContexto = recoveryScope.ServiceProvider.GetRequiredService<WmsDbContext>();

            var entryFresco = await recoveryContexto.WmsOracleInboundStages
                .FirstOrDefaultAsync(s => s.Id == entryId, cancellationToken);

            if (entryFresco is null)
            {
                return;
            }

            entryFresco.Intentos++;
            entryFresco.ProcessedAt = DateTimeOffset.UtcNow;

            if (entryFresco.Intentos >= MaxIntentos)
            {
                entryFresco.Estado = WmsInboundEstado.ErrorStaging;
                entryFresco.MensajeError = $"Fallo de persistencia tras {entryFresco.Intentos} intentos: {fallo.Message}";
            }

            await recoveryContexto.SaveChangesAsync(cancellationToken);
        }
        catch (Exception recoveryEx)
        {
            _logger.LogError(recoveryEx, "Error registrando el fallo de persistencia SVSH para la fila {EntryId}", entryId);
        }
    }
}
```

- [ ] **Step 3: Verificar que el test pasa**

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsSvshStageParserTests`
Esperado: PASS.

- [ ] **Step 4: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSvshStageParser.cs" "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsSvshStageParserTests.cs"
git commit -m "feat(wms): agregar WmsSvshStageParser (BackgroundService de aplanado SVSH)"
```

---

### Task 4: `WmsSvshInventoryReader`

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSvshInventoryReader.cs`
- Test: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsSvshInventoryReaderTests.cs`

**Interfaces:**
- Consumes: `WmsOracleStageSvsh`/`WmsOracleInboundStage` (Task 1), `IIntegrationEntityReader`, `IntegrationRecord` (`PortalSaas.Abstractions.Contratos.Integraciones`).
- Produces: `class WmsSvshInventoryReader : IIntegrationEntityReader` con `EntidadNegocio => "Wms.ConfirmacionIngreso"` — usado en Task 7 (registro en `ModuloWms.cs`).

`SapDocumentConnector.PushAsync` (Core, ya existente) solo soporta hoy
`TipoDocumento == "Inventory"` (StockTransfer). Para `"Sales"`/`"Purchase"`
lanza `NotSupportedException` explícito (mapeo pendiente, ver comentario en el
propio archivo) — ese error ya lo captura `IntegrationSyncHostedService` y lo
guarda como error por registro, sin tumbar el resto del lote. Este reader NO
modifica `SapDocumentConnector`: arma `TipoDocumento` según `BaseType`
(`1250000001` → `"Inventory"`; cualquier otro valor → `"Purchase"`, que hoy
falla limpio con mensaje claro, documentado como limitación conocida).

- [ ] **Step 1: Test que falla primero**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using Modulo.Wms.Tests.TestHelpers;
using PortalSaas.Abstractions.Contratos;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSvshInventoryReaderTests
{
    [Fact]
    public async Task LeerPendientesAsync_AgrupaPorShipmentYBaseEntry_ArmaUnRegistroPorGrupo()
    {
        var companyId = Guid.NewGuid();
        var provider = WmsTestServiceProviderFactory.Crear(companyId, out var scopeFactory);

        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();

        var stage = new WmsOracleInboundStage { CompanyId = companyId, TipoDoc = "SVSH", Formato = WmsInboundFormato.Xml, NombreArchivo = "a.xml", HashArchivo = "h1", Contenido = "" };
        contexto.WmsOracleInboundStages.Add(stage);
        await contexto.SaveChangesAsync();

        contexto.WmsOracleStageSvsh.AddRange(
            new WmsOracleStageSvsh { ParentId = stage.Id, shipment_nbr = "ASN1", shipment_dtl_cust_field_1 = "1250000001", shipment_dtl_cust_field_2 = "700", item_part_a = "ITEM-A", received_qty = "4", Status = WmsSvshStatus.Pendiente },
            new WmsOracleStageSvsh { ParentId = stage.Id, shipment_nbr = "ASN1", shipment_dtl_cust_field_1 = "1250000001", shipment_dtl_cust_field_2 = "700", item_part_a = "ITEM-B", received_qty = "2", Status = WmsSvshStatus.Pendiente },
            new WmsOracleStageSvsh { ParentId = stage.Id, shipment_nbr = "ASN2", shipment_dtl_cust_field_1 = "1250000001", shipment_dtl_cust_field_2 = "701", item_part_a = "ITEM-C", received_qty = "1", Status = WmsSvshStatus.ProcesadoSap });
        await contexto.SaveChangesAsync();

        var reader = new WmsSvshInventoryReader(contexto);
        var registros = await reader.LeerPendientesAsync(companyId, CancellationToken.None);

        Assert.Single(registros);
        Assert.Equal("Inventory", registros[0]["TipoDocumento"]);
        var lineas = (List<PortalSaas.Abstractions.Contratos.Integraciones.IntegrationRecord>)registros[0]["Lineas"]!;
        Assert.Equal(2, lineas.Count);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_Exito_MarcaLasLineasComoProcesadoSap()
    {
        var companyId = Guid.NewGuid();
        var provider = WmsTestServiceProviderFactory.Crear(companyId, out var scopeFactory);

        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();

        var stage = new WmsOracleInboundStage { CompanyId = companyId, TipoDoc = "SVSH", Formato = WmsInboundFormato.Xml, NombreArchivo = "a.xml", HashArchivo = "h2", Contenido = "" };
        contexto.WmsOracleInboundStages.Add(stage);
        await contexto.SaveChangesAsync();

        var fila = new WmsOracleStageSvsh { ParentId = stage.Id, shipment_nbr = "ASN3", shipment_dtl_cust_field_1 = "1250000001", shipment_dtl_cust_field_2 = "800", item_part_a = "ITEM-D", received_qty = "1", Status = WmsSvshStatus.Pendiente };
        contexto.WmsOracleStageSvsh.Add(fila);
        await contexto.SaveChangesAsync();

        var reader = new WmsSvshInventoryReader(contexto);
        var registro = new PortalSaas.Abstractions.Contratos.Integraciones.IntegrationRecord(
            new Dictionary<string, object?> { ["_StagingLineIds"] = new List<long> { fila.LineId } });

        await reader.MarcarProcesadoAsync(companyId, registro, exito: true, mensajeError: null, CancellationToken.None);

        var actualizada = await contexto.WmsOracleStageSvsh.SingleAsync(f => f.LineId == fila.LineId);
        Assert.Equal(WmsSvshStatus.ProcesadoSap, actualizada.Status);
    }
}
```

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsSvshInventoryReaderTests`
Esperado: FAIL (no compila).

- [ ] **Step 2: Implementación**

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Lector del motor genérico de integración para las confirmaciones de
/// recepción SVSH -- agrupa por (shipment_nbr, BaseEntry) y arma un
/// IntegrationRecord por grupo. BaseType 1250000001 (StockTransfer) es el
/// único caso soportado hoy por SapDocumentConnector.PushAsync ("Inventory");
/// cualquier otro BaseType se marca "Purchase", que ese conector rechaza con
/// NotSupportedException explícito -- limitación conocida, no un bug de este
/// reader (ver spec 2026-08-20, sección 1).
/// </summary>
public class WmsSvshInventoryReader : IIntegrationEntityReader
{
    private const string BaseTypeStockTransfer = "1250000001";

    private readonly WmsDbContext _contexto;

    public WmsSvshInventoryReader(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "Wms.ConfirmacionIngreso";

    public async Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var filas = await (
            from linea in _contexto.WmsOracleStageSvsh
            join stage in _contexto.WmsOracleInboundStages on linea.ParentId equals stage.Id
            where stage.CompanyId == companyId && linea.Status == WmsSvshStatus.Pendiente
            select linea
        ).ToListAsync(cancellationToken);

        var grupos = filas.GroupBy(f => (f.shipment_nbr, f.shipment_dtl_cust_field_2));
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
                    ["Quantity"] = fila.received_qty,
                    ["BaseType"] = ParseIntOrNull(fila.shipment_dtl_cust_field_1),
                    ["BaseEntry"] = ParseIntOrNull(fila.shipment_dtl_cust_field_2),
                    ["BaseLine"] = ParseIntOrNull(fila.shipment_dtl_cust_field_3),
                }));
            }

            var primeraFila = grupo.First();
            var tipoDocumento = primeraFila.shipment_dtl_cust_field_1 == BaseTypeStockTransfer ? "Inventory" : "Purchase";

            registros.Add(new IntegrationRecord(new Dictionary<string, object?>
            {
                ["TipoDocumento"] = tipoDocumento,
                ["DocDate"] = ParseDateOrNull(primeraFila.rcvd_date),
                ["Lineas"] = lineas,
                ["_StagingLineIds"] = idsDeLinea,
            }));
        }

        return registros;
    }

    public async Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken)
    {
        var idsDeLinea = (List<long>)registro["_StagingLineIds"]!;

        var filas = await _contexto.WmsOracleStageSvsh
            .Where(f => idsDeLinea.Contains(f.LineId))
            .ToListAsync(cancellationToken);

        foreach (var fila in filas)
        {
            fila.Status = exito ? WmsSvshStatus.ProcesadoSap : WmsSvshStatus.ErrorSap;
            fila.ErrorMsg = exito ? null : mensajeError;
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }

    private static int? ParseIntOrNull(string? valor) => int.TryParse(valor, out var resultado) ? resultado : null;

    private static DateTime? ParseDateOrNull(string? valor) =>
        !string.IsNullOrWhiteSpace(valor) && DateTime.TryParse(valor, out var resultado) ? resultado : null;
}
```

- [ ] **Step 3: Verificar que los tests pasan**

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsSvshInventoryReaderTests`
Esperado: 2/2 PASS.

- [ ] **Step 4: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSvshInventoryReader.cs" "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsSvshInventoryReaderTests.cs"
git commit -m "feat(wms): agregar WmsSvshInventoryReader (confirmacion de ingreso hacia SAP)"
```

---

### Task 5: Migraciones EF (Postgres + SqlServer) para `wms_oracle_stage_svsh`

**Files:**
- Create: migración generada en `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms.Migrations.Postgres/Migrations/`
- Create: migración generada en `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms.Migrations.SqlServer/Migrations/`

**Interfaces:**
- Consumes: el modelo mapeado en Task 1 (`WmsDbContext.OnModelCreating` ya actualizado).
- Produces: tabla `wms_oracle_stage_svsh` en ambos motores — requerido para que Task 3/4 funcionen contra una base real (los tests unitarios usan `UseInMemoryDatabase`, no requieren esta migración).

- [ ] **Step 1: Generar la migración Postgres**

Run: `dotnet ef migrations add AddWmsOracleStageSvsh --project "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms.Migrations.Postgres" --startup-project "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms.Migrations.Postgres"`
Expected: archivo nuevo `..._AddWmsOracleStageSvsh.cs` con `migrationBuilder.CreateTable("wms_oracle_stage_svsh", ...)`.

- [ ] **Step 2: Generar la migración SqlServer**

Run: `dotnet ef migrations add AddWmsOracleStageSvsh --project "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms.Migrations.SqlServer" --startup-project "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms.Migrations.SqlServer"`
Expected: archivo nuevo equivalente para SqlServer.

- [ ] **Step 3: Verificar que no hay diffs pendientes (ambos motores)**

Run (Postgres): `dotnet ef migrations add ZZZCheckPending --project "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms.Migrations.Postgres" --startup-project "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms.Migrations.Postgres"`
Expected: el archivo generado tiene `Up()`/`Down()` vacíos (sin cambios) → borrar ese archivo de verificación (no commitear).
Repetir igual para SqlServer y borrar el archivo de verificación generado ahí también.

- [ ] **Step 4: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms.Migrations.Postgres/Migrations" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms.Migrations.SqlServer/Migrations"
git commit -m "feat(wms): agregar migraciones EF para wms_oracle_stage_svsh (Postgres y SqlServer)"
```

---

### Task 6: Heartbeat de servicio (`IWmsServiceHeartbeatRecorder`) + wiring en los 4 BackgroundService

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/IWmsServiceHeartbeatRecorder.cs`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsServiceHeartbeatRecorder.cs`
- Modify: `WmsSlshStageParser.cs`, `WmsSvshStageParser.cs` (Task 3), `WmsStageErrorReconciler.cs`, `WmsExistsReconciler.cs`
- Test: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsServiceHeartbeatRecorderTests.cs`

**Interfaces:**
- Consumes: `WmsServiceHeartbeat` (modelo ya existente), `WmsDbContext`.
- Produces: `interface IWmsServiceHeartbeatRecorder { Task RecordAsync(Guid companyId, string processorKey, string status, string? error = null, CancellationToken ct = default); }` — usado por Task 3/7/9 (Dashboard) y Task 12 (Estado del Servicio).

- [ ] **Step 1: Test que falla primero**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulo.Wms.Data;
using Modulo.Wms.Services;
using Modulo.Wms.Tests.TestHelpers;
using PortalSaas.Abstractions.Contratos;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsServiceHeartbeatRecorderTests
{
    [Fact]
    public async Task RecordAsync_PrimeraVez_InsertaFila()
    {
        var companyId = Guid.NewGuid();
        var provider = WmsTestServiceProviderFactory.Crear(companyId, out var scopeFactory);

        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();

        var recorder = new WmsServiceHeartbeatRecorder(contexto);
        await recorder.RecordAsync(companyId, "Wms.SlshStageParser", "OK");

        var fila = await contexto.ServiceHeartbeats.SingleAsync();
        Assert.Equal("OK", fila.Status);
        Assert.NotNull(fila.LastRunAt);
    }

    [Fact]
    public async Task RecordAsync_SegundaVezMismaClave_ActualizaEnVezDeDuplicar()
    {
        var companyId = Guid.NewGuid();
        var provider = WmsTestServiceProviderFactory.Crear(companyId, out var scopeFactory);

        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var recorder = new WmsServiceHeartbeatRecorder(contexto);

        await recorder.RecordAsync(companyId, "Wms.SlshStageParser", "OK");
        await recorder.RecordAsync(companyId, "Wms.SlshStageParser", "ERROR", "fallo de red");

        var filas = await contexto.ServiceHeartbeats.ToListAsync();
        Assert.Single(filas);
        Assert.Equal("ERROR", filas[0].Status);
        Assert.Equal("fallo de red", filas[0].LastError);
    }
}
```

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsServiceHeartbeatRecorderTests`
Esperado: FAIL (no compila).

- [ ] **Step 2: Implementación**

```csharp
namespace Modulo.Wms.Services;

public interface IWmsServiceHeartbeatRecorder
{
    Task RecordAsync(Guid companyId, string processorKey, string status, string? error = null, CancellationToken cancellationToken = default);
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

/// <summary>
/// Upsert por (CompanyId, ProcessorKey) sobre wms_oracle_service_heartbeats --
/// mismo contrato que ServiceHeartbeat.RecordAsync del legado. Llamado al
/// final (éxito o error) del ciclo de cada BackgroundService del plugin.
/// </summary>
public class WmsServiceHeartbeatRecorder : IWmsServiceHeartbeatRecorder
{
    private readonly WmsDbContext _contexto;

    public WmsServiceHeartbeatRecorder(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task RecordAsync(Guid companyId, string processorKey, string status, string? error = null, CancellationToken cancellationToken = default)
    {
        var fila = await _contexto.ServiceHeartbeats
            .FirstOrDefaultAsync(h => h.CompanyId == companyId && h.ProcessorKey == processorKey, cancellationToken);

        if (fila is null)
        {
            fila = new WmsServiceHeartbeat { CompanyId = companyId, ProcessorKey = processorKey };
            _contexto.ServiceHeartbeats.Add(fila);
        }

        fila.LastRunAt = DateTimeOffset.UtcNow;
        fila.Status = status;
        fila.LastError = error;

        await _contexto.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 3: Registrar en `ModuloWms.cs::RegisterServices`**

Agregar junto a los demás `AddScoped`:

```csharp
        services.AddScoped<IWmsServiceHeartbeatRecorder, WmsServiceHeartbeatRecorder>();
```

- [ ] **Step 4: Wirear en `WmsSlshStageParser.ProcesarCompaniaAsync`**

En `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSlshStageParser.cs`, dentro de `ProcesarCompaniaAsync`, después del `foreach` de `pendientes` (antes del cierre del método), agregar:

```csharp
        var heartbeat = scope.ServiceProvider.GetRequiredService<IWmsServiceHeartbeatRecorder>();
        await heartbeat.RecordAsync(companyId, "Wms.SlshStageParser", "OK", cancellationToken: cancellationToken);
```

Y envolver el cuerpo completo del método (desde `var contexto = ...` hasta el final) en un `try/catch` que, ante excepción no capturada por el manejo existente, registre `"ERROR"` antes de relanzar — dado que `EjecutarCicloAsync` ya captura por compañía (ver Global Constraints), el catch acá es solo para reportar heartbeat antes de que la excepción suba:

```csharp
        catch (Exception ex)
        {
            await heartbeat.RecordAsync(companyId, "Wms.SlshStageParser", "ERROR", ex.Message, cancellationToken);
            throw;
        }
```

(Reestructurar el método existente para que el bloque de trabajo quede dentro de un `try` seguido de este `catch`, manteniendo el resto de la lógica intacta.)

- [ ] **Step 5: Wirear en `WmsSvshStageParser`, `WmsStageErrorReconciler`, `WmsExistsReconciler`**

Mismo patrón del Step 4, con `ProcessorKey` `"Wms.SvshStageParser"`, `"Wms.StageErrorReconciler"`, `"Wms.ExistsReconciler"` respectivamente, en el método de cada uno que procesa una compañía (`ProcesarCompaniaAsync` o equivalente — revisar el nombre exacto en cada archivo, mismo lugar donde `WmsSlshStageParser` lo hace: al final del procesamiento por compañía, con manejo de éxito y error).

- [ ] **Step 6: Verificar que los tests pasan (heartbeat + los 4 servicios no rompieron)**

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests"`
Esperado: todos los tests existentes + los nuevos de heartbeat en PASS.

- [ ] **Step 7: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/IWmsServiceHeartbeatRecorder.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsServiceHeartbeatRecorder.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSlshStageParser.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSvshStageParser.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsStageErrorReconciler.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsExistsReconciler.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs" "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsServiceHeartbeatRecorderTests.cs"
git commit -m "feat(wms): agregar heartbeat de servicio y cablearlo en los 4 BackgroundService del plugin"
```

---

### Task 7: Registrar SVSH en `ModuloWms.cs`

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs`

**Interfaces:**
- Consumes: `WmsSvshStageParser` (Task 3), `WmsSvshInventoryReader` (Task 4).

- [ ] **Step 1: Agregar el hosted service y el reader**

En `RegisterServices`, junto a los `AddHostedService`/`AddScoped<IIntegrationEntityReader, ...>` existentes:

```csharp
        services.AddScoped<IIntegrationEntityReader, WmsSvshInventoryReader>();
        services.AddHostedService<WmsSvshStageParser>();
```

- [ ] **Step 2: Verificar que el módulo compila y arranca**

Run: `dotnet build "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Modulo.Wms.csproj"`
Expected: Build succeeded, 0 errores.

- [ ] **Step 3: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs"
git commit -m "feat(wms): registrar WmsSvshStageParser y WmsSvshInventoryReader en el modulo"
```

---

### Task 8: Servicio y modelos de Dashboard

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsDashboardModels.cs`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/IWmsDashboardService.cs`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsDashboardService.cs`
- Test: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsDashboardServiceTests.cs`

**Interfaces:**
- Consumes: `WmsDbContext` (todas las tablas stage + `WmsExportValidation` + `ServiceHeartbeats`).
- Produces: `interface IWmsDashboardService { Task<WmsDashboardResumen> ObtenerResumenAsync(Guid companyId, DateTime desde, CancellationToken ct); }`, `class WmsDashboardResumen`, `class WmsEstadisticaTipo` — usado por Task 9 (página Dashboard).

- [ ] **Step 1: Modelos**

```csharp
namespace Modulo.Wms.Models;

public enum WmsTipoTransaccion { EnvioProducto, EnvioSucursal, EnvioOrdenes, EnvioIngresoAsn, ConfirmacionOrdenes, ConfirmacionIngreso }

public static class WmsTipoTransaccionInfo
{
    public static readonly Dictionary<WmsTipoTransaccion, string> Labels = new()
    {
        [WmsTipoTransaccion.EnvioProducto] = "Envío - Producto",
        [WmsTipoTransaccion.EnvioSucursal] = "Envío - Sucursal",
        [WmsTipoTransaccion.EnvioOrdenes] = "Envío - Órdenes",
        [WmsTipoTransaccion.EnvioIngresoAsn] = "Envío - Ingreso ASN",
        [WmsTipoTransaccion.ConfirmacionOrdenes] = "Confirmación Órdenes",
        [WmsTipoTransaccion.ConfirmacionIngreso] = "Confirmación Ingreso",
    };

    public static readonly Dictionary<WmsTipoTransaccion, string> ProcessorKeys = new()
    {
        [WmsTipoTransaccion.ConfirmacionOrdenes] = "Wms.SlshStageParser",
        [WmsTipoTransaccion.ConfirmacionIngreso] = "Wms.SvshStageParser",
    };
}

public class WmsDashboardResumen
{
    public int TotalTransacciones { get; set; }
    public int TotalOk { get; set; }
    public int TotalError { get; set; }
    public int TotalPendiente { get; set; }
    public List<WmsEstadisticaTipo> PorTipo { get; set; } = new();
}

public class WmsEstadisticaTipo
{
    public WmsTipoTransaccion Tipo { get; set; }
    public string Label => WmsTipoTransaccionInfo.Labels[Tipo];
    public int Ok { get; set; }
    public int Error { get; set; }
    public int Pendiente { get; set; }
    public int Total => Ok + Error + Pendiente;
    public int? ConfirmadoWms { get; set; }
    public DateTimeOffset? UltimaEjecucion { get; set; }
    public string? UltimoEstado { get; set; }
}
```

- [ ] **Step 2: Test que falla primero**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using Modulo.Wms.Tests.TestHelpers;
using PortalSaas.Abstractions.Contratos;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsDashboardServiceTests
{
    [Fact]
    public async Task ObtenerResumenAsync_CuentaPorEstadoYTipo()
    {
        var companyId = Guid.NewGuid();
        var provider = WmsTestServiceProviderFactory.Crear(companyId, out var scopeFactory);

        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();

        contexto.WmsSapStageItems.AddRange(
            new WmsSapStageItem { CompanyId = companyId, ItemCode = "I1", ItemName = "Item 1", Status = WmsSapStageStatus.ProcesadoWms },
            new WmsSapStageItem { CompanyId = companyId, ItemCode = "I2", ItemName = "Item 2", Status = WmsSapStageStatus.ErrorWms },
            new WmsSapStageItem { CompanyId = companyId, ItemCode = "I3", ItemName = "Item 3", Status = WmsSapStageStatus.Pendiente });
        await contexto.SaveChangesAsync();

        var service = new WmsDashboardService(contexto);
        var resumen = await service.ObtenerResumenAsync(companyId, DateTime.UtcNow.AddDays(-1), CancellationToken.None);

        var producto = resumen.PorTipo.Single(t => t.Tipo == WmsTipoTransaccion.EnvioProducto);
        Assert.Equal(1, producto.Ok);
        Assert.Equal(1, producto.Error);
        Assert.Equal(1, producto.Pendiente);
        Assert.Equal(1, resumen.TotalOk);
    }
}
```

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsDashboardServiceTests`
Esperado: FAIL (no compila).

- [ ] **Step 3: Implementación**

```csharp
namespace Modulo.Wms.Services;

public interface IWmsDashboardService
{
    Task<Models.WmsDashboardResumen> ObtenerResumenAsync(Guid companyId, DateTime desde, CancellationToken cancellationToken);
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public class WmsDashboardService : IWmsDashboardService
{
    private readonly WmsDbContext _contexto;

    public WmsDashboardService(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<WmsDashboardResumen> ObtenerResumenAsync(Guid companyId, DateTime desde, CancellationToken cancellationToken)
    {
        var heartbeats = await _contexto.ServiceHeartbeats
            .Where(h => h.CompanyId == companyId)
            .ToDictionaryAsync(h => h.ProcessorKey, h => h, cancellationToken);

        var porTipo = new List<WmsEstadisticaTipo>
        {
            await ContarAsync(WmsTipoTransaccion.EnvioProducto,
                await _contexto.WmsSapStageItems.Where(x => x.CompanyId == companyId && x.CreatedAt >= desde).Select(x => (int)x.Status).ToListAsync(cancellationToken)),
            await ContarAsync(WmsTipoTransaccion.EnvioSucursal,
                await _contexto.WmsSapStageStores.Where(x => x.CompanyId == companyId && x.CreatedAt >= desde).Select(x => (int)x.Status).ToListAsync(cancellationToken)),
            await ContarAsync(WmsTipoTransaccion.EnvioOrdenes,
                await _contexto.WmsSapStageOrderHdrs.Where(x => x.CompanyId == companyId && x.CreatedAt >= desde).Select(x => (int)x.Status).ToListAsync(cancellationToken)),
            await ContarAsync(WmsTipoTransaccion.EnvioIngresoAsn,
                await _contexto.WmsSapStageInboundHdrs.Where(x => x.CompanyId == companyId && x.CreatedAt >= desde).Select(x => (int)x.Status).ToListAsync(cancellationToken)),
        };

        var confirmacionOrdenes = await ContarConfirmacionAsync(WmsTipoTransaccion.ConfirmacionOrdenes,
            await (from f in _contexto.WmsOracleStageSlsh
                   join s in _contexto.WmsOracleInboundStages on f.ParentId equals s.Id
                   where s.CompanyId == companyId && s.InsertedAt >= desde
                   select (int)f.Status).ToListAsync(cancellationToken));
        var confirmacionIngreso = await ContarConfirmacionAsync(WmsTipoTransaccion.ConfirmacionIngreso,
            await (from f in _contexto.WmsOracleStageSvsh
                   join s in _contexto.WmsOracleInboundStages on f.ParentId equals s.Id
                   where s.CompanyId == companyId && s.InsertedAt >= desde
                   select (int)f.Status).ToListAsync(cancellationToken));

        porTipo.Add(confirmacionOrdenes);
        porTipo.Add(confirmacionIngreso);

        foreach (var estadistica in porTipo)
        {
            if (WmsTipoTransaccionInfo.ProcessorKeys.TryGetValue(estadistica.Tipo, out var processorKey)
                && heartbeats.TryGetValue(processorKey, out var heartbeat))
            {
                estadistica.UltimaEjecucion = heartbeat.LastRunAt;
                estadistica.UltimoEstado = heartbeat.Status;
            }
        }

        return new WmsDashboardResumen
        {
            TotalTransacciones = porTipo.Sum(t => t.Total),
            TotalOk = porTipo.Sum(t => t.Ok),
            TotalError = porTipo.Sum(t => t.Error),
            TotalPendiente = porTipo.Sum(t => t.Pendiente),
            PorTipo = porTipo,
        };
    }

    private static Task<WmsEstadisticaTipo> ContarAsync(WmsTipoTransaccion tipo, List<int> statuses)
    {
        // WmsSapStageStatus: Pendiente=0, Enviado=1, ProcesadoWms=2, ErrorWms=3.
        var resultado = new WmsEstadisticaTipo
        {
            Tipo = tipo,
            Pendiente = statuses.Count(s => s == (int)WmsSapStageStatus.Pendiente || s == (int)WmsSapStageStatus.Enviado),
            Ok = statuses.Count(s => s == (int)WmsSapStageStatus.ProcesadoWms),
            Error = statuses.Count(s => s == (int)WmsSapStageStatus.ErrorWms),
        };
        return Task.FromResult(resultado);
    }

    private static Task<WmsEstadisticaTipo> ContarConfirmacionAsync(WmsTipoTransaccion tipo, List<int> statuses)
    {
        // WmsSlshStatus/WmsSvshStatus comparten forma: Pendiente=0, ProcesadoSap=1, ErrorSap=2.
        var resultado = new WmsEstadisticaTipo
        {
            Tipo = tipo,
            Pendiente = statuses.Count(s => s == 0),
            Ok = statuses.Count(s => s == 1),
            Error = statuses.Count(s => s == 2),
        };
        return Task.FromResult(resultado);
    }
}
```

- [ ] **Step 4: Verificar que el test pasa**

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsDashboardServiceTests`
Esperado: PASS.

- [ ] **Step 5: Registrar en `ModuloWms.cs`**

```csharp
        services.AddScoped<IWmsDashboardService, WmsDashboardService>();
```

- [ ] **Step 6: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsDashboardModels.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/IWmsDashboardService.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsDashboardService.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs" "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsDashboardServiceTests.cs"
git commit -m "feat(wms): agregar WmsDashboardService (agregados por tipo y estado)"
```

---

### Task 9: Página Dashboard (`/wms/dashboard`)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/Dashboard/Index.cshtml`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/Dashboard/Index.cshtml.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs`

**Interfaces:**
- Consumes: `IWmsDashboardService.ObtenerResumenAsync` (Task 8), `WmsPageModelBase`, `ICurrentCompanyAccessor`.

- [ ] **Step 1: PageModel**

```csharp
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Pages.Dashboard;

public sealed class IndexModel : WmsPageModelBase
{
    private readonly IWmsDashboardService _dashboard;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IWmsDashboardService dashboard, ICurrentCompanyAccessor currentCompany)
    {
        _dashboard = dashboard;
        _currentCompany = currentCompany;
    }

    public WmsDashboardResumen Resumen { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public int DiasAtras { get; set; } = 1;

    public async Task OnGetAsync(CancellationToken ct)
    {
        var desde = DateTime.UtcNow.AddDays(-Math.Max(1, DiasAtras));
        Resumen = await _dashboard.ObtenerResumenAsync(_currentCompany.CompanyId, desde, ct);
    }
}
```

- [ ] **Step 2: Página**

```cshtml
@page
@model Modulo.Wms.Pages.Dashboard.IndexModel
@{
    ViewData["Title"] = "Dashboard WMS";
}

<h1 class="h3 mb-3">Dashboard de Integración WMS</h1>

<div class="mb-3">
    <a class="btn btn-sm @(Model.DiasAtras == 1 ? "btn-primary" : "btn-outline-secondary")" asp-route-DiasAtras="1">Hoy</a>
    <a class="btn btn-sm @(Model.DiasAtras == 7 ? "btn-primary" : "btn-outline-secondary")" asp-route-DiasAtras="7">7 días</a>
    <a class="btn btn-sm @(Model.DiasAtras == 30 ? "btn-primary" : "btn-outline-secondary")" asp-route-DiasAtras="30">30 días</a>
</div>

<div class="row g-3 mb-4">
    <div class="col-md-3"><div class="card p-3"><div class="text-muted small">Total transacciones</div><div class="h3 mb-0">@Model.Resumen.TotalTransacciones</div></div></div>
    <div class="col-md-3"><div class="card p-3"><div class="text-muted small">Procesadas OK</div><div class="h3 mb-0 text-success">@Model.Resumen.TotalOk</div></div></div>
    <div class="col-md-3"><div class="card p-3"><div class="text-muted small">Con error</div><div class="h3 mb-0 text-danger">@Model.Resumen.TotalError</div></div></div>
    <div class="col-md-3"><div class="card p-3"><div class="text-muted small">Pendientes</div><div class="h3 mb-0 text-warning">@Model.Resumen.TotalPendiente</div></div></div>
</div>

<table class="table table-sm">
    <thead><tr><th>Tipo</th><th>OK</th><th>Error</th><th>Pend.</th><th>Última ejecución</th><th>Estado</th></tr></thead>
    <tbody>
    @foreach (var fila in Model.Resumen.PorTipo)
    {
        <tr>
            <td>@fila.Label</td>
            <td class="text-success">@fila.Ok</td>
            <td class="text-danger">@fila.Error</td>
            <td class="text-warning">@fila.Pendiente</td>
            <td>@(fila.UltimaEjecucion?.ToString("dd-MM HH:mm") ?? "—")</td>
            <td>@(fila.UltimoEstado ?? "—")</td>
        </tr>
    }
    </tbody>
</table>
```

- [ ] **Step 3: Agregar entrada de menú en `ModuloWms.cs::GetMenu`**

Insertar antes de `mapeo-campos` (queda primera, orden operativo):

```csharp
        yield return new MenuItemDefinition
        {
            Code = "dashboard",
            ParentCode = "raiz",
            Name = "Dashboard",
            PageRoute = "/wms/dashboard",
            Order = 0,
        };
```

Y renumerar `Order` de `mapeo-campos` (4), `configuracion-servicio` (5), `estado-servicio` (6) para dejar lugar a las páginas de las Tasks 10-12 (`transacciones`=1, `confirmaciones`=2, `archivos`=3).

- [ ] **Step 4: Verificación manual**

Run: `dotnet run --project "Portal SaaS - Core/src/PortalSaas.Host"` (con la compañía WMS de pruebas activa) y navegar a `/wms/dashboard`.
Expected: la página carga, muestra las tarjetas y la tabla sin excepción (aunque los valores estén en 0 si no hay datos).

- [ ] **Step 5: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/Dashboard" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs"
git commit -m "feat(wms): agregar pagina Dashboard"
```

---

### Task 10: Servicio + página Transacciones (`/wms/transacciones?tipo=`)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsTransaccionModels.cs`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/IWmsTransaccionService.cs`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsTransaccionService.cs`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/Transacciones/Index.cshtml(.cs)`
- Modify: `ModuloWms.cs`
- Test: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsTransaccionServiceTests.cs`

**Interfaces:**
- Consumes: `WmsTipoTransaccion` (Task 8, solo los 4 valores `Envio*`), `WmsDbContext`.
- Produces: `interface IWmsTransaccionService { Task<WmsPagedResult<WmsTransaccionRow>> BuscarAsync(Guid companyId, WmsTransaccionFiltro filtro, CancellationToken ct); Task ResetearAsync(Guid companyId, WmsTipoTransaccion tipo, IReadOnlyList<long> lineIds, CancellationToken ct); }`.

- [ ] **Step 1: Modelos**

```csharp
namespace Modulo.Wms.Models;

public class WmsTransaccionFiltro
{
    public WmsTipoTransaccion Tipo { get; set; }
    public string? Estado { get; set; }
    public string? Documento { get; set; }
    public DateTime? Desde { get; set; }
    public DateTime? Hasta { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class WmsTransaccionRow
{
    public long LineId { get; set; }
    public string Documento { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public string? ErrorMsg { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
}

public class WmsPagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}
```

- [ ] **Step 2: Test que falla primero**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using Modulo.Wms.Tests.TestHelpers;
using PortalSaas.Abstractions.Contratos;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsTransaccionServiceTests
{
    [Fact]
    public async Task BuscarAsync_FiltraPorEstadoYTipo()
    {
        var companyId = Guid.NewGuid();
        var provider = WmsTestServiceProviderFactory.Crear(companyId, out var scopeFactory);

        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();

        contexto.WmsSapStageItems.AddRange(
            new WmsSapStageItem { CompanyId = companyId, ItemCode = "I1", ItemName = "Item 1", Status = WmsSapStageStatus.ErrorWms },
            new WmsSapStageItem { CompanyId = companyId, ItemCode = "I2", ItemName = "Item 2", Status = WmsSapStageStatus.ProcesadoWms });
        await contexto.SaveChangesAsync();

        var service = new WmsTransaccionService(contexto);
        var resultado = await service.BuscarAsync(companyId, new WmsTransaccionFiltro { Tipo = WmsTipoTransaccion.EnvioProducto, Estado = "ErrorWms" }, CancellationToken.None);

        Assert.Single(resultado.Items);
        Assert.Equal("I1", resultado.Items[0].Documento);
    }

    [Fact]
    public async Task ResetearAsync_VuelveElEstadoAPendienteYLimpiaError()
    {
        var companyId = Guid.NewGuid();
        var provider = WmsTestServiceProviderFactory.Crear(companyId, out var scopeFactory);

        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();

        var item = new WmsSapStageItem { CompanyId = companyId, ItemCode = "I1", ItemName = "Item 1", Status = WmsSapStageStatus.ErrorWms, ErrorMsg = "boom" };
        contexto.WmsSapStageItems.Add(item);
        await contexto.SaveChangesAsync();

        var service = new WmsTransaccionService(contexto);
        await service.ResetearAsync(companyId, WmsTipoTransaccion.EnvioProducto, new List<long> { item.LineId }, CancellationToken.None);

        var actualizado = await contexto.WmsSapStageItems.SingleAsync();
        Assert.Equal(WmsSapStageStatus.Pendiente, actualizado.Status);
        Assert.Null(actualizado.ErrorMsg);
    }
}
```

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsTransaccionServiceTests`
Esperado: FAIL (no compila).

- [ ] **Step 3: Implementación**

```csharp
namespace Modulo.Wms.Services;

public interface IWmsTransaccionService
{
    Task<Models.WmsPagedResult<Models.WmsTransaccionRow>> BuscarAsync(Guid companyId, Models.WmsTransaccionFiltro filtro, CancellationToken cancellationToken);
    Task ResetearAsync(Guid companyId, Models.WmsTipoTransaccion tipo, IReadOnlyList<long> lineIds, CancellationToken cancellationToken);
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public class WmsTransaccionService : IWmsTransaccionService
{
    private readonly WmsDbContext _contexto;

    public WmsTransaccionService(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<WmsPagedResult<WmsTransaccionRow>> BuscarAsync(Guid companyId, WmsTransaccionFiltro filtro, CancellationToken cancellationToken)
    {
        var query = filtro.Tipo switch
        {
            WmsTipoTransaccion.EnvioProducto => _contexto.WmsSapStageItems.Where(x => x.CompanyId == companyId)
                .Select(x => new WmsTransaccionRow { LineId = x.LineId, Documento = x.ItemCode, Status = x.Status.ToString(), RetryCount = x.RetryCount, ErrorMsg = x.ErrorMsg, CreatedAt = x.CreatedAt, SyncedAt = x.SyncedAt }),
            WmsTipoTransaccion.EnvioSucursal => _contexto.WmsSapStageStores.Where(x => x.CompanyId == companyId)
                .Select(x => new WmsTransaccionRow { LineId = x.LineId, Documento = x.CardCode, Status = x.Status.ToString(), RetryCount = x.RetryCount, ErrorMsg = x.ErrorMsg, CreatedAt = x.CreatedAt, SyncedAt = x.SyncedAt }),
            WmsTipoTransaccion.EnvioOrdenes => _contexto.WmsSapStageOrderHdrs.Where(x => x.CompanyId == companyId)
                .Select(x => new WmsTransaccionRow { LineId = x.LineId, Documento = x.OrderNbr, Status = x.Status.ToString(), RetryCount = x.RetryCount, ErrorMsg = x.ErrorMsg, CreatedAt = x.CreatedAt, SyncedAt = x.SyncedAt }),
            WmsTipoTransaccion.EnvioIngresoAsn => _contexto.WmsSapStageInboundHdrs.Where(x => x.CompanyId == companyId)
                .Select(x => new WmsTransaccionRow { LineId = x.LineId, Documento = x.SapDocEntry.ToString(), Status = x.Status.ToString(), RetryCount = x.RetryCount, ErrorMsg = x.ErrorMsg, CreatedAt = x.CreatedAt, SyncedAt = x.SyncedAt }),
            _ => throw new NotSupportedException($"WmsTransaccionService no soporta el tipo '{filtro.Tipo}' (usar WmsConfirmacionService para confirmaciones)."),
        };

        if (!string.IsNullOrWhiteSpace(filtro.Estado))
        {
            query = query.Where(r => r.Status == filtro.Estado);
        }
        if (!string.IsNullOrWhiteSpace(filtro.Documento))
        {
            query = query.Where(r => r.Documento.Contains(filtro.Documento));
        }
        if (filtro.Desde.HasValue)
        {
            var desde = new DateTimeOffset(filtro.Desde.Value, TimeSpan.Zero);
            query = query.Where(r => r.CreatedAt >= desde);
        }
        if (filtro.Hasta.HasValue)
        {
            var hasta = new DateTimeOffset(filtro.Hasta.Value.AddDays(1), TimeSpan.Zero);
            query = query.Where(r => r.CreatedAt < hasta);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((filtro.Page - 1) * filtro.PageSize)
            .Take(filtro.PageSize)
            .ToListAsync(cancellationToken);

        return new WmsPagedResult<WmsTransaccionRow> { Items = items, TotalCount = total, Page = filtro.Page, PageSize = filtro.PageSize };
    }

    public async Task ResetearAsync(Guid companyId, WmsTipoTransaccion tipo, IReadOnlyList<long> lineIds, CancellationToken cancellationToken)
    {
        switch (tipo)
        {
            case WmsTipoTransaccion.EnvioProducto:
                await ResetearTablaAsync(_contexto.WmsSapStageItems.Where(x => x.CompanyId == companyId && lineIds.Contains(x.LineId)), cancellationToken);
                break;
            case WmsTipoTransaccion.EnvioSucursal:
                await ResetearTablaAsync(_contexto.WmsSapStageStores.Where(x => x.CompanyId == companyId && lineIds.Contains(x.LineId)), cancellationToken);
                break;
            case WmsTipoTransaccion.EnvioOrdenes:
                await ResetearTablaAsync(_contexto.WmsSapStageOrderHdrs.Where(x => x.CompanyId == companyId && lineIds.Contains(x.LineId)), cancellationToken);
                break;
            case WmsTipoTransaccion.EnvioIngresoAsn:
                await ResetearTablaAsync(_contexto.WmsSapStageInboundHdrs.Where(x => x.CompanyId == companyId && lineIds.Contains(x.LineId)), cancellationToken);
                break;
            default:
                throw new NotSupportedException($"WmsTransaccionService no soporta resetear el tipo '{tipo}'.");
        }
    }

    private async Task ResetearTablaAsync(IQueryable<WmsSapStageItem> filas, CancellationToken ct)
    {
        foreach (var fila in await filas.ToListAsync(ct)) { fila.Status = WmsSapStageStatus.Pendiente; fila.ErrorMsg = null; }
        await _contexto.SaveChangesAsync(ct);
    }

    private async Task ResetearTablaAsync(IQueryable<WmsSapStageStore> filas, CancellationToken ct)
    {
        foreach (var fila in await filas.ToListAsync(ct)) { fila.Status = WmsSapStageStatus.Pendiente; fila.ErrorMsg = null; }
        await _contexto.SaveChangesAsync(ct);
    }

    private async Task ResetearTablaAsync(IQueryable<WmsSapStageOrderHdr> filas, CancellationToken ct)
    {
        foreach (var fila in await filas.ToListAsync(ct)) { fila.Status = WmsSapStageStatus.Pendiente; fila.ErrorMsg = null; }
        await _contexto.SaveChangesAsync(ct);
    }

    private async Task ResetearTablaAsync(IQueryable<WmsSapStageInboundHdr> filas, CancellationToken ct)
    {
        foreach (var fila in await filas.ToListAsync(ct)) { fila.Status = WmsSapStageStatus.Pendiente; fila.ErrorMsg = null; }
        await _contexto.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Verificar que los tests pasan**

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsTransaccionServiceTests`
Esperado: 2/2 PASS.

- [ ] **Step 5: PageModel**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Pages.Transacciones;

public sealed class IndexModel : WmsPageModelBase
{
    private readonly IWmsTransaccionService _service;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IWmsTransaccionService service, ICurrentCompanyAccessor currentCompany)
    {
        _service = service;
        _currentCompany = currentCompany;
    }

    [BindProperty(SupportsGet = true)]
    public WmsTipoTransaccion Tipo { get; set; } = WmsTipoTransaccion.EnvioProducto;

    [BindProperty(SupportsGet = true)]
    public string? Estado { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Documento { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Page { get; set; } = 1;

    public WmsPagedResult<WmsTransaccionRow> Resultado { get; private set; } = new();

    public string TipoLabel => WmsTipoTransaccionInfo.Labels[Tipo];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Resultado = await _service.BuscarAsync(_currentCompany.CompanyId, new WmsTransaccionFiltro
        {
            Tipo = Tipo,
            Estado = Estado,
            Documento = Documento,
            Page = Page,
        }, ct);
    }

    public async Task<IActionResult> OnPostResetearAsync(WmsTipoTransaccion tipo, long[] lineIds, CancellationToken ct)
    {
        try
        {
            await _service.ResetearAsync(_currentCompany.CompanyId, tipo, lineIds, ct);
            SuccessMessage = $"{lineIds.Length} registro(s) reseteado(s) a Pendiente.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { tipo });
    }
}
```

- [ ] **Step 6: Página**

```cshtml
@page
@model Modulo.Wms.Pages.Transacciones.IndexModel
@using Modulo.Wms.Models
@{
    ViewData["Title"] = Model.TipoLabel;
}

<h1 class="h3 mb-3">@Model.TipoLabel</h1>

<form method="get" class="row g-2 mb-3">
    <input type="hidden" name="Tipo" value="@((int)Model.Tipo)" />
    <div class="col-auto">
        <select name="Estado" class="form-select form-select-sm">
            <option value="">Todos los estados</option>
            @foreach (var estado in new[] { "Pendiente", "Enviado", "ProcesadoWms", "ErrorWms" })
            {
                <option value="@estado" selected="@(Model.Estado == estado ? "selected" : null)">@estado</option>
            }
        </select>
    </div>
    <div class="col-auto">
        <input type="text" name="Documento" value="@Model.Documento" class="form-control form-control-sm" placeholder="Buscar documento..." />
    </div>
    <div class="col-auto">
        <button type="submit" class="btn btn-sm btn-primary">Buscar</button>
    </div>
</form>

<form method="post" asp-page-handler="Resetear">
    <input type="hidden" name="tipo" value="@((int)Model.Tipo)" />
    <table class="table table-sm">
        <thead><tr><th></th><th>Documento</th><th>Estado</th><th>Reintentos</th><th>Error</th><th>Creado</th><th>Sincronizado</th></tr></thead>
        <tbody>
        @foreach (var fila in Model.Resultado.Items)
        {
            <tr>
                <td><input type="checkbox" name="lineIds" value="@fila.LineId" /></td>
                <td>@fila.Documento</td>
                <td>@fila.Status</td>
                <td>@fila.RetryCount</td>
                <td>@fila.ErrorMsg</td>
                <td>@fila.CreatedAt.ToString("dd-MM HH:mm")</td>
                <td>@(fila.SyncedAt?.ToString("dd-MM HH:mm") ?? "—")</td>
            </tr>
        }
        </tbody>
    </table>
    <button type="submit" class="btn btn-sm btn-outline-secondary">Resetear seleccionados a Pendiente</button>
</form>

<nav>
    @for (var p = 1; p <= Model.Resultado.TotalPages; p++)
    {
        <a class="btn btn-sm @(p == Model.Resultado.Page ? "btn-primary" : "btn-outline-secondary")"
           asp-route-Tipo="@Model.Tipo" asp-route-Page="@p" asp-route-Estado="@Model.Estado" asp-route-Documento="@Model.Documento">@p</a>
    }
</nav>
```

- [ ] **Step 7: Registrar servicio y menú en `ModuloWms.cs`**

```csharp
        services.AddScoped<IWmsTransaccionService, WmsTransaccionService>();
```

```csharp
        yield return new MenuItemDefinition
        {
            Code = "transacciones",
            ParentCode = "raiz",
            Name = "Transacciones",
            PageRoute = "/wms/transacciones",
            Order = 1,
        };
```

- [ ] **Step 8: Verificación manual**

Navegar a `/wms/transacciones?Tipo=0` (Producto) y `/wms/transacciones?Tipo=2` (Órdenes); confirmar que filtros, paginación y el checkbox de reset funcionan sin excepción.

- [ ] **Step 9: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsTransaccionModels.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/IWmsTransaccionService.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsTransaccionService.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/Transacciones" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs" "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsTransaccionServiceTests.cs"
git commit -m "feat(wms): agregar pagina y servicio de Transacciones (Producto/Sucursal/Ordenes/IngresoAsn)"
```

---

### Task 11: Servicio + página Confirmaciones (`/wms/confirmaciones?tipo=`)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsConfirmacionModels.cs`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/IWmsConfirmacionService.cs`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsConfirmacionService.cs`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/Confirmaciones/Index.cshtml(.cs)`
- Modify: `ModuloWms.cs`
- Test: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsConfirmacionServiceTests.cs`

**Interfaces:**
- Consumes: `WmsOracleStageSlsh`/`WmsOracleStageSvsh` (Task 1), `WmsTipoTransaccion.ConfirmacionOrdenes/ConfirmacionIngreso` (Task 8).
- Produces: `interface IWmsConfirmacionService { Task<WmsPagedResult<WmsConfirmacionRow>> BuscarAsync(...); Task ResetearAsync(...); }`.

- [ ] **Step 1: Modelo de fila (agrupada por documento, no por línea)**

```csharp
namespace Modulo.Wms.Models;

public class WmsConfirmacionRow
{
    public string Documento { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int LineasCount { get; set; }
    public string? ErrorMsg { get; set; }
    public List<long> LineIds { get; set; } = new();
}
```

- [ ] **Step 2: Test que falla primero**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using Modulo.Wms.Tests.TestHelpers;
using PortalSaas.Abstractions.Contratos;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsConfirmacionServiceTests
{
    [Fact]
    public async Task BuscarAsync_TipoIngreso_AgrupaLineasPorShipmentNbr()
    {
        var companyId = Guid.NewGuid();
        var provider = WmsTestServiceProviderFactory.Crear(companyId, out var scopeFactory);

        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();

        var stage = new WmsOracleInboundStage { CompanyId = companyId, TipoDoc = "SVSH", Formato = WmsInboundFormato.Xml, NombreArchivo = "a.xml", HashArchivo = "hh1", Contenido = "" };
        contexto.WmsOracleInboundStages.Add(stage);
        await contexto.SaveChangesAsync();

        contexto.WmsOracleStageSvsh.AddRange(
            new WmsOracleStageSvsh { ParentId = stage.Id, shipment_nbr = "ASN9", item_part_a = "X", Status = WmsSvshStatus.ErrorSap, ErrorMsg = "falló" },
            new WmsOracleStageSvsh { ParentId = stage.Id, shipment_nbr = "ASN9", item_part_a = "Y", Status = WmsSvshStatus.ErrorSap, ErrorMsg = "falló" });
        await contexto.SaveChangesAsync();

        var service = new WmsConfirmacionService(contexto);
        var resultado = await service.BuscarAsync(companyId, new WmsConfirmacionFiltro { Tipo = WmsTipoTransaccion.ConfirmacionIngreso }, CancellationToken.None);

        Assert.Single(resultado.Items);
        Assert.Equal("ASN9", resultado.Items[0].Documento);
        Assert.Equal(2, resultado.Items[0].LineasCount);
    }
}
```

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsConfirmacionServiceTests`
Esperado: FAIL (no compila).

- [ ] **Step 3: Implementación**

```csharp
namespace Modulo.Wms.Models;

public class WmsConfirmacionFiltro
{
    public WmsTipoTransaccion Tipo { get; set; }
    public string? Estado { get; set; }
    public string? Documento { get; set; }
}
```

```csharp
namespace Modulo.Wms.Services;

public interface IWmsConfirmacionService
{
    Task<Models.WmsPagedResult<Models.WmsConfirmacionRow>> BuscarAsync(Guid companyId, Models.WmsConfirmacionFiltro filtro, CancellationToken cancellationToken);
    Task ResetearAsync(Guid companyId, Models.WmsTipoTransaccion tipo, string documento, CancellationToken cancellationToken);
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public class WmsConfirmacionService : IWmsConfirmacionService
{
    private readonly WmsDbContext _contexto;

    public WmsConfirmacionService(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<WmsPagedResult<WmsConfirmacionRow>> BuscarAsync(Guid companyId, WmsConfirmacionFiltro filtro, CancellationToken cancellationToken)
    {
        List<WmsConfirmacionRow> agrupado;

        if (filtro.Tipo == WmsTipoTransaccion.ConfirmacionOrdenes)
        {
            var filas = await (from f in _contexto.WmsOracleStageSlsh
                                join s in _contexto.WmsOracleInboundStages on f.ParentId equals s.Id
                                where s.CompanyId == companyId
                                select f).ToListAsync(cancellationToken);

            agrupado = filas.GroupBy(f => f.order_hdr_cust_field_4 ?? "(sin número)")
                .Select(g => new WmsConfirmacionRow
                {
                    Documento = g.Key,
                    Status = g.First().Status.ToString(),
                    LineasCount = g.Count(),
                    ErrorMsg = g.FirstOrDefault(x => x.ErrorMsg != null)?.ErrorMsg,
                    LineIds = g.Select(x => x.LineId).ToList(),
                }).ToList();
        }
        else if (filtro.Tipo == WmsTipoTransaccion.ConfirmacionIngreso)
        {
            var filas = await (from f in _contexto.WmsOracleStageSvsh
                                join s in _contexto.WmsOracleInboundStages on f.ParentId equals s.Id
                                where s.CompanyId == companyId
                                select f).ToListAsync(cancellationToken);

            agrupado = filas.GroupBy(f => f.shipment_nbr ?? "(sin número)")
                .Select(g => new WmsConfirmacionRow
                {
                    Documento = g.Key,
                    Status = g.First().Status.ToString(),
                    LineasCount = g.Count(),
                    ErrorMsg = g.FirstOrDefault(x => x.ErrorMsg != null)?.ErrorMsg,
                    LineIds = g.Select(x => x.LineId).ToList(),
                }).ToList();
        }
        else
        {
            throw new NotSupportedException($"WmsConfirmacionService no soporta el tipo '{filtro.Tipo}'.");
        }

        if (!string.IsNullOrWhiteSpace(filtro.Estado))
        {
            agrupado = agrupado.Where(r => r.Status == filtro.Estado).ToList();
        }
        if (!string.IsNullOrWhiteSpace(filtro.Documento))
        {
            agrupado = agrupado.Where(r => r.Documento.Contains(filtro.Documento, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return new WmsPagedResult<WmsConfirmacionRow> { Items = agrupado, TotalCount = agrupado.Count, Page = 1, PageSize = Math.Max(agrupado.Count, 1) };
    }

    public async Task ResetearAsync(Guid companyId, WmsTipoTransaccion tipo, string documento, CancellationToken cancellationToken)
    {
        if (tipo == WmsTipoTransaccion.ConfirmacionOrdenes)
        {
            var filas = await (from f in _contexto.WmsOracleStageSlsh
                                join s in _contexto.WmsOracleInboundStages on f.ParentId equals s.Id
                                where s.CompanyId == companyId && f.order_hdr_cust_field_4 == documento
                                select f).ToListAsync(cancellationToken);
            foreach (var fila in filas) { fila.Status = WmsSlshStatus.Pendiente; fila.ErrorMsg = null; }
        }
        else if (tipo == WmsTipoTransaccion.ConfirmacionIngreso)
        {
            var filas = await (from f in _contexto.WmsOracleStageSvsh
                                join s in _contexto.WmsOracleInboundStages on f.ParentId equals s.Id
                                where s.CompanyId == companyId && f.shipment_nbr == documento
                                select f).ToListAsync(cancellationToken);
            foreach (var fila in filas) { fila.Status = WmsSvshStatus.Pendiente; fila.ErrorMsg = null; }
        }
        else
        {
            throw new NotSupportedException($"WmsConfirmacionService no soporta resetear el tipo '{tipo}'.");
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Verificar que el test pasa**

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsConfirmacionServiceTests`
Esperado: PASS.

- [ ] **Step 5: PageModel + página**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Pages.Confirmaciones;

public sealed class IndexModel : WmsPageModelBase
{
    private readonly IWmsConfirmacionService _service;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IWmsConfirmacionService service, ICurrentCompanyAccessor currentCompany)
    {
        _service = service;
        _currentCompany = currentCompany;
    }

    [BindProperty(SupportsGet = true)]
    public WmsTipoTransaccion Tipo { get; set; } = WmsTipoTransaccion.ConfirmacionOrdenes;

    [BindProperty(SupportsGet = true)]
    public string? Estado { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Documento { get; set; }

    public WmsPagedResult<WmsConfirmacionRow> Resultado { get; private set; } = new();

    public string TipoLabel => WmsTipoTransaccionInfo.Labels[Tipo];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Resultado = await _service.BuscarAsync(_currentCompany.CompanyId, new WmsConfirmacionFiltro { Tipo = Tipo, Estado = Estado, Documento = Documento }, ct);
    }

    public async Task<IActionResult> OnPostResetearAsync(WmsTipoTransaccion tipo, string documento, CancellationToken ct)
    {
        try
        {
            await _service.ResetearAsync(_currentCompany.CompanyId, tipo, documento, ct);
            SuccessMessage = $"Documento {documento} reseteado a Pendiente.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { tipo });
    }
}
```

```cshtml
@page
@model Modulo.Wms.Pages.Confirmaciones.IndexModel
@{
    ViewData["Title"] = Model.TipoLabel;
}

<h1 class="h3 mb-3">@Model.TipoLabel</h1>

<div class="mb-3">
    <a class="btn btn-sm @(Model.Tipo == Modulo.Wms.Models.WmsTipoTransaccion.ConfirmacionOrdenes ? "btn-primary" : "btn-outline-secondary")" asp-route-Tipo="ConfirmacionOrdenes">Órdenes</a>
    <a class="btn btn-sm @(Model.Tipo == Modulo.Wms.Models.WmsTipoTransaccion.ConfirmacionIngreso ? "btn-primary" : "btn-outline-secondary")" asp-route-Tipo="ConfirmacionIngreso">Ingreso</a>
</div>

<table class="table table-sm">
    <thead><tr><th>Documento</th><th>Estado</th><th>Líneas</th><th>Error</th><th></th></tr></thead>
    <tbody>
    @foreach (var fila in Model.Resultado.Items)
    {
        <tr>
            <td>@fila.Documento</td>
            <td>@fila.Status</td>
            <td>@fila.LineasCount</td>
            <td>@fila.ErrorMsg</td>
            <td>
                <form method="post" asp-page-handler="Resetear" class="d-inline">
                    <input type="hidden" name="tipo" value="@((int)Model.Tipo)" />
                    <input type="hidden" name="documento" value="@fila.Documento" />
                    <button type="submit" class="btn btn-sm btn-outline-secondary">Resetear</button>
                </form>
            </td>
        </tr>
    }
    </tbody>
</table>
```

- [ ] **Step 6: Registrar servicio y menú en `ModuloWms.cs`**

```csharp
        services.AddScoped<IWmsConfirmacionService, WmsConfirmacionService>();
```

```csharp
        yield return new MenuItemDefinition
        {
            Code = "confirmaciones",
            ParentCode = "raiz",
            Name = "Confirmaciones",
            PageRoute = "/wms/confirmaciones",
            Order = 2,
        };
```

- [ ] **Step 7: Verificación manual**

Navegar a `/wms/confirmaciones?Tipo=ConfirmacionOrdenes` y `?Tipo=ConfirmacionIngreso`; confirmar agrupado por documento y que "Resetear" no tumba la página.

- [ ] **Step 8: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsConfirmacionModels.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/IWmsConfirmacionService.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsConfirmacionService.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/Confirmaciones" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs" "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsConfirmacionServiceTests.cs"
git commit -m "feat(wms): agregar pagina y servicio de Confirmaciones (Ordenes/Ingreso)"
```

---

### Task 12: Página Archivos WMS (`/wms/archivos`)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/IWmsArchivoService.cs`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsArchivoService.cs`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/ArchivosWms/Index.cshtml(.cs)`
- Modify: `ModuloWms.cs`
- Test: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsArchivoServiceTests.cs`

**Interfaces:**
- Consumes: `WmsOracleInboundStage` (modelo ya existente).
- Produces: `interface IWmsArchivoService { Task<List<WmsOracleInboundStage>> ListarAsync(Guid companyId, string? tipoDoc, string? estado, CancellationToken ct); Task ReintentarAsync(Guid companyId, long id, CancellationToken ct); }`.

- [ ] **Step 1: Test que falla primero**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using Modulo.Wms.Tests.TestHelpers;
using PortalSaas.Abstractions.Contratos;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsArchivoServiceTests
{
    [Fact]
    public async Task ReintentarAsync_ArchivoEnError_VuelveAPendienteYLimpiaMensaje()
    {
        var companyId = Guid.NewGuid();
        var provider = WmsTestServiceProviderFactory.Crear(companyId, out var scopeFactory);

        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();

        var stage = new WmsOracleInboundStage
        {
            CompanyId = companyId, TipoDoc = "SLSH", Formato = WmsInboundFormato.Xml,
            NombreArchivo = "err.xml", HashArchivo = "hx", Contenido = "<Message></Message>",
            Estado = WmsInboundEstado.ErrorEstructura, MensajeError = "falló",
        };
        contexto.WmsOracleInboundStages.Add(stage);
        await contexto.SaveChangesAsync();

        var service = new WmsArchivoService(contexto);
        await service.ReintentarAsync(companyId, stage.Id, CancellationToken.None);

        var actualizado = await contexto.WmsOracleInboundStages.SingleAsync();
        Assert.Equal(WmsInboundEstado.Pendiente, actualizado.Estado);
        Assert.Null(actualizado.MensajeError);
    }
}
```

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsArchivoServiceTests`
Esperado: FAIL (no compila).

- [ ] **Step 2: Implementación**

```csharp
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public interface IWmsArchivoService
{
    Task<List<WmsOracleInboundStage>> ListarAsync(Guid companyId, string? tipoDoc, string? estado, CancellationToken cancellationToken);
    Task ReintentarAsync(Guid companyId, long id, CancellationToken cancellationToken);
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public class WmsArchivoService : IWmsArchivoService
{
    private readonly WmsDbContext _contexto;

    public WmsArchivoService(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<List<WmsOracleInboundStage>> ListarAsync(Guid companyId, string? tipoDoc, string? estado, CancellationToken cancellationToken)
    {
        var query = _contexto.WmsOracleInboundStages.Where(x => x.CompanyId == companyId);

        if (!string.IsNullOrWhiteSpace(tipoDoc))
        {
            query = query.Where(x => x.TipoDoc == tipoDoc);
        }
        if (!string.IsNullOrWhiteSpace(estado) && Enum.TryParse<WmsInboundEstado>(estado, out var estadoEnum))
        {
            query = query.Where(x => x.Estado == estadoEnum);
        }

        return await query.OrderByDescending(x => x.InsertedAt).Take(200).ToListAsync(cancellationToken);
    }

    public async Task ReintentarAsync(Guid companyId, long id, CancellationToken cancellationToken)
    {
        var fila = await _contexto.WmsOracleInboundStages.SingleAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);
        fila.Estado = WmsInboundEstado.Pendiente;
        fila.MensajeError = null;
        await _contexto.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 3: Verificar que el test pasa**

Ejecutar: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests" --filter WmsArchivoServiceTests`
Esperado: PASS.

- [ ] **Step 4: PageModel + página**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Pages.ArchivosWms;

public sealed class IndexModel : WmsPageModelBase
{
    private readonly IWmsArchivoService _service;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IWmsArchivoService service, ICurrentCompanyAccessor currentCompany)
    {
        _service = service;
        _currentCompany = currentCompany;
    }

    [BindProperty(SupportsGet = true)]
    public string? TipoDoc { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Estado { get; set; }

    public List<WmsOracleInboundStage> Archivos { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Archivos = await _service.ListarAsync(_currentCompany.CompanyId, TipoDoc, Estado, ct);
    }

    public async Task<IActionResult> OnPostReintentarAsync(long id, CancellationToken ct)
    {
        try
        {
            await _service.ReintentarAsync(_currentCompany.CompanyId, id, ct);
            SuccessMessage = "Archivo vuelto a Pendiente.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }
}
```

```cshtml
@page
@model Modulo.Wms.Pages.ArchivosWms.IndexModel
@{
    ViewData["Title"] = "Archivos WMS";
}

<h1 class="h3 mb-3">Archivos WMS</h1>

<form method="get" class="row g-2 mb-3">
    <div class="col-auto">
        <select name="TipoDoc" class="form-select form-select-sm">
            <option value="">Todos</option>
            @foreach (var tipo in new[] { "SLSH", "SVSH" })
            {
                <option value="@tipo" selected="@(Model.TipoDoc == tipo ? "selected" : null)">@tipo</option>
            }
        </select>
    </div>
    <div class="col-auto"><button type="submit" class="btn btn-sm btn-primary">Filtrar</button></div>
</form>

<table class="table table-sm">
    <thead><tr><th>Archivo</th><th>Tipo</th><th>Estado</th><th>Intentos</th><th>Error</th><th>Insertado</th><th></th></tr></thead>
    <tbody>
    @foreach (var archivo in Model.Archivos)
    {
        <tr>
            <td>@archivo.NombreArchivo</td>
            <td>@archivo.TipoDoc</td>
            <td>@archivo.Estado</td>
            <td>@archivo.Intentos</td>
            <td>@archivo.MensajeError</td>
            <td>@archivo.InsertedAt.ToString("dd-MM HH:mm")</td>
            <td>
                <form method="post" asp-page-handler="Reintentar" class="d-inline">
                    <input type="hidden" name="id" value="@archivo.Id" />
                    <button type="submit" class="btn btn-sm btn-outline-secondary">Reintentar</button>
                </form>
            </td>
        </tr>
    }
    </tbody>
</table>
```

- [ ] **Step 5: Registrar servicio y menú en `ModuloWms.cs`**

```csharp
        services.AddScoped<IWmsArchivoService, WmsArchivoService>();
```

```csharp
        yield return new MenuItemDefinition
        {
            Code = "archivos-wms",
            ParentCode = "raiz",
            Name = "Archivos WMS",
            PageRoute = "/wms/archivos-wms",
            Order = 3,
        };
```

- [ ] **Step 6: Verificación manual**

Navegar a `/wms/archivos-wms`; confirmar listado y que "Reintentar" sobre una fila en error la vuelve a Pendiente.

- [ ] **Step 7: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/IWmsArchivoService.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsArchivoService.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/ArchivosWms" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs" "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsArchivoServiceTests.cs"
git commit -m "feat(wms): agregar pagina y servicio de Archivos WMS"
```

---

### Task 13: Página Estado del Servicio (`/wms/estado-servicio`)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/EstadoServicio/Index.cshtml(.cs)`
- Modify: `ModuloWms.cs` (la entrada de menú ya existe, línea 57-64 — no requiere cambio)

**Interfaces:**
- Consumes: `WmsDbContext.ServiceHeartbeats` directamente (grilla simple de solo lectura, no amerita servicio propio).

- [ ] **Step 1: PageModel**

```csharp
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Pages.EstadoServicio;

public sealed class IndexModel : WmsPageModelBase
{
    private readonly WmsDbContext _contexto;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(WmsDbContext contexto, ICurrentCompanyAccessor currentCompany)
    {
        _contexto = contexto;
        _currentCompany = currentCompany;
    }

    public List<WmsServiceHeartbeat> Heartbeats { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Heartbeats = await _contexto.ServiceHeartbeats
            .Where(h => h.CompanyId == _currentCompany.CompanyId)
            .OrderBy(h => h.ProcessorKey)
            .ToListAsync(ct);
    }
}
```

- [ ] **Step 2: Página**

```cshtml
@page
@model Modulo.Wms.Pages.EstadoServicio.IndexModel
@{
    ViewData["Title"] = "Estado del Servicio";
}

<h1 class="h3 mb-3">Estado del Servicio</h1>

@if (Model.Heartbeats.Count == 0)
{
    <p class="text-muted">Todavía no hay ciclos registrados. Los procesos del plugin reportan su estado en cada ciclo (cada 15-60s según el proceso).</p>
}

<table class="table table-sm">
    <thead><tr><th>Proceso</th><th>Estado</th><th>Última ejecución</th><th>Último error</th></tr></thead>
    <tbody>
    @foreach (var hb in Model.Heartbeats)
    {
        <tr>
            <td>@hb.ProcessorKey</td>
            <td>
                <span class="badge @(hb.Status == "OK" ? "bg-success" : hb.Status == "ERROR" ? "bg-danger" : "bg-secondary")">@hb.Status</span>
            </td>
            <td>@(hb.LastRunAt?.ToString("dd-MM HH:mm:ss") ?? "—")</td>
            <td>@hb.LastError</td>
        </tr>
    }
    </tbody>
</table>
```

- [ ] **Step 3: Verificación manual**

Navegar a `/wms/estado-servicio` (ruta ya existía en el menú, hoy 404). Confirmar que carga sin excepción, muestra los 4 `ProcessorKey` del plugin una vez que los `BackgroundService` corrieron al menos un ciclo (Task 6).

- [ ] **Step 4: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/EstadoServicio"
git commit -m "feat(wms): agregar pagina Estado del Servicio (cablea entrada de menu existente)"
```

---

### Task 14: Suite completa + revisión final

**Files:** ninguno nuevo — verificación de cierre.

- [ ] **Step 1: Correr toda la suite de tests del plugin**

Run: `dotnet test "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests"`
Expected: todos los tests (los preexistentes + los ~9 archivos de test nuevos de este plan) en PASS, 0 fallas.

- [ ] **Step 2: Build completo del plugin y del Host**

Run: `dotnet build "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Modulo.Wms.csproj"` y `dotnet build "Portal SaaS - Core/src/PortalSaas.Host/PortalSaas.Host.csproj"`
Expected: Build succeeded, 0 errores, 0 warnings nuevos.

- [ ] **Step 3: Verificación manual de punta a punta**

Con el Host corriendo y una compañía WMS activa: navegar en orden a `/wms/dashboard`, `/wms/transacciones`, `/wms/confirmaciones`, `/wms/archivos-wms`, `/wms/estado-servicio` — confirmar que el menú "Integración WMS" muestra las 7 entradas en el orden Dashboard → Transacciones → Confirmaciones → Archivos WMS → Mapeo de Campos → Configuración del Servicio → Estado del Servicio, y que ninguna pantalla lanza excepción sin datos.

- [ ] **Step 4: Commit final (si quedó algo suelto)**

```bash
git status
git add -A
git commit -m "chore(wms): cierre de visualizacion de transacciones WMS" --allow-empty
```

---

## Self-Review

**Spec coverage:**
- Backend SVSH (modelo, parser, XML parser, reader, migraciones) → Tasks 1-5, 7. ✓
- Heartbeat → Task 6. ✓
- Dashboard → Tasks 8-9. ✓
- Transacciones (4 tipos Envío) → Task 10. ✓
- Confirmaciones (Órdenes + Ingreso) → Task 11. ✓
- Archivos WMS → Task 12. ✓
- Estado del Servicio → Task 13. ✓
- Menú → distribuido en Tasks 9-13 (una entrada por página). ✓
- Testing (spec, sección "Testing") → cada Task de backend/servicio incluye su test TDD; páginas Razor se verifican a mano (Step de verificación manual en cada Task 9-13), tal como el spec declaró explícitamente fuera de alcance la UI automatizada. ✓

**Placeholder scan:** sin "TBD"/"TODO"/"similar a la Task N" — cada Task tiene código completo, incluso los métodos sobrecargados de reset que parecen repetitivos (Task 10 Step 3) están escritos en su totalidad porque C# no permite genéricos sobre distintos DbSet sin reflexión, que sería sobre-ingeniería para 4 tablas.

**Type consistency:** `WmsTipoTransaccion` (Task 8) se reutiliza sin redefinir en Tasks 10/11/13. `IWmsServiceHeartbeatRecorder.RecordAsync` (Task 6) usa la misma firma en los 4 wirings del Step 5. `WmsPagedResult<T>`/`WmsTransaccionRow`/`WmsConfirmacionRow` (Tasks 10-11) no chocan de nombre con `WmsDashboardResumen`/`WmsEstadisticaTipo` (Task 8).
