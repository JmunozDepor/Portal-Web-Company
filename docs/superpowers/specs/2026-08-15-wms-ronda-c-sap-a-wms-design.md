# Migración Wms — Ronda C: SAP → WMS (Artículos, Tiendas, Traslados) — Diseño

**Fecha:** 2026-08-15
**Estado:** Aprobado para plan de implementación
**Precede a:** cierre del ciclo bidireccional completo del piloto — con esta ronda, un Traslado creado en SAP puede llegar al WMS, y el WMS puede confirmarlo de vuelta a SAP (Ronda B, ya mergeada).
**Depende de:** Ronda B (motor + SAP, ya mergeada) — el `BaseEntry` que Ronda B espera en el XML de confirmación del WMS es exactamente el `DocEntry` de la Solicitud de Traslado (OWTQ) que esta ronda envía al WMS.

## Contexto

El sistema legado `WmsSapIntegration.Service` (`C:\PROYECTOS\WMS_Suite`) implementa la dirección SAP→WMS ("Outbound") con 5 `BackgroundService` (Artículos, Tiendas, Picking/Órdenes de venta, Traslados/Devoluciones/Contenedores, Códigos de Barra — este último nunca alimentado del lado SAP, no cuenta) más 2 de confirmación (`ExistsProcessor`/`StageErrorProcessor`) y 1 de limpieza por retención.

**Mecanismo de detección legado:** triggers nativos de SAP B1 sobre HANA (`SBO_SP_PostTransactionNotice` → `SP_DEP_WMS_INTEGRATION_TRIGGER` → SPs de proceso → tablas `STG_SAP_*`). Es invasivo (exige acceso e instalación de SQL dentro de la base HANA de cada compañía SAP) y solo funciona si SAP B1 corre sobre HANA — no hay equivalente para SQL Server.

**Filtro de negocio confirmado por entidad** (investigación del SQL real, no reconstruido — ninguno de los 4 SPs relevantes filtra solo por tipo de objeto):

| Entidad | Filtro real |
|---|---|
| Artículos (`OITM`) | `U_NX_EnviarWMS = 'Y'` + `InvntItem = 'Y'` + código de barras no vacío/no `'0'` |
| Tiendas/Socios (`OCRD`) | `U_NX_EnviarWMS = 'Y'` + dirección tipo Ship-to (`CRD1.AdresType='S'`) |
| Picking (`OPKL`) | `Status = 'R'` (liberada) — sin UDF, es estado estándar del documento |
| Traslados/Devoluciones (`OWTQ`/`ORRR`) | `U_NX_WMS_SEND` en (`'Y'`, `'EN PROCESO ENVIO WMS'`, `'EN PROCESO RE-ENVIO WMS'`) + `U_NX_shipment_type` no vacío + `shipped_qty ≠ 0` |

**Decisión de arquitectura (ya acordada con el dueño del proyecto):** replicar el filtro de negocio exacto, pero vía **Service Layer + `$filter` OData + polling incremental**, no vía triggers HANA — coherente con la decisión de transporte del motor genérico completo (REST/HTTP, sin tocar la base de datos de SAP directamente), y funciona igual esté SAP sobre HANA o SQL Server.

**Descomposición del alcance completo (ya acordada):** esta ronda cubre solo Artículos + Tiendas + Traslados — el mínimo necesario para que un Traslado real llegue al WMS y se pueda validar el flujo completo. Picking/Órdenes de venta y la confirmación de llegada real a WMS (equivalente a `ExistsProcessor`/`StageErrorProcessor`) quedan para rondas futuras — no bloquean la validación del piloto, porque Ronda B ya confirma el Traslado específico cuando el WMS contesta con el evento de traslado hecho.

## Decisión

Dos etapas, reusando el motor genérico existente con dos `IntegrationDefinition` por entidad (una `Direccion.Bajada`, una `Direccion.Subida`) — igual patrón que separaba ingestión (Ronda A) de posteo (Ronda B), pero en sentido contrario.

### Etapa 1 — Bajada: SAP → staging local (`Modulo.Wms`)

**Infraestructura HTTP reutilizada, no nueva:** `ISapSession.GetAsync<T>(recurso, filtroOData, expandOData, ct)` (`PortalSaas.Abstractions/Contratos/ISapConnectionProvider.cs`) ya existe, ya está en uso en producción (`ItemCrossReferenceService`, `SalesDocumentService`), y maneja login/sesión/renovación internamente vía B1SLayer (`SapConnectionProvider`/`SapSessionCache`). No se construye ningún cliente HTTP nuevo.

**`SapDocumentConnector.PullAsync`** (hoy `NotSupportedException` incondicional) se completa. Como la firma de `PullAsync(string conectorConfigJson, CancellationToken)` no recibe la `IntegrationDefinition` ni el `EntidadNegocio`, el `ConectorConfigCifrado` de cada definición declara qué entidad le corresponde:

```json
{ "TipoEntidad": "Item" }
```
(`"Item"` / `"Store"` / `"InboundTraslado"`, un valor por definición — 3 `IntegrationDefinition` de Bajada, una por entidad, cada una con su propio `ConectorConfigCifrado`).

