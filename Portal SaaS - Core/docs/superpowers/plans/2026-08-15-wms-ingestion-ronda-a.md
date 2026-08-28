# Migración Wms — Ronda A (Ingestión + Staging) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recibir XML de confirmación de traslado (`SLSH`) desde Oracle WMS vía un endpoint autenticado con el esquema `ExternalApiKey` (Ronda 0), guardarlo en staging, y aplanarlo dinámicamente a filas de negocio — replicando fielmente el patrón del sistema legado `WmsSapIntegration.Service`.

**Architecture:** Tablas nuevas en el `WmsDbContext` propio del plugin `Modulo.Wms` (no en `PortalSaas.Core`, siguiendo la arquitectura de plugins). Un endpoint Minimal API en `PortalSaas.Host` llama a una interfaz nueva de `PortalSaas.Abstractions` que el plugin implementa y registra (primer caso de este patrón inverso host↔plugin). Un `BackgroundService` nuevo en el plugin hace el aplanado, mismo patrón que `IntegrationSyncHostedService`.

**Tech Stack:** .NET 8, ASP.NET Core Minimal API, EF Core 8 (motor dual Postgres/SqlServer resuelto en runtime por `IExternalDatabaseConnectionService`), `System.Xml.Linq`, xUnit + EF Core InMemory.

## Global Constraints

