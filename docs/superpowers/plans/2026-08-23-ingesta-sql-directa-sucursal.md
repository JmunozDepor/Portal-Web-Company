# Ingesta SQL Directa — Sucursal (Store) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to
> implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replicar para Sucursal (`WmsSapStageStore`) el mismo patrón que ya funciona en
producción para Items: `SqlDirectConnector` (SQL directo a HANA), columna `Pk` (clave de
negocio ya resuelta por la Query) + `extra_fields` (jsonb) para todo lo demás, y XML dinámico
en la Subida a Oracle WMS Cloud (`ArmarNodoStoreDinamico`, igual que `ArmarNodoItemDinamico`).

**Architecture:** Ver decisiones A-F en
`docs/superpowers/plans/2026-08-22-ingesta-sql-directa-sucursal-orden-asn.md` (documento de
diseño, ya aprobado). Referencia de código: `WmsSapStageItemWriter.cs`,
`WmsSapStageItemReader.cs`, `WmsDbContext.cs` (mapeo de `ExtraFieldsJson`),
`WmsCloudConnector.ArmarNodoItemDinamico`.

**Tech Stack:** .NET 8, EF Core (Postgres + SQL Server duales), xUnit.

## Global Constraints

- Clave de negocio = columna `"PK"` que la propia Query SQL calcula
  (`UPPER(REPLACE(T0."CardCode", '-', '') || '-' || CAST(T1."LineNum" AS NVARCHAR))`) — el
  motor genérico (Writer/Reader) upsertea por esa columna, sin lógica propia de "cuál es mi
  clave".
- `extra_fields` (jsonb en Postgres, nvarchar(max) en SQL Server) contiene TODAS las claves del
  registro salvo `PK`, `SourceUpdateDate`, `TipoDocumento`, `_StagingLineIds` (mismo criterio
  que `WmsSapStageItemWriter.SepararCampos`).
- Reenvío por `wms_validation_fields` (valor cambia entre ciclos → `Status=Pendiente`), no por
  fecha — mismo criterio que Items.
- Subida (`WmsCloudConnector`) pasa a ser dinámica para Store: itera `registro.Fields` (salvo
  las claves internas) igual que `ArmarNodoItemDinamico`, con el nodo `"code"` mapeado
  explícitamente desde `registro["PK"]` (no es un campo de negocio real, es la clave del
  motor) y `"parent_company_id"` desde `config.ParentCompanyCode` (no viene del registro).
- Cursor de polling: `T0."U_NX_UPDATEDATE"` (UDF de cabecera en `OCRD`, confirmado por el
  usuario 22 ago 2026).
- Este plan NO toca Órdenes ni Ingresos ASN (esos van en un plan aparte, después de su propio
  spike contra HANA — ver el documento de diseño).

---

### Task 1: Spike — validar la Query de Sucursal contra HANA real

**Files:**
- No se tocan archivos de producto. Este task es de verificación pura, usando la app ya
  corriendo (Host + Modulo.Wms) contra la compañía "Depor (Testing)".

**Interfaces:**
- Consume: `IHanaService.QueryDynamicAsync` (ya existe, sin cambios) vía un scratch console o
  el mecanismo de "Describir Query" del admin de integraciones.

- [ ] **Step 1: Correr la Query candidata contra HANA real**

Ejecutar (vía scratch console con `IHanaService`, mismo patrón que se usó para el spike de
Items) exactamente:

```sql
SELECT
    UPPER(REPLACE(T0."CardCode", '-', '') || '-' || CAST(T1."LineNum" AS NVARCHAR)) AS "PK",
    UPPER(CASE
        WHEN T0."QryGroup1" = 'Y' THEN (CASE WHEN T1."Address2" <> '' THEN T1."Address2" ELSE T1."Street" END)
        WHEN T0."CardCode" = 'C76030680-0' THEN (CASE WHEN T1."Address2" <> '' THEN T1."Address2" ELSE T1."Street" END)
        ELSE T0."CardName"
    END) AS "name",
    UPPER(CASE WHEN T1."Address2" <> '' THEN T1."Address2" ELSE T1."Street" END) AS "address_1",
    SUBSTRING(UPPER(T0."CardName"), 1, 40) AS "address_3",
    UPPER(T1."County") AS "locality",
    UPPER(T1."City") AS "city",
    UPPER(T1."State") AS "state",
    UPPER(T0."LicTradNum") AS "zip",
    UPPER(T1."Country") AS "country",
    SUBSTRING(UPPER(T1."Address"), 1, 25) AS "cust_field_1",
    SUBSTRING(UPPER(T1."Street"), 1, 25) AS "cust_field_2",
    UPPER(T0."CardCode") AS "cust_field_5",
    T0."U_NX_UPDATEDATE" AS "SourceUpdateDate"
FROM "OCRD" T0
INNER JOIN "CRD1" T1 ON T0."CardCode" = T1."CardCode"
WHERE T0."U_NX_EnviarWMS" = 'Y'
  AND T1."AdresType" = 'S'
  AND (:cursor IS NULL OR T0."U_NX_UPDATEDATE" >= :cursor)
ORDER BY T0."U_NX_UPDATEDATE"
```

con `:cursor = NULL` primero (trae todo). Confirmar:
1. Compila sin error de columna/tabla inexistente.
2. `T0."U_NX_UPDATEDATE"` existe en `OCRD` con ese nombre exacto y trae valores no nulos para
   al menos algunos registros.
3. `"PK"` no tiene duplicados dentro del resultset (si los tiene, revisar si hace falta
   `DISTINCT` o si hay más de una dirección `AdresType='S'` por `LineNum` repetido — no
   debería, pero confirmar con datos reales).
4. El tipo de columna de `T0."U_NX_UPDATEDATE"` es compatible con el binding de `:cursor` que
   ya usa `HanaService` (mismo mecanismo que Items, sin cambios de código esperados aquí).

- [ ] **Step 2: Si algo no calza, ajustar la Query y volver a correr**

Documentar en el report cualquier ajuste hecho a la Query del Step 1 (columna con otro nombre,
`DISTINCT` necesario, etc.) — la Query final validada es la que se usa en la
`IntegrationDefinition` del Task 5.

- [ ] **Step 3: Reportar**

Reportar DONE con la Query final (ajustada si hizo falta) y una muestra de 3-5 filas reales
obtenidas.

---

### Task 2: Modelo + Migración — `Pk` y `ExtraFieldsJson` en `WmsSapStageStore`

**Files:**
- Modify: `src/Modulo.Wms/Models/WmsSapStageStore.cs`
- Modify: `src/Modulo.Wms/Data/WmsDbContext.cs`
- Create: migración EF nueva (Postgres) — `dotnet ef migrations add AddWmsSapStageStorePkAndExtraFields --context WmsDbContext` desde `src/Modulo.Wms` (verificar `appsettings`/provider activo, mismo flujo que se usó para `AddWmsSapStageItemExtraFields`)

**Interfaces:**
- Produce: `WmsSapStageStore.Pk` (string, no nulo), `WmsSapStageStore.ExtraFieldsJson` (string?)
  — consumidos por Task 3 (Writer/Reader) y Task 4 (WmsCloudConnector vía Reader).

- [ ] **Step 1: Reemplazar el modelo**

`src/Modulo.Wms/Models/WmsSapStageStore.cs` pasa a:

```csharp
namespace Modulo.Wms.Models;

public class WmsSapStageStore
{
    public long LineId { get; set; }
    public Guid CompanyId { get; set; }
    public string Pk { get; set; } = string.Empty;
    public string? ExtraFieldsJson { get; set; }
    public DateTime SourceUpdateDate { get; set; }
    public WmsSapStageStatus Status { get; set; } = WmsSapStageStatus.Pendiente;
    public int RetryCount { get; set; }
    public string? ErrorMsg { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SyncedAt { get; set; }
}
```

