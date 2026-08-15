# Migración Wms — Ronda A: Ingestión + Staging — Diseño

**Fecha:** 2026-08-15
**Estado:** Aprobado para plan de implementación
**Precede a:** Ronda B (motor genérico + push real a SAP), documentada en `docs/12-MOTOR-INTEGRACION-ERP-PENDIENTES.md` punto 3.
**Depende de:** Ronda 0 (autenticación máquina-a-máquina), ya mergeada a `main` — este es su primer consumidor real.

## Contexto

El sistema legado (`WmsSapIntegration.Service`, en `WMS_Suite`) implementa hoy en producción el ciclo WMS↔SAP completo. La decisión de negocio es reemplazarlo gradualmente, piloto en una compañía de bajo volumen. Esta ronda cubre la primera mitad del camino WMS→SAP para un solo tipo de documento piloto: confirmación de traslado (`mapper_key = ORDER_CONFIRM_STOCKTRANSFER`, `TipoDoc` legado = `SLSH`).

Investigación del sistema legado confirmó:
- **Recepción**: `WmsInboundController.receive-xml` (`WmsApiRest.webservices`) — endpoint HTTP autenticado (hoy OAuth2+HMAC), inserta XML crudo en `INT_WMS_STAGE` con validación anti-XXE, whitelist `Entity→TipoDoc`, idempotencia por hash SHA-256.
- **Staging**: `SLSH_Processor.cs` — `BackgroundService` que toma filas `PENDIENTE`, aplana el XML dinámicamente (mapeo columna↔elemento XML: primero busca en `ob_stop`, luego `load`, luego `Header`) hacia `STG_WMS_SLSH` (~200 columnas, estándar completo de interfaz "shipped_load" de Oracle WMS Cloud), una fila por cada `ob_stop`.
- La lógica de negocio real (crossdock `XDK2`, envío a SAP Service Layer) vive en `WmsInbound_OrderConfirmProcessor.cs` — eso es Ronda B, no esta ronda.

Nueva información (2026-08-15): Oracle WMS puede enviar datos en XML, JSON o TXT — no solo XML como asume el legado. Se diseña la estructura para soportar los tres formatos, pero solo se implementa el parser XML en esta ronda (es el único formato real hoy).

## Decisión

### Ubicación: `Modulo.Wms` (plugin), no `PortalSaas.Core`

Siguiendo la arquitectura de plugins ya establecida (`PortalSaas.Host` no referencia ensamblados de plugins directamente — los carga dinámicamente vía `PluginLoadContext` y solo les habla a través de interfaces registradas en el mismo `IServiceCollection`), las tablas de staging nuevas viven en el `WmsDbContext` propio del plugin `Modulo.Wms` (mismo prefijo `wms_oracle_*` que ya usan `wms_oracle_field_mappings`/`wms_oracle_service_configs`). El endpoint HTTP vive en `PortalSaas.Host` (los plugins no soportan Minimal API/Controllers hoy), y llama a una interfaz nueva `IWmsInboundIngestionService` definida en `PortalSaas.Abstractions` e implementada/registrada por `Modulo.Wms`.

### Modelo de datos (`WmsDbContext`, plugin `Modulo.Wms`)

```
wms_oracle_inbound_stage   -- espejo de INT_WMS_STAGE + company_id + formato
  id              bigint PK
  company_id      FK -> companies (not null)
  tipo_doc        varchar(10)   -- 'SLSH' (único valor aceptado en esta ronda)
  formato         varchar(10)   -- 'xml' | 'json' | 'txt' (check constraint, solo 'xml' tiene parser)
  nombre_archivo  varchar(255)
  hash_archivo    varchar(64)   -- SHA-256 hex, UNIQUE por company_id (idempotencia)
  contenido       text          -- el XML/JSON/TXT crudo, sea cual sea el formato
  estado          varchar(20)   -- 'pendiente' | 'aplanado' | 'error_estructura' | 'error_staging'
  intentos        int
  mensaje_error   text
  sap_doc_entry   varchar(50)
  inserted_at     timestamptz
  processed_at    timestamptz

wms_oracle_stage_slsh      -- espejo fiel de STG_WMS_SLSH (~200 columnas Oracle WMS estándar)
  line_id         bigint PK
  parent_id       FK -> wms_oracle_inbound_stage (cascade delete)
  status          varchar(20)   -- 'pendiente' | 'procesado_sap' | 'error_sap' (Ronda B las escribe)
  error_msg       text
  retry_count     int
  sap_doc_entry   int
  -- + todas las columnas de negocio del estándar "shipped_load" de Oracle WMS,
  --   replicadas 1:1 desde el DDL real confirmado en WMS_Suite/db/provisioning/hana/005_stg_wms_slsh.sql
  --   (ob_lpn_nbr, order_type, item_part_a, dest_facility_code, etc. — lista completa en el plan)
```

