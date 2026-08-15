# Migración Wms — Ronda B: Motor + SAP — Diseño

**Fecha:** 2026-08-15
**Estado:** Aprobado para plan de implementación
**Precede a:** cierre del ciclo completo WMS→SAP para el piloto (después de esto, Ronda A + Ronda B juntas reemplazan, para el piloto, la parte WMS→SAP de `WmsSapIntegration.Service`).
**Depende de:** Ronda A (ingestión + staging), ya mergeada — este diseño lee `wms_oracle_stage_slsh`, que Ronda A produce.

## Contexto

Ronda A dejó filas aplanadas en `wms_oracle_stage_slsh` (una por `ob_stop` del XML), pero el motor genérico (`PortalSaas.Integrations`) nunca las lee: `IntegrationSyncHostedService.EjecutarIntegracionAsync` tiene `registrosExternos` hardcodeado vacío en la rama `Subida`, con un comentario explícito marcando el hueco. Y `SapDocumentConnector.PushAsync` (rama `Inventory`) lanza `NotSupportedException` — sin mapeo DTO real todavía.

Investigación previa a este diseño encontró dos hallazgos que cambian el alcance:

1. **El piloto (`StockTransfer` vía confirmación de traslado) es un Copy-From en SAP**, no una creación directa — el legado nunca resuelve almacén origen/destino desde los datos del WMS: cada línea trae `BaseType`/`BaseEntry`/`BaseLine` apuntando a una Solicitud de Traslado (`OWTQ`) ya existente, y SAP copia los almacenes de ahí. **Ninguno de los 3 motores genéricos (Venta/Compra/Inventario) soporta Copy-From hoy** — es infraestructura nueva, no solo mapeo.
2. El `BaseEntry`/`BaseLine` que llega en el XML del piloto depende de que Oracle WMS haya recibido antes la Solicitud de Traslado desde SAP (camino SAP→WMS, fuera de alcance permanente de este proyecto por ahora). **Se asume que ese dato llega válido en el XML real** — decisión explícita del dueño del proyecto, sin confirmación empírica todavía.

Confirmado además: el mecanismo real de Copy-From en SAP B1 Service Layer es `BaseType`/`BaseEntry`/`BaseLine` seteado directo en cada línea del documento nuevo — el patrón `DocumentReferences` que usa el legado (`WmsSapIntegration.Service`) es sospechoso, probablemente una estructura propia de ese proyecto, no el mecanismo estándar de SAP. Este diseño usa el patrón estándar confirmado, no el del legado.

## Decisión

Cinco piezas, cada una con su propia tarea en el plan de implementación:

### 1. Copy-From en los 3 motores genéricos (paridad)

Agregar a `SapSalesDocumentLine`, `SapPurchaseDocumentLine`, `SapInventoryDocumentLine` (`PortalSaas.Core/Ventas|Compras|Inventario/`):

```csharp
public int? BaseType { get; set; }
public int? BaseEntry { get; set; }
public int? BaseLine { get; set; }
```

Mismo patrón que la propiedad existente `LineNum` en las tres clases (nullable, sin atributos, PascalCase literal). Agregar los mismos tres campos a `SalesDocumentLineDto`/`PurchaseDocumentLineDto`/`InventoryDocumentLineDto` (`PortalSaas.Abstractions/Modelos/`), y poblarlos en cada `MapLine` de los tres servicios (`SalesDocumentService.MapLine`, `PurchaseDocumentService.MapLine`, `InventoryDocumentService.MapLine`) — cambio local y aditivo, no toca `BuildRequestBody` (que serializa el POCO tal cual, con o sin estas propiedades). Se hace en los 3 motores a la vez (decisión explícita), aunque solo Inventario tenga un caso real que lo ejercite en esta ronda — cierra de paso la brecha ya documentada de `PurchaseOrder` (Compras), que en el sistema original nunca se crea sin Copy-From.

### 2. Mecanismo de ack en `IIntegrationEntityReader`

