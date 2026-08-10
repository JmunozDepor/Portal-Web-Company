# Arquitectura — Modulo.Wms

Plan de arquitectura para portar la integración SAP Business One ↔ WMS Oracle
(hoy `WMS_Suite`, `C:\PROYECTOS\WMS_Suite`) como plugin externo del Portal SaaS,
siguiendo el mismo patrón que `Modulo.Rendiciones` (repo propio en
`Portal SaaS - Plugins\`, compilado aparte, copiado a `artifacts/plugins/` del
Host — ver `docs/09-GUIA-DESARROLLO-PLUGINS.md` de `Portal SaaS - Core`).

Este documento es el resultado de una sesión de análisis (arquitecto de
soluciones) — antes de seguir implementando, releer esto para no repetir las
decisiones ya tomadas ni las que quedaron explícitamente abiertas.

## Estado actual (2026-08-09)

**Fase 1, arrancada — scaffolding del plugin + migración inicial, código sin
UI todavía.**

- `src/Modulo.Wms/` — proyecto del plugin (`Microsoft.NET.Sdk.Razor`, mismo
  patrón exacto que `Modulo.Rendiciones.csproj`: `ProjectReference` temporal a
  `PortalSaas.Abstractions`, motor dual `Npgsql`/`SqlServer` referenciado sin
  fijar proveedor en compilación, target `PublicarComoPlugin`).
- `Models/WmsFieldMapping.cs` / `WmsServiceConfig.cs` / `WmsServiceHeartbeat.cs`
  — las 3 entidades de Fase 1, `CompanyId` (`Guid`) real en las tres.
- `Data/WmsDbContext.cs` — mapeo completo a `wms_oracle_field_mappings` /
  `wms_oracle_service_configs` / `wms_oracle_service_heartbeats`
  (`snake_case`, convención de nombres del Portal).
- `ModuloWms.cs` — `IModuloPortal` implementado (`ModuleCode = "Wms"`), menú
  base (Mapeo de Campos/Configuración del Servicio/Estado del Servicio, sin
  páginas Razor detrás todavía), `RegisterServices` resuelve `WmsDbContext` vía
  `IExternalDatabaseConnectionService` — mismo patrón exacto que
  `ModuloRendiciones.RegisterServices`.
- `src/Modulo.Wms.Migrations.Postgres/` y `.SqlServer/` — `DesignTimeDbContextFactory`
  + migración `InitialCreate` generada para los dos motores (`dotnet ef
  migrations add`, confirmado 1:1 contra el diseño de este documento: las 3
  tablas, mismas columnas). **`dotnet build` en 0 errores/0 advertencias** para
  los 4 proyectos.
- **Migración aplicada contra bases reales de desarrollo, los dos motores**
  (2026-08-09, sesión posterior a la anterior nota de bloqueo — Docker/SQL
  Server local ya disponibles): `modulo_wms_dev` creada y confirmada con las 3
  tablas reales tanto en Postgres (`docker exec ... \dt`) como en SQL Server
  (`sys.tables`). Comando usado, para repetir en otro entorno:
  ```
  dotnet ef database update --project src/Modulo.Wms.Migrations.Postgres/Modulo.Wms.Migrations.Postgres.csproj --startup-project src/Modulo.Wms.Migrations.Postgres/Modulo.Wms.Migrations.Postgres.csproj --context WmsDbContext
  ```
  (análogo para `.SqlServer`, ver `WMS_CONNECTION_STRING` en el
  `DesignTimeDbContextFactory` de cada uno para la cadena de conexión default
  de desarrollo).
- De paso, en la misma sesión, se aplicaron también las migraciones pendientes
  que tenía `Portal SaaS - Core` (Postgres y SQL Server), incluida
  `AddLicensingSignedToken` en SQL Server dev (pendiente desde el 29 jul). Dos
  conflictos de datos reales de prueba encontrados y resueltos al aplicar
  Postgres (no forzados a ciegas): una fila huérfana en
  `generic_import_configs` (borrada, sin valor de negocio) y dos filas de
  `module_external_connections` con `company_id` NULL que iban a colisionar
  contra el mismo default (remapeadas a la compañía real de cada
  organización). Detalle completo en el historial de la sesión, no repetido
  acá para no duplicar.
- Verificado además, por primera vez, que `PortalSaas.Host` levanta de punta a
  punta contra Postgres real con este estado (`/Account/Login` → 200, `/` →
  302 sin sesión, los 7 plugins existentes cargan, CSS del rediseño con
  sistema de 4 temas confirmado servido) — sin herramienta de navegador
  disponible en el entorno, así que es verificación HTTP real, no una captura
  visual.

**Falta explícitamente, no iniciado todavía — este es el punto real donde
retomar:**
- **El plugin no está publicado a `artifacts/plugins/` del Host** — solo
  existe en `dist/` de este repo (`PublicarComoPlugin` corre, pero nadie
  copió el resultado a `Portal SaaS - Core/artifacts/plugins/Modulo.Wms/1.0.0/`
  todavía, ni existe un `publish-dist.ps1` propio como el que sí tiene
  `Modulo.Rendiciones`). Confirmado con el Host real: el log de plugins
  cargados no lo menciona.
- Páginas Razor del port de `WmsPortal.Web` (Mapeo de Campos/Configuración del
  Servicio/Estado del Servicio) — hoy solo existen las entradas de menú, sin
  contenido detrás. **Sugerencia de orden: empezar por Mapeo de Campos**, es
  la más simple de las tres (CRUD directo sobre `wms_oracle_field_mappings`,
  mismo patrón que `Admin/PlatformModules` del Core).
- Ninguna fila de `module_external_connections` está configurada todavía para
  `module_code = "Wms"` — sin esto, `WmsDbContext` falla al primer intento de
  resolver conexión aunque el plugin ya esté cargado y con páginas.
- Migración de `PORTAL_USERS`/`PORTAL_COMPANIES` reales a
  `Organization`/`Company`/`User` del Portal.
- El cambio en `WmsSapIntegration.Service` para que lea estas 3 tablas desde
  el Portal en vez de su schema HANA local (la excepción acotada de Fase 1,
  ver más abajo).
- `config_source_effective` en el heartbeat existe en el modelo/tabla, pero
  todavía no lo escribe ningún proceso real (eso vive del lado de
  `WmsSapIntegration.Service`, no tocado en esta entrega).
- Toda la Fase 2 (maestro SAP, outbox, `wms_oracle_export_*`, licenciamiento).

## Qué es esto

`WMS_Suite` integra SAP Business One (HANA) con WMS Oracle Cloud (antes
comercializado como "Logfire") en tres proyectos hoy independientes:
`WmsPortal.Web` (UI de administración/dashboard), `WmsSapIntegration.Service`
(Windows Service, captura y sincronización), `WmsApiRest.webservices` (API que
recibe confirmaciones del WMS). Los tres corren standalone, con su propio modelo
de usuario/tenant, sin depender del Portal SaaS.

Este plan porta **`WmsPortal.Web`** como plugin del Portal (`Modulo.Wms`),
consumiendo la identidad/tenant/UI ya construida ahí (`ICurrentUserContext`,
`ICurrentCompanyAccessor`, motores genéricos de documento, catálogos SAP ya
portados) en vez de mantener un segundo sistema de usuarios paralelo.
`WmsSapIntegration.Service`/`WmsApiRest.webservices` **no se migran** — siguen
standalone, con una sola excepción acotada (ver Fase 1).

## Decisiones ya tomadas (no reabrir sin una razón nueva y explícita)

- **El módulo vive dentro del Portal SaaS, como plugin externo** — no como
  Windows Service standalone aparte. Decisión explícita del dueño del proyecto,
  no una inferencia de este análisis.
- **Todo el diseño de datos usa `company_id` real como columna, en la base
  propia del Portal (motor dual Postgres/SQL Server) — mismo criterio que
  `Modulo.Rendiciones`.** No se mantiene el aislamiento por schema físico de
  HANA que usa `WMS_Suite` hoy, salvo en el único punto donde el motor no deja
  otra opción (ver más abajo). Decisión tomada explícitamente después de
  evaluar la alternativa (resolver el schema por `Company` sin mover el dato) y
  descartarla a favor de esta.
- **`WmsSapIntegration.Service` no se migra al Portal en esta fase** — sigue
  siendo un Windows Service standalone. La única excepción: gana una conexión
  de solo lectura/escritura a 3 tablas puntuales del Portal (mapeo, config,
  heartbeat — ver Fase 1). Esto es un cable angosto, **no** es la integración
  completa `ICompanyProvider`↔Portal (esa sigue diferida, es un problema más
  grande y aparte).
- **No se construye una abstracción formal `IWmsAdapter`/`IErpAdapter` todavía**
  — el mismo roadmap de `WMS_Suite` ya la había marcado como backlog
  ("no priorizar hasta un segundo caso real"). En su lugar, el crecimiento a
  otros WMS se resuelve por la **forma del dato** (maestro SAP neutral + motor
  de mapeo configurable, ver más abajo), sin necesitar una interfaz formal
  todavía. Ver "Cómo esto resuelve el crecimiento a otros WMS".
- **Un solo motor de mapeo configurable, para las dos direcciones** (SAP→WMS y
  WMS→SAP) — no dos mecanismos separados. Ya existe precedente real en
  producción: `WmsPortal.Web` → `Admin/FieldMapping` (tabla
  `INT_SAP_FIELD_MAPPING` real de HANA, con templates `{header.MessageId}`,
  concatenación `;`, toggle `Activo` para portabilidad entre clientes) — se
  porta este motor tal cual (mismo lenguaje de templates, no reescribirlo) y se
  generaliza para cubrir también el mapeo de campos estándar del maestro SAP
  hacia el formato de Oracle WMS, hoy hardcodeado en los stored procedures.
- **El motor de inyección a SAP no es un cliente Service Layer nuevo** — arma
  el DTO desde el mapeo configurado y llama al `CreateAsync` ya existente de los
  motores genéricos de documento del Portal (`SalesDocumentService`/
  `PurchaseDocumentService`/`InventoryDocumentService`), ganando sesión SL
  cacheada y manejo de error gratis.
- **Nomenclatura de tablas**: `wms_oracle_*` para lo específico de este WMS
  (formato de campos, confirmaciones, config del servicio), nombre neutro
  (`sap_*`) para el maestro de SAP que en principio podría alimentar a
  cualquier WMS destino. Ver la sección de nomenclatura para el detalle
  completo y la corrección que motivó esta distinción.
- **La forma ya traducida al formato de Oracle antes de enviarla se persiste**
  (`wms_oracle_export_*`) — no se arma al vuelo en cada intento. Da
  trazabilidad real de qué se envió, con status/reintentos/error por fila,
  mismo patrón que ya usa `STG_WMS_VALIDATION` para el camino inverso.

## Contexto: por qué evoluciona de `WMS_Suite`, no se reconstruye

Ver el análisis completo en la conversación que originó este documento — resumen:

- `WmsPortal.Web` (.NET 8, MVC, 3 proyectos: `Core`/`Data`/`Web`) trae su propio
  modelo de tenant (`PORTAL_USERS`/`PORTAL_COMPANIES`/`PORTAL_USER_COMPANIES`,
  sesión con BCrypt) — redundante con `Organization`/`Company`/`User` del
  Portal. Migrar usuarios reales a ese modelo es trabajo de datos, no solo de
  código — hay que planearlo como tal.
- El mecanismo de captura SAP→staging (`SBO_SP_PostTransactionNotice` →
  `SP_DEP_WMS_INTEGRATION_TRIGGER` → tablas `STG_SAP_*`) corre **síncrono,
  dentro de la transacción de SAP**, con joins directos entre tablas nativas de
  SAP y las de staging **en el mismo schema HANA** — riesgo real de bloquear
  una transacción de negocio del cliente si el SP de staging falla. No portable
  a SQL Server tal cual (documentado como brecha sin resolver en el roadmap
  original de `WMS_Suite`).
- `Servicios SAP` (familia de Windows Services standalone del Portal, ver
  `Portal SaaS - Servicios SAP/CLAUDE.md`) no tiene hoy ningún concepto de
  licencia — el Portal ya construyó un mecanismo ECDSA P-256 completo
  (`LicenseTokenService`, `OnPremiseLicense.SignedStatusToken`,
  `LicenseActivatorBackgroundService` con heartbeat y ventana de gracia
  offline) que es reutilizable para licenciar Windows Services sin depender de
  la integración `ICompanyProvider` completa — son problemas independientes.

## Fase 1 — alcance

**Entra:**
- Portar `WmsPortal.Web` → `Modulo.Wms` (MVC → Razor Pages, `PORTAL_USERS`/
  `PORTAL_COMPANIES` migrado a `Organization`/`Company`/`User` del Portal, auth
  vía `ICurrentUserContext`/`ICurrentCompanyAccessor` en vez de sesión propia
  con BCrypt).
- Portar las 3 tablas de administración ya validadas como config real (no
  decorativas — confirmado contra el DDL real de `WMS_Suite`):
  `wms_oracle_field_mappings`, `wms_oracle_service_configs`,
  `wms_oracle_service_heartbeats` (ver diseño completo abajo). Viven en la base
  del Portal, `company_id` real.
- **Excepción acotada al alcance "no tocar `WmsSapIntegration.Service`"**:
  ese servicio gana una conexión de solo lectura a esas 3 tablas en el Portal
  (reemplaza su lectura actual desde el schema HANA local vía
  `ConfigSource: Database`). Es un cambio angosto — 3 tablas puntuales, no la
  integración `ICompanyProvider` completa.
- Fix barato, orthogonal al resto: sumar `config_source_effective` a
  `wms_oracle_service_heartbeats` — hoy la UI real de `WMS_Suite` tiene un
  footgun documentado (editar `Configuración del Servicio` no tiene efecto si
  la instancia real está en modo `File`, sin ninguna señal en la UI). Con el
  heartbeat reportando el modo real, se puede mostrar la advertencia en vez de
  fallar en silencio.
- Constantes compartidas (`MapperKey`, `ConfigKey`) movidas a
  `PortalSaas.Abstractions` en vez de mantenerse como "contrato manual de
  strings" duplicado a mano entre dos proyectos (así lo documenta el propio
  comentario del SQL de `WMS_Suite`) — gratis, no exige tocar la lógica de
  `WmsSapIntegration.Service`, solo compartir la constante.
- Antes de dar por cerrada la migración del `FieldMapping`: confirmar que el
  motor de templates (`{lpn}`, `{header.MessageId}`, concatenación con `;`,
  `OrdenWms:{x} ; ShipmentWms:{y}`) se porta **tal cual**, sin reescribirlo —
  es lógica de negocio real, ya en producción.

**No entra (queda para Fase 2):**
- El maestro SAP neutral (`sap_items`, `sap_order_headers/lines`, etc.).
- El outbox liviano en HANA + el procesador asíncrono que reemplaza el join
  pesado hoy hardcodeado en los stored procedures.
- Las tablas `wms_oracle_export_*` (forma ya traducida a Oracle).
- La extensión del motor de mapeo para cubrir campos estándar (hoy solo cubre
  UDFs).

## Fase 2 — alcance

Todo lo que Fase 1 dejó afuera, en este orden sugerido (cada uno depende del
anterior):

1. Outbox liviano en el trigger HANA (`ObjType`/`DocEntry`/`TransType`/
   `CapturedAt`) — reemplaza el `INSERT` pesado actual dentro de la transacción
   síncrona de SAP. Es el único cambio de esta fase que toca código HANA/SAP.
2. Procesador asíncrono en `WmsSapIntegration.Service` que lee el outbox, hace
   el join contra las tablas nativas de SAP (una sola vez, ahí donde tiene que
   estar por restricción de motor), y escribe el resultado en el maestro SAP
   neutral (`sap_*`) en la base del Portal.
3. Extender el motor de mapeo (`wms_oracle_field_mappings`, generalizado más
   allá de UDFs) para traducir del maestro SAP neutral a la forma que Oracle
   WMS espera.
4. Componente de subida que lee `wms_oracle_export_*` y postea a
   `init_stage_interface` de Oracle WMS.
5. Licenciamiento de `Servicios SAP` reutilizando el mecanismo ECDSA del
   Portal — independiente de los 4 anteriores, se puede hacer en paralelo.

## Diseño de datos completo

Todo vive en la base propia del Portal (motor dual Postgres/SQL Server),
`company_id` como columna real (FK a `companies`, nunca `organization_id`
directo, sin fallback — regla dura del Portal), **excepto** el outbox de HANA
(único punto físicamente atado al motor de SAP).

### Único punto que se queda en HANA (restricción de motor, no de diseño)

Confirmado contra el DDL real de `WMS_Suite`
(`STG_SAP_PRODUCTS`/`014_sp_dep_wms_integration_trigger.sql`): el trigger
`SBO_SP_PostTransactionNotice` → `SP_DEP_WMS_INTEGRATION_TRIGGER` corre una
stored procedure de HANA, síncrona con la transacción de SAP — no puede
escribir directo a una base Postgres/SQL Server externa. Este es el único
punto de toda la arquitectura que no sigue el modelo `company_id` del Portal:

```
WMS_OUTBOX_EVENTS (HANA, schema de la compañía SAP del cliente)
  ObjType, DocEntry, TransType, CapturedAt, Consumed
```

Minimal a propósito (Fase 2, ítem 1) — reemplaza el `INSERT` de ~140 columnas
que hoy corre dentro del camino crítico de la transacción SAP.

### Config/administración (Fase 1)

```
wms_oracle_field_mappings
  id                bigint PK
  company_id        FK -> companies (not null)
  mapper_key        varchar(50)   -- ORDER_CONFIRM_STOCKTRANSFER, y a futuro
                                     también entidades salientes (Item/Order/Shipment)
  field_name        varchar(100)
  value_template    varchar(500)  -- {lpn}, {header.MessageId}, etc. -- mismo
                                     lenguaje de templates que WmsPortal.Web hoy
  is_active         boolean
  updated_at        timestamptz
  updated_by        varchar(50)
  UNIQUE (company_id, mapper_key, field_name)

wms_oracle_service_configs
  id                bigint PK
  company_id        FK -> companies (not null)
  config_key        varchar(150)  -- HostedServices:IHTH_Processor, etc.
  config_value      varchar(500)
  is_active         boolean
  updated_at        timestamptz
  updated_by        varchar(50)
  UNIQUE (company_id, config_key)

wms_oracle_service_heartbeats
  company_id                FK -> companies (not null)
  processor_key             varchar(100)
  last_run_at               timestamptz
  status                    varchar(20)
  last_error                varchar(500)
  config_source_effective   varchar(20)   -- nuevo, cierra el footgun documentado
  PK (company_id, processor_key)
```

### Maestro SAP — neutral, vocabulario de SAP (Fase 2)

Acotado a lo que realmente hace falta sincronizar, no un espejo completo del
esquema de SAP. Reemplaza la captura directa de `STG_SAP_*` de hoy, que
aunque nace en SAP ya está pre-traducida al vocabulario de Oracle
(`hazmat`/`conveyable`/`putaway_type` no son campos de SAP).

```
sap_items                (company_id, código, nombre, UoM, código de barras,
                           peso/dimensiones si SAP los trackea)
sap_business_partners    (company_id, ...)
sap_warehouses           (company_id, ...)   -- confirmar contra STG_SAP_STORE
                                                 qué representa exactamente
sap_order_headers        (company_id, ...)
sap_order_lines          (company_id, order_header_id FK, ...)
sap_shipment_headers     (company_id, ...)
sap_shipment_lines       (company_id, shipment_header_id FK, ...)
```

### Forma traducida a Oracle WMS, persistida (Fase 2)

```
wms_oracle_export_items
wms_oracle_export_item_barcodes
wms_oracle_export_stores
wms_oracle_export_order_headers / wms_oracle_export_order_lines
wms_oracle_export_shipment_headers / wms_oracle_export_shipment_lines
```
Todas con `company_id`, `status`/`retry_count`/`error_message`/`synced_at` —
mismo patrón que ya usa `STG_WMS_VALIDATION` en `WMS_Suite`.

### Entrada WMS→SAP (sin restricción física, se porta directo)

```
wms_oracle_inbound_stage      -- reemplaza INT_WMS_STAGE
  company_id, document_type, file_name, file_hash, xml_content, status,
  attempts, error_message, sap_doc_entry, inserted_at, processed_at
  UNIQUE (company_id, file_hash)

wms_oracle_inbound_logs       -- reemplaza INT_WMS_LOGS

wms_oracle_stage_ihth         -- reemplaza STG_WMS_IHTH (entity Oracle: inventory_history)
wms_oracle_stage_slsh         -- reemplaza STG_WMS_SLSH
wms_oracle_stage_svsh         -- reemplaza STG_WMS_SVSH
  company_id, parent_id FK -> wms_oracle_inbound_stage, + columnas de negocio
  (confirmadas contra el DDL real, ver WMS_Suite/db/provisioning/hana/004-006)

wms_oracle_export_validations -- reemplaza STG_WMS_VALIDATION
  company_id, document_type, key, sent_at, wms_status_id, wms_status_desc,
  wms_error_message, validated_at, attempts
```

### No se porta

`INT_WMS_CONFIG` — tabla legacy del downloader SFTP viejo, sin ningún
consumidor en el código actual (confirmado).

## Pipeline completo (Fase 1 + Fase 2)

```
[HANA, físico — único límite real]
SBO_PostTransactionNotice → trigger → WMS_OUTBOX_EVENTS (mínimo)
                                            │
                                            ▼
[Portal DB, company_id real — todo lo demás]
Procesador asíncrono lee el outbox + hace el join contra tablas nativas de SAP
                                            │
                                            ▼
              sap_items / sap_business_partners / sap_warehouses /
              sap_order_headers+lines / sap_shipment_headers+lines
                                            │
                    [motor de mapeo, wms_oracle_field_mappings,
                     MapperKey por entidad, config por company_id]
                                            │
                                            ▼
              wms_oracle_export_items / _item_barcodes / _stores /
              _order_headers+lines / _shipment_headers+lines
                                            │
                                            ▼
                   POST a Oracle WMS (init_stage_interface)
                                            │
                                            ▼
                    wms_oracle_export_validations (reconciliación)

[Camino inverso WMS→SAP, sin restricción física]
wms_oracle_inbound_stage → wms_oracle_stage_ihth/slsh/svsh →
(mismo motor de mapeo) → motores genéricos de documento del Portal → SAP
```

## Cómo esto resuelve el crecimiento a otros WMS

Un WMS nuevo el día de mañana no toca `sap_*` (maestro neutral) ni el motor de
mapeo — agrega sus propios `mapper_key` y su propio componente de
subida/recepción que hable el protocolo de ese proveedor específico. El
crecimiento queda resuelto por la forma del dato (maestro neutral + mapeo
configurable), no por una interfaz `IWmsAdapter` construida sin un segundo caso
real que la valide — mismo criterio YAGNI que ya rige el resto del Portal.

## Piezas del Portal que se reutilizan tal cual, no se reinventan

- `ICurrentUserContext`/`ICurrentCompanyAccessor` — identidad y scoping,
  reemplaza la sesión propia de `WmsPortal.Web`.
- `IExternalDatabaseConnectionService`/`ModuleExternalConnection` — mismo
  mecanismo que ya usa `Modulo.Rendiciones` para resolver su base externa por
  compañía.
- `IItemCrossReferenceService`/`IPriceListService`/
  `IBusinessPartnerDefaultsService` (portados como prerrequisito de
  `Modulo.ImportacionGenerica`) — crosswalk de códigos, reutilizable tal cual
  para el mapeo SAP↔WMS.
- Motores genéricos de documento (`SalesDocumentService`/
  `PurchaseDocumentService`/`InventoryDocumentService`) — el motor de inyección
  a SAP llama a su `CreateAsync` en vez de un cliente Service Layer nuevo.
- Mecanismo de licenciamiento ECDSA (`LicenseTokenService`,
  `OnPremiseLicense.SignedStatusToken`, `LicenseActivatorBackgroundService`) —
  reutilizable para licenciar `Servicios SAP`, independiente de este plugin.

## Puntos abiertos, pendientes de confirmar antes de implementar

- Qué representa exactamente `STG_SAP_STORE` en el negocio real (¿sucursal de
  Comercial Depor? ¿bodega SAP? ¿ambas cosas mezcladas?) — define si
  `sap_warehouses` alcanza o hace falta separar en dos entidades.
- Confirmar semántica completa de columnas de `STG_WMS_SLSH`/`STG_WMS_SVSH`
  (ya confirmada para `STG_WMS_IHTH` contra el DDL real) antes de finalizar el
  diseño de `wms_oracle_stage_slsh`/`wms_oracle_stage_svsh`.
- Plan concreto de migración de datos de `PORTAL_USERS`/`PORTAL_COMPANIES`
  reales hacia `Organization`/`Company`/`User` del Portal — no es solo un
  problema de código.
- Si el módulo de `Servicios SAP` que gana licenciamiento (Fase 2, ítem 5) es
  una unidad vendible aparte o se considera incluida en la licencia on-premise
  general de la organización — decisión de negocio, no técnica.
