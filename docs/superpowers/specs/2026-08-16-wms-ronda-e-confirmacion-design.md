# Migración Wms — Ronda E: Confirmación de llegada a WMS + detección de rechazos — Diseño

**Fecha:** 2026-08-16
**Estado:** Aprobado para plan de implementación
**Precede a:** cierre completo del ciclo de calidad SAP→WMS — con Ronda C/D, una fila puede llegar a `ProcesadoWms` (POST aceptado por Oracle WMS Cloud), pero eso solo confirma que el POST no fue rechazado en el momento, no que el documento efectivamente exista y sea válido en el WMS.
**Depende de:** Rondas C y D (SAP→WMS: Artículos, Tiendas, Traslados, Picking — todas mergeadas), sobre cuyas 4 tablas de staging opera esta ronda.

## Contexto

El sistema legado (`WmsSapIntegration.Service`, `C:\PROYECTOS\WMS_Suite`) tiene 2 processors dedicados exclusivamente a verificar el resultado real de lo ya enviado — ninguno de los dos envía datos nuevos:

**`WmsOutbound_ExistsProcessor`**: confirma que un documento ya enviado (`Status='SYNC_OK'` en la tabla `STG_SAP_*` origen) efectivamente existe en la entidad final del WMS. Hace GET al endpoint REST JSON de solo lectura (`WmsIntegration:LgfApiBaseUrl`) con una clave distinta por entidad: `ITEM`→`item?part_a=...`, `STORE`→`facility?code=...`, `ORDER`→`order_hdr?order_nbr=...`, `ASN`→`ib_shipment?shipment_nbr=...`. Máximo **20 intentos** (`MaxIntentos`), ciclo cada **300 segundos** (recorre las 4 entidades por ciclo, no es un intervalo por fila). Filas candidatas: `Status='SYNC_OK'` en la tabla origen, excluyendo ya-confirmadas (`WmsStatusId=90`) o agotadas (`Intentos>=20`) vía `STG_WMS_VALIDATION`. `ORDER`/`ASN` (documentos transaccionales) tienen ventana de fecha (`CreatedAt >= hoy - N días`, default 2); `ITEM`/`STORE` (maestros) no.

**`WmsOutbound_StageErrorProcessor`**: detecta rechazo explícito. GET `{entidad_stage}?company_code={code}&status_id=101` (paginado, por compañía) contra el endpoint de staging de WMS (`stage_item`/`stage_store`/`stage_order_hdr`/`stage_ib_shipment` — entidad de *staging* del lado WMS, distinta de la entidad final que consulta `ExistsProcessor`). Al detectar, marca `ERR_WMS` en `STG_WMS_VALIDATION` **y** en la tabla `STG_SAP_*` origen, inmediato, sin reintentos. Ciclo cada **60 segundos**. Corre sobre el mismo universo (`Status='SYNC_OK'`) que `ExistsProcessor`, sin ventana de fecha. Una vez que marca `ERR_WMS`, la fila sale de `Status='SYNC_OK'` y `ExistsProcessor` deja de tocarla.

Ambos corren sobre las **4 entidades** (Artículo/Tienda/Orden/Traslado), sin distinción salvo la ventana de fecha de `ExistsProcessor`.

**Decisión de alcance (ya confirmada): los 2 mecanismos, las 4 entidades — paridad completa.**

## Decisión

### Estado nuevo en `WmsSapStageStatus`

El enum (`Pendiente`/`ProcesadoWms`/`ErrorWms`, compartido por las 4 tablas de staging de Rondas C/D) gana un estado terminal nuevo: **`Confirmado`**. Redefine el significado de `ProcesadoWms`: pasa a ser "el POST fue aceptado, esperando confirmación de que existe de verdad en WMS" — ya no es terminal. `Confirmado` es el nuevo estado terminal de éxito real. Cambio aditivo — los readers/writers de Rondas C/D no necesitan cambios (nunca asignan ni consultan `Confirmado`, siguen funcionando igual con `Pendiente`/`ProcesadoWms`/`ErrorWms`).

**Reutiliza `RetryCount`** (ya existe en las 4 tablas, hoy sin uso — ver `docs/12-MOTOR-INTEGRACION-ERP-PENDIENTES.md`, "columna inerte") como contador de intentos de confirmación — no se crea una tabla nueva equivalente a `STG_WMS_VALIDATION`; el estado de confirmación vive directo en la fila de staging que ya existe.

### `WmsCloudValidationClient` (nuevo, `Modulo.Wms`)

Cliente HTTP GET contra el endpoint REST JSON de solo lectura de Oracle WMS Cloud (`LgfApiBaseUrl`, distinto de la URL de POST que usa `WmsCloudConnector`), Basic Auth. Dos métodos:
- `ExisteAsync(entidad, filtro)`: GET puntual (`item?part_a=...`, etc.) — retorna `true`/`false`.
- `BuscarRechazadosAsync(entidadStage, companyCode)`: GET paginado (`stage_item?company_code=...&status_id=101`, etc.) — retorna las claves rechazadas de esa compañía.

### `WmsStageErrorChecker` (nuevo `BackgroundService`, `Modulo.Wms`)

Ciclo cada 60s (mismo intervalo que el legado). Por cada compañía con filas `ProcesadoWms` en cualquiera de las 4 tablas de staging: `BuscarRechazadosAsync` por entidad-de-staging, y por cada clave rechazada encontrada, marca esa fila `ErrorWms` + `ErrorMsg` inmediato — mismo patrón exacto que el legado, sin reintentos.

### `WmsExistsChecker` (nuevo `BackgroundService`, `Modulo.Wms`)

Ciclo cada 300s. Por cada fila `ProcesadoWms` (que `WmsStageErrorChecker` no haya movido a `ErrorWms` en el mismo período) con `RetryCount < 20`: `ExisteAsync` con la clave de la entidad final correspondiente — encontrado → `Confirmado`; no encontrado y `RetryCount` alcanza 20 → `ErrorWms` con mensaje "no confirmado tras 20 intentos"; si no, incrementa `RetryCount`, reintenta el próximo ciclo. Ventana de fecha (`CreatedAt >= hoy - 2 días`) para Traslado/Picking (documentos transaccionales), sin ventana para Artículo/Tienda (maestros) — mismo criterio del legado.

## Fuera de alcance

- Tabla equivalente a `STG_WMS_VALIDATION` — reemplazada por `RetryCount` + `Status` ya existentes en cada tabla de staging.
- Reintentos automáticos de reenvío tras `ErrorWms` (una fila en `ErrorWms` por confirmación fallida requiere el mismo tratamiento manual/reingesta que cualquier otra fila en `ErrorWms` — Rondas C/D ya definieron ese comportamiento para fallos de POST, esta ronda no lo cambia).
- Verificación end-to-end contra un ambiente SAP/WMS real — igual que las rondas anteriores, se implementa y prueba con fakes.

## Criterio de éxito

- Una fila `ProcesadoWms` cuyo documento existe realmente en WMS pasa a `Confirmado` dentro de las siguientes ejecuciones del ciclo de 300s.
- Una fila `ProcesadoWms` cuyo documento fue rechazado explícitamente por WMS pasa a `ErrorWms` dentro del siguiente ciclo de 60s, sin esperar los 20 intentos de confirmación.
- Una fila que nunca se confirma ni se rechaza explícitamente pasa a `ErrorWms` tras 20 intentos de `WmsExistsChecker`.