- Ninguna columna de `WmsDbContext` usa `HasColumnType` específico de un solo motor — el mismo modelo debe generar migraciones válidas para Postgres y SQL Server (regla dura documentada en el doc-comment de `WmsDbContext.cs`).
- Migraciones del plugin viven en proyectos separados: `Modulo.Wms.Migrations.Postgres` y `Modulo.Wms.Migrations.SqlServer`, cada uno con su propio `DesignTimeDbContextFactory.cs` — generar migración con `dotnet ef migrations add <Nombre> -o Migrations` ejecutado DESDE cada uno de esos proyectos (no desde `Modulo.Wms` ni desde `PortalSaas.Host`).
- Nombres de tabla/columna en inglés, snake_case, consistente con `wms_oracle_field_mappings`/`wms_oracle_service_configs`/`wms_oracle_service_heartbeats` ya existentes.
- `IWmsInboundIngestionService` vive en `PortalSaas.Abstractions.Contratos` — el Host la resuelve vía `sp.GetRequiredService<IWmsInboundIngestionService>()` sin referenciar `Modulo.Wms.dll` directamente (arquitectura de plugin dinámica).
- El endpoint Minimal API sigue el patrón de `LicensingEndpoints.cs` (`src/PortalSaas.Host/Licenciamiento/`): método de extensión `MapXxxEndpoints(this WebApplication app)`, handler estático con parámetros inyectados por DI, mapeado en `Program.cs` después de `app.MapRazorPages()`.
- `[Authorize(AuthenticationSchemes = "ExternalApiKey")]` en el endpoint — la `CompanyId` se lee de `HttpContext.User` (claim `"CompanyId"`, ya poblado por `ApiKeyAuthenticationHandler` de la Ronda 0), nunca del payload.
- Formato JSON/TXT: la estructura (`Formato` enum) debe existir, pero solo el parser XML se implementa en esta ronda — cualquier intento con otro formato responde 400 explícito, no se inserta en staging.
- Tests: xUnit + `UseInMemoryDatabase(Guid.NewGuid().ToString())`, sin mocking framework, siguiendo el patrón de `tests/PortalSaas.Core.Tests/` (este plan usa un proyecto de test análogo dentro de `Modulo.Wms`, ver Task 1 Step 1 para confirmarlo o crearlo).
- **Riesgo conocido de datos SAP/HANA**: algunos nombres de columna en sistemas legados de este tipo pueden traer espacios al final (padding) en el DDL real de producción, aunque no se detectaron en los archivos DDL revisados para este plan (`WMS_Suite/db/provisioning/hana/004_stg_wms_ihth.sql`, `005_stg_wms_slsh.sql`, `006_stg_wms_svsh.sql`, `001_int_wms_stage.sql` — los cuatro se revisaron explícitamente, ninguno tiene espacios). Antes de dar por buena la Tarea 1, volver a `grep` el DDL real contra el que se generó el modelo (`grep -oP '"[^"]*"' <archivo.sql> | awk -F'"' '{ if ($2 ~ /^ | $/) print }'`) y, si aparece alguno, usar `.Trim()` al asignar el valor en `WmsSlshXmlParser` (Task 4) en vez de cambiar el nombre de columna en sí (el nombre de columna del lado C#/BD debe quedar limpio de espacios siempre, sea cual sea el dato crudo que traiga el XML).

---

## File Structure

**Nuevos archivos en `Modulo.Wms` (`Portal SaaS - Plugins/Modulo.Wms`):**
- `src/Modulo.Wms/Models/WmsOracleInboundStage.cs`
- `src/Modulo.Wms/Models/WmsOracleStageSlsh.cs`
- `src/Modulo.Wms/Services/WmsInboundIngestionService.cs`
- `src/Modulo.Wms/Services/WmsSlshXmlParser.cs` (parseo XML puro, testeable sin BD)
- `src/Modulo.Wms/Services/WmsSlshStageParser.cs` (`BackgroundService`)
- Migraciones nuevas en `Modulo.Wms.Migrations.Postgres/Migrations/` y `Modulo.Wms.Migrations.SqlServer/Migrations/`
- Tests: `tests/Modulo.Wms.Tests/Services/WmsInboundIngestionServiceTests.cs`, `tests/Modulo.Wms.Tests/Services/WmsSlshXmlParserTests.cs` (ruta exacta del proyecto de test se confirma en Task 1 Step 1)

**Modificados en `Modulo.Wms`:**
- `src/Modulo.Wms/Data/WmsDbContext.cs` — nuevos `DbSet` + Fluent API.
- `src/Modulo.Wms/ModuloWms.cs` — registrar `IWmsInboundIngestionService` y `WmsSlshStageParser`.

**Nuevos archivos en `Portal SaaS - Core`:**
- `src/PortalSaas.Abstractions/Contratos/IWmsInboundIngestionService.cs`
- `src/PortalSaas.Host/Wms/WmsInboundEndpoints.cs`
- `src/PortalSaas.Host/Wms/SecureXmlHelper.cs` (parseo anti-XXE, reutilizable)

**Modificados en `Portal SaaS - Core`:**
- `src/PortalSaas.Host/Program.cs` — mapear `app.MapWmsInboundEndpoints()`.

---

### Task 1: Entidades de staging + migraciones

**Files:**
- Create: `src/Modulo.Wms/Models/WmsOracleInboundStage.cs`
- Create: `src/Modulo.Wms/Models/WmsOracleStageSlsh.cs`
- Modify: `src/Modulo.Wms/Data/WmsDbContext.cs`
- Migraciones nuevas en `Modulo.Wms.Migrations.Postgres/Migrations/` y `Modulo.Wms.Migrations.SqlServer/Migrations/`

**Interfaces:**
- Consumes: nada de tareas anteriores.
- Produces: `WmsOracleInboundStage { Id, CompanyId, TipoDoc, Formato, NombreArchivo, HashArchivo, Contenido, Estado, Intentos, MensajeError, SapDocEntry, InsertedAt, ProcessedAt }`; `WmsOracleStageSlsh { LineId, ParentId, Status, ErrorMsg, RetryCount, SapDocEntry, ... (columnas de negocio abajo) }` — consumidos por `WmsInboundIngestionService` (Task 2) y `WmsSlshStageParser` (Task 4).

- [ ] **Step 1: Confirmar la ubicación del proyecto de tests del plugin**

Run: `find "Portal SaaS - Plugins/Modulo.Wms" -iname "*.Tests.csproj"` (o `Get-ChildItem -Recurse -Filter "*.Tests.csproj"` en PowerShell, desde la raíz de `Portal SaaS - Plugins/Modulo.Wms`)
Expected: si existe un proyecto de test (ej. `tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj`), usar esa ruta para todos los tests de este plan. Si NO existe ninguno, créalo antes de continuar: `dotnet new xunit -o tests/Modulo.Wms.Tests -n Modulo.Wms.Tests`, agrégalo a la solución del plugin (`dotnet sln add tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj` si hay un `.sln`), y agrégale `ProjectReference` a `src/Modulo.Wms/Modulo.Wms.csproj` más el paquete `Microsoft.EntityFrameworkCore.InMemory` (misma versión de EF Core que usa `Modulo.Wms.csproj` — revisa ese `.csproj` para la versión exacta). Ajusta las rutas de los Steps siguientes (y de las Tasks 2-4) según lo que encuentres aquí.

- [ ] **Step 1b: Verificar que el DDL real no tenga columnas con espacios al final**

Run: `grep -oP '"[^"]*"' "C:\PROYECTOS\WMS_Suite\db\provisioning\hana\005_stg_wms_slsh.sql" | awk -F'"' '{ if ($2 ~ /^ | $/) print "["$2"]" }'`
Expected: sin salida (ningún identificador entre comillas con espacio al inicio o al final). Si aparece alguno, anota el nombre exacto con espacio y usa la versión SIN espacio como nombre de propiedad C#/columna en los Steps 3-4 de este task — el espacio es un defecto del dato de origen, no algo que deba propagarse al modelo nuevo.

- [ ] **Step 2: Crear `WmsOracleInboundStage`**

```csharp
namespace Modulo.Wms.Models;

public enum WmsInboundFormato { Xml, Json, Txt }
public enum WmsInboundEstado { Pendiente, Aplanado, ErrorEstructura, ErrorStaging }

public class WmsOracleInboundStage
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public string TipoDoc { get; set; } = string.Empty;
    public WmsInboundFormato Formato { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    public string HashArchivo { get; set; } = string.Empty;
    public string Contenido { get; set; } = string.Empty;
    public WmsInboundEstado Estado { get; set; } = WmsInboundEstado.Pendiente;
    public int Intentos { get; set; }
    public string? MensajeError { get; set; }
    public string? SapDocEntry { get; set; }
    public DateTimeOffset InsertedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
}
```

- [ ] **Step 3: Crear `WmsOracleStageSlsh`**

Réplica 1:1 de las columnas de negocio de `STG_WMS_SLSH` (confirmadas contra `WMS_Suite/db/provisioning/hana/005_stg_wms_slsh.sql`), todas `string?` salvo las de metadata de staging:

```csharp
namespace Modulo.Wms.Models;

public enum WmsSlshStatus { Pendiente, ProcesadoSap, ErrorSap }

public class WmsOracleStageSlsh
{
    public long LineId { get; set; }
    public long ParentId { get; set; }
    public WmsSlshStatus Status { get; set; } = WmsSlshStatus.Pendiente;
    public string? ErrorMsg { get; set; }
    public int RetryCount { get; set; }
    public int? SapDocEntry { get; set; }
    public int? SapObject { get; set; }

    // Columnas de negocio (estándar "shipped_load" de Oracle WMS Cloud) — todas nullable,
    // nombres idénticos al DDL real (ya en snake_case, sin traducir).
    public string? DocumentVersion { get; set; }
    public string? OriginSystem { get; set; }
    public string? ClientEnvCode { get; set; }
    public string? ParentCompanyCode { get; set; }
    public string? Entity { get; set; }
    public string? TimeStamp { get; set; }
    public string? MessageId { get; set; }
    public string? facility_code { get; set; }
    public string? company_code { get; set; }
    public string? action_code { get; set; }
    public string? load_type { get; set; }
    public string? load_manifest_nbr { get; set; }
    public string? trailer_nbr { get; set; }
    public string? trailer_type { get; set; }
    public string? driver { get; set; }
    public string? seal_nbr { get; set; }
    public string? pro_nbr { get; set; }
    public string? route_nbr { get; set; }
    public string? freight_class { get; set; }
    public string? hdr_bol_nbr { get; set; }
    public string? total_nbr_of_oblpns { get; set; }
    public string? total_weight { get; set; }
    public string? total_volume { get; set; }
    public string? total_shipping_charge { get; set; }
    public string? ship_date { get; set; }
    public string? sched_delivery_date { get; set; }
    public string? carrier_code { get; set; }
    public string? externally_planned_load_nbr { get; set; }
    public string? ship_date_time { get; set; }
    public string? sched_delivery_date_time { get; set; }
    public string? line_nbr { get; set; }
    public string? seq_nbr { get; set; }
    public string? stop_shipment_nbr { get; set; }
    public string? stop_bol_nbr { get; set; }
    public string? stop_nbr_of_oblpns { get; set; }
    public string? stop_weight { get; set; }
    public string? stop_volume { get; set; }
    public string? stop_shipping_charge { get; set; }
    public string? shipto_facility_code { get; set; }
    public string? shipto_name { get; set; }
    public string? shipto_addr { get; set; }
    public string? shipto_addr2 { get; set; }
    public string? shipto_addr3 { get; set; }
    public string? shipto_city { get; set; }
    public string? shipto_state { get; set; }
    public string? shipto_zip { get; set; }
    public string? shipto_country { get; set; }
    public string? shipto_phone_nbr { get; set; }
    public string? shipto_email { get; set; }
    public string? shipto_contact { get; set; }
    public string? dest_facility_code { get; set; }
    public string? cust_name { get; set; }
    public string? cust_addr { get; set; }
    public string? cust_addr2 { get; set; }
    public string? cust_addr3 { get; set; }
    public string? cust_city { get; set; }
    public string? cust_state { get; set; }
    public string? cust_zip { get; set; }
    public string? cust_country { get; set; }
    public string? cust_phone_nbr { get; set; }
    public string? cust_email { get; set; }
    public string? cust_contact { get; set; }
    public string? cust_nbr { get; set; }
    public string? order_nbr { get; set; }
    public string? ord_date { get; set; }
    public string? exp_date { get; set; }
    public string? req_ship_date { get; set; }
    public string? start_ship_date { get; set; }
    public string? stop_ship_date { get; set; }
    public string? host_allocation_nbr { get; set; }
    public string? customer_po_nbr { get; set; }
    public string? sales_order_nbr { get; set; }
    public string? sales_channel { get; set; }
    public string? dest_dept_nbr { get; set; }
    public string? order_hdr_cust_field_1 { get; set; }
    public string? order_hdr_cust_field_2 { get; set; }
    public string? order_hdr_cust_field_3 { get; set; }
    public string? order_hdr_cust_field_4 { get; set; }
    public string? order_hdr_cust_field_5 { get; set; }
    public string? order_seq_nbr { get; set; }
    public string? order_dtl_cust_field_1 { get; set; }
    public string? order_dtl_cust_field_2 { get; set; }
    public string? order_dtl_cust_field_3 { get; set; }
    public string? order_dtl_cust_field_4 { get; set; }
    public string? order_dtl_cust_field_5 { get; set; }
    public string? ob_lpn_nbr { get; set; }
    public string? item_alternate_code { get; set; }
    public string? item_part_a { get; set; }
    public string? item_part_b { get; set; }
    public string? item_part_c { get; set; }
    public string? item_part_d { get; set; }
    public string? item_part_e { get; set; }
    public string? item_part_f { get; set; }
    public string? pre_pack_code { get; set; }
    public string? pre_pack_ratio { get; set; }
    public string? pre_pack_ratio_seq { get; set; }
    public string? pre_pack_total_units { get; set; }
    public string? invn_attr_a { get; set; }
    public string? invn_attr_b { get; set; }
    public string? invn_attr_c { get; set; }
    public string? hazmat { get; set; }
    public string? shipped_uom { get; set; }
    public string? shipped_qty { get; set; }
    public string? pallet_nbr { get; set; }
    public string? dest_company_code { get; set; }
    public string? batch_nbr { get; set; }
    public string? expiry_date { get; set; }
    public string? tracking_nbr { get; set; }
    public string? master_tracking_nbr { get; set; }
    public string? package_type { get; set; }
    public string? payment_method { get; set; }
    public string? carrier_account_nbr { get; set; }
    public string? ship_via_code { get; set; }
    public string? ob_lpn_weight { get; set; }
    public string? ob_lpn_volume { get; set; }
    public string? ob_lpn_shipping_charge { get; set; }
    public string? ob_lpn_type { get; set; }
    public string? ob_lpn_asset_nbr { get; set; }
    public string? ob_lpn_asset_seal_nbr { get; set; }
    public string? serial_nbr { get; set; }
    public string? customer_po_type { get; set; }
    public string? customer_vendor_code { get; set; }
    public string? order_hdr_cust_date_1 { get; set; }
    public string? order_hdr_cust_date_2 { get; set; }
    public string? order_hdr_cust_date_3 { get; set; }
    public string? order_hdr_cust_date_4 { get; set; }
    public string? order_hdr_cust_date_5 { get; set; }
    public string? order_hdr_cust_number_1 { get; set; }
    public string? order_hdr_cust_number_2 { get; set; }
    public string? order_hdr_cust_number_3 { get; set; }
    public string? order_hdr_cust_number_4 { get; set; }
    public string? order_hdr_cust_number_5 { get; set; }
    public string? order_hdr_cust_decimal_1 { get; set; }
    public string? order_hdr_cust_decimal_2 { get; set; }
    public string? order_hdr_cust_decimal_3 { get; set; }
    public string? order_hdr_cust_decimal_4 { get; set; }
    public string? order_hdr_cust_decimal_5 { get; set; }
    public string? order_hdr_cust_short_text_1 { get; set; }
    public string? order_hdr_cust_short_text_2 { get; set; }
    public string? order_hdr_cust_short_text_3 { get; set; }
    public string? order_hdr_cust_short_text_4 { get; set; }
    public string? order_hdr_cust_short_text_5 { get; set; }
    public string? order_hdr_cust_short_text_6 { get; set; }
    public string? order_hdr_cust_short_text_7 { get; set; }
    public string? order_hdr_cust_short_text_8 { get; set; }
    public string? order_hdr_cust_short_text_9 { get; set; }
    public string? order_hdr_cust_short_text_10 { get; set; }
    public string? order_hdr_cust_short_text_11 { get; set; }
    public string? order_hdr_cust_short_text_12 { get; set; }
    public string? order_hdr_cust_long_text_1 { get; set; }
    public string? order_hdr_cust_long_text_2 { get; set; }
    public string? order_hdr_cust_long_text_3 { get; set; }
    public string? order_dtl_cust_date_1 { get; set; }
    public string? order_dtl_cust_date_2 { get; set; }
    public string? order_dtl_cust_date_3 { get; set; }
    public string? order_dtl_cust_date_4 { get; set; }
    public string? order_dtl_cust_date_5 { get; set; }
    public string? order_dtl_cust_number_1 { get; set; }
    public string? order_dtl_cust_number_2 { get; set; }
    public string? order_dtl_cust_number_3 { get; set; }
    public string? order_dtl_cust_number_4 { get; set; }
    public string? order_dtl_cust_number_5 { get; set; }
    public string? order_dtl_cust_decimal_1 { get; set; }
    public string? order_dtl_cust_decimal_2 { get; set; }
    public string? order_dtl_cust_decimal_3 { get; set; }
    public string? order_dtl_cust_decimal_4 { get; set; }
    public string? order_dtl_cust_decimal_5 { get; set; }
    public string? order_dtl_cust_short_text_1 { get; set; }
    public string? order_dtl_cust_short_text_2 { get; set; }
    public string? order_dtl_cust_short_text_3 { get; set; }
    public string? order_dtl_cust_short_text_4 { get; set; }
    public string? order_dtl_cust_short_text_5 { get; set; }
    public string? order_dtl_cust_short_text_6 { get; set; }
    public string? order_dtl_cust_short_text_7 { get; set; }
    public string? order_dtl_cust_short_text_8 { get; set; }
    public string? order_dtl_cust_short_text_9 { get; set; }
    public string? order_dtl_cust_short_text_10 { get; set; }
    public string? order_dtl_cust_short_text_11 { get; set; }
    public string? order_dtl_cust_short_text_12 { get; set; }
    public string? order_dtl_cust_long_text_1 { get; set; }
    public string? order_dtl_cust_long_text_2 { get; set; }
    public string? order_dtl_cust_long_text_3 { get; set; }
    public string? invn_attr_d { get; set; }
    public string? invn_attr_e { get; set; }
    public string? invn_attr_f { get; set; }
    public string? invn_attr_g { get; set; }
    public string? order_type { get; set; }
    public string? rcvd_trailer_nbr { get; set; }
    public string? stop_seal_nbr { get; set; }
    public string? ship_request_line { get; set; }
}
```

- [ ] **Step 4: Registrar `DbSet` y Fluent API en `WmsDbContext`**

Agregar junto a los `DbSet` existentes:

```csharp
public DbSet<WmsOracleInboundStage> WmsOracleInboundStages => Set<WmsOracleInboundStage>();
public DbSet<WmsOracleStageSlsh> WmsOracleStageSlsh => Set<WmsOracleStageSlsh>();
```

En `OnModelCreating`, siguiendo el estilo exacto de las entidades existentes (Fluent API completa, `HasColumnName` snake_case explícito en cada propiedad, sin `HasColumnType` de un solo motor):

```csharp
modelBuilder.Entity<WmsOracleInboundStage>(entity =>
{
    entity.ToTable("wms_oracle_inbound_stage");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasColumnName("id");
    entity.Property(e => e.CompanyId).HasColumnName("company_id");
    entity.Property(e => e.TipoDoc).HasColumnName("tipo_doc").HasMaxLength(10);
    entity.Property(e => e.Formato).HasColumnName("formato").HasConversion<string>().HasMaxLength(10);
    entity.Property(e => e.NombreArchivo).HasColumnName("nombre_archivo").HasMaxLength(255);
    entity.Property(e => e.HashArchivo).HasColumnName("hash_archivo").HasMaxLength(64);
    entity.Property(e => e.Contenido).HasColumnName("contenido");
    entity.Property(e => e.Estado).HasColumnName("estado").HasConversion<string>().HasMaxLength(20);
    entity.Property(e => e.Intentos).HasColumnName("intentos");
    entity.Property(e => e.MensajeError).HasColumnName("mensaje_error");
    entity.Property(e => e.SapDocEntry).HasColumnName("sap_doc_entry").HasMaxLength(50);
    entity.Property(e => e.InsertedAt).HasColumnName("inserted_at");
    entity.Property(e => e.ProcessedAt).HasColumnName("processed_at");
    entity.HasIndex(e => new { e.CompanyId, e.HashArchivo }).IsUnique().HasDatabaseName("ix_wms_oracle_inbound_stage_company_hash");
    entity.HasIndex(e => e.Estado).HasDatabaseName("ix_wms_oracle_inbound_stage_estado");
});

modelBuilder.Entity<WmsOracleStageSlsh>(entity =>
{
    entity.ToTable("wms_oracle_stage_slsh");
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
    entity.Property(e => e.facility_code).HasColumnName("facility_code").HasMaxLength(50);
    entity.Property(e => e.company_code).HasColumnName("company_code").HasMaxLength(50);
    entity.Property(e => e.action_code).HasColumnName("action_code").HasMaxLength(50);
    entity.Property(e => e.load_type).HasColumnName("load_type").HasMaxLength(50);
    entity.Property(e => e.load_manifest_nbr).HasColumnName("load_manifest_nbr").HasMaxLength(50);
    entity.Property(e => e.trailer_nbr).HasColumnName("trailer_nbr").HasMaxLength(50);
    entity.Property(e => e.trailer_type).HasColumnName("trailer_type").HasMaxLength(50);
    entity.Property(e => e.driver).HasColumnName("driver").HasMaxLength(50);
    entity.Property(e => e.seal_nbr).HasColumnName("seal_nbr").HasMaxLength(50);
    entity.Property(e => e.pro_nbr).HasColumnName("pro_nbr").HasMaxLength(50);
    entity.Property(e => e.route_nbr).HasColumnName("route_nbr").HasMaxLength(50);
    entity.Property(e => e.freight_class).HasColumnName("freight_class").HasMaxLength(50);
    entity.Property(e => e.hdr_bol_nbr).HasColumnName("hdr_bol_nbr").HasMaxLength(50);
    entity.Property(e => e.total_nbr_of_oblpns).HasColumnName("total_nbr_of_oblpns").HasMaxLength(50);
    entity.Property(e => e.total_weight).HasColumnName("total_weight").HasMaxLength(50);
    entity.Property(e => e.total_volume).HasColumnName("total_volume").HasMaxLength(50);
    entity.Property(e => e.total_shipping_charge).HasColumnName("total_shipping_charge").HasMaxLength(50);
    entity.Property(e => e.ship_date).HasColumnName("ship_date").HasMaxLength(50);
    entity.Property(e => e.sched_delivery_date).HasColumnName("sched_delivery_date").HasMaxLength(50);
    entity.Property(e => e.carrier_code).HasColumnName("carrier_code").HasMaxLength(50);
    entity.Property(e => e.externally_planned_load_nbr).HasColumnName("externally_planned_load_nbr").HasMaxLength(50);
    entity.Property(e => e.ship_date_time).HasColumnName("ship_date_time").HasMaxLength(50);
    entity.Property(e => e.sched_delivery_date_time).HasColumnName("sched_delivery_date_time").HasMaxLength(50);
    entity.Property(e => e.line_nbr).HasColumnName("line_nbr").HasMaxLength(50);
    entity.Property(e => e.seq_nbr).HasColumnName("seq_nbr").HasMaxLength(50);
    entity.Property(e => e.stop_shipment_nbr).HasColumnName("stop_shipment_nbr").HasMaxLength(50);
    entity.Property(e => e.stop_bol_nbr).HasColumnName("stop_bol_nbr").HasMaxLength(50);
    entity.Property(e => e.stop_nbr_of_oblpns).HasColumnName("stop_nbr_of_oblpns").HasMaxLength(50);
    entity.Property(e => e.stop_weight).HasColumnName("stop_weight").HasMaxLength(50);
    entity.Property(e => e.stop_volume).HasColumnName("stop_volume").HasMaxLength(50);
    entity.Property(e => e.stop_shipping_charge).HasColumnName("stop_shipping_charge").HasMaxLength(50);
    entity.Property(e => e.shipto_facility_code).HasColumnName("shipto_facility_code").HasMaxLength(100);
    entity.Property(e => e.shipto_name).HasColumnName("shipto_name").HasMaxLength(100);
    entity.Property(e => e.shipto_addr).HasColumnName("shipto_addr").HasMaxLength(100);
    entity.Property(e => e.shipto_addr2).HasColumnName("shipto_addr2").HasMaxLength(100);
    entity.Property(e => e.shipto_addr3).HasColumnName("shipto_addr3").HasMaxLength(100);
    entity.Property(e => e.shipto_city).HasColumnName("shipto_city").HasMaxLength(100);
    entity.Property(e => e.shipto_state).HasColumnName("shipto_state").HasMaxLength(100);
    entity.Property(e => e.shipto_zip).HasColumnName("shipto_zip").HasMaxLength(100);
    entity.Property(e => e.shipto_country).HasColumnName("shipto_country").HasMaxLength(100);
    entity.Property(e => e.shipto_phone_nbr).HasColumnName("shipto_phone_nbr").HasMaxLength(100);
    entity.Property(e => e.shipto_email).HasColumnName("shipto_email").HasMaxLength(100);
    entity.Property(e => e.shipto_contact).HasColumnName("shipto_contact").HasMaxLength(100);
    entity.Property(e => e.dest_facility_code).HasColumnName("dest_facility_code").HasMaxLength(100);
    entity.Property(e => e.cust_name).HasColumnName("cust_name").HasMaxLength(100);
    entity.Property(e => e.cust_addr).HasColumnName("cust_addr").HasMaxLength(100);
    entity.Property(e => e.cust_addr2).HasColumnName("cust_addr2").HasMaxLength(100);
    entity.Property(e => e.cust_addr3).HasColumnName("cust_addr3").HasMaxLength(100);
    entity.Property(e => e.cust_city).HasColumnName("cust_city").HasMaxLength(100);
    entity.Property(e => e.cust_state).HasColumnName("cust_state").HasMaxLength(100);
    entity.Property(e => e.cust_zip).HasColumnName("cust_zip").HasMaxLength(100);
    entity.Property(e => e.cust_country).HasColumnName("cust_country").HasMaxLength(100);
    entity.Property(e => e.cust_phone_nbr).HasColumnName("cust_phone_nbr").HasMaxLength(100);
    entity.Property(e => e.cust_email).HasColumnName("cust_email").HasMaxLength(100);
    entity.Property(e => e.cust_contact).HasColumnName("cust_contact").HasMaxLength(100);
    entity.Property(e => e.cust_nbr).HasColumnName("cust_nbr").HasMaxLength(100);
    entity.Property(e => e.order_nbr).HasColumnName("order_nbr").HasMaxLength(50);
    entity.Property(e => e.ord_date).HasColumnName("ord_date").HasMaxLength(50);
    entity.Property(e => e.exp_date).HasColumnName("exp_date").HasMaxLength(50);
    entity.Property(e => e.req_ship_date).HasColumnName("req_ship_date").HasMaxLength(50);
    entity.Property(e => e.start_ship_date).HasColumnName("start_ship_date").HasMaxLength(50);
    entity.Property(e => e.stop_ship_date).HasColumnName("stop_ship_date").HasMaxLength(50);
    entity.Property(e => e.host_allocation_nbr).HasColumnName("host_allocation_nbr").HasMaxLength(50);
    entity.Property(e => e.customer_po_nbr).HasColumnName("customer_po_nbr").HasMaxLength(50);
    entity.Property(e => e.sales_order_nbr).HasColumnName("sales_order_nbr").HasMaxLength(50);
    entity.Property(e => e.sales_channel).HasColumnName("sales_channel").HasMaxLength(50);
    entity.Property(e => e.dest_dept_nbr).HasColumnName("dest_dept_nbr").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_field_1).HasColumnName("order_hdr_cust_field_1").HasMaxLength(200);
    entity.Property(e => e.order_hdr_cust_field_2).HasColumnName("order_hdr_cust_field_2").HasMaxLength(200);
    entity.Property(e => e.order_hdr_cust_field_3).HasColumnName("order_hdr_cust_field_3").HasMaxLength(200);
    entity.Property(e => e.order_hdr_cust_field_4).HasColumnName("order_hdr_cust_field_4").HasMaxLength(200);
    entity.Property(e => e.order_hdr_cust_field_5).HasColumnName("order_hdr_cust_field_5").HasMaxLength(200);
    entity.Property(e => e.order_seq_nbr).HasColumnName("order_seq_nbr").HasMaxLength(200);
    entity.Property(e => e.order_dtl_cust_field_1).HasColumnName("order_dtl_cust_field_1").HasMaxLength(200);
    entity.Property(e => e.order_dtl_cust_field_2).HasColumnName("order_dtl_cust_field_2").HasMaxLength(200);
    entity.Property(e => e.order_dtl_cust_field_3).HasColumnName("order_dtl_cust_field_3").HasMaxLength(200);
    entity.Property(e => e.order_dtl_cust_field_4).HasColumnName("order_dtl_cust_field_4").HasMaxLength(200);
    entity.Property(e => e.order_dtl_cust_field_5).HasColumnName("order_dtl_cust_field_5").HasMaxLength(200);
    entity.Property(e => e.ob_lpn_nbr).HasColumnName("ob_lpn_nbr").HasMaxLength(50);
    entity.Property(e => e.item_alternate_code).HasColumnName("item_alternate_code").HasMaxLength(50);
    entity.Property(e => e.item_part_a).HasColumnName("item_part_a").HasMaxLength(50);
    entity.Property(e => e.item_part_b).HasColumnName("item_part_b").HasMaxLength(50);
    entity.Property(e => e.item_part_c).HasColumnName("item_part_c").HasMaxLength(50);
    entity.Property(e => e.item_part_d).HasColumnName("item_part_d").HasMaxLength(50);
    entity.Property(e => e.item_part_e).HasColumnName("item_part_e").HasMaxLength(50);
    entity.Property(e => e.item_part_f).HasColumnName("item_part_f").HasMaxLength(50);
    entity.Property(e => e.pre_pack_code).HasColumnName("pre_pack_code").HasMaxLength(50);
    entity.Property(e => e.pre_pack_ratio).HasColumnName("pre_pack_ratio").HasMaxLength(50);
    entity.Property(e => e.pre_pack_ratio_seq).HasColumnName("pre_pack_ratio_seq").HasMaxLength(50);
    entity.Property(e => e.pre_pack_total_units).HasColumnName("pre_pack_total_units").HasMaxLength(50);
    entity.Property(e => e.invn_attr_a).HasColumnName("invn_attr_a").HasMaxLength(50);
    entity.Property(e => e.invn_attr_b).HasColumnName("invn_attr_b").HasMaxLength(50);
    entity.Property(e => e.invn_attr_c).HasColumnName("invn_attr_c").HasMaxLength(50);
    entity.Property(e => e.hazmat).HasColumnName("hazmat").HasMaxLength(50);
    entity.Property(e => e.shipped_uom).HasColumnName("shipped_uom").HasMaxLength(50);
    entity.Property(e => e.shipped_qty).HasColumnName("shipped_qty").HasMaxLength(50);
    entity.Property(e => e.pallet_nbr).HasColumnName("pallet_nbr").HasMaxLength(50);
    entity.Property(e => e.dest_company_code).HasColumnName("dest_company_code").HasMaxLength(50);
    entity.Property(e => e.batch_nbr).HasColumnName("batch_nbr").HasMaxLength(50);
    entity.Property(e => e.expiry_date).HasColumnName("expiry_date").HasMaxLength(50);
    entity.Property(e => e.tracking_nbr).HasColumnName("tracking_nbr").HasMaxLength(50);
    entity.Property(e => e.master_tracking_nbr).HasColumnName("master_tracking_nbr").HasMaxLength(50);
    entity.Property(e => e.package_type).HasColumnName("package_type").HasMaxLength(50);
    entity.Property(e => e.payment_method).HasColumnName("payment_method").HasMaxLength(50);
    entity.Property(e => e.carrier_account_nbr).HasColumnName("carrier_account_nbr").HasMaxLength(50);
    entity.Property(e => e.ship_via_code).HasColumnName("ship_via_code").HasMaxLength(50);
    entity.Property(e => e.ob_lpn_weight).HasColumnName("ob_lpn_weight").HasMaxLength(50);
    entity.Property(e => e.ob_lpn_volume).HasColumnName("ob_lpn_volume").HasMaxLength(50);
    entity.Property(e => e.ob_lpn_shipping_charge).HasColumnName("ob_lpn_shipping_charge").HasMaxLength(50);
    entity.Property(e => e.ob_lpn_type).HasColumnName("ob_lpn_type").HasMaxLength(50);
    entity.Property(e => e.ob_lpn_asset_nbr).HasColumnName("ob_lpn_asset_nbr").HasMaxLength(50);
    entity.Property(e => e.ob_lpn_asset_seal_nbr).HasColumnName("ob_lpn_asset_seal_nbr").HasMaxLength(50);
    entity.Property(e => e.serial_nbr).HasColumnName("serial_nbr").HasMaxLength(50);
    entity.Property(e => e.customer_po_type).HasColumnName("customer_po_type").HasMaxLength(50);
    entity.Property(e => e.customer_vendor_code).HasColumnName("customer_vendor_code").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_date_1).HasColumnName("order_hdr_cust_date_1").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_date_2).HasColumnName("order_hdr_cust_date_2").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_date_3).HasColumnName("order_hdr_cust_date_3").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_date_4).HasColumnName("order_hdr_cust_date_4").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_date_5).HasColumnName("order_hdr_cust_date_5").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_number_1).HasColumnName("order_hdr_cust_number_1").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_number_2).HasColumnName("order_hdr_cust_number_2").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_number_3).HasColumnName("order_hdr_cust_number_3").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_number_4).HasColumnName("order_hdr_cust_number_4").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_number_5).HasColumnName("order_hdr_cust_number_5").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_decimal_1).HasColumnName("order_hdr_cust_decimal_1").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_decimal_2).HasColumnName("order_hdr_cust_decimal_2").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_decimal_3).HasColumnName("order_hdr_cust_decimal_3").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_decimal_4).HasColumnName("order_hdr_cust_decimal_4").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_decimal_5).HasColumnName("order_hdr_cust_decimal_5").HasMaxLength(50);
    entity.Property(e => e.order_hdr_cust_short_text_1).HasColumnName("order_hdr_cust_short_text_1").HasMaxLength(100);
    entity.Property(e => e.order_hdr_cust_short_text_2).HasColumnName("order_hdr_cust_short_text_2").HasMaxLength(100);
    entity.Property(e => e.order_hdr_cust_short_text_3).HasColumnName("order_hdr_cust_short_text_3").HasMaxLength(100);
    entity.Property(e => e.order_hdr_cust_short_text_4).HasColumnName("order_hdr_cust_short_text_4").HasMaxLength(100);
    entity.Property(e => e.order_hdr_cust_short_text_5).HasColumnName("order_hdr_cust_short_text_5").HasMaxLength(100);
    entity.Property(e => e.order_hdr_cust_short_text_6).HasColumnName("order_hdr_cust_short_text_6").HasMaxLength(100);
    entity.Property(e => e.order_hdr_cust_short_text_7).HasColumnName("order_hdr_cust_short_text_7").HasMaxLength(100);
    entity.Property(e => e.order_hdr_cust_short_text_8).HasColumnName("order_hdr_cust_short_text_8").HasMaxLength(100);
    entity.Property(e => e.order_hdr_cust_short_text_9).HasColumnName("order_hdr_cust_short_text_9").HasMaxLength(100);
    entity.Property(e => e.order_hdr_cust_short_text_10).HasColumnName("order_hdr_cust_short_text_10").HasMaxLength(100);
    entity.Property(e => e.order_hdr_cust_short_text_11).HasColumnName("order_hdr_cust_short_text_11").HasMaxLength(100);
    entity.Property(e => e.order_hdr_cust_short_text_12).HasColumnName("order_hdr_cust_short_text_12").HasMaxLength(100);
    entity.Property(e => e.order_hdr_cust_long_text_1).HasColumnName("order_hdr_cust_long_text_1").HasMaxLength(100);
    entity.Property(e => e.order_hdr_cust_long_text_2).HasColumnName("order_hdr_cust_long_text_2").HasMaxLength(100);
    entity.Property(e => e.order_hdr_cust_long_text_3).HasColumnName("order_hdr_cust_long_text_3").HasMaxLength(100);
    entity.Property(e => e.order_dtl_cust_date_1).HasColumnName("order_dtl_cust_date_1").HasMaxLength(50);
    entity.Property(e => e.order_dtl_cust_date_2).HasColumnName("order_dtl_cust_date_2").HasMaxLength(50);
    entity.Property(e => e.order_dtl_cust_date_3).HasColumnName("order_dtl_cust_date_3").HasMaxLength(50);
    entity.Property(e => e.order_dtl_cust_date_4).HasColumnName("order_dtl_cust_date_4").HasMaxLength(50);
    entity.Property(e => e.order_dtl_cust_date_5).HasColumnName("order_dtl_cust_date_5").HasMaxLength(50);
    entity.Property(e => e.order_dtl_cust_number_1).HasColumnName("order_dtl_cust_number_1").HasMaxLength(50);
    entity.Property(e => e.order_dtl_cust_number_2).HasColumnName("order_dtl_cust_number_2").HasMaxLength(50);
    entity.Property(e => e.order_dtl_cust_number_3).HasColumnName("order_dtl_cust_number_3").HasMaxLength(50);
    entity.Property(e => e.order_dtl_cust_number_4).HasColumnName("order_dtl_cust_number_4").HasMaxLength(50);
    entity.Property(e => e.order_dtl_cust_number_5).HasColumnName("order_dtl_cust_number_5").HasMaxLength(50);
    entity.Property(e => e.order_dtl_cust_decimal_1).HasColumnName("order_dtl_cust_decimal_1").HasMaxLength(50);
    entity.Property(e => e.order_dtl_cust_decimal_2).HasColumnName("order_dtl_cust_decimal_2").HasMaxLength(50);
    entity.Property(e => e.order_dtl_cust_decimal_3).HasColumnName("order_dtl_cust_decimal_3").HasMaxLength(50);
    entity.Property(e => e.order_dtl_cust_decimal_4).HasColumnName("order_dtl_cust_decimal_4").HasMaxLength(50);
    entity.Property(e => e.order_dtl_cust_decimal_5).HasColumnName("order_dtl_cust_decimal_5").HasMaxLength(50);
    entity.Property(e => e.order_dtl_cust_short_text_1).HasColumnName("order_dtl_cust_short_text_1").HasMaxLength(100);
    entity.Property(e => e.order_dtl_cust_short_text_2).HasColumnName("order_dtl_cust_short_text_2").HasMaxLength(100);
    entity.Property(e => e.order_dtl_cust_short_text_3).HasColumnName("order_dtl_cust_short_text_3").HasMaxLength(100);
    entity.Property(e => e.order_dtl_cust_short_text_4).HasColumnName("order_dtl_cust_short_text_4").HasMaxLength(100);
    entity.Property(e => e.order_dtl_cust_short_text_5).HasColumnName("order_dtl_cust_short_text_5").HasMaxLength(100);
    entity.Property(e => e.order_dtl_cust_short_text_6).HasColumnName("order_dtl_cust_short_text_6").HasMaxLength(100);
    entity.Property(e => e.order_dtl_cust_short_text_7).HasColumnName("order_dtl_cust_short_text_7").HasMaxLength(100);
    entity.Property(e => e.order_dtl_cust_short_text_8).HasColumnName("order_dtl_cust_short_text_8").HasMaxLength(100);
    entity.Property(e => e.order_dtl_cust_short_text_9).HasColumnName("order_dtl_cust_short_text_9").HasMaxLength(100);
    entity.Property(e => e.order_dtl_cust_short_text_10).HasColumnName("order_dtl_cust_short_text_10").HasMaxLength(100);
    entity.Property(e => e.order_dtl_cust_short_text_11).HasColumnName("order_dtl_cust_short_text_11").HasMaxLength(100);
    entity.Property(e => e.order_dtl_cust_short_text_12).HasColumnName("order_dtl_cust_short_text_12").HasMaxLength(100);
    entity.Property(e => e.order_dtl_cust_long_text_1).HasColumnName("order_dtl_cust_long_text_1").HasMaxLength(100);
    entity.Property(e => e.order_dtl_cust_long_text_2).HasColumnName("order_dtl_cust_long_text_2").HasMaxLength(100);
    entity.Property(e => e.order_dtl_cust_long_text_3).HasColumnName("order_dtl_cust_long_text_3").HasMaxLength(100);
    entity.Property(e => e.invn_attr_d).HasColumnName("invn_attr_d").HasMaxLength(100);
    entity.Property(e => e.invn_attr_e).HasColumnName("invn_attr_e").HasMaxLength(100);
    entity.Property(e => e.invn_attr_f).HasColumnName("invn_attr_f").HasMaxLength(100);
    entity.Property(e => e.invn_attr_g).HasColumnName("invn_attr_g").HasMaxLength(100);
    entity.Property(e => e.order_type).HasColumnName("order_type").HasMaxLength(50);
    entity.Property(e => e.rcvd_trailer_nbr).HasColumnName("rcvd_trailer_nbr").HasMaxLength(50);
    entity.Property(e => e.stop_seal_nbr).HasColumnName("stop_seal_nbr").HasMaxLength(50);
    entity.Property(e => e.ship_request_line).HasColumnName("ship_request_line").HasMaxLength(50);
    entity.HasOne<WmsOracleInboundStage>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Cascade);
    entity.HasIndex(e => e.ParentId).HasDatabaseName("ix_wms_oracle_stage_slsh_parent");
    entity.HasIndex(e => new { e.Status, e.RetryCount }).HasDatabaseName("ix_wms_oracle_stage_slsh_status_retry");
    entity.HasIndex(e => e.ob_lpn_nbr).HasDatabaseName("ix_wms_oracle_stage_slsh_lpn");
});
```

- [ ] **Step 5: Compilar `Modulo.Wms`**

Run: `dotnet build src/Modulo.Wms/Modulo.Wms.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 6: Generar migración Postgres**

Run (desde la raíz del repo del plugin): `dotnet ef migrations add AddWmsInboundStaging --project Modulo.Wms.Migrations.Postgres -o Migrations`
Expected: se crea archivo de migración nuevo en `Modulo.Wms.Migrations.Postgres/Migrations/`.

- [ ] **Step 7: Generar migración SQL Server**

Run: `dotnet ef migrations add AddWmsInboundStaging --project Modulo.Wms.Migrations.SqlServer -o Migrations`
Expected: se crea archivo de migración nuevo en `Modulo.Wms.Migrations.SqlServer/Migrations/`.

- [ ] **Step 8: Commit**

```bash
git add src/Modulo.Wms/Models/WmsOracleInboundStage.cs src/Modulo.Wms/Models/WmsOracleStageSlsh.cs src/Modulo.Wms/Data/WmsDbContext.cs Modulo.Wms.Migrations.Postgres/Migrations/ Modulo.Wms.Migrations.SqlServer/Migrations/
git commit -m "feat: agregar tablas de staging wms_oracle_inbound_stage y wms_oracle_stage_slsh"
```

---

### Task 2: `IWmsInboundIngestionService` + implementación

**Files:**
- Create: `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IWmsInboundIngestionService.cs`
- Create: `src/Modulo.Wms/Services/WmsInboundIngestionService.cs`
- Modify: `src/Modulo.Wms/ModuloWms.cs`
- Test: `tests/Modulo.Wms.Tests/Services/WmsInboundIngestionServiceTests.cs`

**Interfaces:**
- Consumes: `WmsOracleInboundStage`, `WmsDbContext.WmsOracleInboundStages` (Task 1).
- Produces: `IWmsInboundIngestionService.InsertPendingAsync(Guid companyId, string tipoDoc, string formato, string nombreArchivo, string hashArchivo, string contenido, CancellationToken ct)` retornando `WmsInboundIngestionResult { bool Insertado, bool Duplicado }` — consumido por el endpoint Minimal API (Task 3).

**Nota sobre el proyecto de este plugin**: `Modulo.Wms` referencia `PortalSaas.Abstractions` (verificar en `Modulo.Wms.csproj` — si no lo referencia todavía, agregar `<ProjectReference Include="...\PortalSaas.Abstractions.csproj" />`, ruta relativa según la estructura real del repo de plugins; los demás plugins de este Portal ya siguen este patrón).

- [ ] **Step 1: Crear `IWmsInboundIngestionService` en Abstractions**

```csharp
namespace PortalSaas.Abstractions.Contratos;

public interface IWmsInboundIngestionService
{
    Task<WmsInboundIngestionResult> InsertPendingAsync(
        Guid companyId,
        string tipoDoc,
        string formato,
        string nombreArchivo,
        string hashArchivo,
        string contenido,
        CancellationToken cancellationToken);
}

public sealed class WmsInboundIngestionResult
{
    public required bool Insertado { get; init; }
    public required bool Duplicado { get; init; }
}
```

- [ ] **Step 2: Escribir el test que falla**

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsInboundIngestionServiceTests
{
    private static WmsDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(options);
    }

    [Fact]
    public async Task InsertPendingAsync_ConDatosNuevos_InsertaYRetornaInsertadoTrue()
    {
        await using var contexto = CrearContexto();
        var servicio = new WmsInboundIngestionService(contexto);
        var companyId = Guid.NewGuid();

        var resultado = await servicio.InsertPendingAsync(
            companyId, "SLSH", "xml", "test.xml", "hash-unico-1", "<xml/>", CancellationToken.None);

        Assert.True(resultado.Insertado);
        Assert.False(resultado.Duplicado);
        var fila = await contexto.WmsOracleInboundStages.FirstAsync();
        Assert.Equal(companyId, fila.CompanyId);
        Assert.Equal("SLSH", fila.TipoDoc);
    }

    [Fact]
    public async Task InsertPendingAsync_ConHashDuplicadoParaLaMismaCompany_NoInsertaDeNuevo()
    {
        await using var contexto = CrearContexto();
        var servicio = new WmsInboundIngestionService(contexto);
        var companyId = Guid.NewGuid();
        await servicio.InsertPendingAsync(companyId, "SLSH", "xml", "test.xml", "hash-repetido", "<xml/>", CancellationToken.None);

        var resultado = await servicio.InsertPendingAsync(companyId, "SLSH", "xml", "test2.xml", "hash-repetido", "<xml/>", CancellationToken.None);

        Assert.False(resultado.Insertado);
        Assert.True(resultado.Duplicado);
        Assert.Equal(1, await contexto.WmsOracleInboundStages.CountAsync());
    }

    [Fact]
    public async Task InsertPendingAsync_ConMismoHashEnCompaniasDistintas_InsertaAmbas()
    {
        await using var contexto = CrearContexto();
        var servicio = new WmsInboundIngestionService(contexto);

        await servicio.InsertPendingAsync(Guid.NewGuid(), "SLSH", "xml", "a.xml", "hash-compartido", "<xml/>", CancellationToken.None);
        var resultado = await servicio.InsertPendingAsync(Guid.NewGuid(), "SLSH", "xml", "b.xml", "hash-compartido", "<xml/>", CancellationToken.None);

        Assert.True(resultado.Insertado);
        Assert.Equal(2, await contexto.WmsOracleInboundStages.CountAsync());
    }
}
```

- [ ] **Step 3: Ejecutar el test y verificar que falla**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsInboundIngestionServiceTests`
Expected: FAIL — `WmsInboundIngestionService` no existe.

- [ ] **Step 4: Implementar `WmsInboundIngestionService`**

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Services;

public class WmsInboundIngestionService : IWmsInboundIngestionService
{
    private readonly WmsDbContext _contexto;

    public WmsInboundIngestionService(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<WmsInboundIngestionResult> InsertPendingAsync(
        Guid companyId,
        string tipoDoc,
        string formato,
        string nombreArchivo,
        string hashArchivo,
        string contenido,
        CancellationToken cancellationToken)
    {
        var yaExiste = await _contexto.WmsOracleInboundStages
            .AnyAsync(s => s.CompanyId == companyId && s.HashArchivo == hashArchivo, cancellationToken);

        if (yaExiste)
        {
            return new WmsInboundIngestionResult { Insertado = false, Duplicado = true };
        }

        _contexto.WmsOracleInboundStages.Add(new WmsOracleInboundStage
        {
            CompanyId = companyId,
            TipoDoc = tipoDoc,
            Formato = Enum.Parse<WmsInboundFormato>(formato, ignoreCase: true),
            NombreArchivo = nombreArchivo,
            HashArchivo = hashArchivo,
            Contenido = contenido,
            Estado = WmsInboundEstado.Pendiente,
        });
        await _contexto.SaveChangesAsync(cancellationToken);

        return new WmsInboundIngestionResult { Insertado = true, Duplicado = false };
    }
}
```

- [ ] **Step 5: Ejecutar el test y verificar que pasa**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsInboundIngestionServiceTests`
Expected: PASS, 3/3.

- [ ] **Step 6: Registrar en `ModuloWms.RegisterServices`**

Agregar, junto al registro existente de `IFieldMappingService`:

```csharp
services.AddScoped<IWmsInboundIngestionService, WmsInboundIngestionService>();
```

Agregar el `using PortalSaas.Abstractions.Contratos;` si no está ya presente en `ModuloWms.cs`.

- [ ] **Step 7: Compilar y ejecutar toda la suite de tests del plugin**

Run: `dotnet build src/Modulo.Wms/Modulo.Wms.csproj && dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj`
Expected: Build succeeded, todos los tests pasan.

- [ ] **Step 8: Commit**

```bash
git add src/Modulo.Wms/Services/WmsInboundIngestionService.cs src/Modulo.Wms/ModuloWms.cs tests/Modulo.Wms.Tests/Services/WmsInboundIngestionServiceTests.cs
git commit -m "feat: agregar WmsInboundIngestionService"
```

Además, en `Portal SaaS - Core`:

```bash
git add src/PortalSaas.Abstractions/Contratos/IWmsInboundIngestionService.cs
git commit -m "feat: agregar contrato IWmsInboundIngestionService"
```

---

### Task 3: `SecureXmlHelper` + endpoint Minimal API de recepción

**Files:**
- Create: `Portal SaaS - Core/src/PortalSaas.Host/Wms/SecureXmlHelper.cs`
- Create: `Portal SaaS - Core/src/PortalSaas.Host/Wms/WmsInboundEndpoints.cs`
- Modify: `Portal SaaS - Core/src/PortalSaas.Host/Program.cs`

**Interfaces:**
- Consumes: `IWmsInboundIngestionService`, `WmsInboundIngestionResult` (Task 2).
- Produces: endpoint `POST /api/wms/inbound/receive` — no produce nada consumido por otra tarea de este plan (hoja del árbol junto con Task 4).

- [ ] **Step 1: Crear `SecureXmlHelper`**

```csharp
using System.Xml;
using System.Xml.Linq;

namespace PortalSaas.Host.Wms;

public static class SecureXmlHelper
{
    public static XDocument ParseSecurely(string xmlContent)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        using var stringReader = new StringReader(xmlContent);
        using var xmlReader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(xmlReader);
    }

    public static string? GetValue(XElement? root, string parentLocalName, string childLocalName)
    {
        var parent = root?.Descendants().FirstOrDefault(x => x.Name.LocalName == parentLocalName);
        return parent?.Elements().FirstOrDefault(x => x.Name.LocalName == childLocalName)?.Value;
    }
}
```

- [ ] **Step 2: Crear el endpoint Minimal API**

```csharp
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Host.Wms;