(Se eliminan `CardCode`, `CardName`, `Street`, `City`, `ZipCode` — no hay datos reales
dependientes todavía, este módulo nunca tuvo una integración de Sucursal funcionando en
producción con SqlDirectConnector.)

- [ ] **Step 2: Actualizar el mapeo en `WmsDbContext.cs`**

Buscar el bloque `modelBuilder.Entity<WmsSapStageStore>(entity => { ... })` (hoy mapea
`CardCode`/`CardName`/etc a columnas `card_code`/`card_name`/etc, con un índice único sobre
`(CompanyId, CardCode)`) y reemplazarlo por:

```csharp
modelBuilder.Entity<WmsSapStageStore>(entity =>
{
    entity.ToTable("wms_sap_stage_store");
    entity.HasKey(e => e.LineId);
    entity.Property(e => e.LineId).HasColumnName("line_id");
    entity.Property(e => e.CompanyId).HasColumnName("company_id");
    entity.Property(e => e.Pk).HasColumnName("pk").HasMaxLength(50);
    entity.Property(e => e.SourceUpdateDate).HasColumnName("source_update_date");
    entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
    entity.Property(e => e.RetryCount).HasColumnName("retry_count");
    entity.Property(e => e.ErrorMsg).HasColumnName("error_msg").HasMaxLength(500);
    entity.Property(e => e.CreatedAt).HasColumnName("created_at");
    entity.Property(e => e.SyncedAt).HasColumnName("synced_at");
    var extraFields = entity.Property(e => e.ExtraFieldsJson).HasColumnName("extra_fields");
    // Misma razón que WmsSapStageItem.ExtraFieldsJson (ver comentario ahí) -- sin esto Npgsql
    // manda el parámetro como texto plano y Postgres rechaza el INSERT/UPDATE.
    if (Database.IsNpgsql())
    {
        extraFields.HasColumnType("jsonb");
    }
    entity.HasIndex(e => new { e.CompanyId, e.Pk }).IsUnique().HasDatabaseName("ix_wms_sap_stage_store_company_pk");
    entity.HasIndex(e => e.Status).HasDatabaseName("ix_wms_sap_stage_store_status");
});
```

- [ ] **Step 3: Generar la migración (Postgres)**

Desde `src/Modulo.Wms`, con el provider activo apuntado a Postgres:

```bash
dotnet ef migrations add AddWmsSapStageStorePkAndExtraFields --context WmsDbContext
```

**Antes de correr esto**, verificar que no hay cambios de modelo NO relacionados sin commitear
en el working tree (mismo problema real que contaminó una migración de Items — ver
`WmsDbContextModelSnapshot.cs` en el historial de Items) — si los hay, hacer `git stash` de lo
ajeno primero, generar la migración, y recién después `git stash pop`.

- [ ] **Step 4: Confirmar que la migración generada tiene `jsonb` para `extra_fields`, no `text`**

Abrir el archivo de migración generado y el `WmsDbContextModelSnapshot.cs` actualizado —
confirmar que ambos dicen `jsonb` para la columna `extra_fields` de `wms_sap_stage_store` (no
`text`) y `character varying(50)` (o similar) para `pk`.

- [ ] **Step 5: Aplicar la migración contra la BD real de "Depor (Testing)"**

`ps_comdepor_wms_qa` (mismo host/credenciales usadas en toda la Ronda de Items — pedir al
controller si no las tenés a mano, no hardcodear secretos nuevos en el repo).

- [ ] **Step 6: Compilar y correr la suite existente**

