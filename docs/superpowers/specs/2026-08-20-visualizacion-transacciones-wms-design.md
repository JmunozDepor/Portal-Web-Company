# Visualización de transacciones WMS — Dashboard, grillas y confirmación de ingreso

## Contexto

Modulo.Wms (el plugin nuevo, dentro de Portal SaaS - Core) construyó en entregas
anteriores el motor de integración: batching SAP→WMS, dos reconciliadores, el
parser SLSH (confirmación de traslados WMS→SAP), mapeo de campos y configuración
de servicio. Pero nunca se portaron las pantallas de **visualización operativa**
que sí existían en el sistema legado standalone (`WMS_Suite/src/WmsPortal.Web`):
Dashboard, grillas de transacciones por tipo, archivos WMS y confirmaciones.

Hoy el menú "Integración WMS" solo tiene 2 páginas funcionales (Mapeo de Campos,
Configuración del Servicio) más una entrada muerta ("Estado del Servicio", apunta
a `/wms/estado-servicio`, que no existe). No hay forma de ver, desde el Portal,
qué se envió, qué quedó pendiente o en error, ni de reintentar nada — solo
consultando la base a mano.

Al investigar el alcance real se encontraron dos huecos además de las pantallas:

1. **Falta el backend de "Confirmación Ingreso" (SVSH)** — WMS confirma la
   recepción de un ASN/traslado/devolución y eso debe reflejarse en SAP
   (GRPO/StockTransfer/Devolución según `shipment_dtl_cust_field_1`). El legado
   tiene esto (`SVSH_Processor` + `WmsInbound_ReceipConfirmProcessor`); Modulo.Wms
   solo portó el equivalente de órdenes (SLSH), nunca el de ingreso.
2. **Ningún `BackgroundService` del plugin escribe heartbeat.** `WmsServiceHeartbeat`
   existe como tabla mapeada pero nada la alimenta — sin esto, "Estado del
   Servicio" y el "última ejecución" del Dashboard quedarían siempre vacíos.

## Alcance

### 1. Backend de Confirmación Ingreso (SVSH)

Espejo del patrón SLSH ya existente en el plugin:

- **`WmsOracleStageSvsh`** (nuevo modelo): fila por línea de detalle
  (`ib_shipment_dtl`), con `LineId`, `ParentId` (→ `WmsOracleInboundStage.Id`),
  `Status` (enum `WmsSvshStatus { Pendiente, ProcesadoSap, ErrorSap }`),
  `ErrorMsg`, `RetryCount`, `SapDocEntry`, `SapObject`, más las columnas de
  negocio del legado: `shipment_nbr`, `manifest_nbr`, `MessageId`, `load_nbr`,
  `shipment_dtl_cust_field_1/2/3` (BaseType/BaseEntry/LineNum), `item_part_a`,
  `received_qty`, y el resto de campos de `ib_shipment_hdr`/`ib_shipment_dtl`
  presentes en el XML real de Oracle WMS Cloud (mismo criterio que
  `WmsOracleStageSlsh`: todos `string?` salvo los campos administrativos).
- **`WmsSvshXmlParser`** (nuevo, estático): a diferencia de `WmsSlshXmlParser`
  (aplanado de un solo nivel `ob_stop`), el XML de SVSH tiene 3 niveles
  (`Header` global → `ib_shipment` → `ib_shipment_hdr` + `ib_shipment_dtl[]`,
  ver `SVSH_Processor.cs` legado). El parser debe recorrer cada `ib_shipment`,
  y para cada `ib_shipment_dtl` completar columnas por prioridad: detalle →
  header del shipment → header global (mismo criterio de prioridad que el
  legado, adaptado a reflexión sobre las propiedades de `WmsOracleStageSvsh`
  igual que hace `WmsSlshXmlParser`).
- **`WmsSvshStageParser`** (nuevo `BackgroundService`, mismo esqueleto exacto
  que `WmsSlshStageParser`: 15s de intervalo, itera compañías activas vía
  `IExternalDatabaseConnectionService.ListActiveCompanyIdsAsync`, fija
  `ICurrentCompanyOverride` antes de resolver `WmsDbContext`, toma pendientes de
  `WmsOracleInboundStages` con `TipoDoc == "SVSH"`, parsea con
  `WmsSvshXmlParser`, marca `Aplanado`/`ErrorEstructura`/`ErrorStaging`, mismo
  manejo de fallo de persistencia con reintentos acotados).