`PullAsync` deserializa `TipoEntidad` y hace `switch`:

```csharp
public async Task<IReadOnlyList<IntegrationRecord>> PullAsync(string conectorConfigJson, CancellationToken cancellationToken)
{
    var config = JsonSerializer.Deserialize<SapWmsOutboundConfig>(conectorConfigJson)
        ?? throw new InvalidOperationException("Config de conector Sap (Bajada) inválida o vacía.");

    var session = await _sapSessionProvider.GetSessionAsync(cancellationToken);

    return config.TipoEntidad switch
    {
        "Item" => await LeerItemsAsync(session, cancellationToken),
        "Store" => await LeerStoresAsync(session, cancellationToken),
        "InboundTraslado" => await LeerTrasladosAsync(session, cancellationToken),
        _ => throw new InvalidOperationException($"TipoEntidad '{config.TipoEntidad}' no soportado en PullAsync."),
    };
}
```

Cada `LeerXAsync` arma el `$filter` OData con el criterio de negocio de la tabla de arriba (usando `ODataFilterHelper`, ya existente, para escapar valores) **más** una condición de cursor incremental: `UpdateDate ge {cursor}`, donde `cursor` es el máximo `SourceUpdateDate` ya presente en la tabla de staging correspondiente para esa compañía (consultado antes del GET a SAP) — **sin agregar ningún campo nuevo a `IntegrationDefinition`**, el cursor vive completamente en el staging.

Cada fila devuelta por SAP se convierte en un `IntegrationRecord` con los campos que la Etapa 2 necesita (nombres explícitos, no un volcado dinámico de todo lo que devuelve SAP):

- **Item**: `ItemCode`, `ItemName`, `BarCode`, `SourceUpdateDate`.
- **Store**: `CardCode`, `CardName`, dirección Ship-to (`Street`, `City`, etc. — confirmar campos exactos contra el `$expand=BPAddresses` en el Step 1 del plan), `SourceUpdateDate`.
- **InboundTraslado**: cabecera (`DocEntry`, `U_NX_shipment_type`, `SourceUpdateDate`) + `Lineas` (`List<IntegrationRecord>`: `ItemCode`, `Quantity`, `WhsCode` origen/destino, `LineNum`) — mismo patrón de `Fields["Lineas"]` ya usado en Ronda B.

**Escritura vía `IIntegrationEntityWriter`** (contrato ya existe en `PortalSaas.Abstractions`, hoy sin ningún implementador real — se completa en esta ronda). 3 implementaciones nuevas en `Modulo.Wms`: `WmsSapStageItemWriter`, `WmsSapStageStoreWriter`, `WmsSapStageInboundWriter`, cada una con su propio `EntidadNegocio` (`"SapWms.Item"` / `"SapWms.Store"` / `"SapWms.Traslado"`), escribiendo en su tabla de staging correspondiente con `Status = Pendiente`, `RetryCount = 0`.

**Cableado del motor genérico, rama `Bajada`** (`IntegrationSyncHostedService.EjecutarIntegracionAsync`, hoy `NotSupportedException` incondicional para `Bajada`/`Ambas` — se completa solo para `Bajada`; `Ambas` sigue sin implementar, no hay caso de uso todavía): resuelve el `IIntegrationEntityWriter` por `EntidadNegocio` (mismo patrón que la resolución de `IIntegrationEntityReader` en Ronda B), llama `conector.PullAsync`, y por cada registro llama `writer.EscribirAsync(...)` (nombre exacto de método a confirmar contra la interfaz real en el Step 1 del plan — no asumir sin leerla).

### Nuevas tablas de staging (`Modulo.Wms`, `WmsDbContext`)

Mismo patrón que `wms_oracle_stage_slsh` (Ronda A): columnas técnicas comunes `Status` (enum `Pendiente`/`ProcesadoWms`/`ErrorWms`), `RetryCount`, `ErrorMsg`, `CreatedAt`, `SyncedAt`, `SourceUpdateDate` (el cursor).

- `wms_sap_stage_item`: `LineId` (PK), `CompanyId`, `ItemCode`, `ItemName`, `BarCode`.
- `wms_sap_stage_store`: `LineId` (PK), `CompanyId`, `CardCode`, `CardName`, campos de dirección Ship-to.
- `wms_sap_stage_inbound_hdr` (`LineId` PK, `CompanyId`, `SapDocEntry`, `ShipmentType`) + `wms_sap_stage_inbound_dtl` (`LineId` PK, `ParentId` FK cascade, `ItemCode`, `Quantity`, `WhsCode`, `LineNum`) — mismo patrón cabecera/detalle que `wms_oracle_inbound_stage`/`wms_oracle_stage_slsh`.

### Etapa 2 — Subida: staging → Oracle WMS Cloud real

**`WmsCloudConnector`** (nuevo, `Modulo.Wms` o `PortalSaas.Core` — a decidir en el plan según dónde vivan las dependencias HTTP; probablemente `Modulo.Wms` porque no depende de nada de SAP), implementa `IIntegrationConnector`, `Tipo = "WmsCloud"`. `PushAsync` arma el XML real descubierto en el legado (`WmsApiService.SendXmlWithResponseAsync`, `C:\PROYECTOS\WMS_Suite`):