Se agrega un método nuevo a la interfaz existente (`PortalSaas.Abstractions/Contratos/Integraciones/IIntegrationEntityReader.cs`):

```csharp
public interface IIntegrationEntityReader
{
    string EntidadNegocio { get; }
    Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken);
    Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken);
}
```

El reader es quien mejor sabe cómo identificar y actualizar sus propias filas de origen — recibe de vuelta el mismo `IntegrationRecord` que él produjo (con los IDs de línea que él mismo embebió, ver punto 3), y decide qué actualizar. Esto evita que el orquestador genérico (`IntegrationSyncHostedService`) tenga que conocer el esquema de datos de cada módulo consumidor.

`IIntegrationEntityWriter` (dirección Bajada) no se toca — sigue fuera de alcance (`NotSupportedException` ya establecido).

### 3. `WmsSlshInventoryReader` (nuevo, plugin `Modulo.Wms`)

Implementa `IIntegrationEntityReader`, `EntidadNegocio = "Wms.ConfirmacionTraslado"`.

**`LeerPendientesAsync`**: lee `wms_oracle_stage_slsh` con `Status == Pendiente`, join a `wms_oracle_inbound_stage` (para filtrar por `CompanyId` — `wms_oracle_stage_slsh` no tiene `CompanyId` propio, lo hereda de `ParentId`), **agrupadas por documento** (`order_hdr_cust_field_4`, que es el `BaseEntry` de la Solicitud de Traslado origen — un mismo traslado puede tener varias líneas/`ob_stop`). Por cada grupo, arma un `IntegrationRecord` con:

```
Fields:
  TipoDocumento: "Inventory"
  DocDate: <de TimeStamp o ord_date del grupo>
  Lineas: List<IntegrationRecord>   -- uno por fila de wms_oracle_stage_slsh del grupo
    cada línea: ItemCode (item_part_a), Quantity (shipped_qty),
                BaseType: 1250000001, BaseEntry (order_hdr_cust_field_4), BaseLine (order_dtl_cust_number_2)
  _StagingLineIds: List<long>       -- LineId de cada fila incluida (campo interno, para el ack)
```

`_StagingLineIds` es metadata interna del reader (prefijo `_`, por convención no es un campo de negocio) — el motor genérico nunca lo interpreta, solo lo transporta de vuelta en el `MarcarProcesadoAsync`.

**`MarcarProcesadoAsync`**: lee `_StagingLineIds` del `IntegrationRecord` recibido, actualiza esas filas de `wms_oracle_stage_slsh`: `Status = ProcesadoSap` (+ `SapDocEntry` si `exito`) o `Status = ErrorSap` (+ `ErrorMsg = mensajeError`) si no.

### 4. Mapeo real en `SapDocumentConnector` (rama `Inventory`)

Reemplaza el `NotSupportedException` actual. Lee `registro["Lineas"]` (cast a `IReadOnlyList<IntegrationRecord>`), arma un `InventoryDocumentLineDto` por línea (`ItemCode`, `Quantity`, `BaseType`/`BaseEntry`/`BaseLine` — **sin** `FromWarehouseCode`/`ToWarehouseCode` explícitos, porque SAP los resuelve del Copy-From; se pasan como `null`/vacío, consistente con que el DTO ya los declara nullable-friendly para este caso), arma el `InventoryDocumentDto` (header con `DocDate` del registro), y llama:

```csharp
await _inventoryDocumentService.CreateAsync(InventoryDocumentType.StockTransfer, "wms-integration", dto, cancellationToken);
```

(`portalUsername` fijo `"wms-integration"` para trazabilidad en el UDF `U_PortalUser` — no hay un usuario de portal real detrás de esta integración).

Las ramas `Sales`/`Purchase` del `switch` **no se tocan** — siguen lanzando `NotSupportedException` (sin caso real todavía, ver plan de la ronda anterior).

### 5. Cableado en `IntegrationSyncHostedService`

