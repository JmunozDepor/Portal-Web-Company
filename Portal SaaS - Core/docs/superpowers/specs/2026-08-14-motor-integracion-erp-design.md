# Motor de Integración ERP/Externo — Diseño

**Fecha:** 2026-08-14
**Estado:** Aprobado para plan de implementación
**Autor:** Sesión de brainstorming (Claude Code) con Jorge Muñoz

## Contexto y motivación

Hoy no existe un motor de integración genérico y reutilizable entre módulos del Portal. Cada plugin resuelve su integración con SAP de forma ad-hoc:

- `Modulo.Wms` tiene `FieldMappingService`/`WmsFieldMapping` (tabla propia `wms_oracle_field_mappings`), pero es solo configuración: no existe ejecución real de sincronización dentro del plugin. La sync real Wms↔SAP hoy la hace `WmsSapIntegration.Service`, un Windows Service **externo** al Portal (proyecto `WMS_Suite`), disparado por trigger HANA (`SBO_SP_PostTransactionNotice`).
- `Modulo.Rendiciones`, Ventas, Compras e Inventario hablan contra recursos SAP fijos (vía `ICostCenterCatalogService`, `IGeneralLedgerAccountCatalogService`, `SalesDocumentService`, etc.), sin mapeo dinámico de campos.
- El `ARQUITECTURA.md` de `Modulo.Wms` ya documenta una Fase 2 pendiente (motor de inyección Portal→SAP, reusando `SalesDocumentService`/`PurchaseDocumentService`/`InventoryDocumentService`) — **no construida todavía**.

Dos señales concretas empujan a generalizar en vez de construir esa Fase 2 como algo específico de Wms:

1. **Wms↔Sorter**: proyecto concretizable a corto plazo — WMS Oracle Cloud necesita integrarse con un sistema Sorter, además de SAP.
2. **Rendiciones como producto independiente**: en conversación comercial para negociarse desconectado de SAP, probablemente requiriendo integración con otro ERP o plataforma a definir por el cliente.

Con dos-tres casos reales identificados (no uno), generalizar cumple el umbral razonable para evitar sobre-diseño especulativo.

## Decisión

Construir un motor de integración genérico (`PortalSaas.Integrations`) como la implementación real de la Fase 2 de Wms, diseñado desde el inicio sin acoplarse a SAP ni a Wms, de modo que Rendiciones (u otro módulo futuro) pueda adoptarlo sin rediseño.

**Fuera de alcance, explícitamente:**
- No se toca ni se reemplaza `WmsSapIntegration.Service` (resuelve un problema distinto: captura de eventos SAP→staging por trigger HANA, síncrono dentro de la transacción SAP).
- No se migra a Ventas/Compras/Inventario/Rendiciones en su forma de uso actual de SAP — siguen sin cambios.
- No se implementan conectores de colas/eventos en tiempo real (MQ/Kafka/sockets) — solo SAP (vía document services existentes), REST y archivo (CSV/SFTP), que cubren los casos reales conocidos hoy.
- No hay migración forzada de ningún módulo existente al motor; es infraestructura disponible, no una migración de plataforma.

## Arquitectura

Tres capas independientes y reemplazables:

1. **Conectores** (`IIntegrationConnector`): abstraen *cómo* se habla con el sistema externo. Implementaciones iniciales: `SapDocumentConnector` (envuelve `SalesDocumentService`/`PurchaseDocumentService`/`InventoryDocumentService` existentes, sin cliente SAP nuevo), `RestConnector`, `ArchivoConnector` (CSV/SFTP).
2. **Mapeo de campos** (`IFieldMappingService` genérico): generaliza el patrón de `FieldMappingService` de Wms. Define, por integración, cómo un campo de una **entidad de negocio abstracta** (ej. "Rendicion", "PickingConfirmado") corresponde a un campo del sistema externo. No conoce nombres de tablas SAP ni de ningún ERP específico — eso es lo que le permite servir tanto a Wms↔SAP/Sorter como a Rendiciones↔ERP-por-definir.
3. **Orquestación** (`IntegrationSyncHostedService`): `BackgroundService` de ASP.NET Core en el mismo proceso del Host, en su propio hilo/cola — no bloquea requests HTTP. Ejecuta integraciones programadas (cron) y bajo demanda ("ejecutar ahora" desde el admin, que solo marca `NextRunAt = now` sin ejecutar inline). Concurrencia acotada con semáforo para proteger el pool de BD y las llamadas a SAP/externos.