- **`WmsSvshInventoryReader`** (nuevo `IIntegrationEntityReader`,
  `EntidadNegocio => "Wms.ConfirmacionIngreso"`): lee `WmsOracleStageSvsh` en
  `Pendiente`, agrupa por `shipment_nbr` y luego por `shipment_dtl_cust_field_2`
  (BaseEntry, como hace el legado), arma un `IntegrationRecord` por grupo con
  las líneas y el `BaseType` (`shipment_dtl_cust_field_1`) para que
  `SapDocumentConnector` (u otro conector SAP existente) decida el endpoint —
  **si el conector genérico actual no soporta ramificar por BaseType a
  StockTransfers/Returns/PurchaseDeliveryNotes, el implementador debe extenderlo
  o documentar la limitación**; no se prescribe aquí el detalle de ese cableado,
  queda para la fase de plan/implementación revisando `SapDocumentConnector`
  actual. `MarcarProcesadoAsync` actualiza `Status`/`ErrorMsg` de las filas del
  grupo, mismo patrón que `WmsSlshInventoryReader`.
- Migraciones EF Core para `wms_oracle_stage_svsh` en `Modulo.Wms.Migrations.Postgres`
  y `.SqlServer`, generadas con `dotnet ef migrations add` real (no a mano).
- Registro en `ModuloWms.cs`: `AddHostedService<WmsSvshStageParser>()`,
  `AddScoped<IIntegrationEntityReader, WmsSvshInventoryReader>()`.

### 2. Heartbeat de servicio

- Un helper simple (p.ej. `WmsServiceHeartbeatRecorder`, con acceso a
  `WmsDbContext`) con un método `RecordAsync(string processorKey, string status,
  string? error = null)` que hace upsert sobre `WmsServiceHeartbeat` por
  `(CompanyId, ProcessorKey)` — mismo contrato que `ServiceHeartbeat.RecordAsync`
  del legado (`ProcessorKey` es el mismo string usado como clave, ver
  comentario en `TransaccionModels.cs`).
- Se llama al final (éxito) y en el `catch` general (error) del ciclo de cada
  `BackgroundService` del plugin: `WmsSlshStageParser`, `WmsSvshStageParser`
  (nuevo), `WmsStageErrorReconciler`, `WmsExistsReconciler`. `ProcessorKey` para
  cada uno: se define un nombre propio por servicio (no reutilizar los del
  legado, que corresponden a otro sistema) — p.ej. `"Wms.SlshStageParser"`,
  `"Wms.SvshStageParser"`, `"Wms.StageErrorReconciler"`, `"Wms.ExistsReconciler"`.
- Fuera de alcance: heartbeat de `IntegrationSyncHostedService` (motor genérico
  en `PortalSaas.Integrations`, otro repo) — el Dashboard de este plugin no
  mostrará última-ejecución para los conectores Bajada/Subida SAP↔staging,
  solo para los 4 `BackgroundService` propios del plugin. Anotado como
  limitación conocida, no como pendiente de este plan.

### 3. Página Dashboard (`/wms/dashboard`)

KPIs y tabla por tipo, adaptando `DashboardService`/`DashboardController` del
legado a las tablas nuevas:

- Tarjetas: total transacciones (hoy / ayer / 7 días / 30 días, selector como
  el legado), OK, con error, pendientes — agregando sobre
  `WmsSapStage{Item,Store,OrderHdr,InboundHdr}.Status` +
  `WmsOracleStage{Slsh,Svsh}.Status`.
- Tabla "Transacciones por tipo": una fila por cada uno de los 6 tipos (Producto,
  Sucursal, Órdenes, Ingreso ASN, Confirmación Órdenes, Confirmación Ingreso)
  con conteos por estado, `ConfirmadoWms` (join contra `WmsExportValidation`
  para los 4 tipos "Envío"), y "última ejecución" resuelta desde
  `WmsServiceHeartbeat` por el `ProcessorKey` que corresponde a cada tipo.
- Sin panorama histórico separado (el "Panorama general" del legado se
  simplifica a un solo rango seleccionable, ver decisión pendiente de UI abajo).