En `EjecutarIntegracionAsync`, rama `Subida` (reemplaza el bloque hardcodeado):

```csharp
var readers = scope.ServiceProvider.GetServices<IIntegrationEntityReader>().ToList();
var reader = readers.FirstOrDefault(r => r.EntidadNegocio == definicion.EntidadNegocio)
    ?? throw new InvalidOperationException($"No hay reader registrado para entidad '{definicion.EntidadNegocio}'.");

var registrosExternos = await reader.LeerPendientesAsync(definicion.CompanyId, cancellationToken);
var registrosMapeados = new List<IntegrationRecord>();
foreach (var registroLocal in registrosExternos)
{
    registrosMapeados.Add(await mapeoServicio.MapToExternalAsync(definicion.Id, registroLocal));
}

await conector.PushAsync(conectorConfigJson, registrosMapeados, cancellationToken);

foreach (var registroLocal in registrosExternos)
{
    await reader.MarcarProcesadoAsync(definicion.CompanyId, registroLocal, exito: true, mensajeError: null, cancellationToken);
}

log.RegistrosProcesados = registrosExternos.Count;
```

**Aislamiento por registro**: si `conector.PushAsync` falla para todo el lote (falla de red/SAP), el `catch` externo ya existente marca `log.Resultado = Error` para todo el ciclo — pero para que un solo documento con datos malformados no bloquee a los demás del mismo ciclo, `PushAsync` debe iterar internamente y capturar por documento (ya es responsabilidad de `SapDocumentConnector`, que ya itera `foreach (var registro in registros)` — ver ajuste necesario: hoy el `foreach` de `SapDocumentConnector.PushAsync` deja que una excepción de una línea aborte todo el método; se ajusta para capturar por registro y solo re-lanzar si TODOS los registros del lote fallaron, permitiendo que `MarcarProcesadoAsync(exito: false, ...)` se llame para los que fallaron individualmente sin perder los que sí funcionaron). Esto es un ajuste a la implementación de Task 4 de la ronda anterior (dentro del alcance de esta ronda, no una regresión).

## Fuera de alcance (explícito)

- Camino SAP→WMS (no se toca, no se construye).
- Copy-From en Venta/Compra ejercitado por un caso real — solo se agrega la infraestructura (Pieza 1), sin consumidor todavía en esos dos motores.
- Otros tipos de documento del piloto (`ReceiptConfirm`, `IHTH`, `SVSH`) — solo `StockTransfer`/`SLSH`.
- Verificación end-to-end contra un ambiente SAP real con el Transaction Notification de `PurchaseOrder` — no aplica a este motor (es Inventario, no Compras), pero sigue siendo un riesgo abierto documentado en `CLAUDE.md` para cuando se ejercite Compras.
- Reintentos/circuit-breaker más allá del ya existente `Intentos`/`ErrorStaging` de Ronda A — el ack de esta ronda solo marca éxito/error, no reintenta automáticamente (una fila en `ErrorSap` requiere intervención manual o una reingesta, igual que el legado).

## Criterio de éxito

- Una fila `Pendiente` en `wms_oracle_stage_slsh` (creada por Ronda A) es leída por `WmsSlshInventoryReader`, mapeada, y provoca un `POST` real a `StockTransfers` en SAP Service Layer con `BaseType`/`BaseEntry`/`BaseLine` correctos por línea.
- Si SAP acepta el documento: la fila queda `ProcesadoSap` con el `SapDocEntry` real.
- Si SAP rechaza el documento (ej. `BaseEntry` inválido): la fila queda `ErrorSap` con el mensaje real de SAP, sin afectar otras filas/documentos del mismo ciclo.
- Los 3 motores genéricos (`SalesDocumentDto`/`PurchaseDocumentDto`/`InventoryDocumentDto`) aceptan `BaseType`/`BaseEntry`/`BaseLine` por línea, aunque solo Inventario lo use en esta ronda.
