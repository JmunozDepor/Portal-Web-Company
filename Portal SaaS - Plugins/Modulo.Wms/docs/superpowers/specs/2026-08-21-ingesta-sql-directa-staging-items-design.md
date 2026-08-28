# Ingesta SQL directa a staging completo (Items) — Diseño

**Alcance de esta ronda:** solo la entidad **Item**. Store, Traslado, Order y el resto
de entidades del flujo Bajada quedan explícitamente fuera — se implementan después,
reusando el mismo mecanismo, una vez que el flujo de Items esté funcionando y validado
en producción.

## Contexto y motivación

El flujo actual (Bajada SAP → staging → Subida WMS Cloud) mueve solo 3 campos de
negocio para Items (`ItemCode`, `ItemName`, `BarCode`), leídos vía SAP Service Layer
(OData). El sistema legado (`C:\PROYECTOS\WMS_Suite`) enviaba a Oracle WMS Cloud un
conjunto mucho más amplio de campos (~40, ver `SP_DEP_WMS_PROCESS_ITEMS` en HANA:
dimensiones, `season_code`, `brand_code`, jerarquías, `putaway_type`, etc.), varios de
ellos derivados de joins (`OITB`, `@GSP_BSSECCION`, `@NX_PUTAWAY_TYPE`) que Service
Layer no puede expresar como un simple `$filter`/`$select` — SL exige declarar de
antemano cada propiedad/recurso exacto, sin joins libres.

Además, el legado no marcaba un artículo para reenviar a WMS ante *cualquier* cambio en
SAP: su SP de UPDATE solo ponía `Status='PENDIENTE'` si cambiaba el **valor** de un
subconjunto específico de campos (`description`, `barcode`, `brand_code`,
`putaway_type`) — un cambio de precio o de otra clasificación no relevante para WMS no
disparaba reenvío. El sistema nuevo, tal como quedó tras la ronda de fixes anterior a
este spec, marca `Pendiente` con solo comparar `SourceUpdateDate` (cualquier cambio en
SAP dispara resync), lo cual es más agresivo de lo necesario.

## Decisiones ya tomadas (no reabrir sin motivo)

- **Staging sigue viviendo en nuestra base propia** (Postgres/SQL Server, tabla
  `wms_sap_stage_item`), no en HANA. Se evaluó adoptar el modelo del legado completo
  (staging en `STG_SAP_PRODUCTS` de HANA, poblado por el trigger/SP ya en producción) y
  se descartó explícitamente: depender de un trigger externo que no controlamos/no
  versionamos en este repo es un riesgo mayor que el trabajo de construir la ingesta acá.
- **La ingesta SAP→staging es por consulta SQL directa** (HANA/SQL Server, vía
  `IHanaService`, ya existente), no por Service Layer — evita repetir el patrón de
  adivinar nombres de propiedad OData que ya falló 3 veces esta sesión (`InvntItem`,
  `CodeBars`, casi con `UpdateDate`) para campos con joins.
- **Modelo de polling con cursor** (no event-driven/trigger). Se evaluó explícitamente
  replicar el modelo `sp_TransactionNotification` del legado y se descartó por ahora:
  requiere tocar SAP (crear/mantener un trigger), mientras que polling con cursor ya
  está construido, probado y no toca SAP en absoluto.
- **El armado del XML hacia WMS Cloud pasa a ser dinámico** (itera todas las claves del
  registro), no una lista fija de `CampoXml` por campo — así agregar un campo nuevo al
  flujo no requiere tocar `WmsCloudConnector`.
- **Qué campos disparan reenvío a WMS es configurable en una tabla nueva**
  (`wms_validation_fields`), no una lista hardcodeada en C# — arranca con el mismo
  conjunto que usaba el legado (`description`, `barcode`, `brand_code`,
  `putaway_type`), editable sin deploy.

## Arquitectura