public static class WmsInboundEndpoints
{
    private const long MaxBodySizeBytes = 10 * 1024 * 1024;

    public static void MapWmsInboundEndpoints(this WebApplication app)
    {
        app.MapPost("/api/wms/inbound/receive", HandleAsync)
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes("ExternalApiKey").RequireAuthenticatedUser());
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IWmsInboundIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        var companyIdClaim = httpContext.User.FindFirst("CompanyId")?.Value;
        if (companyIdClaim is null || !Guid.TryParse(companyIdClaim, out var companyId))
        {
            return Results.Unauthorized();
        }

        var contentType = httpContext.Request.ContentType ?? string.Empty;
        string formato;
        if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            formato = "Xml";
        }
        else if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            formato = "Json";
        }
        else if (contentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase))
        {
            formato = "Txt";
        }
        else
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        if (formato != "Xml")
        {
            return Results.BadRequest(new
            {
                success = false,
                message = $"Formato '{formato}' reconocido pero sin parser implementado todavía — solo XML soportado en esta ronda.",
            });
        }

        using var memoryStream = new MemoryStream();
        await httpContext.Request.Body.CopyToAsync(memoryStream, cancellationToken);
        var rawBody = memoryStream.ToArray();

        if (rawBody.Length == 0)
        {
            return Results.BadRequest(new { success = false, message = "El cuerpo de la petición está vacío." });
        }

        if (rawBody.LongLength > MaxBodySizeBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var xmlContent = Encoding.UTF8.GetString(rawBody);

        System.Xml.Linq.XDocument xmlDoc;
        try
        {
            xmlDoc = SecureXmlHelper.ParseSecurely(xmlContent);
        }
        catch (XmlException)
        {
            return Results.BadRequest(new { success = false, message = "El XML está mal formado o contiene elementos no permitidos (DOCTYPE/DTD no soportado)." });
        }

        var messageId = SecureXmlHelper.GetValue(xmlDoc.Root, "Header", "MessageId");
        var entity = SecureXmlHelper.GetValue(xmlDoc.Root, "Header", "Entity");

        if (string.IsNullOrWhiteSpace(entity))
        {
            return Results.BadRequest(new { success = false, message = "Falta el campo obligatorio Header/Entity." });
        }

        if (!string.Equals(entity, "shipped_load", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { success = false, message = $"Entity '{entity}' no está permitida en esta ronda (solo 'shipped_load' → SLSH)." });
        }

        var nombreArchivo = string.IsNullOrWhiteSpace(messageId)
            ? $"{Guid.NewGuid():N}.xml"
            : $"{SanitizeForFileName(messageId)}.xml";
        var hashArchivo = Convert.ToHexString(SHA256.HashData(rawBody)).ToLowerInvariant();

        var resultado = await ingestionService.InsertPendingAsync(
            companyId, "SLSH", formato, nombreArchivo, hashArchivo, xmlContent, cancellationToken);

        if (resultado.Duplicado)
        {
            return Results.Ok(new { success = true, message = "Mensaje ya recibido previamente (idempotente)." });
        }

        return Results.Ok(new { success = true, message = "Recibido y encolado correctamente.", nombreArchivo });
    }

    private static string SanitizeForFileName(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
            {
                sb.Append(c);
            }
        }
        var cleaned = sb.ToString();
        if (cleaned.Length == 0)
        {
            cleaned = Guid.NewGuid().ToString("N")[..12];
        }
        return cleaned.Length > 100 ? cleaned[..100] : cleaned;
    }
}
```

- [ ] **Step 3: Mapear el endpoint en `Program.cs`**

Ubicar `app.MapRazorPages();` (línea ~397) y agregar inmediatamente después:

```csharp
app.MapWmsInboundEndpoints();
```

Agregar `using PortalSaas.Host.Wms;` al inicio de `Program.cs` si no está presente.

- [ ] **Step 4: Compilar la solución completa**

Run: `dotnet build PortalSaas.sln`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 5: Ejecutar toda la suite de tests**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj`
Expected: todos los tests pasan, sin regresiones (este task no agrega tests unitarios de `PortalSaas.Core.Tests` — el endpoint Minimal API se verifica por build + revisión manual, siguiendo el mismo criterio que `LicensingEndpoints.cs`, que tampoco tiene tests unitarios en este proyecto).