`dotnet build src/Modulo.Wms/Modulo.Wms.csproj` y `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj`
van a fallar en los archivos que todavía referencian `CardCode`/`CardName` (Writer, Reader,
WmsExistsReconciler, WmsTransaccionService, tests existentes) — **NO los arregles en este
task**, eso es Task 3. Reportar la lista de errores de compilación como evidencia de que el
modelo cambió correctamente; no hace falta que compile al final de este task.

- [ ] **Step 7: Commit**

```bash
git add src/Modulo.Wms/Models/WmsSapStageStore.cs src/Modulo.Wms/Data/WmsDbContext.cs src/Modulo.Wms/Migrations/
git commit -m "feat: agregar Pk y extra_fields a WmsSapStageStore"
```

---

### Task 3: Writer + Reader + `WmsExistsReconciler` — upsert por `Pk`, reenvío por `wms_validation_fields`

**Files:**
- Modify: `src/Modulo.Wms/Services/WmsSapStageStoreWriter.cs`
- Modify: `src/Modulo.Wms/Services/WmsSapStageStoreReader.cs`
- Modify: `src/Modulo.Wms/Services/WmsExistsReconciler.cs` (línea ~118-121, la llamada a
  `ProcesarEntidadAsync` para `"Store"` usa `f => f.CardCode` — cambiar a `f => f.Pk`)
- Modify: `src/Modulo.Wms/Services/WmsTransaccionService.cs` (usa `x.CardCode` como
  `Documento` para `EnvioSucursal` — cambiar a `x.Pk`, y quitar el reset que tocaba
  `CardCode`/`CardName` si aplica)
- Test: `tests/Modulo.Wms.Tests/Services/WmsSapStageStoreWriterTests.cs` (existe, reescribir)
- Test: `tests/Modulo.Wms.Tests/Services/WmsSapStageStoreReaderTests.cs` (existe, reescribir)

**Interfaces:**
- Consume: `WmsSapStageStore.Pk`/`ExtraFieldsJson` (Task 2).
- Consume: `WmsValidationField` (ya existe, usado por Items — filtrar
  `TipoEntidad == "Store"`).
- Produce: `IntegrationRecord` con clave `"PK"` (mayúsculas, como en la Query) +
  `"SourceUpdateDate"` + todas las claves de `extra_fields` — consumido por Task 4
  (`WmsCloudConnector`).

- [ ] **Step 1: Reescribir `WmsSapStageStoreWriter.cs`**

Mismo patrón que `WmsSapStageItemWriter.EscribirAsync`/`SepararCampos`, pero SIN los 2 campos
identidad especiales de Item (`item_alternate_code`→clave se llama `"PK"` acá también, pero no
hay `description`/`barcode` para separar — Store no tiene campos "casi-identidad" que Items sí
tenía antes de esta decisión, así que TODO lo que no sea `PK` va a `extra_fields`):

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Escritor del motor genérico (IIntegrationEntityWriter) para Sucursales/Tiendas que llegan
/// desde SAP vía SqlDirectConnector -- upsert por Pk (clave de negocio que la propia Query SQL
/// calcula, ver plan 2026-08-23-ingesta-sql-directa-sucursal.md). Mismo criterio que
/// WmsSapStageItemWriter: Pendiente si nuevo, reenvío solo si cambió un campo listado en
/// wms_validation_fields (TipoEntidad="Store").
/// </summary>
public class WmsSapStageStoreWriter : IIntegrationEntityWriter
{
    private readonly WmsDbContext _contexto;