```
SAP HANA/SQL Server           Nuestra plataforma (staging propio)         Oracle WMS Cloud
┌──────────────┐   SELECT     ┌─────────────────────────┐    XML POST    ┌──────────────┐
│ OITM/OITB/    │ ───────────▶│ wms_sap_stage_item        │ ─────────────▶│  LogFire      │
│ @GSP_BSSECCION│  (IHanaService,│  - item_code (fijo)      │  (dinámico,   │              │
│ @NX_PUTAWAY_  │  cursor por  │  - item_name (fijo)       │   itera todas │              │
│  TYPE         │  UpdateDate) │  - bar_code (fijo)        │   las claves) │              │
└──────────────┘              │  - extra_fields (jsonb)   │                └──────────────┘
                               └─────────────────────────┘
                                        ▲
                                        │ compara SOLO los campos listados
                                        │ en wms_validation_fields antes de
                                        │ marcar Status=Pendiente
                               ┌─────────────────────────┐
                               │ wms_validation_fields     │
                               │  (TipoEntidad, CampoJson) │
                               └─────────────────────────┘
```

Dos `IntegrationDefinition` independientes (igual que hoy):
- **Bajada** ("WMS - Items (Bajada SQL)"): `ConectorTipo=Sql`, `Direccion=Bajada`,
  `EntidadNegocio=SapWms.Item`.
- **Subida** ("WMS - Items (Subida WMS)"): `ConectorTipo=WmsCloud`, `Direccion=Subida`,
  `EntidadNegocio=SapWms.Item.Subida` — **sin cambios de diseño**, ya funciona; el único
  cambio ahí es que `WmsCloudConnector.ArmarXmlLote` pasa a iterar el registro completo
  en vez de 3 campos fijos.

## Componentes nuevos/modificados

### 1. `IIntegrationConnector` — nuevo `ConectorTipo=Sql`

`IntegrationConectorTipo` (enum en `IntegrationDefinition.cs`) gana el valor `Sql`.

Config del conector (JSON cifrado, igual patrón que `SapWmsOutboundConfig`):

```csharp
private sealed record SqlDirectConfig(string Query, int? PageSize = null);
```

`Query` es el `SELECT` completo, dialecto HANA (mismos joins que
`SP_DEP_WMS_PROCESS_ITEMS`), editable en `/Admin/Integraciones/Nuevo` igual que hoy es
editable `Filtro` para el conector Sap. Debe incluir un placeholder `:cursor` en el
`WHERE` para el cursor incremental (ver más abajo) — el conector lo reemplaza por un
parámetro real antes de ejecutar, nunca por interpolación de string cruda (evitar
inyección SQL, aunque la query la escribe un admin, no un usuario final).

Query de referencia (idéntica a la del SP legado, sin las columnas 100% constantes que
el legado nunca pobló con datos reales — ver sección "Campos fuera de alcance"):

```sql
SELECT
    UPPER(T0."ItemCode") AS "item_alternate_code",
    UPPER(REPLACE(T0."ItemName",'&','-')) AS "description",
    T0."CodeBars" AS "barcode",
    T0."BLength1" AS "unit_length",
    T0."BWidth1" AS "unit_width",
    T0."BHeight1" AS "unit_height",
    UPPER(TO_VARCHAR(T0."U_GSP_Season")) AS "season_code",
    UPPER(T2."ItmsGrpNam") AS "brand_code",
    UPPER(T2."ItmsGrpNam") AS "hierarchy1_code",
    UPPER(IFNULL(T3."Name",'')) AS "hierarchy2_code",
    UPPER(T0."U_GSP_SECTION") AS "hierarchy2_description",
    UPPER(T0."U_GSP_REFERENCE") AS "external_style",
    UPPER(SUBSTRING(REPLACE(T0."ItemName",'&','-'),1,30)) AS "short_descr",
    UPPER(IFNULL(T4."U_putaway_type",'')) AS "putaway_type",
    T0."UpdateDate" AS "SourceUpdateDate"
FROM "OITM" T0
    INNER JOIN "OITB" T2 ON T0."ItmsGrpCod" = T2."ItmsGrpCod"
    LEFT JOIN "@GSP_BSSECCION" T3 ON T3."Code" = T0."U_GSP_SECTION"
    LEFT JOIN "@NX_PUTAWAY_TYPE" T4 ON T4."U_ItmsGrpNam" = T2."ItmsGrpNam" AND T4."U_GSP_SECTION" = T3."Name"
WHERE T0."U_NX_EnviarWMS" = 'Y'
    AND T0."InvntItem" = 'Y'
    AND IFNULL(NULLIF(T0."CodeBars", '0'), '0') != '0'
    AND (:cursor IS NULL OR T0."UpdateDate" >= :cursor)
```