- [ ] **Step 6: Commit**

```bash
git add src/PortalSaas.Host/Wms/ src/PortalSaas.Host/Program.cs
git commit -m "feat: agregar endpoint de recepción /api/wms/inbound/receive"
```

---

### Task 4: `WmsSlshXmlParser` (puro) + `WmsSlshStageParser` (`BackgroundService`)

**Files:**
- Create: `src/Modulo.Wms/Services/WmsSlshXmlParser.cs`
- Create: `src/Modulo.Wms/Services/WmsSlshStageParser.cs`
- Modify: `src/Modulo.Wms/ModuloWms.cs`
- Test: `tests/Modulo.Wms.Tests/Services/WmsSlshXmlParserTests.cs`

**Interfaces:**
- Consumes: `WmsOracleInboundStage`, `WmsOracleStageSlsh`, `WmsDbContext` (Task 1).
- Produces: `WmsSlshXmlParser.Parse(string xmlContent)` retornando `IReadOnlyList<WmsOracleStageSlsh>` (sin `LineId`/`ParentId` poblados — eso lo asigna el `BackgroundService` al guardar) — consumido por `WmsSlshStageParser`, que es el final de esta ronda.

- [ ] **Step 1: Escribir el test del parser puro que falla**

```csharp
using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSlshXmlParserTests
{
    private const string XmlDeEjemplo = @"
        <Message>
          <Header>
            <MessageId>MSG-1</MessageId>
            <Entity>shipped_load</Entity>
          </Header>
          <load>
            <order_type>NORMAL</order_type>
            <dest_facility_code>WH01</dest_facility_code>
          </load>
          <ob_stop>
            <ob_lpn_nbr>LPN-001</ob_lpn_nbr>
            <item_part_a>ITEM-A</item_part_a>
          </ob_stop>
          <ob_stop>
            <ob_lpn_nbr>LPN-002</ob_lpn_nbr>
            <item_part_a>ITEM-B</item_part_a>
          </ob_stop>
        </Message>";

    [Fact]
    public void Parse_ConDosObStop_RetornaDosFilas()
    {
        var filas = WmsSlshXmlParser.Parse(XmlDeEjemplo);

        Assert.Equal(2, filas.Count);
    }

    [Fact]
    public void Parse_TomaCamposDeObStopPrimero()
    {
        var filas = WmsSlshXmlParser.Parse(XmlDeEjemplo);

        Assert.Equal("LPN-001", filas[0].ob_lpn_nbr);
        Assert.Equal("ITEM-A", filas[0].item_part_a);
        Assert.Equal("LPN-002", filas[1].ob_lpn_nbr);
    }

    [Fact]
    public void Parse_CaeAloadCuandoNoEstaEnObStop()
    {
        var filas = WmsSlshXmlParser.Parse(XmlDeEjemplo);

        Assert.Equal("NORMAL", filas[0].order_type);
        Assert.Equal("WH01", filas[0].dest_facility_code);
    }

    [Fact]
    public void Parse_CaeAHeaderCuandoNoEstaEnObStopNiEnLoad()
    {
        var filas = WmsSlshXmlParser.Parse(XmlDeEjemplo);

        Assert.Equal("MSG-1", filas[0].MessageId);
    }

    [Fact]
    public void Parse_SinNodosObStop_RetornaListaVacia()
    {
        const string xmlSinStops = @"<Message><Header><MessageId>X</MessageId><Entity>shipped_load</Entity></Header><load></load></Message>";

        var filas = WmsSlshXmlParser.Parse(xmlSinStops);

        Assert.Empty(filas);
    }
}
```