    public WmsSapStageStoreWriter(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Store";

    public async Task EscribirAsync(Guid companyId, IReadOnlyList<IntegrationRecord> registros, CancellationToken cancellationToken)
    {
        var pks = registros.Select(r => (string)r["PK"]!).ToList();
        var existentes = await _contexto.WmsSapStageStores
            .Where(f => f.CompanyId == companyId && pks.Contains(f.Pk))
            .ToDictionaryAsync(f => f.Pk, cancellationToken);

        var camposValidacion = (await _contexto.ValidationFields
            .Where(v => v.CompanyId == companyId && v.TipoEntidad == "Store" && v.IsActive)
            .Select(v => v.FieldName)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var registro in registros)
        {
            var pk = (string)registro["PK"]!;
            var sourceUpdateDate = (DateTime)registro["SourceUpdateDate"]!;
            var existente = existentes.GetValueOrDefault(pk);

            var extra = registro.Fields
                .Where(kvp => kvp.Key is not ("PK" or "SourceUpdateDate"))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var extraFieldsJson = System.Text.Json.JsonSerializer.Serialize(extra);

            if (existente is null)
            {
                _contexto.WmsSapStageStores.Add(new WmsSapStageStore
                {
                    CompanyId = companyId,
                    Pk = pk,
                    ExtraFieldsJson = extraFieldsJson,
                    SourceUpdateDate = sourceUpdateDate,
                    Status = WmsSapStageStatus.Pendiente,
                });
                continue;
            }

            var extraFieldsExistentes = string.IsNullOrEmpty(existente.ExtraFieldsJson)
                ? new Dictionary<string, object?>()
                : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(existente.ExtraFieldsJson)!;
            var extraFieldsNuevos = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(extraFieldsJson)!;

            var cambioAlgunCampoDeValidacion = camposValidacion.Any(campo => ValorCambio(extraFieldsExistentes, extraFieldsNuevos, campo));

            if (cambioAlgunCampoDeValidacion || existente.Status == WmsSapStageStatus.ErrorWms)
            {
                existente.Status = WmsSapStageStatus.Pendiente;
                existente.ErrorMsg = null;
            }

            existente.ExtraFieldsJson = extraFieldsJson;
            existente.SourceUpdateDate = sourceUpdateDate;
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }

    private static bool ValorCambio(Dictionary<string, object?> existentes, Dictionary<string, object?> nuevos, string campo)
    {
        var tieneExistente = existentes.TryGetValue(campo, out var valorExistente);
        var tieneNuevo = nuevos.TryGetValue(campo, out var valorNuevo);
        if (!tieneExistente && !tieneNuevo) return false;
        return valorExistente?.ToString() != valorNuevo?.ToString();
    }
}
```

- [ ] **Step 2: Reescribir `WmsSapStageStoreReader.cs`**

Mismo patrón que `WmsSapStageItemReader.LeerPendientesAsync`/`MarcarProcesadoAsync`:

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

public class WmsSapStageStoreReader : IIntegrationEntityReader
{
    private const int MaximoPorCicloPorDefecto = 500;

    private readonly WmsDbContext _contexto;

    public WmsSapStageStoreReader(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "SapWms.Store.Subida";

    public async Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, int? limiteMaximo, CancellationToken cancellationToken)
    {
        var maximoPorCiclo = limiteMaximo is > 0 ? limiteMaximo.Value : MaximoPorCicloPorDefecto;
        var filas = await _contexto.WmsSapStageStores
            .Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Pendiente)
            .OrderBy(f => f.CreatedAt)
            .Take(maximoPorCiclo)
            .ToListAsync(cancellationToken);

        return filas
            .Select(f =>
            {
                var campos = new Dictionary<string, object?>
                {
                    ["TipoDocumento"] = "Store",
                    ["PK"] = f.Pk,
                    ["_StagingLineIds"] = new List<long> { f.LineId },
                };

                if (!string.IsNullOrEmpty(f.ExtraFieldsJson))
                {
                    var extra = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(f.ExtraFieldsJson)!;
                    foreach (var (clave, valor) in extra)
                    {
                        campos[clave] = valor;
                    }
                }

                return new IntegrationRecord(campos);
            })
            .ToList();
    }

    public async Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken)
    {
        var idsDeLinea = (List<long>)registro["_StagingLineIds"]!;

        var filas = await _contexto.WmsSapStageStores
            .Where(f => idsDeLinea.Contains(f.LineId))
            .ToListAsync(cancellationToken);

        foreach (var fila in filas)
        {
            fila.Status = exito ? WmsSapStageStatus.Enviado : WmsSapStageStatus.ErrorWms;
            fila.ErrorMsg = exito ? null : mensajeError;
            fila.SyncedAt = exito ? DateTimeOffset.UtcNow : fila.SyncedAt;

            if (exito)
            {
                await ResetearValidacionAsync(companyId, fila.Pk, cancellationToken);
            }
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }

    private async Task ResetearValidacionAsync(Guid companyId, string clave, CancellationToken cancellationToken)
    {
        var validacion = await _contexto.WmsExportValidations
            .FirstOrDefaultAsync(v => v.CompanyId == companyId && v.TipoDoc == "Store" && v.Clave == clave, cancellationToken);
        if (validacion is null)
        {
            validacion = new WmsExportValidation { CompanyId = companyId, TipoDoc = "Store", Clave = clave };
            _contexto.WmsExportValidations.Add(validacion);
        }

        validacion.Intentos = 0;
        validacion.WmsErrorMsg = null;
        validacion.ValidadoEn = null;
        validacion.WmsStatusId = null;
        validacion.EnviadoEn = DateTimeOffset.UtcNow;
    }
}
```

- [ ] **Step 3: Actualizar `WmsExistsReconciler.cs`**

En el bloque `ProcesarEntidadAsync(..., "Store", "facility", "stage_store", "code", ...)`,
cambiar el selector de clave de `f => f.CardCode` a `f => f.Pk`. El resto de los argumentos
(`entidadFinal="facility"`, `keyField="code"`) no cambian — siguen siendo el nombre del campo
del lado de Oracle, no del lado de nuestra tabla.

- [ ] **Step 4: Actualizar `WmsTransaccionService.cs`**

En el caso `WmsTipoTransaccion.EnvioSucursal` del switch de `BuscarAsync`, cambiar
`Documento = x.CardCode` a `Documento = x.Pk`. Revisar si hay algún otro uso de
`CardCode`/`CardName` de `WmsSapStageStore` en este archivo (el reset de status no debería
tocar esos campos, solo `Status`/`ErrorMsg`, así que no debería requerir cambios ahí).

- [ ] **Step 5: Escribir/actualizar tests**

`WmsSapStageStoreWriterTests.cs`: casos mínimos —
1. Alta nueva (Pk no existe) → status Pendiente, extra_fields tiene todas las claves salvo
   PK/SourceUpdateDate.
2. Reenvío: cambia un campo listado en `wms_validation_fields` para esa compañía → status
   vuelve a Pendiente.
3. No-reenvío: cambia un campo NO listado → status se mantiene (ej. sigue ProcesadoWms), pero
   el dato en `extra_fields` se actualiza igual.
4. Reenvío forzado si status actual es ErrorWms, sin importar si cambió algo.

`WmsSapStageStoreReaderTests.cs`: casos mínimos —
1. `LeerPendientesAsync` reconstruye correctamente `PK` + todas las claves de `extra_fields`.
2. Límite por ciclo (`limiteMaximo`) se respeta — mismo test que ya existe para Items
   (`LeerPendientesAsync_ConMasDe500PendientesDelMismoTipo_TopaEn500PorCiclo`), adaptado.
3. `MarcarProcesadoAsync` con éxito pone Enviado + resetea validación; con error pone ErrorWms.

- [ ] **Step 6: Compilar y correr toda la suite**

`dotnet build` y `dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj` — deben quedar
0 errores. Puede seguir habiendo fallas relacionadas a `WmsCloudConnector` (Task 4) — anotarlas
en el report, no arreglarlas acá.

- [ ] **Step 7: Commit**

```bash
git add src/Modulo.Wms/Services/WmsSapStageStoreWriter.cs src/Modulo.Wms/Services/WmsSapStageStoreReader.cs src/Modulo.Wms/Services/WmsExistsReconciler.cs src/Modulo.Wms/Services/WmsTransaccionService.cs tests/Modulo.Wms.Tests/Services/WmsSapStageStoreWriterTests.cs tests/Modulo.Wms.Tests/Services/WmsSapStageStoreReaderTests.cs
git commit -m "feat: Writer/Reader de Sucursal upsertean por Pk y reenvían por wms_validation_fields"
```

---

### Task 4: `WmsCloudConnector` — nodo dinámico de Sucursal

**Files:**
- Modify: `src/Modulo.Wms/Services/WmsCloudConnector.cs`
- Test: `tests/Modulo.Wms.Tests/Services/WmsCloudConnectorTests.cs` (existe, agregar casos)

**Interfaces:**
- Consume: `IntegrationRecord` con `"PK"` + claves dinámicas de Sucursal (Task 3).

- [ ] **Step 1: Reemplazar el caso `"Store"` fijo por uno dinámico**

En `ArmarXmlLote`, el switch que hoy tiene:

```csharp
"Store" => new XElement(nombreItem,
    CampoXml(mapeos, "SAPWMS_STORE", "code", registro, registro["CardCode"]),
    CampoXml(mapeos, "SAPWMS_STORE", "name", registro, registro["CardName"]),
    CampoXml(mapeos, "SAPWMS_STORE", "parent_company_id", registro, config.ParentCompanyCode)),
```

pasa a:

```csharp
"Store" => ArmarNodoStoreDinamico(registro, config, mapeos),
```

- [ ] **Step 2: Agregar `ArmarNodoStoreDinamico`**

Cerca de `ArmarNodoItemDinamico`, agregar `"PK"` a `ClavesInternasExcluidas` (hoy tiene
`"TipoDocumento", "_StagingLineIds", "SourceUpdateDate"` — agregar `"PK"` porque no es un campo
de negocio real, es la clave del motor, y ya se emite explícitamente como `"code"` abajo):

```csharp
/// <summary>
/// Igual que ArmarNodoItemDinamico, pero Sucursal tiene dos casos especiales que no son
/// claves de extra_fields: "code" sale de la clave de motor PK (no es un campo de negocio, es
/// la clave de upsert -- ver WmsSapStageStoreWriter), y "parent_company_id" sale de la config
/// del conector, no del registro.
/// </summary>
private static XElement ArmarNodoStoreDinamico(IntegrationRecord registro, WmsCloudConfig config, IReadOnlyDictionary<(string MapperKey, string FieldName), string> mapeos)
{
    var elementos = new List<XElement>
    {
        CampoXml(mapeos, "SAPWMS_STORE", "code", registro, registro["PK"]),
        CampoXml(mapeos, "SAPWMS_STORE", "parent_company_id", registro, config.ParentCompanyCode),
    };

    elementos.AddRange(registro.Fields
        .Where(kvp => !ClavesInternasExcluidas.Contains(kvp.Key))
        .Select(kvp => CampoXml(mapeos, "SAPWMS_STORE", kvp.Key.ToLowerInvariant(), registro, kvp.Value)));

    return new XElement("store", elementos);
}
```

(Confirmar el nombre real del elemento XML esperado por Oracle -- el código actual usaba
`nombreItem`, una variable ya resuelta más arriba en `ArmarXmlLote` según `tipoDocumento`;
usar esa misma variable en vez de literal `"store"` si `nombreItem` ya trae el valor correcto
para este tipo de documento -- revisar el código real antes de asumir.)

- [ ] **Step 3: Agregar `"PK"` a `ClavesInternasExcluidas`**

```csharp
private static readonly HashSet<string> ClavesInternasExcluidas = new(StringComparer.OrdinalIgnoreCase)
{
    "TipoDocumento", "_StagingLineIds", "SourceUpdateDate", "PK",
};
```

(Esto afecta también a `ArmarNodoItemDinamico`, que ya usa este mismo set — confirmar que
Items no tiene una clave real llamada `"PK"` que se rompería al excluirla; no debería, Items
usa `item_alternate_code` como clave, no `PK`.)

- [ ] **Step 4: Tests**

Agregar a `WmsCloudConnectorTests.cs`:
1. `PushAsync` con un registro Store con 5+ claves dinámicas en extra_fields → el XML generado
   tiene un nodo por cada clave (salvo las internas), más `code` (=PK) y `parent_company_id`.
2. Regresión: `PK` no aparece como nodo XML literal (mismo tipo de test que ya existe para
   `SourceUpdateDate` en Item — `PushAsync_ConSourceUpdateDateEnElRegistro_NoLoIncluyeComoNodoXml`).

- [ ] **Step 5: Compilar y correr TODA la suite**

`dotnet build src/Modulo.Wms/Modulo.Wms.csproj` y
`dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj` — 0 errores, 0 fallas.

- [ ] **Step 6: Commit**

```bash
git add src/Modulo.Wms/Services/WmsCloudConnector.cs tests/Modulo.Wms.Tests/Services/WmsCloudConnectorTests.cs
git commit -m "feat: XML dinámico de Sucursal en WmsCloudConnector"
```

---

### Task 5: Verificación end-to-end contra HANA real + WMS Cloud real

**Files:** ninguno de producto — este task es de configuración/datos + verificación en vivo,
igual que Task 11 del plan de Items.

- [ ] **Step 1: Publicar el plugin**

`publish-dist.ps1` (detiene el Host, compila Release, publica a `dist/`).

- [ ] **Step 2: Seed de `wms_validation_fields` para `TipoEntidad="Store"`**

Insertar (vía script contra la BD real o UI si existe) al menos: `name`, `city`, `zip` — campos
que el SP legado (`Store.txt` líneas 78-82) ya usa como disparador de reenvío. Ajustar según lo
que confirme el spike del Task 1 (nombres reales de columnas del extra_fields).

- [ ] **Step 3: Crear la `IntegrationDefinition` de Bajada**

`ConectorTipo=Sql`, `Direccion=Bajada`, `EntidadNegocio=SapWms.Store`, `ModuloOrigen=Wms`,
Query = la validada en Task 1. Mismo mecanismo que se usó para crear
"WMS - Items (Bajada SQL)" (vía UI en `/Admin/Integraciones/Nuevo` o vía script directo si la
UI todavía no soporta bien el flujo — usar lo que ya haya funcionado para Items).

- [ ] **Step 4: Correr un ciclo de Bajada y confirmar en Transacciones**

Activar la integración, esperar/forzar un ciclo, y confirmar en `/wms/transacciones` (tab
Sucursales, una vez que Transacciones tenga soporte visual para Store — si no lo tiene todavía,
confirmar directo contra la tabla `wms_sap_stage_store`) que llegaron filas con `Pk` y
`extra_fields` poblado.

- [ ] **Step 5: Correr un ciclo de Subida y confirmar contra Oracle WMS Cloud real (`cd_test`)**

Activar la integración de Subida de Sucursal (crearla si no existe, mismo patrón que
"WMS - Items (Subida WMS)"), correr un ciclo con pocos registros primero, y confirmar que
Oracle responde éxito (no rechazo por campo no reconocido — mismo tipo de bug real que apareció
con Items en la Ronda anterior, "sourceupdatedate is not a valid field").

- [ ] **Step 6: Documentar hallazgos**

Si aparecen bugs reales (van a aparecer, pasó con Items 6 veces), corregirlos directamente
(no re-dispatchear un subagente para esto — mismo criterio que Task 11 de Items, el controller
corrige en caliente con guía del usuario) y dejar constancia en el ledger de este plan.
