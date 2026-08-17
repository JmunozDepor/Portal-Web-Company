# Diseño: batching real + reconciliación de resultado en la Subida SAP→WMS

Fecha: 2026-08-17
Estado: Aprobado para implementación

## Contexto

Hoy `WmsCloudConnector.PushAsync` envía **un XML por registro** (un `<item>` por
POST) a `init_stage_interface`, y da por exitoso cualquier registro cuyo POST
devuelva HTTP 2xx — sin leer el cuerpo de la respuesta. `init_stage_interface` es un
endpoint de staging asíncrono: un 2xx confirma solo que Oracle recibió el XML bien
formado, no que el contenido de negocio (código de ítem, bodega, etc.) sea válido.
Esa validación real llega después, del lado de Oracle.

El legado (`C:\PROYECTOS\WMS_Suite`, no forma parte de este repo) ya resolvía ambos
problemas con dos piezas separadas, confirmadas contra su código real:

- **Envío**: cada processor arma un lote (`WmsIntegration:BatchSize`, default 50,
  vía `LIMIT` en la consulta) y lo manda como **un solo XML con N nodos**, un solo
  POST — no uno por registro. La respuesta de ese POST es un ack agregado del lote
  completo (`IsSuccess`/`ErrorMessage`), sin granularidad por ítem.
- **Reconciliación** (lo que compensa esa pérdida de granularidad): dos
  `BackgroundService` separados, contra un endpoint totalmente distinto de Oracle
  (**LGFAPI REST**, `GET {LgfApiBaseUrl}/{entidad}?{clave}=...&company_code=...`,
  no `init_stage_interface`):
  - **Detector de rechazos** (cada 60s): consulta las entidades `stage_*` filtrando
    `status_id=101` (Failed) → marca error con el mensaje real de Oracle.
  - **Confirmador** (cada 300s): consulta las entidades **finales**
    (`item`/`facility`/`order_hdr`/`ib_shipment`, no las `stage_*` — Oracle saca el
    registro de `stage_*` una vez procesado) → si aparece, confirma éxito real;
    reintenta hasta 20 veces antes de dar error definitivo.
  - Ambos escriben a una tabla dedicada (`STG_WMS_VALIDATION` en el legado — el
    precedente que `ARQUITECTURA.md` de este repo ya había anotado como pendiente,
    `wms_oracle_export_validations`, nunca construida).

## Objetivo

Portar ambas piezas a `Modulo.Wms`, adaptadas al patrón de este repo (motor de
integración genérico, `BackgroundService` + `IServiceScopeFactory` +
`ICurrentCompanyOverride`, dual-engine Postgres/SqlServer) — y, a diferencia del
legado, sin perder la granularidad por ítem que `Modulo.Wms` ya tiene hoy: el
batching gana eficiencia en el envío, pero es la reconciliación (que sí consulta por
clave individual) la que decide el estado final de cada registro.

## Diseño

### 1. Nuevo contrato en Abstractions — acceso a la config de un conector sin
   cruzar la frontera plugin/Core

Un plugin nunca referencia `PortalSaas.Core`/`PortalSaas.Data` directamente (regla
dura del proyecto), pero los dos workers de reconciliación necesitan la config de
`IntegrationDefinition` (Usuario/Clave/LgfApiBaseUrl), que vive cifrada en la base de
la plataforma. Contrato nuevo, mínimo:

```csharp
// PortalSaas.Abstractions.Contratos
public interface IIntegrationConnectorConfigService
{
    /// <summary>Config JSON ya descifrada del conector activo que matchea
    /// (companyId, moduloOrigen, conectorTipo) -- null si no hay ninguna fila.
    /// Si hay más de una (ej. una Subida y una Bajada del mismo tipo), devuelve la
    /// primera activa encontrada -- las dos entidades de este diseño (workers de
    /// reconciliación) solo necesitan un WmsCloud por compañía en la práctica.</summary>
    Task<string?> GetDecryptedConfigAsync(Guid companyId, string moduloOrigen, string conectorTipo, CancellationToken ct = default);
}
```

