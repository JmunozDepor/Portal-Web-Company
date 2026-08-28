# Migración Wms — Ronda D: SAP → WMS (Picking) — Diseño

**Fecha:** 2026-08-16
**Estado:** Aprobado para plan de implementación
**Precede a:** paridad completa de las 4 entidades SAP→WMS del legado (Artículos/Tiendas/Traslados ya cubiertos por Ronda C; Picking cierra la cuarta).
**Depende de:** Ronda C (SAP→WMS: Artículos, Tiendas, Traslados, ya mergeada) — reusa el mismo motor de 2 etapas (Bajada con staging local, Subida con `WmsCloudConnector`) y el mismo patrón de upsert/resync ya probado.

## Contexto

El sistema legado (`WmsSapIntegration.Service`, `C:\PROYECTOS\WMS_Suite`) tiene una cuarta entidad Outbound sin portar: **Picking**, disparada cuando se libera una Lista de Picking (`OPKL`) en SAP. A diferencia de Artículos/Tiendas/Traslados (filtradas por un UDF de bandera explícita), Picking se filtra únicamente por **estado del documento**: `OPKL.Status = 'R'` (Liberada) + `OITM.InvntItem = 'Y'` en el ítem — sin UDF, confirmado en `SP_DEP_WMS_PROCESS_PICKING` (`014_sp_dep_wms_integration_trigger.sql`, `C:\PROYECTOS\WMS_Suite\db\provisioning\hana\`).

El legado unifica 3 tipos de documento base posibles para una Lista de Picking liberada, vía `UNION ALL`: Órdenes de Venta (`ORDR`, ObjType `17`), Facturas (`OINV`, ObjType `13`), y Traslados (`OWTQ`, ObjType `1250000001`) — este último es un camino **distinto** del de la entidad "Traslado" ya cubierta por Ronda C (que dispara por `U_NX_WMS_SEND`, no por liberación de Picking). **Decisión de alcance (ya confirmada): paridad completa con el legado, los 3 tipos de documento base.**

**Complejidad real de diseño encontrada** (no presente en Ronda C): en SAP B1 Service Layer, el recurso `PickLists` y sus líneas (`PickListsLines`) solo traen `BaseObjectType`/`OrderEntry`/`OrderLine` — el `ItemCode`/cantidad/almacén de cada línea vive en el documento base real, no en la Lista de Picking. Hace falta una consulta de segundo nivel al documento base correspondiente (`Orders`/`Invoices`/`InventoryTransferRequests`, según `BaseObjectType`) para completar cada línea. **Decisión ya confirmada**: agrupar líneas por documento base distinto y hacer una sola consulta por documento base (no una por línea), para minimizar llamadas HTTP por ciclo — análogo al `JOIN` SQL del legado, pero en 2 pasos.

**Vocabulario de campo real confirmado contra el DDL del legado** (`010_stg_sap_order_hdr.sql`/`011_stg_sap_order_dtl.sql`) y el mapeo real de `SP_DEP_WMS_PROCESS_PICKING` — de las ~60 columnas de negocio que tiene `STG_SAP_ORDER_HDR`/`DTL`, el SP legado solo llena un subconjunto (el resto queda `NULL` en producción, nunca alimentado):

| Columna (staging legado) | Origen SAP real (confirmado en el SP) |
|---|---|
| `order_nbr` (cabecera) | `order_type` (UDF `OCRD.U_NX_order_type`) + `DocNum` del doc base (sufijo `-AbsEntry` si es Orden de Venta) |
| `order_type` | `OCRD.U_NX_order_type` |
| `ord_date` | `OPKL.PickDate` |
| `exp_date` | `DocBase.CancelDate` |
| `req_ship_date` | `DocBase.DocDueDate` |
| `ref_nbr`/`customer_po_nbr` | `DocBase.NumAtCard` (o `U_NumAtCard`) |
| `dest_dept_nbr`/`cust_field_1` | `DocBase.ShipToCode` |
| `cust_field_2` | `OPKL.AbsEntry` (el `PickListAbsEntry`) |
| `cust_field_3` | `DocBase.CardName` |
| `cust_field_4` | `DocBase.DocEntry` |
| `cust_field_5` | `DocBase.ObjType` |
| `cust_short_text_1` | `OPKL.AbsEntry` (duplicado, mismo valor que `cust_field_2`) |
| `cust_short_text_2` | `DocBase.CardCode` |
| `facility_code` | fijo (`'BO02'` en el legado — valor específico de esa instalación, en este proyecto será configurable/fijo según la organización) |
| `priority` | fijo (`'1'`) |
| `order_nbr`/`seq_nbr` (detalle) | mismo `order_nbr` de cabecera + `LineNum` de la línea del doc base |
| `item_alternate_code` | `ItemCode` de la línea del doc base |
| `ord_qty` | `RelQtty` de `PKL1` (cantidad **liberada** en el picking, no la cantidad total del pedido) |
| `cust_number_1`/`cust_number_2` (detalle) | `DocEntry`/`LineNum` de la línea del doc base |
| `cust_short_text_1` (detalle) | `PKL1.AbsEntry` (el mismo `PickListAbsEntry`) |

El resto de las columnas del DDL (dirección de envío completa `shipto_*`, `sales_order_nbr`, `carrier_account_nbr`, `payment_method`, `currency_code`, etc.) **no están alimentadas por el SP real** — quedan fuera de alcance, no se replican especulativamente.

**Formato XML confirmado** (`WmsOutbound_OrderProcessor.cs`, líneas 121-133): nodo raíz `ListOfOrders`, un nodo `order` por Lista de Picking liberada, con `order_hdr` (uno) + N × `order_dtl` anidados — mismo patrón cabecera+detalle que `ib_shipment` (Ronda C).

## Decisión

Mismo motor de 2 etapas que Ronda C, con una entidad nueva ("Picking") a lo largo de las 2.

### Etapa 1 — Bajada: SAP → staging local (`Modulo.Wms`)

`SapDocumentConnector.PullAsync` gana una rama nueva `TipoEntidad="Picking"`:

1. Consulta `PickLists` con `$filter` por el estado liberado (nombre de campo/valor exacto de Service Layer a confirmar en el Step 1 del plan de implementación — no asumir sin verificar, mismo criterio ya usado en Ronda C para `Items`/`BusinessPartners`) y `$expand` de `PickListsLines`.
2. Agrupa las líneas devueltas por `(BaseObjectType, OrderEntry)` distinto, y por cada grupo hace **una** consulta al recurso correspondiente (`Orders`/`Invoices`/`InventoryTransferRequests` según `BaseObjectType` `17`/`13`/`1250000001`) para completar `ItemCode`/cantidad de pedido/almacén de cada `OrderLine` referenciada — cruzando con `ReleasedQuantity` de la línea de picking (no la cantidad total del documento base).
3. Arma un `IntegrationRecord` por Lista de Picking liberada, campos de cabecera (`OrderNbr`, `OrderType`, `PickListAbsEntry`, `BaseObjectType`, `BaseEntry`, `CardCode`, `CardName`, `CustomerPoNbr`, `OrdDate`, `ExpDate`, `ReqShipDate`, `ShipToCode`, `SourceUpdateDate`) + `Lineas` (`ItemCode`, `Quantity`=`ReleasedQuantity`, `WhsCode`, `LineNum`, `SeqNbr`).

Escribe vía un `IIntegrationEntityWriter` nuevo (`WmsSapStageOrderWriter`, `EntidadNegocio="SapWms.Order"`) en 2 tablas nuevas de staging, mismo patrón hdr+dtl que `wms_sap_stage_inbound_hdr`/`_dtl` de Ronda C: `wms_sap_stage_order_hdr` (clave natural `(CompanyId, OrderNbr)`) + `wms_sap_stage_order_dtl`. Mismo upsert por clave natural + resync por `SourceUpdateDate`, **incluido el resync desde `ErrorWms`** (mismo fix ya aplicado a los 3 writers de Ronda C — no repetir el bug encontrado ahí).

### Etapa 2 — Subida: staging → Oracle WMS Cloud real

`WmsSapStageOrderReader` (nuevo, `EntidadNegocio="SapWms.Order.Subida"`) lee `Pendiente` de `wms_sap_stage_order_hdr`/`_dtl`, arma `IntegrationRecord`s con `Fields["TipoDocumento"]="Order"`.

`WmsCloudConnector` gana una rama nueva para `"Order"`: arma `ListOfOrders` > `order` > `order_hdr` + N × `order_dtl`, con los nombres de nodo XML confirmados contra el DDL real (`order_nbr`, `order_type`, `ord_date`, `exp_date`, `req_ship_date`, `ref_nbr`, `dest_dept_nbr`, `facility_code`, `priority` en cabecera; `order_nbr`, `seq_nbr`, `item_alternate_code`, `ord_qty` en detalle).

## Fuera de alcance (explícito)

- Los ~40 campos opcionales del DDL legado que el propio SP nunca llena (dirección de envío completa, `sales_order_nbr`, `carrier_account_nbr`, `payment_method`, `currency_code`, etc.).
- Confirmación de llegada real a WMS y detección de rechazos (`ExistsProcessor`/`StageErrorProcessor` del legado) — Ronda E, ronda separada.
- `facility_code`/`priority` fijos como en el legado (`'BO02'`/`'1'`) — se asumen constantes por ahora, igual que el legado; si una organización real necesita otro valor, es una configuración a agregar después, no bloqueante para el piloto.
- Limpieza/retención de las 2 tablas de staging nuevas.

## Riesgos y supuestos explícitos

- **Nombre/valor exacto del campo de estado liberado en `PickLists`** (Service Layer) no confirmado contra código real de este repo ni contra un ambiente SAP real — a verificar en el Step 1 del plan, mismo criterio que Ronda C.
- **Volumen de consultas por ciclo**: con la agrupación por documento base distinto, el número de llamadas HTTP por ciclo es `1 (PickLists) + N (documentos base distintos)` — aceptable para un piloto de bajo volumen, pero no probado contra un catálogo real de Listas de Picking liberadas simultáneamente.
- **Verificación end-to-end contra SAP/WMS reales pendiente** — igual que Rondas B/C, esta ronda se implementa y prueba con fakes/EF Core InMemory.

## Criterio de éxito

- Una Lista de Picking liberada en SAP (con líneas de Orden de Venta, Factura, o Traslado) aparece como `Pendiente` en `wms_sap_stage_order_hdr`/`_dtl` tras un ciclo de Bajada, con el `ItemCode`/cantidad liberada correctos de cada línea.
- Esa fila `Pendiente` es leída, transformada a XML (`ListOfOrders`) y enviada a Oracle WMS Cloud tras un ciclo de Subida.
- Una Lista de Picking que falla al postear se recupera automáticamente (vuelve a `Pendiente`) cuando SAP la vuelve a mandar — mismo criterio ya corregido en Ronda C para las otras 3 entidades.