### 4. Página Transacciones (`/wms/transacciones?tipo=`)

Página genérica (mismo patrón que `TransaccionesController.Index` del legado):
un enum `TipoTransaccionWms { EnvioProducto, EnvioSucursal, EnvioOrdenes,
EnvioIngresoAsn }` (los 4 tipos "Envío" comparten esta grilla; las
confirmaciones van en la página aparte del punto 5 porque su modelo es
estructuralmente distinto — header/detalle con muchas columnas de negocio vs.
las stage tables planas). Por tipo:

- Filtros: estado (`Pendiente/Enviado/ProcesadoWms/ErrorWms`), documento
  (código de ítem / código de sucursal / N° orden / N° shipment según tipo),
  rango de fechas (default últimas 24h, igual que el legado).
- Grilla paginada con columnas específicas por tipo (reutilizar el concepto de
  `ColumnDef`/`GetColumnsForTipoAsync` del legado, adaptado).
- Detalle: para Órdenes/Ingreso ASN (que tienen tabla `*Dtl` separada), un
  endpoint AJAX que devuelve las líneas del documento.
- Acción "Reset a Pendiente" por selección múltiple (mismo criterio de permisos
  simple que el legado: cualquier usuario Admin del módulo puede resetear;
  Modulo.Wms no tiene roles granulares hoy, no se agregan en este plan).

### 5. Página Confirmaciones (`/wms/confirmaciones?tipo=`)

Genérica para los 2 tipos: `Ordenes` (`WmsOracleStageSlsh`) e `Ingreso`
(`WmsOracleStageSvsh`, nuevo). Mismos filtros/paginación/reset que el punto 4,
adaptados a estas tablas (agrupando por `shipment_nbr`/`order_hdr_cust_field_4`
para no mostrar una fila por línea suelta, igual que el legado agrupa por
documento en su grilla).

### 6. Página Archivos WMS (`/wms/archivos`)

Grilla sobre `WmsOracleInboundStage`: filtros por `TipoDoc`, `Estado`, rango de
fechas; ver contenido (XML/JSON crudo) en modal; reintentar (vuelve el
`Estado` a `Pendiente` para que el parser correspondiente lo tome de nuevo).

### 7. Página Estado del Servicio (`/wms/estado-servicio`)

Cablea la entrada de menú ya existente (`ModuloWms.cs` línea 57-64, hoy apunta
a una ruta sin página). Grilla simple sobre `WmsServiceHeartbeat`: un renglón
por `ProcessorKey` de este plugin, con `LastRunAt`, `Status`, `LastError`,
`ConfigSourceEffective`.

### 8. Menú

Se agregan 4 entradas nuevas en `ModuloWms.cs::GetMenu()` (Dashboard,
Transacciones, Confirmaciones, Archivos WMS), ordenadas antes de las 3
existentes (Mapeo de Campos pasa a ser configuración, no operación).

## Fuera de alcance

- No se toca `WmsPortal.Web` (legado) — sigue como está, es un sistema
  independiente que ya funciona en producción.
- No se agregan roles/permisos granulares — se usa el mismo gate de
  autenticación que ya tienen Mapeo de Campos/Configuración del Servicio.
- No se rediseña visualmente nada fuera de estas pantallas (el pedido de
  rediseño UI/UX de toda la plataforma, planteado en otra conversación, es un
  proyecto aparte).
- Heartbeat del motor de integración genérico (`IntegrationSyncHostedService`)
  queda fuera, ver nota en sección 2.

## Testing

- Tests unitarios para `WmsSvshXmlParser` (XML de 3 niveles → filas correctas,
  prioridad de resolución detalle > header shipment > header global) y
  `WmsSvshStageParser`/`WmsSvshInventoryReader` (mismo nivel de cobertura que
  sus equivalentes SLSH: aislamiento por compañía, manejo de error de
  persistencia, agrupado correcto).
- Tests para el heartbeat recorder (upsert, no duplica filas por
  `(CompanyId, ProcessorKey)`).
- Tests de los servicios de lectura que alimentan Dashboard/grillas (conteos
  correctos por estado y tipo).
- Sin pruebas de UI automatizadas (Razor Pages simples, se verifican a mano).
