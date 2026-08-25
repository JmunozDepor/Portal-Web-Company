# Ingesta SQL Directa — Sucursal, Órdenes, Ingresos ASN

> **Para agentes:** REQUIERE SUB-SKILL: superpowers:subagent-driven-development para ejecutar
> este plan tarea por tarea, igual que se hizo con Items
> (`.superpowers/sdd/2026-08-21-ingesta-sql-directa-staging-items/progress.md`).
> Decisiones A-F ya confirmadas (23 ago 2026, ver sección "Decisiones"). Sucursal tiene su
> Query lista (Anexo) y puede arrancar ya. Órdenes e Ingresos ASN necesitan un spike contra
> HANA real primero (Query compleja, UNION de 3 fuentes cada una) — no arrancar esas dos sin
> pasar por ese spike.

**Objetivo:** Replicar para Sucursal (`WmsSapStageStore`), Órdenes (`WmsSapStageOrderHdr`/`Dtl`)
e Ingresos ASN (`WmsSapStageInboundHdr`/`Dtl`) el mismo patrón que ya funciona en producción
para Items: `SqlDirectConnector` (consulta SQL directa a HANA, sin Service Layer) + columnas
identidad tipadas + `extra_fields` (jsonb) para todo lo demás + `wms_validation_fields`
configurable por entidad para decidir reenvío.

**Arquitectura de referencia (Items, ya en producción):** ver
`docs/superpowers/specs/2026-08-21-ingesta-sql-directa-staging-items-design.md` y el código real
en `WmsSapStageItemWriter.cs` / `WmsSapStageItemReader.cs` / `WmsDbContext.cs` (mapeo de
`extra_fields`) / `WmsCloudConnector.ArmarNodoItemDinamico`.

---

## Diferencia clave frente a Items: header + detalle

Items es una tabla plana (1 fila = 1 producto). Sucursal también es plana (1 fila = 1
sucursal) — mapea 1:1 con el patrón de Items sin cambios de diseño.

Órdenes e Ingresos ASN, en cambio, son **header + detalle** (`WmsSapStageOrderHdr` +
`WmsSapStageOrderDtl`, `WmsSapStageInboundHdr` + `WmsSapStageInboundDtl`). Hoy sus Writers
(`WmsSapStageOrderWriter.cs`, `WmsSapStageInboundWriter.cs`) esperan un `IntegrationRecord` de
header con una clave `"Lineas"` = `List<IntegrationRecord>` anidada — eso viene del conector
viejo por Service Layer (OData `$expand`). `SqlDirectConnector` en cambio devuelve filas
**planas** (una consulta SQL = una lista de diccionarios, sin anidamiento).

## Decisiones — RESUELTAS (22 ago 2026, en base a `C:\PROYECTOS\captura\sql\*.txt`)

Se leyeron los 4 SP legados reales (`SP_DEP_WMS_PROCESS_ITEMS/STORES/PICKING/INBOUND`). Cambian
varias cosas respecto del borrador inicial de este plan:

**A. Convención `PK` / `LineaPK` (reemplaza la idea original de "columnas identidad tipadas
por entidad").** En vez de que cada Writer sepa cuál es la clave de negocio de su entidad
(distinto criterio por tabla: `CardCode` solo, `CardCode+LineNum`, `OrderNbr` calculado con
`CASE`, etc.), **toda Query de Bajada debe exponer una columna `"PK"`** con la clave ya resuelta
como corresponda a esa entidad (la misma expresión que ya usa el SP legado). Ejemplo real,
Sucursal (línea 25 de `Store.txt`):

```sql
UPPER(REPLACE(T0."CardCode", '-', '') || '-' || CAST(T1."LineNum" AS NVARCHAR)) AS "PK"
```

Para header+detalle, las líneas exponen además `"LineaPK"` (en Picking ya existe como
`seq_nbr`/`TD."LineNum"`, en Inbound como `D."LineNum"`/`D."U_DocLine"`). El motor genérico
(Writer/Reader) upsertea por `PK` sin necesitar lógica propia por entidad — mismo principio que
`extra_fields`: la Query decide, el código genérico no. `PK`/`LineaPK` se excluyen de
`extra_fields` igual que `TipoDocumento`/`_StagingLineIds`/`SourceUpdateDate` hoy.

Consecuencia: las columnas hoy tipadas en `WmsSapStageStore`/`OrderHdr`/`InboundHdr` (`CardCode`,
`CardName`, `OrderNbr`, etc.) **dejan de necesitar tipo propio** — con `PK` alcanza para el
upsert, y todo lo demás baja a `extra_fields` (mismo criterio ya acordado para `item_name` en
Items). Único campo tipado nuevo por header: `Pk` (string) + `SourceUpdateDate` (cursor) +
las columnas de control (`Status`/`RetryCount`/`ErrorMsg`/`CreatedAt`/`SyncedAt`). Detalle:
`LineaPk` (string) + `ExtraFieldsJson`.

**B. Cursor de polling: `U_NX_UPDATEDATE`, un único nombre de campo en TODAS las tablas de
cabecera de documento SAP** (confirmado por el usuario, 22 ago 2026 — es el UDF que SAP usa
hoy como registro de cambio a nivel de cabecera, ya que los documentos no tienen una columna de
fecha nativa confiable para esto). Se usa igual en `OCRD` (Sucursal), `ORDR`/`OINV`/`OWTQ`
(Picking) y `OWTQ`/`ORRR`/`@HCO_CONTENEDOR_CAB` (Inbound) — **una** convención, sin variar por
tabla. El filtro de cursor se aplica **dentro de cada rama de la UNION** de header (no se puede
aplicar afuera, porque afuera ya no hay una tabla concreta a la que apuntar).

**C. Forma de la Query para Órdenes / Ingresos ASN (header+detalle): una sola Query
desnormalizada, 1 fila = 1 línea de detalle**, columnas de header repetidas por fila — **RESUELTO,
así queda**. El Writer agrupa las filas planas por `PK` en memoria, arma 1 header (desde la
primera fila del grupo) + N líneas (una por fila del grupo, usando `LineaPK`).

## Decisiones — todavía abiertas

**D. `extra_fields` en detalle, no solo en header.**

Para Órdenes/ASN, los campos dinámicos pueden aparecer tanto a nivel de header (ej. canal,
centro de costo) como de línea (ej. lote, número de serie, UDF de línea — el legado
`SP_DEP_WMS_PROCESS_ITEMS` ya mostraba este patrón para line-level UDFs). Recomendado: agregar
`extra_fields` (jsonb) tanto a `WmsSapStageOrderHdr`/`InboundHdr` como a
`WmsSapStageOrderDtl`/`InboundDtl` — mismo mecanismo, dos niveles.

**D. RESUELTO (23 ago 2026): solo header.** `extra_fields` únicamente en
`WmsSapStageOrderHdr`/`WmsSapStageInboundHdr`. Las líneas (`Dtl`) se quedan con sus columnas
tipadas actuales (`ItemCode`, `Quantity`, `WhsCode`, etc.), sin `extra_fields` por ahora (YAGNI
hasta que aparezca un caso real de campo dinámico de línea).

**E. RESUELTO (23 ago 2026): misma lógica que Items, Bajada Y Subida dinámicas.** No hay
Fase 1/Fase 2 — se hace todo junto, igual que Items. Esto amplía el alcance original de este
plan: además de poblar `extra_fields` en Bajada, `WmsCloudConnector` pasa a armar el XML de
Sucursal/Órdenes/Ingresos ASN **dinámicamente** en vez de con nodos fijos hardcodeados —
mismo patrón que `ArmarNodoItemDinamico` hoy.

Alcance concreto por entidad:
- **Sucursal:** nueva `ArmarNodoStoreDinamico` (reemplaza el caso `"Store"` inline en
  `ArmarXmlLote`) — itera `registro.Fields` igual que Item, ya que Sucursal no tiene detalle.
- **Órdenes:** `ArmarNodoOrder` se separa en header dinámico (itera `extra_fields` del header,
  reemplaza los `CampoXml(..., registro["OrderType"])` fijos de hoy) + detalle **sigue fijo**
  (las líneas no tienen `extra_fields` por la Decisión D, así que `order_dtl` se sigue armando
  con las columnas tipadas de `WmsSapStageOrderDtl` como hoy).
- **Ingresos ASN:** mismo criterio que Órdenes — `ib_shipment_hdr` dinámico, `ib_shipment_dtl`
  fijo.
- El mecanismo de `wms_oracle_field_mappings` (override de plantilla por campo) sigue aplicando
  igual, solo que ahora sobre el set completo de claves dinámicas del header, no solo las 3-5
  fijas de antes — mismo criterio que ya se aplicó para `SAPWMS_ITEM`.

**F. RESUELTO (23 ago 2026): vista SQL vs. query inline no cambia nada de esto.**
`SqlDirectConnector` ejecuta el texto de `Query` tal cual contra HANA sin importarle si es un
`SELECT` plano o un `SELECT ... FROM vista` — mismo mecanismo que ya usa Items, sin cambios de
código. Queda a criterio de quien arme cada Query (view o inline) según qué tan compleja sea —
para Sucursal alcanza con query inline (ya está más abajo), para Órdenes/Ingresos ASN se
recomienda vista por la complejidad del UNION de 3 fuentes, pero es una decisión de
implementación, no de arquitectura.

---

## Alcance de este plan

Con la convención `PK`/`LineaPK` (decisión A), el modelo de columnas tipadas se simplifica y
queda igual para las 3 entidades:

- **Header:** `Pk` (string, clave de upsert), `SourceUpdateDate` (cursor, desde
  `U_NX_UPDATEDATE`), `Status`/`RetryCount`/`ErrorMsg`/`CreatedAt`/`SyncedAt` (control, ya
  existen), `ExtraFieldsJson` (nuevo, jsonb).
- **Detalle (Órdenes/ASN):** `LineaPk` (string), `ParentId` (ya existe), `ExtraFieldsJson`
  (nuevo, jsonb, sujeto a la decisión D todavía abierta).

Esto **reemplaza** las columnas tipadas actuales (`CardCode`, `CardName`, `OrderNbr`,
`OrderType`, `SapDocEntry`, `ShipmentType`, `ItemCode`, `Quantity`, `WhsCode`, etc.) — todas
pasan a vivir en `extra_fields`, mismo criterio acordado para `item_name`/`bar_code` en Items.
Implica una migración que agrega `Pk`/`LineaPk`/`ExtraFieldsJson` y, en un paso aparte,
deprecar las columnas viejas (no borrarlas en la misma migración que las agrega — dejarlas un
ciclo sin uso y confirmar en Transacciones/Confirmaciones que nada las lee antes de un DROP).

### Entidad: Sucursal (`WmsSapStageStore`) — Query resuelta, ver abajo

- **`wms_validation_fields`** seed sugerido para `TipoEntidad="Store"`: `name`, `city`, `zip`
  (a confirmar — son los únicos que el propio SP legado usa como disparador de UPDATE, línea
  78-82 de `Store.txt`).

### Entidad: Órdenes (`WmsSapStageOrderHdr` + `WmsSapStageOrderDtl`) — Query pendiente de spike

Fuente real (`Picking.txt`): header sale de una unión `ORDR` (pedido, ObjType 17) ∪ `OINV`
(factura, ObjType 13) ∪ `OWTQ` (traslado, ObjType 1250000001), filtrado por
`OPKL."Status" = 'R'` (lista de picking liberada) vía `PKL1`. Detalle sale de la unión
correspondiente `RDR1`/`INV1`/`WTQ1`. `order_nbr` ya es una expresión calculada
(`CASE WHEN ObjType='17' THEN ... || AbsEntry ELSE ... END`) — pasa directo a ser el `PK`.

- **`wms_validation_fields`** seed sugerido para `TipoEntidad="Order"`: a definir una vez que el
  spike confirme qué campos van a `extra_fields` (candidatos por lo que ya dispara reenvío
  implícito en el legado: fechas del pedido, `dest_dept_nbr`).

### Entidad: Ingresos ASN (`WmsSapStageInboundHdr` + `WmsSapStageInboundDtl`) — Query pendiente de spike

Fuente real (`Inbound.txt`): header sale de `OWTQ` (traslado) ∪ `ORRR` (devolución) ∪
`@HCO_CONTENEDOR_CAB` (addon custom de contenedores), filtrado por
`U_NX_WMS_SEND IN ('Y','EN PROCESO ENVIO WMS','EN PROCESO RE-ENVIO WMS')`. `shipment_nbr` ya es
expresión calculada (`U_NX_shipment_type || DocNum`) — pasa a ser el `PK`. La rama de
contenedor (`@HCO_CONTENEDOR_CAB`) es una tabla custom sin `U_NX_UPDATEDATE` confirmado — **el
spike debe verificar si esa tabla tiene el mismo UDF o si necesita un campo de cursor
alternativo** (podría no ser pollable de la misma forma; a definir en el spike, no acá a
ciegas).

### Trabajo común a las 3 entidades (por entidad, se repite 3 veces)

1. Migración EF: agregar columna(s) `extra_fields` jsonb (Postgres) / nvarchar(max) (SQL
   Server) — mismo patrón que `AddWmsSapStageItemExtraFields`, incluyendo el
   `Database.IsNpgsql()` guard en `WmsDbContext.cs` que costó un bug real en Items.
2. Nuevo Writer (o refactor del existente) que:
   - Recibe filas **planas** de `SqlDirectConnector.PullAsync`.
   - Para header+detalle: agrupa por clave natural de header, arma 1 header + N líneas por
     grupo.
   - Separa columnas identidad (tipadas) del resto (→ `extra_fields`), igual que
     `SepararCampos` en `WmsSapStageItemWriter`.
   - Aplica el mismo criterio de reenvío por `wms_validation_fields` (valor cambia → Pendiente)
     en vez del cursor por fecha.
3. Nuevo Reader (`LeerPendientesAsync`) que reconstruye el `IntegrationRecord` completo
   (identidad + `extra_fields` de header deserializado) — para header+detalle, arma también la
   lista `"Lineas"` a partir de los `Dtl` relacionados, con sus columnas tipadas tal como hoy
   (las líneas no tienen `extra_fields`, Decisión D).
4. `WmsCloudConnector`: nuevo `ArmarNodo<Entidad>Dinamico` para el header (itera
   `registro.Fields` igual que `ArmarNodoItemDinamico`) — el detalle (`order_dtl`/
   `ib_shipment_dtl`) se sigue armando como hoy, con columnas fijas, porque las líneas no son
   dinámicas (Decisión D).
5. Nueva `IntegrationDefinition` de Bajada (`ConectorTipo=Sql`, `EntidadNegocio=SapWms.<Entidad>`)
   con la Query SQL real (inline o `SELECT * FROM` una vista — Decisión F, ambas formas
   funcionan igual contra `SqlDirectConnector` sin cambios de código).
6. `wms_validation_fields` seed para la entidad.
7. Tests: mínimo equivalente a lo hecho para Items — writer (alta/reenvío/no-reenvío por campo
   no-validación), reader (reconstrucción de `extra_fields`, límite por ciclo), migración
   (columna existe con tipo correcto en ambos motores), `WmsCloudConnector` (nodo dinámico de
   header, regresión de exclusión de claves internas igual que se hizo para Item con
   `SourceUpdateDate`).
8. Verificación end-to-end contra HANA real + staging real + WMS Cloud real antes de dar la
   entidad por terminada (mismo criterio que Task 11 de Items — ahí aparecieron 6 bugs reales
   que ningún test unitario iba a encontrar).

## Orden de ejecución sugerido

1. **Sucursal primero** (sin header/detalle, riesgo bajo, valida el patrón `extra_fields` en
   una entidad simple antes de meterse con el caso header+detalle).
2. **Ingresos ASN** (header+detalle más simple: sin `Lineas` anidadas en el XML de Subida más
   allá de `ib_shipment_dtl`, ver `ArmarNodoIbShipment`).
3. **Órdenes** (header+detalle con más campos y más lógica en `ArmarNodoOrder` — dejarla al
   final porque es la de mayor superficie).

Cada entidad es su propia tanda de tareas dentro de `subagent-driven-development` (como se hizo
con Items), con su propio ciclo de implementer → review → verificación real antes de pasar a la
siguiente. No se arranca la entidad siguiente hasta que la anterior esté verificada contra datos
reales.

## Qué NO cambia (heredado del acuerdo original de Items)

- El modelo "tabla de staging completa + servicio genérico que solo lee y sube" se mantiene tal
  cual — ninguna de estas entidades cambia esa arquitectura.
- El motor de integración genérico (`IIntegrationEntityReader`/`Writer`/`IntegrationSyncHostedService`)
  no necesita cambios — ya soporta `limiteMaximo` por ciclo y el cursor incremental quedó
  reemplazado por value-diff en `wms_validation_fields`, ambos genéricos.
- `SqlDirectConnector`/`IHanaService.QueryDynamicAsync` no necesitan cambios — ya son
  entidad-agnósticos.

## Anexo: Query de Sucursal (Bajada) — lista para usar

Adaptada de `Store.txt` líneas 15-53 al modelo de polling (se quita el filtro de una sola
`CardCode` y el `NOT EXISTS`, que dependían del disparo por transacción; se agrega el cursor):

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

**Sin validar contra HANA real todavía** — igual que con Items, el primer paso de ejecución de
esta entidad debe ser un spike que corra esta Query tal cual contra `CLPRDDEPOR`/QA y confirme
que compila, que `U_NX_UPDATEDATE` existe en `OCRD` con ese nombre exacto, y que los tipos de
columna calzan con lo esperado (mismo criterio que Task 1 de Items).

## Qué falta para arrancar Órdenes e Ingresos ASN

La convención `PK`/`LineaPK` y el cursor `U_NX_UPDATEDATE` ya están confirmados — lo que falta
es concretamente el spike de cada Query contra HANA real (armar el equivalente de polling de
`Picking.txt`/`Inbound.txt`, con el cursor aplicado dentro de cada rama de la UNION de header,
como primer paso de la tanda de tareas de esa entidad — no tiene sentido escribir esas dos
Queries completas a ciegas sin poder correrlas contra el schema real, dada su complejidad
(UNION de 3 fuentes, joins a `PKL1`/`OPKL`/`@GSP_TCSIZE`/tabla custom de contenedores).

## Cómo seguimos

Todas las decisiones (A-F) están confirmadas. Arranco:

1. **Sucursal** con `subagent-driven-development` — Query lista (Anexo arriba), incluye Bajada
   (staging + `extra_fields`) y Subida dinámica (`ArmarNodoStoreDinamico`), mismo alcance
   completo que Items.
2. En paralelo o después, spike de **Ingresos ASN** y **Órdenes** contra HANA real (o contra la
   vista, si deciden armarla del lado de HANA) para cerrar sus Queries antes de escribir el
   resto de esas dos tandas.
