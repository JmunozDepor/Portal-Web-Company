# Motor de Integración ERP — Puntos Pendientes

**Fecha:** 2026-08-15
**Estado del motor:** Fase 1 (base) y Fase 2 (conector real + tooling + UI admin) mergeadas a `main`.
**Specs y planes de referencia:**
- `docs/superpowers/specs/2026-08-14-motor-integracion-erp-design.md` + `docs/superpowers/plans/2026-08-14-motor-integracion-erp.md` (motor base)
- `docs/superpowers/specs/2026-08-15-motor-integracion-erp-fase2-design.md` + `docs/superpowers/plans/2026-08-15-motor-integracion-erp-fase2.md` (Fase 2)

Este documento consolida los puntos que quedaron fuera de las dos rondas ya mergeadas, para que no se pierdan entre sesiones. No es un plan ejecutable — cuando se retome cualquiera de estos puntos, corresponde pasar por `brainstorming` → spec → plan como las rondas anteriores.

## Qué existe hoy (resumen)

- `PortalSaas.Abstractions.Contratos.Integraciones`: `IIntegrationConnector`, `IIntegrationFieldMappingService`, `IIntegrationEntityReader`, `IIntegrationEntityWriter`, `IntegrationRecord`.
- `PortalSaas.Data.Entities.Integraciones`: `IntegrationDefinition`, `IntegrationFieldMapping`, `IntegrationRunLog` (tablas `integration_definitions`, `integration_field_mappings`, `integration_run_logs`, nombres en inglés).
- `PortalSaas.Integrations`: `IntegrationFieldMappingService`, `IntegrationSyncHostedService` (`BackgroundService`, polling cada 1 min, un scope nuevo por integración, cifra/descifra `ConectorConfigCifrado` vía `ISecretoCifradoService` con fallback seguro si el valor no está cifrado).
- `PortalSaas.Core.Integraciones.SapDocumentConnector`: `Tipo == "Sap"`, `PullAsync` lanza `NotSupportedException` (bajada fuera de alcance), `PushAsync` inyecta `ISalesDocumentService`/`IPurchaseDocumentService`/`IInventoryDocumentService` pero lanza `NotSupportedException` explícito por cada `TipoDocumento` ("mapeo DTO pendiente de caso real de negocio").
- UI admin en `PortalSaas.Host/Pages/Admin/Integraciones/`: `Index` (listado + "Ejecutar ahora", con guard server-side de `Activo`) y `Bitacora` (historial de `IntegrationRunLog`). Sin formulario de creación/edición. Enlazada desde el navbar admin.

## Puntos pendientes

### 1. Mapeo DTO real en `SapDocumentConnector`
`PushAsync` tiene la estructura lista (document services inyectados, `switch` por `TipoDocumento`) pero cada rama lanza `NotSupportedException`. Falta construir el mapeo real `IntegrationRecord` → `SalesDocumentDto`/`PurchaseDocumentDto`/`InventoryDocumentDto` (incluye `portalUsername` y el enum `*DocumentType` correspondiente). No se puede hacer de forma especulativa — necesita un caso real de negocio que defina qué campos trae el `IntegrationRecord` (ver punto 3, Wms).

### 2. `PullAsync` (bajada) sin implementar
Tanto en `SapDocumentConnector` como en `IntegrationSyncHostedService.EjecutarIntegracionAsync` (que lanza `NotSupportedException` explícito para `Direccion.Bajada`/`Ambas`). Requiere diseño propio: de dónde vienen los datos a bajar, qué `IIntegrationEntityWriter` los recibe, y cómo se evita duplicar registros ya procesados.

### 3. Migrar `Modulo.Wms` para consumir el motor

**Hallazgo crítico (2026-08-15):** `WmsSapIntegration.Service` (el sistema legado, en `C:\PROYECTOS\WMS_Suite`) **ya implementa el ciclo completo WMS↔SAP en producción hoy** — no es solo captura de eventos, como se pensaba antes. Son 15 `BackgroundService`: ingestión de XML (API + file watcher), 3 processors de staging (`IHTH`/`SLSH`/`SVSH_Processor`), 2 processors que ya postean a SAP Service Layer (`WmsInbound_OrderConfirmProcessor`/`ReceipConfirmProcessor`, endpoints `StockTransfers`/`DeliveryNotes`/`Returns`/`PurchaseDeliveryNotes`), más 5 processors del camino SAP→WMS y limpieza de tablas. Esto **corrige** la nota anterior de "fuera de alcance permanente" sobre `WmsSapIntegration.Service` — ese punto describía solo la captura por trigger HANA, no todo lo que este servicio realmente hace.

**Decisión de negocio (2026-08-15):** sí se migra, gradualmente, empezando como piloto en una compañía de bajo volumen transaccional. Objetivo declarado: un producto controlable desde el Portal igual que el resto de módulos administrados (no un Windows Service aparte con configuración por archivo).

**Descomposición del piloto** (documento no ejecutable — cada ronda necesita su propio spec/plan cuando se retome):