- [ ] **Step 2: Ejecutar el test y verificar que falla**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsSlshXmlParserTests`
Expected: FAIL — `WmsSlshXmlParser` no existe.

- [ ] **Step 3: Implementar `WmsSlshXmlParser`**

Mapeo dinámico vía reflexión sobre las propiedades públicas `string?` de `WmsOracleStageSlsh` (excluyendo `LineId`, `ParentId`, `Status`, `ErrorMsg`, `RetryCount`, `SapDocEntry`, `SapObject`, que son metadata de staging, no datos de negocio) — equivalente al `GetTableColumnsAsync` dinámico del legado, pero basado en el modelo C# en vez de una query a la BD:

```csharp
using System.Reflection;
using System.Xml.Linq;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public static class WmsSlshXmlParser
{
    private static readonly HashSet<string> ColumnasMetadata = new(StringComparer.OrdinalIgnoreCase)
    {
        "LineId", "ParentId", "Status", "ErrorMsg", "RetryCount", "SapDocEntry", "SapObject",
    };

    private static readonly PropertyInfo[] ColumnasDeNegocio = typeof(WmsOracleStageSlsh)
        .GetProperties()
        .Where(p => p.PropertyType == typeof(string) && !ColumnasMetadata.Contains(p.Name))
        .ToArray();

    public static IReadOnlyList<WmsOracleStageSlsh> Parse(string xmlContent)
    {
        var doc = XDocument.Parse(xmlContent);

        var headerFields = doc.Descendants().Where(x => x.Name.LocalName == "Header")
            .Elements().ToDictionary(e => e.Name.LocalName, e => e.Value, StringComparer.OrdinalIgnoreCase);

        var loadFields = doc.Descendants().Where(x => x.Name.LocalName == "load")
            .Elements().ToDictionary(e => e.Name.LocalName, e => e.Value, StringComparer.OrdinalIgnoreCase);

        var obStops = doc.Descendants().Where(x => x.Name.LocalName == "ob_stop").ToList();

        var filas = new List<WmsOracleStageSlsh>();
        foreach (var obStop in obStops)
        {
            var stopFields = obStop.Elements()
                .ToDictionary(e => e.Name.LocalName, e => e.Value, StringComparer.OrdinalIgnoreCase);

            var fila = new WmsOracleStageSlsh();
            foreach (var columna in ColumnasDeNegocio)
            {
                string? valor = null;
                if (stopFields.TryGetValue(columna.Name, out var stopValue))
                {
                    valor = stopValue;
                }
                else if (loadFields.TryGetValue(columna.Name, out var loadValue))
                {
                    valor = loadValue;
                }
                else if (headerFields.TryGetValue(columna.Name, out var headerValue))
                {
                    valor = headerValue;
                }

                if (valor is not null)
                {
                    columna.SetValue(fila, valor);
                }
            }

            filas.Add(fila);
        }

        return filas;
    }
}
```

- [ ] **Step 4: Ejecutar el test y verificar que pasa**

Run: `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter WmsSlshXmlParserTests`
Expected: PASS, 5/5.

- [ ] **Step 5: Implementar `WmsSlshStageParser` (`BackgroundService`)**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulo.Wms.Data;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public sealed class WmsSlshStageParser : BackgroundService
{
    private static readonly TimeSpan IntervaloCiclo = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WmsSlshStageParser> _logger;

    public WmsSlshStageParser(IServiceScopeFactory scopeFactory, ILogger<WmsSlshStageParser> logger)
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
                _logger.LogError(ex, "Error inesperado ejecutando el ciclo de aplanado SLSH");
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
        using var scope = _scopeFactory.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();

        var pendientes = await contexto.WmsOracleInboundStages
            .Where(s => s.Estado == WmsInboundEstado.Pendiente && s.TipoDoc == "SLSH")
            .ToListAsync(cancellationToken);

        foreach (var entry in pendientes)
        {
            try
            {
                var filas = WmsSlshXmlParser.Parse(entry.Contenido);

                if (filas.Count == 0)
                {
                    entry.Estado = WmsInboundEstado.ErrorEstructura;
                    entry.MensajeError = "No se encontraron nodos ob_stop válidos.";
                }
                else
                {
                    foreach (var fila in filas)
                    {
                        fila.ParentId = entry.Id;
                        contexto.WmsOracleStageSlsh.Add(fila);
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
        }

        if (pendientes.Count > 0)
        {
            await contexto.SaveChangesAsync(cancellationToken);
        }
    }
}
```