`SVSH`/`IHTH` (otros flujos: crossdock, inventario) quedan fuera de esta ronda — no se crean sus tablas de staging todavía.

### Endpoint de recepción (`PortalSaas.Host`)

`POST /api/wms/inbound/receive`, `[Authorize(AuthenticationSchemes = "ExternalApiKey")]` (Ronda 0 — la `CompanyId` ya viene resuelta del `ClaimsPrincipal`, no se pide en el payload). Réplica de la validación del legado:

1. Lee el `Content-Type` de la request para determinar `Formato`: `application/xml`/`text/xml` → `Xml`; `application/json` → `Json`; `text/plain` → `Txt`. Cualquier otro `Content-Type` → 415.
2. Si `Formato != Xml`: responde 400 explícito ("Formato 'Json'/'Txt' reconocido pero sin parser implementado todavía — solo XML soportado en esta ronda"). No se inserta en staging — mismo criterio que `SapDocumentConnector` (estructura lista, rama no implementada es explícita, no silenciosa).
3. Para `Xml`: parseo seguro anti-XXE/DTD (réplica de `SecureXmlHelper.ParseSecurely` del legado), extrae `Header/MessageId` + `Header/Entity`, valida `Entity` contra whitelist (en esta ronda, whitelist de un solo valor fijo mapeando a `TipoDoc = 'SLSH'` — no configurable todavía, hardcodeado; generalizar a configuración por compañía es una extensión futura si aparece un segundo `TipoDoc`).
4. Límite de tamaño 10MB (`RequestSizeLimit`).
5. Idempotencia: hash SHA-256 del cuerpo crudo, `UNIQUE(company_id, hash_archivo)` — si ya existe, responde 200 idempotente sin insertar de nuevo.
6. Inserta en `wms_oracle_inbound_stage` con `Estado = 'pendiente'` vía `IWmsInboundIngestionService.InsertPendingAsync(...)`.

### `WmsSlshStageParser` (`BackgroundService`, plugin `Modulo.Wms`)

Mismo patrón que `IntegrationSyncHostedService` (scope nuevo por ciclo, `IServiceScopeFactory`, try/catch que no tumba el host). Cada ciclo (poll cada 15s, igual que el legado):

1. Toma filas `wms_oracle_inbound_stage` con `Estado = 'pendiente' AND TipoDoc = 'SLSH' AND Formato = 'xml'`.
2. Parsea el XML: extrae `Header` (diccionario elemento→valor), `load` (diccionario), y cada nodo `ob_stop` (una fila de salida por nodo).
3. Para cada columna real de `wms_oracle_stage_slsh` (excluyendo `line_id`/`parent_id`/`status`/`error_msg`/`retry_count`/`sap_doc_entry`, que son metadata de staging, no datos de negocio): busca primero en el `ob_stop` actual, si no está busca en `load`, si no está busca en `Header`. Si no hay match en ninguno, la columna queda `NULL` para esa fila.
4. Si se insertó al menos una fila: marca la `inbound_stage` como `'aplanado'`. Si no había ningún `ob_stop` válido: marca `'error_estructura'` con mensaje explícito.
5. Cualquier excepción durante el parseo de un archivo individual: marca esa fila `'error_staging'` con el mensaje, sin afectar el resto del batch (mismo aislamiento por-fila que el legado).

## Fuera de alcance (explícito)

- Parsers JSON/TXT — estructura lista (`Formato`), sin implementación.
- Staging de `IHTH`/`SVSH` — otros flujos, no este piloto.
- Envío real a SAP (lectura de `wms_oracle_stage_slsh` → `IIntegrationEntityReader` → motor genérico → `SapDocumentConnector`) — Ronda B.
- Firma HMAC adicional — endurecimiento futuro si el piloto lo requiere.
- Flujo crossdock `XDK2` — lógica de negocio de Ronda B, no de ingestión/staging.
- Whitelist configurable de `Entity→TipoDoc` por compañía — hardcodeado a un solo valor en esta ronda.

## Criterio de éxito

- Un XML real de confirmación de traslado (`SLSH`), enviado con `POST /api/wms/inbound/receive` autenticado con una API key de la Ronda 0, queda insertado en `wms_oracle_inbound_stage` con `Estado = 'pendiente'`.
- El mismo XML reenviado (mismo hash) no duplica la fila — responde 200 idempotente.
- `WmsSlshStageParser` toma esa fila pendiente y genera una fila en `wms_oracle_stage_slsh` por cada `ob_stop` del XML, con los campos correctamente poblados desde `ob_stop`/`load`/`Header` según corresponda, y marca la `inbound_stage` como `'aplanado'`.
- Un XML mal formado, o sin nodos `ob_stop`, queda en `wms_oracle_inbound_stage` con `Estado = 'error_estructura'`/`'error_staging'` y mensaje explícito, sin tumbar el `BackgroundService`.
- Un intento con `Content-Type: application/json` responde 400 con mensaje explícito de "no implementado todavía", sin ensuciar el staging.