- **Ronda 0 — Autenticación máquina-a-máquina** (spec ya escrito: `docs/superpowers/specs/2026-08-15-auth-maquina-a-maquina-design.md`, plan pendiente). Prerrequisito de plataforma: no existe ningún esquema de auth para llamadas de sistema externo (solo login de usuario y `"PlatformAdmin"`), ni forma de resolver `CompanyId` sin sesión. Diseño: `ApiClientCredential` + esquema `"ExternalApiKey"`, reutilizando `ICurrentCompanyAccessor` sin modificarlo (es claims-based, agnóstico del esquema que pobló los claims).
- **Ronda A — Ingestión + staging.** Endpoint que recibe XML de Oracle WMS (equivalente a `WmsInboundController.receive-xml` del legado, pero autenticado vía Ronda 0) + parseo hacia `wms_oracle_inbound_stage`/`wms_oracle_stage_slsh`/`wms_oracle_stage_svsh` en el Portal, acotado a un solo tipo de documento piloto: `ORDER_CONFIRM_STOCKTRANSFER` (el `mapper_key` mejor documentado, corresponde a `StockTransfers` sin variantes). Probable sin tocar el motor genérico todavía.
- **Ronda B — Motor + SAP.** Extender el contrato `IIntegrationEntityReader` con un mecanismo de ack (marcar fila como procesada/con error — hoy no existe, y el legado sí lo hace vía `PROCESADO_SAP`/`ERROR_SAP` en `STG_WMS_SLSH`/`SVSH`), implementar el reader en Wms leyendo el staging de la Ronda A, y completar el mapeo DTO real en `SapDocumentConnector` (rama `Inventory`) para `StockTransfers`. Esto cierra el ciclo y resuelve también el punto 1 de este documento (mapeo DTO real).

Ninguna ronda tiene plan escrito todavía excepto el spec de la Ronda 0. Los demás tipos de documento (`ReceiptConfirm`, etc.) y la dirección SAP→WMS quedan para después de validar el patrón con este piloto.

### 4. Conector Sorter (Wms↔Sorter)
**Bloqueado por falta de información externa.** Se mencionó como proyecto concretizable pero no se conoce el protocolo/API del sistema Sorter. Cuando esa información exista: agregar un conector nuevo (`RestConnector` o `ArchivoConnector`, según corresponda) siguiendo el mismo patrón que `SapDocumentConnector` — el mapeo, scheduler y bitácora del motor ya están listos, solo falta el conector y el caso de negocio.

### 5. Consumidor `Modulo.Rendiciones`
**Bloqueado por falta de información externa.** Depende de qué ERP/plataforma se negocie con el cliente para el producto independiente. Sin cambios al motor cuando llegue — solo declarar las entidades de negocio (`Rendicion`, `CentroCosto`) vía los contratos existentes.

### 6. Formulario de creación/edición de `IntegrationDefinition`
La UI admin actual es de solo listado/ejecución/bitácora. Falta CRUD completo para crear integraciones desde la UI (hoy se siembran por migración de datos). Ligado al punto 7.

### 7. Cifrado en escritura de `ConectorConfigCifrado`
El motor ya descifra correctamente al leer (`ISecretoCifradoService.Decrypt` con fallback seguro y log de advertencia si el valor no está cifrado). Pero nada cifra el valor al escribirlo — porque no existe ningún flujo de escritura real (ver punto 6). Se resuelve solo cuando se construya el formulario de creación: ese es el lugar natural para llamar a `ISecretoCifradoService.Encrypt`.

### 8. Gap de tooling EF en `PortalSaas.Host` — verificación end-to-end pendiente
El fix (paquete `Microsoft.EntityFrameworkCore.Design` + referencias a proyectos de migraciones) está aplicado y el build compila limpio. Pero la verificación completa de `dotnet ef migrations add --startup-project src/PortalSaas.Host` quedó parqueada: en el sandbox de desarrollo, el comando ahora sí arranca el DI container completo del Host (antes ni eso lograba), pero se bloquea porque el Host está configurado contra el SQL Server de producción (`sqlsap.cdepor.cl`), inalcanzable desde este entorno. Cuando alguien tenga acceso real a ese servidor (o a una base de datos de desarrollo equivalente), vale la pena re-correr el comando exacto una vez para cerrar la verificación end-to-end.

## Fuera de alcance permanente (decisiones ya tomadas, no re-abrir sin razón nueva)

- **`WmsSapIntegration.Service` — nota corregida (2026-08-15):** la decisión anterior ("no se toca ni se reemplaza, resuelve un problema distinto") estaba basada en información incompleta — se pensaba que solo hacía captura de eventos por trigger HANA. Investigación posterior confirmó que este servicio implementa el ciclo WMS↔SAP completo en producción (15 `BackgroundService`, ver punto 3 arriba). La decisión de negocio actualizada es reemplazarlo gradualmente, con piloto en una compañía de bajo volumen — este ítem ya NO está fuera de alcance, se movió al punto 3 como proyecto activo (descompuesto en Rondas 0/A/B).
- Migración de Ventas/Compras/Inventario/Rendiciones (uso actual de SAP) al motor genérico — no la necesitan, siguen sin mapeo dinámico.
- Conectores de colas/eventos en tiempo real (MQ/Kafka/sockets) — sin caso de uso real que lo justifique hoy.

## Mejoras menores diferidas (no bloquean nada, quedaron en el ledger de las revisiones)

- `Pages/Admin/Integraciones/Index.cshtml` no muestra la compañía/tenant de cada `IntegrationDefinition` — en un listado multi-tenant esto puede confundir si dos empresas tienen integraciones con el mismo nombre.
- `Bitacora.cshtml` usa `default:` para el caso `Error` del enum `IntegrationRunResultado` en vez de un `case` explícito — si se agrega un cuarto valor al enum en el futuro, se renderizaría como "Error" sin aviso del compilador.
- `IIntegrationEntityReader`/`IIntegrationEntityWriter` no tienen todavía ningún implementador real — se validarán de verdad recién con el punto 3 (Wms).