- [ ] **Step 6: Registrar en `ModuloWms.RegisterServices`**

```csharp
services.AddHostedService<WmsSlshStageParser>();
```

- [ ] **Step 7: Compilar y ejecutar toda la suite de tests del plugin**

Run: `dotnet build src/Modulo.Wms/Modulo.Wms.csproj && dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj`
Expected: Build succeeded, todos los tests pasan (los anteriores + los 5 nuevos de `WmsSlshXmlParserTests`).

- [ ] **Step 8: Commit**

```bash
git add src/Modulo.Wms/Services/WmsSlshXmlParser.cs src/Modulo.Wms/Services/WmsSlshStageParser.cs src/Modulo.Wms/ModuloWms.cs tests/Modulo.Wms.Tests/Services/WmsSlshXmlParserTests.cs
git commit -m "feat: agregar WmsSlshXmlParser y WmsSlshStageParser"
```

---

## Self-Review

**1. Cobertura del spec:** Modelo de datos (`wms_oracle_inbound_stage`, `wms_oracle_stage_slsh`) → Task 1. Endpoint de recepción (validación anti-XXE, whitelist, idempotencia, formato) → Task 3, apoyado en `IWmsInboundIngestionService` de Task 2. `BackgroundService` de aplanado dinámico → Task 4. Criterio de éxito del spec: los 5 puntos son verificables con los tests de Task 2 (idempotencia) y Task 4 (aplanado correcto, caída a `load`/`Header`, error_estructura sin `ob_stop`) más verificación manual del endpoint (formato no-XML → 400, según Task 3).