Ningún módulo consumidor conoce el motor por dentro: solo implementa `IIntegrationEntityReader<T>`/`IIntegrationEntityWriter<T>` para sus propias entidades de negocio.

## Modelo de datos

Tres tablas nuevas en el esquema compartido de `PortalSaas.Core` (no en `WmsDbContext` ni en schema de ningún plugin):

- **`IntegrationDefinition`**: `Id, CompanyId, Nombre, ModuloOrigen, EntidadNegocio, ConectorTipo (Sap|Rest|Archivo), ConectorConfig (JSON cifrado, mismo esquema que credenciales SAP hoy), Direccion (Subida|Bajada|Ambas), Activo, ProgramacionCron, NextRunAt`.
- **`IntegrationFieldMapping`**: `Id, IntegrationDefinitionId, CampoLocal, CampoExterno, Transformacion (nullable), Obligatorio`.
- **`IntegrationRunLog`**: `Id, IntegrationDefinitionId, IniciadoEn, FinalizadoEn, Resultado (Exito|Error|Parcial), RegistrosProcesados, RegistrosConError, DetalleError, DisparadoPor (Programado|Manual)`.

Scoping multi-tenant vía `ICurrentCompanyAccessor` (patrón ya existente), soportando que distintas empresas usen distintos conectores/ERPs bajo el mismo motor.

## Flujo de sincronización

1. `IntegrationSyncHostedService` despierta cada minuto, busca `IntegrationDefinition` activas con `NextRunAt <= now`.
2. Por cada una, abre `IServiceScope`, resuelve el conector por `ConectorTipo` vía factory/DI.
3. Bajada: conector trae datos externos → `IFieldMappingService` transforma a la entidad de negocio local → `IIntegrationEntityWriter<T>` del módulo origen persiste.
4. Subida: `IIntegrationEntityReader<T>` del módulo origen expone pendientes → se mapean → el conector los envía.
5. Se escribe `IntegrationRunLog`; se recalcula `NextRunAt`.

## Tareas por proyecto/módulo

**`PortalSaas.Abstractions`** (Portal SaaS - Core):
- Contratos: `IIntegrationConnector`, `IFieldMappingService`, `IIntegrationEntityReader<T>`, `IIntegrationEntityWriter<T>`; modelos `IntegrationDefinition`, `IntegrationFieldMapping`, `IntegrationRunLog`.

**Nuevo proyecto `PortalSaas.Integrations`** (Portal SaaS - Core):
- `IntegrationSyncHostedService`, motor de orquestación, semáforo de concurrencia.
- `SapDocumentConnector`, `RestConnector`, `ArchivoConnector`.
- Migraciones EF Core para las 3 tablas, scoping por `Company.Id`.
- UI admin genérica: listar integraciones, ver bitácora, "ejecutar ahora".

**`Modulo.Wms`** (Portal SaaS - Plugins):
- Declarar entidad de negocio (ej. `PickingConfirmado`) vía `IIntegrationEntityReader`/`Writer`.
- Migrar `WmsFieldMapping`/`FieldMappingService` propios a `IntegrationFieldMapping` del motor; retirar tabla y servicio propios tras migrar.
- Configurar `IntegrationDefinition` para Wms↔SAP (`SapDocumentConnector`) y Wms↔Sorter (`RestConnector`, cuando el proyecto Sorter se concrete).
- Actualizar `ARQUITECTURA.md` de Wms: la Fase 2 pasa a ser "consumir el motor genérico", no una implementación ad-hoc.

**`Modulo.Rendiciones`** (Portal SaaS - Plugins) — consumidor futuro, sin trabajo inmediato:
- Cuando se concrete el ERP/plataforma destino del producto independiente: declarar entidades (`Rendicion`, `CentroCosto`) vía los mismos contratos. No requiere cambios en el motor.

## Criterio de éxito / salida

- Wms↔SAP funcionando sobre el motor reemplaza la necesidad de construir la Fase 2 ad-hoc.
- Si en 12 meses no aparece un tercer caso de integración real (más allá de Wms↔Sorter), no se invierte más en generalizar: los dos casos ya justifican la inversión hecha.