```
LgfData
  Header (DocumentVersion=24D, OriginSystem=LogFire, ClientEnvCode, ParentCompanyCode, Entity, TimeStamp, MessageId)
  ListOfItems / ListOfStores / ListOfIbShipments   -- según Fields["TipoDocumento"] del registro
```

POST `form-urlencoded` (`xml_data={xml}`) con auth Basic contra la URL de Oracle WMS Cloud (`ConectorConfigCifrado` de esta definición trae `ApiUrl`/usuario/clave del WMS — nunca las de SAP). Mismo patrón de aislamiento por registro y `IntegrationPushResult` por registro que Ronda B (Fix C2) — un traslado que SAP rechaza no debe tumbar los artículos/tiendas del mismo ciclo, y viceversa (cada entidad corre en su propia `IntegrationDefinition`/ciclo, así que en la práctica el aislamiento es automático: no hay lotes mixtos de tipos distintos).

**3 `IIntegrationEntityReader` nuevos** (`Modulo.Wms`, mismo patrón que `WmsSlshInventoryReader` de Ronda B): `WmsSapStageItemReader`, `WmsSapStageStoreReader`, `WmsSapStageInboundReader` — cada uno lee `Status = Pendiente` de su tabla, arma `IntegrationRecord`s con `Fields["TipoDocumento"]` (`"Item"`/`"Store"`/`"IbShipment"`) para que `WmsCloudConnector` sepa qué lista XML armar, y su propio `MarcarProcesadoAsync` actualiza `Status`/`ErrorMsg` en su tabla.

**El cierre del círculo:** el `wms_sap_stage_inbound_hdr.SapDocEntry` que sale como `ib_shipment` hacia el WMS en esta ronda es el mismo valor que Ronda B espera de vuelta como `order_hdr_cust_field_4`/`BaseEntry` en el XML de confirmación del traslado — con esta ronda mergeada, un Traslado real creado en SAP puede recorrer el ciclo completo: SAP → (Ronda C) → WMS → (Ronda B, ya mergeada) → SAP, confirmado.

## Fuera de alcance (explícito)

- Picking/Órdenes de venta (`OPKL`) — entidad 4ª del legado, ronda futura, no bloquea la validación del piloto de Traslados.
- Confirmación de llegada real a WMS y detección de rechazos (equivalente a `WmsOutbound_ExistsProcessor`/`StageErrorProcessor`) — ronda futura. Ronda B ya cierra el círculo del Traslado específico cuando el WMS contesta con el evento de confirmación; esto sería una capa adicional de verificación/alerta, no bloqueante.
- Códigos de barra (`item_barcode`) — nunca implementado del lado SAP en el legado tampoco, sin caso de negocio real.
- `Direccion.Ambas` en `IntegrationSyncHostedService` — sigue sin implementar, ninguna `IntegrationDefinition` de esta ronda la usa.
- Limpieza/retención de las tablas de staging nuevas (equivalente a `TableCleanupWorker`) — no se replica en esta ronda; si el volumen del piloto lo justifica, se agrega después.
- Deserialización dinámica de UDFs no declarados (no existe hoy en el repo, ver investigación) — los campos leídos de SAP están declarados explícitamente por entidad (arriba), no hay un mecanismo de "leer cualquier UDF" genérico.

## Riesgos y supuestos explícitos

- **Nombres exactos de recurso/campo en Service Layer** (`Items`, `BusinessPartners`, `InventoryTransferRequests`, y los nombres reales de los campos de dirección Ship-to en `BPAddresses`) se asumen por convención estándar de SAP B1 Service Layer, no confirmados contra un ambiente SAP real — el Step 1 de cada tarea del plan debe confirmarlos (mismo patrón de "no asumir código ilustrativo" ya usado en rondas anteriores).
- **Verificación end-to-end contra SAP/WMS reales pendiente** — igual que Ronda B, esta ronda se implementa y prueba con fakes/EF Core InMemory; el criterio de éxito real (un Traslado real en SAP llega al WMS y el WMS lo confirma de vuelta) no se ejercita contra un ambiente real dentro de este plan.
- **Nombre exacto del método de `IIntegrationEntityWriter`** no se asume — se confirma leyendo la interfaz real en el Step 1 del plan (existe en el repo desde el motor base, nunca implementada hasta ahora).

## Criterio de éxito

- Una fila de Artículo/Tienda/Traslado que cumple el filtro de negocio en SAP aparece como `Pendiente` en la tabla de staging correspondiente tras un ciclo de Bajada.
- Esa fila `Pendiente` es leída, transformada a XML y enviada (`POST` real) a Oracle WMS Cloud tras un ciclo de Subida — queda `ProcesadoWms` si el WMS acepta, `ErrorWms` con el mensaje real si rechaza.
- El `DocEntry` de una Solicitud de Traslado enviada por esta ronda coincide con el `BaseEntry` que Ronda B necesita para procesar la confirmación de ese mismo traslado cuando el WMS responde.