Implementación en `PortalSaas.Core` (`Integraciones/IntegrationConnectorConfigService.cs`):
consulta `PortalSaasDbContext.IntegrationDefinitions` filtrando por
`CompanyId`/`ModuloOrigen`/`ConectorTipo`/`Activo`, descifra con
`ISecretoCifradoService`. Registrado `AddScoped` en `Program.cs` del Host, mismo
criterio que el resto de contratos de Abstractions.

### 2. `WmsCloudConfig` gana `BatchSize` y `LgfApiBaseUrl`

```csharp
private sealed record WmsCloudConfig(string ApiUrl, string Usuario, string Clave,
    string ClientEnvCode, string ParentCompanyCode, int BatchSize = 50, string? LgfApiBaseUrl = null);
```

`BatchSize` con default 50 (mismo valor que el legado) si no viene en el JSON —
ninguna `IntegrationDefinition` ya creada se rompe. `LgfApiBaseUrl` puede venir vacío
(los workers de reconciliación simplemente no hacen nada para esa compañía hasta que
se configure). Expuestos como campos nuevos en `Admin/Integraciones/Nuevo` (bloque
WmsCloud).

### 3. Batching real en `WmsCloudConnector.PushAsync`

Reemplaza el `foreach (var registro in registros)` actual por
`registros.Chunk(config.BatchSize)`: arma un XML por chunk (`<ListOfItems>` con N
`<item>`, análogo para Store/Order/IbShipment) y un solo POST por chunk. El ack del
POST (HTTP 2xx) ya no marca el registro como `ProcesadoWms` -- pasa a un estado
intermedio `Enviado` (nuevo valor en `WmsSapStageStatus`, ver abajo). Si el POST
entero falla (status no-2xx), todo el chunk se marca `ErrorWms` con el mensaje HTTP
crudo, igual que hoy -- eso no cambia, es un fallo de transporte, no de negocio.

### 4. Estado intermedio `Enviado`

`WmsSapStageStatus` (compartido por `WmsSapStageItem`/`Store`/`OrderHdr`/`InboundHdr`
-- confirmar los 4 usan el mismo enum antes de tocarlo) gana un valor:

```csharp
public enum WmsSapStageStatus { Pendiente, Enviado, ProcesadoWms, ErrorWms }
```

`Pendiente` → lee el reader de Subida (sin cambios) → tras el batching, `Enviado`
→ los dos workers de reconciliación (abajo) lo mueven a `ProcesadoWms`/`ErrorWms`.

### 5. Tabla `wms_oracle_export_validations` (nueva, en `WmsDbContext`)

```
wms_oracle_export_validations
  id                bigint PK
  company_id        FK -> companies (not null)
  tipo_doc          varchar(20)   -- Item / Store / Order / IbShipment
  clave             varchar(100)  -- ItemCode / CardCode / OrderNbr / SapDocEntry
  enviado_en        timestamptz
  wms_status_id     int null      -- último status_id visto en LGFAPI
  wms_status_desc   varchar(100) null
  wms_error_msg     varchar(500) null
  validado_en       timestamptz null
  intentos          int default 0
  UNIQUE (company_id, tipo_doc, clave)
```

Espejo directo de `STG_WMS_VALIDATION` del legado, con `company_id` real (regla dura
del proyecto) en vez de aislamiento por schema.

### 6. `IWmsValidationApiClient` / `WmsValidationApiClient` (nuevo)

Puerto directo del cliente del legado, mismo contrato de consulta:

```csharp
public interface IWmsValidationApiClient
{
    Task<WmsStageCheckResult> CheckStageRecordAsync(string lgfApiBaseUrl, string usuario, string clave,
        string entity, string keyField, string keyValue, string? companyCode, bool filtrarPorUrl, CancellationToken ct);
}

public sealed record WmsStageCheckResult(bool Found, int? StatusId, string? ErrorMessage);
```