**Nota importante**: a diferencia de Service Layer, `InvntItem` y `CodeBars` SÍ son
nombres de columna válidos en SQL directo contra HANA (son las columnas reales de la
tabla `OITM`) — los errores de esta sesión (`Property 'InvntItem' of 'Item' is
invalid`, `Property 'CodeBars' of 'Item' is invalid`) fueron específicos de Service
Layer, que expone un modelo de objeto distinto al de las tablas físicas. Este punto se
verifica en la Tarea 1 del plan de implementación antes de asumirlo como cierto.

### 2. `IHanaService.QueryDynamicAsync` (nuevo método)

Agregar a `IHanaService`/`HanaService`:

```csharp
Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryDynamicAsync(
    string sqlParametrizado, object? parametros = null, CancellationToken ct = default);
```

Mismo patrón dual-motor (HANA/SqlServer vía `HanaToSqlServerTranslator`) que ya tiene
`QueryAsync<T>`, pero mapea cada fila a `Dictionary<string, object?>` por nombre de
columna (`reader.GetName(i)` → `reader.GetValue(i)`), sin necesitar un DTO fijo — no
reutiliza `RowReflectionMapper` (que exige un `T` con propiedades conocidas de
antemano).

### 3. `SqlDirectConnector : IIntegrationConnector` (nuevo)

- `Tipo => "Sql"`.
- `PullAsync`: reemplaza `:cursor` por `cursorIncremental` (o `DBNull`/null si es la
  primera corrida), ejecuta `QueryDynamicAsync`, y convierte cada fila en un
  `IntegrationRecord` que preserva **todas** las columnas devueltas, sin filtrar
  ninguna (la query ya decide qué trae). No hay `$select`/`$top` de por medio — el
  tamaño de página lo controla el propio `LIMIT`/`TOP` de la query si el admin lo
  agrega, o el `PageSize` del config se usa como paginación en memoria antes de
  entregarlo al motor genérico (a definir en plan, ver Tarea 3).
- `DescribirConsulta`: devuelve la query resuelta (con el cursor ya interpolado, solo
  para mostrar en Bitácora — la ejecución real sigue parametrizada).
- `PushAsync`: `throw NotSupportedException` — este conector es solo Bajada, igual
  criterio que `WmsCloudConnector.PullAsync` para Subida.

### 4. `wms_sap_stage_item` — nueva columna `extra_fields JSONB`

Migración Postgres + SqlServer (JSONB en Postgres, `nvarchar(max)` con
`OwnsOne`/serialización manual en SQL Server — mismo patrón dual-motor que el resto del
proyecto).

`WmsSapStageItem` (entidad EF) gana:

```csharp
public string? ExtraFieldsJson { get; set; } // serializado, no tipado -- ver writer
```

Campos identidad (`item_code`, `item_name`, `bar_code`) **se mantienen como columnas
tipadas** — el motor los necesita para la clave de upsert y no cambian.

### 5. `wms_validation_fields` (tabla nueva)

```
CompanyId    Guid       (FK a Companies, igual patrón que wms_oracle_field_mappings)
TipoEntidad  string     -- "Item", "Store", ... (para cuando se extienda)
FieldName    string     -- nombre de la clave dentro del registro (columna tipada o
                        -- clave de extra_fields, mismo espacio de nombres)
IsActive     bool
```

Índice único `(CompanyId, TipoEntidad, FieldName)`, igual convención que
`wms_oracle_field_mappings`. Seed inicial (vía migración de datos o script, a definir
en plan) para `TipoEntidad="Item"`: `description`, `barcode`, `brand_code`,
`putaway_type`.

### 6. `WmsSapStageItemWriter` — reescritura de la lógica de resync

Reemplaza la comparación por `SourceUpdateDate` por comparación de **valores**:

1. Carga la lista de campos de validación activos para `(CompanyId, "Item")` desde
   `wms_validation_fields` (una vez por lote, no por registro — mismo criterio que ya
   usa `WmsCloudConnector.PushAsync` para cargar `wms_oracle_field_mappings`).
2. Para cada registro entrante, compara — SOLO para las claves listadas como campo de
   validación — el valor nuevo contra el valor ya guardado (columna tipada si es
   `item_name`/`bar_code`, o clave dentro de `extra_fields` si es cualquier otro campo).