**2. Placeholder scan:** sin "TBD"/"TODO". El único punto marcado explícitamente como no implementado (parsers JSON/TXT) es una decisión de alcance documentada en el spec, no un placeholder — el endpoint responde 400 explícito, no falla silenciosamente.

**3. Consistencia de tipos:** `IWmsInboundIngestionService.InsertPendingAsync` (Task 2) se llama con la misma firma exacta desde `WmsInboundEndpoints.HandleAsync` (Task 3). `WmsOracleStageSlsh` (Task 1) es el tipo de retorno de `WmsSlshXmlParser.Parse` (Task 4) y el tipo que persiste `WmsSlshStageParser` — mismos nombres de propiedad en los tres puntos, verificado contra el DDL real de `STG_WMS_SLSH` en las tres tareas que lo tocan (Task 1 lo define, Task 4 lo llena por reflexión usando exactamente esos nombres).

**4. Riesgo señalado explícitamente:** Task 1 Step 1 reconoce que no se confirmó si existe ya un proyecto de tests para `Modulo.Wms` — da la instrucción exacta de qué hacer en ambos casos (usar el existente o crear uno nuevo) en vez de asumir. Task 2 nota explícitamente que hay que verificar si `Modulo.Wms.csproj` ya referencia `PortalSaas.Abstractions`. Ambos son puntos que un plan genérico podría pasar por alto y romper el build del implementador.