`GET {lgfApiBaseUrl}{entity}?{keyField}={keyValue}&company_code={companyCode}`, Basic
Auth, mismo parseo de `LgfApiListResponse` (JSON con `results: List<JsonElement>`,
forma variable por entidad) que el legado -- portado tal cual, no rediseñado.
Registrado vía `AddHttpClient<IWmsValidationApiClient, WmsValidationApiClient>()`.

**Simplificación respecto al legado**: `GetFailedStageRecordsAsync` (consulta bulk
paginada por `status_id`) no se porta en esta entrega -- alcanza con
`CheckStageRecordAsync` por clave individual, que es lo que necesitan ambos workers
de abajo. Si el volumen real lo justifica, se agrega después (más eficiente en
llamadas HTTP para compañías con muchos registros pendientes de reconciliar).

### 7. Dos `BackgroundService` nuevos en `Modulo.Wms`

Mismo patrón corregido de `WmsSlshStageParser` (iterar
`ListActiveCompanyIdsAsync("Wms", ct)`, `ICurrentCompanyOverride.Set(companyId)`
antes de resolver `WmsDbContext`):

- **`WmsStageErrorReconciler`** (intervalo configurable, default 60s): por cada
  registro `Enviado` en `WmsSapStageItems`/`Stores`/`OrderHdrs`/`InboundHdrs`,
  consulta la entidad `stage_*` correspondiente vía `IWmsValidationApiClient`; si
  `StatusId == 101`, marca `ErrorWms` con `ErrorMessage` y upsert en
  `wms_oracle_export_validations`.
- **`WmsExistsReconciler`** (intervalo configurable, default 300s): por cada
  registro `Enviado` no resuelto por el anterior, consulta la entidad **final**
  (`item`/`facility`/`order_hdr`/`ib_shipment`); si `Found`, marca `ProcesadoWms`;
  si no, incrementa `Intentos` en `wms_oracle_export_validations` y, al llegar a 20,
  marca `ErrorWms` ("no confirmado tras 20 intentos").

Ambos leen la config (`LgfApiBaseUrl`/`Usuario`/`Clave`) vía
`IIntegrationConnectorConfigService.GetDecryptedConfigAsync(companyId, "Wms", "WmsCloud", ct)`
-- si devuelve null (no hay `IntegrationDefinition` WmsCloud activa, o
`LgfApiBaseUrl` vacío), la compañía se saltea ese ciclo sin error.

## Fuera de alcance

- `GetFailedStageRecordsAsync` bulk paginado (ver punto 6).
- Reintento automático del *envío* cuando la reconciliación da error -- igual que el
  legado, el reintento de envío lo sigue haciendo el reader de Subida normal (que
  recoge `Pendiente`/`ErrorWms` -- ver si `ErrorWms` debe volver a quedar elegible
  para reenvío es una decisión de negocio aparte, no se cambia el criterio actual del
  reader en esta entrega).
- Extender el batching a la Bajada (SAP→staging) -- ya trae todo de una sola consulta
  Service Layer, no tiene el mismo problema.
- UI para ver el estado de `wms_oracle_export_validations` (una futura "Estado del
  Servicio", hoy un link muerto en el menú de `Modulo.Wms` -- no se construye en esta
  entrega).

## Testing

- `WmsValidationApiClientTests`: parseo de `LgfApiListResponse` (found/not-found,
  match por `company_code`, `status_id` presente/ausente), con `HttpMessageHandler`
  fake -- mismo patrón que `WmsCloudConnectorTests`.
- `WmsCloudConnectorTests`: nuevo test confirmando que N registros se agrupan en un
  solo POST cuando `BatchSize >= N`, y en varios POSTs cuando `BatchSize < N`.
- `WmsStageErrorReconcilerTests`/`WmsExistsReconcilerTests`: contenedor DI real (mismo
  patrón que `WmsSlshStageParserTests`, con fakes de
  `ICurrentCompanyAccessor`/`Override`/`IExternalDatabaseConnectionService`/
  `IIntegrationConnectorConfigService`/`IWmsValidationApiClient`) -- confirmar
  transición `Enviado` → `ProcesadoWms`/`ErrorWms` según la respuesta del fake LGFAPI,
  y que el tope de 20 intentos se respeta.