3. Si **alguno** de esos campos cambió (o el registro es nuevo), `Status=Pendiente`. Si
   ninguno cambió, actualiza igual todos los datos en staging (mantenerlos frescos) pero
   **no** toca `Status` — así no se dispara un reenvío innecesario a WMS por un cambio
   irrelevante (ej. precio).
4. Sigue aplicando el batching por `TamanoLoteEscritura` ya construido
   (`IntegrationSyncHostedService`) — sin cambios ahí.

### 7. `WmsCloudConnector.ArmarXmlLote` — caso `"Item"` pasa a ser dinámico

En vez de las 3 líneas `CampoXml(...)` hardcodeadas, itera todas las claves del
`IntegrationRecord` (excluyendo las internas: `TipoDocumento`, `_StagingLineIds`),
generando `XElement(clave.ToLower(), valor)` por cada una — mismo criterio que
`WmsOutbound_ItemProcessor.GenerateDynamicXml` del legado. El mecanismo de
`wms_oracle_field_mappings` (override de plantilla por campo) se mantiene intacto,
aplicado sobre cada clave dinámica en vez de solo las 3 fijas de antes.

### 8. `WmsSapStageItemReader` — vuelca `extra_fields` al `IntegrationRecord`

`LeerPendientesAsync` deserializa `ExtraFieldsJson` y agrega cada clave al diccionario
del registro, además de las 3 columnas fijas de siempre.

## Campos fuera de alcance (constantes en el legado, no se traen de SAP)

El legado hardcodeaba a `1` (o valores fijos como `'UNITS'`, `'R'`, `4`, `5`) casi 30 de
sus ~40 campos (`unit_cost`, `unit_weight`, `unit_volume`, `retail_price`, `net_cost`,
todos los `std_pack_*`/`std_case_*`, `dimension1-3`, `product_life`,
`percent_acceptable_product_life`, `lpns_per_tier`, `tiers_per_pallet`,
`req_batch_nbr_flg`, `serial_nbr_tracking`, `regularity_code`, `min_dispatch_uom`,
`company_code`, `part_a`, `action_code`). Esta ronda **no** replica esos campos
constantes — si Oracle WMS Cloud los requiere, se agregan como valores fijos
directamente en la `Query` del conector (`SELECT 'DEPOR' AS company_code, ...`, igual
que hacía el legado), sin necesitar código nuevo, dado el diseño dinámico. Confirmar con
el equipo de WMS si alguno de estos es realmente obligatorio antes de la puesta en
producción (fuera del alcance de este spec, se resuelve como config).

## No-goals explícitos de esta ronda

- Store, Traslado, Order/Picking: quedan con el mecanismo actual (Service Layer) hasta
  que Items esté validado en producción con el modelo nuevo.
- Modelo event-driven/trigger (`sp_TransactionNotification`): no se implementa.
- Verificación de si `InvntItem`/`CodeBars` funcionan en SQL directo (se asume que sí,
  por ser columnas físicas reales de `OITM`, pero se verifica en Tarea 1 del plan antes
  de depender de eso).
- Campos constantes del legado (ver sección anterior): no se replican automáticamente.

## Riesgos conocidos

- **Ejecutar SQL arbitrario editable por un admin contra SAP** es más potente (y más
  peligroso si se configura mal) que un filtro OData acotado — un `SELECT` mal escrito
  puede ser lento o traer de más. Mitigación: `DescribirConsulta` siempre muestra la
  query real que corrió (ya existe este mecanismo), y el `PageSize`/paginación en
  memoria evita que un resultado gigante sature la escritura (mismo problema ya resuelto
  esta sesión para el conector Sap).
- **Credenciales de conexión directa a HANA/SQL Server** (`SapConnectionStringFactory`)
  reutilizan las mismas credenciales técnicas ya configuradas por `Instance` — no se
  agrega superficie nueva de secretos.
- **`extra_fields JSONB`** no tiene validación de esquema — un campo con nombre mal
  escrito en la `Query` o en `wms_validation_fields` falla silenciosamente (la
  comparación nunca encuentra el campo, nunca dispara `Pendiente`). Mitigación:
  cobertura de tests unitarios para el writer con nombres de campo típicos, y que
  `DescribirConsulta`/Bitácora sigan mostrando exactamente qué se trajo.
