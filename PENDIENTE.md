# Pendiente — Modulo.Rendiciones

Estado: **fases 1-6 completas (27 jul 2026) — código base portado, migraciones
generadas y aplicadas contra los dos motores reales, plugin cargando de verdad en el
Host real, 4 bugs reales encontrados y corregidos, diseño visual del original portado
1:1, OCR/Azure Maps ahora soportan múltiples proveedores configurables con fallback y
control de consumo por cuenta**. Ver "Fase 4/5/6" abajo. Sigue sin probarse: flujo de
negocio completo con clic real en navegador (gasto→informe→aprobación) y una llamada
real a Azure con claves reales.

## Fase 7 (propuesta, no iniciada) — integración bidireccional con SAP: pedido explícito del dueño del proyecto (29 jul 2026)

Pedido textual: amarrar el Fondo por Rendir a su origen real en SAP y al usuario,
soportar caja chica de tienda (fondo compartido, no personal), y definir cómo las
rendiciones aprobadas se postean de vuelta a SAP en "algún estado por revisar". No es
un fix chico -- toca el modelo de datos y agrega un flujo de exportación nuevo. Se
analizó el problema (29 jul 2026) pero **no se escribió código todavía** -- faltan
decisiones de negocio que no se pueden asumir. Desglose:

### 1. Amarrar `ExpenseFund` a su documento de origen en SAP
Hoy `ExpenseFund` (`Models/ExpenseFund.cs`) es **100% carga manual** vía
`FondosPorRendir/Index` -- el usuario tipea monto/moneda/fecha, sin ningún vínculo a
un documento SAP real. Lo pedido es importar/sincronizar desde el documento que SAP ya
genera al entregar un anticipo. El portal ya tiene el cliente reutilizable para esto
-- `ISapConnectionProvider`/`ISapSession` (`PortalSaas.Abstractions`, `GetAsync<T>`
contra Service Layer), mismo mecanismo que usa `ICostCenterCatalogService` para leer
OPRC -- no hay que inventar conectividad nueva.

**Bloqueante real, primera pregunta a resolver con el equipo contable de Comercial
Depor**: qué documento SAP es la fuente de verdad del anticipo hoy (¿Down Payment
Request `ODPI`? ¿un Journal Entry manual? ¿otro?) y cómo se resuelve el usuario dueño
(¿por `CardCode` del empleado? ¿por vendedor/`SlpCode` asignado?). Sin esto no se
puede diseñar el mapeo.

### 2. Caja chica de tienda (fondo compartido, no personal)
Cambio de modelo más profundo que el punto 1: `ExpenseFund.UserId` hoy asume **un
fondo = una persona** (`required Guid UserId`). Una caja chica de tienda es **un fondo
= un centro de costo/tienda, usado por varios usuarios** (turnos, vendedores
distintos). Implica:
- `ExpenseFund` necesita poder existir sin `UserId` obligatorio, ligado en cambio a
  `CostCenterCode` como dueño.
- La lógica de saldo (`ExpenseFundService.CalculatePendingBalanceAsync`, filtra solo
  por `ExpenseFund.Id`) ya funciona igual de bien para un fondo compartido -- no
  requiere cambio ahí.
- Falta el concepto de **reposición periódica** -- la caja chica se rellena
  recurrentemente, no es un anticipo único que se salda una vez como el de un viaje.
  El `Status` actual (`Open`/`Settled`/`Overdue`) no modela un fondo "revolvente".
- Quién puede registrar contra la caja chica de una tienda -- probablemente cualquier
  usuario asignado a ese centro de costo vía `UserCostCenter` (ya existe, calza bien
  sin cambios).

### 3. Postear rendiciones aprobadas de vuelta a SAP en estado "por revisar"
El punto más abierto. Técnicamente factible con `ISapSession.PostAsync` (ya soporta
crear documentos), pero antes de tocar código hace falta decidir:
- Qué tipo de documento se crea en SAP (¿Gasto/AP Invoice? ¿JE? ¿liquidación del Down
  Payment original?).
- Si se crea un documento por informe aprobado, o se consolida por período/tienda.
- Cómo se referencia el fondo original (para descontar el anticipo) vs. gastos que no
  vienen de un fondo.

### Siguiente paso concreto para retomar
Sesión corta con el dueño del proyecto (o quien maneje contabilidad en Comercial
Depor) para fijar las tres decisiones de negocio de arriba, **o** explorar primero
qué documentos existen realmente en el SAP de Comercial Depor vía el Service Layer ya
conectado (`ISapConnectionProvider`) para proponer opciones concretas en vez de
preguntar en abstracto. No iniciar el cambio de modelo de `ExpenseFund` (punto 2)
hasta tener claridad de los tres puntos -- se tocan las mismas tablas/páginas.

## Fase 6 — múltiples proveedores configurables + consumo por cuenta (27 jul 2026)

Pedido explícito del dueño del proyecto: las integraciones de Azure Maps (kilometraje)
y Azure Document Intelligence (OCR) debían soportar **más de un proveedor disponible**
por servicio, con su propio control de consumo -- reemplaza el esquema anterior (una
sola clave por servicio, fija en `appsettings`/`dotnet user-secrets`, sin forma de
agregar una segunda cuenta sin tocar configuración de despliegue).

- **`ExternalServiceProvider`** (tabla nueva `external_service_providers`) -- una fila
  por cuenta/credencial: `ServiceType` (`AzureMaps`/`AzureDocumentIntelligence`),
  `Name` (para distinguir cuentas del mismo servicio), `Endpoint` (requerido solo para
  Document Intelligence), `ApiKeyEncrypted` (AES-256-GCM vía `ISecretoCifradoService`
  -- el plugin ya podía inyectarlo, es un contrato de `PortalSaas.Abstractions`),
  `MonthlyLimit`, `Priority` (menor se prueba primero), `IsActive`. Puede haber **N
  filas por (CompanyId, ServiceType)**.
- **`ExternalServiceUsage` rediseñada**: pasó de trackear por `(CompanyId,
  ServiceName)` a trackear por `ProviderId` (FK a `ExternalServiceProvider`, `ON
  DELETE CASCADE`) -- cada cuenta lleva su propio contador mensual, no uno solo por
  servicio.
- **`IExternalServiceProviderSelector`** (nuevo) -- centraliza el fallback: recorre
  los proveedores activos de un servicio en orden de `Priority` y devuelve el primero
  con cupo. Dos variantes según el patrón de costo:
  - `SelectForReservationAsync` (costo fijo conocido por llamada, ej. Azure Maps: 1
    transacción por request) -- reserva atómica en el primer proveedor con cupo.
  - `SelectAvailableAsync` (costo real solo se conoce DESPUÉS de la llamada, ej.
    Document Intelligence factura por página) -- devuelve el primero bajo su límite
    sin reservar nada; el llamador registra el consumo real después con
    `RecordAsync(providerId, ...)` sobre ESE MISMO proveedor.
- **`AzureMapsRoutingService`/`AzureDocumentIntelligenceExtractorService` reescritos**
  para usar el selector -- ya no leen `IConfiguration`/`Rendiciones:AzureMaps:*` ni
  `dotnet user-secrets`, las credenciales son 100% self-service desde la UI. Si la
  cuenta de mayor prioridad agotó su cuota, prueban la siguiente automáticamente antes
  de devolver "no disponible".
- **`IExternalServiceProviderService`** (CRUD) + página nueva
  **`Configuracion/Proveedores/Index`** (`/rendiciones/configuracion/proveedores`,
  agregada al menú) -- alta/edición/baja de proveedores por compañía, clave
  write-only (mismo patrón que `Instance`/`Company` del portal: nunca se vuelve a
  mostrar, dejarla en blanco al editar no la cambia).
- **`Configuracion/ConsumoServicios/Index` actualizada** -- ahora lista el consumo
  **por proveedor** (nombre de cuenta + servicio), no un total fijo de 2 constantes;
  cada tarjeta indica si ese proveedor específico alcanzó su límite (no todo el
  servicio, mientras haya otra cuenta con cupo).
- **Migraciones generadas y aplicadas contra Postgres y SQL Server reales**
  (`AddExternalServiceProviders`, incremental sobre el esquema de la fase 4) --
  confirmado con `\d` en Postgres: `external_service_providers` con FK entrante desde
  `external_service_usages.provider_id` (`ON DELETE CASCADE`), índice único
  `(provider_id, year, month)`.
- **Verificado**: `dotnet build` 0/0, plugin recargado en el Host real
  (`"Módulo Rendiciones v1.0.0 cargado (16 entradas de menú)"`, antes 15 -- la nueva
  entrada "Proveedores de Servicios Externos"), `/rendiciones/configuracion/proveedores`
  y `/rendiciones/configuracion/consumo-servicios` responden `302` sin crashear.

## Fase 5 — diseño visual portado 1:1 del original (27 jul 2026)

Pedido explícito del dueño del proyecto: "el plugin debe quedar con el mismo diseño
visual" que el original (`PortalSAP_v2`), no la traducción a Bootstrap genérico de la
fase 3. Se releyó `theme.css` completo del original (1.803 líneas) y se portaron los
selectores exactos que usan las páginas de Rendiciones -- mismo layout/spacing/
tipografía, con los nombres de variable de color traducidos a las que ya define
`site.css` de este portal (nunca un valor literal nuevo, mismo criterio que el resto
del proyecto):

| Original | Este portal |
|---|---|
| `--page-bg` | `--bs-body-bg` |
| `--success`/`--danger`/`--warning` | `--bs-success`/`--bs-danger`/`--bs-warning` |
| `--card-bg`/`--card-border`/`--text-main`/`--text-muted`/`--accent`/`--accent-2`/`--input-bg`/`--radius-sm`/`--radius-md`/`--shadow-card` | mismos nombres, ya existen en `site.css` |

Clases portadas a `wwwroot/css/rendiciones.css` (scoped al propio plugin, nunca toca
`site.css` del portal): `.card-ps`, `.admin-card`/`.admin-card-header`,
`.admin-subcard`, `.admin-muted`, `.admin-alert*`, `.admin-table` (+`.admin-fila-
inactiva`/`.admin-col-accion`), `.admin-row-actions`, `.admin-form-asignar`,
`.admin-master-detail`/`.admin-master-list`/`.admin-master-activo`, `.form-row`/
`.form-group`/`.form-control`/`.form-row-check`, `.btn-erp-primary`/`.btn-module-
action`/`.btn-module-danger`. Las ~20 páginas se reescribieron para usar estos
selectores en vez de las clases Bootstrap genéricas de la fase 3 (`btn btn-primary`→
`btn-erp-primary`, `table table-sm`→`admin-table`, `alert alert-success`→`admin-alert
admin-alert-success`, `mb-3`→`form-group`, `form-check`→`form-row-check`, wrappers
`<div class="card-ps admin-card">` agregados donde el original los tenía -- Detalle de
Gasto, Detalle de Informe, Cierre, y las 5 pantallas de Configuración).

**Decisión explícita, no un olvido**: los **íconos siguen siendo Bootstrap Icons**
(`bi-*`), no FontAwesome del original -- el portal ya decidió Bootstrap Icons como
estándar único para todos los plugins (ver `CLAUDE.md` "Sidebar dinámico"); agregar
FontAwesome solo para este plugin habría creado dos sistemas de íconos conviviendo en
el mismo shell. El resto del lenguaje visual (tarjetas, tablas, botones, formularios,
maestro-detalle, badges de estado) es una copia 1:1 del original, selector por
selector.

**Verificado**: `dotnet build` 0/0, plugin recargado en el Host real
(`"Módulo Rendiciones v1.0.0 cargado (15 entradas de menú)"`), las 7 rutas
principales devuelven `302` (login) sin crashear. **No verificado visualmente en
navegador todavía** -- sin herramienta de captura de pantalla disponible en esta
sesión, igual limitación que el resto del proyecto.

## Fase 4 — configuración y pruebas (27 jul 2026, completa en lo que no requiere UI)

**Migraciones EF Core generadas y aplicadas contra Postgres y SQL Server reales de
desarrollo** (antes no existían -- el módulo solo tenía scripts SQL sueltos en el
original). Dos proyectos de herramientas nuevos, **separados** del proyecto del plugin
(mismo patrón que `PortalSaas.Data.Migrations.PostgreSql`/`SqlServer` del portal):
`src/Modulo.Rendiciones.Migrations.Postgres/` y `src/Modulo.Rendiciones.Migrations.SqlServer/`,
cada uno con su propio `DesignTimeDbContextFactory`. **Bug real encontrado al intentar
hacerlo en un solo proyecto primero**: con los dos proveedores en el mismo ensamblado,
`dotnet ef migrations add` para SQL Server encontró el `ModelSnapshot` de Postgres ya
en el ensamblado y generó una migración de **`ALTER COLUMN`** (diff entre tipos
`uuid`→`uniqueidentifier`, etc.) en vez de `CREATE TABLE` -- corrupta, con advertencia
real de "pérdida de datos". Confirmado que separar en dos proyectos (cada uno con su
propio ensamblado, su propio `ModelSnapshot`) lo resuelve limpio -- verificado
regenerando ambas migraciones desde cero: la de SQL Server generó 13 `CreateTable`
reales, sin advertencias. Aplicadas contra bases nuevas (`modulo_rendiciones_dev`,
creada en el mismo contenedor Postgres del portal y en la instancia SQL Server local)
-- **14 tablas confirmadas en los dos motores** (13 + `__EFMigrationsHistory`).

**Fila real insertada en `module_external_connections`** (base del portal) para dos
organizaciones de prueba ya existentes (`Comercial Depor`→Postgres,
`Comercial GE2`/`Block QA`→SQL Server) -- verificado con un programa de prueba aislado
(descartado al terminar) que `IExternalDatabaseConnectionService.ResolveConnectionAsync`
descifra la contraseña real (AES-256-GCM con la clave maestra real del entorno) y arma
el connection string correcto para **los dos motores por separado**, cada organización
resolviendo a su propio motor sin interferencia.

**Plugin copiado a `artifacts/plugins/Modulo.Rendiciones/1.0.0/` del portal y cargado
de verdad contra el Host real (`dotnet run`, no solo `dotnet build`)** -- dos bugs
reales encontrados y corregidos en el camino, ninguno visible compilando:

1. **`TypeLoadException` al cargar el plugin, bug de plataforma (no de este
   repo)** -- `PluginLoadContext` del portal duplicaba `Microsoft.Extensions.
   DependencyInjection.Abstractions.dll` (arrastrada por los providers de EF Core con
   `CopyLocalLockFileAssemblies=true`), causando que `RegisterServices(IServiceCollection)`
   fallara porque el `IServiceCollection` del plugin y el del Host dejaban de ser "el
   mismo tipo". **Corregido del lado del portal** (`PortalSaas.Core.Infraestructura.
   PluginLoadContext.Load`, ver su `CLAUDE.md` "Bug real de plataforma") -- afecta a
   cualquier plugin futuro con dependencias NuGet propias, no específico de acá.
2. **500 en vez de redirect a login para requests anónimos** -- `RendicionesPageModelBase`/
   `ComprobanteAccesoBase` no tenían `[Authorize]` (a diferencia de
   `Modulo.Ventas`/`Modulo.Administracion`, que sí lo tienen en su base -- no hay
   convención global en el Host). Corregido acá, agregando `[Authorize]` a las dos
   clases base.

**Verificado con `curl` real contra `https://localhost:7207`** (sin sesión, solo para
confirmar que no crashea): `/rendiciones/{gastos,informes,fondos,aprobaciones,
configuracion/tipos-gasto}` devuelven `302` a login, ninguno `500`. Log de arranque
confirma `"Módulo Rendiciones v1.0.0 cargado (15 entradas de menú)"` -- coincide
exactamente con las 15 entradas declaradas en `GetMenu()`. `dotnet test` del portal:
**109/109 en verde** después de los dos fixes.

## Fase 3 — páginas Razor (26 jul 2026, completa)

Las ~20 páginas del original, todas portadas y compilando:

- `Gastos/{Index,Detalle,Importar,Comprobante,ComprobanteArchivo,ComprobanteAccesoBase}` —
  CRUD de gasto suelto (con el flujo de kilometraje vía `IRoutingService` y su preview
  AJAX), import masivo por OCR, y el visor de comprobante (zoom/rotar/pantalla completa,
  `Layout = null`, se abre en pestaña nueva).
- `Informes/{Index,Detalle}` — armar un informe desde gastos sueltos, cabecera, agregar/
  desvincular gastos, y el flujo completo enviar → aprobar/rechazar → reabrir.
- `Aprobaciones/Index` — bandeja del aprobador, reutiliza `Informes/Detalle` para decidir.
- `FondosPorRendir/Index` — listar y crear fondos por rendir.
- `Reportes/Cierre` — reporte de cierre con exportación CSV.
- `Configuracion/{TiposGasto,TiposDocumento,GruposAprobacion,PoliticasGasto,
  CentrosCostoUsuario,ConsumoServicios}/Index` — catálogos de mantención + maestro-
  detalle de grupos de aprobación + consumo de cuota de Azure.
- `RendicionesPageModelBase.cs`/`RendicionesUi.cs` — infra compartida (mensajes
  TempData, `LoadCatalogSafeAsync`) e iconografía (traducida a **Bootstrap Icons**,
  `bi-*`, ya vendored por el portal — el original usaba FontAwesome, `fas fa-*`, que
  este portal no trae).
- `wwwroot/css/rendiciones.css` — portado y **adaptado a las variables CSS reales del
  portal** (`--card-bg`/`--card-border`/`--accent`/`--bs-success`/`--bs-warning`/
  `--bs-danger`/`--radius-sm`/`--radius-md`/`--shadow-card`, ya definidas en
  `site.css` del Host) -- el original usaba nombres de variable de `theme.css` de
  `PortalSAP_v2` (`--success`/`--warning`/`--danger`/`--page-bg`) que no existen acá.
  Cero colores literales nuevos, mismo criterio que la regla de oro del original.

**Decisiones de esta fase**:

1. **Markup traducido a Bootstrap 5 puro** (`form-label`/`form-control`/`form-select`/
   `mb-3`/`btn btn-primary`/`table table-sm`/`alert alert-*`) en vez de las clases de
   `theme.css` del original (`admin-table`/`admin-card`/`btn-erp-primary`/
   `btn-module-action`/`admin-subcard`/etc.), que no existen en este portal (`site.css`
   trae un juego de clases distinto, ver `docs/09-GUIA-DESARROLLO-PLUGINS.md`). Las
   clases `rnd-*` propias del plugin (`rnd-header`/`rnd-stat-*`/`rnd-card-*`/
   `rnd-badge*`/`rnd-consumo-*`) se mantuvieron tal cual -- son del CSS propio del
   módulo, no del portal.
2. **`<partial name="_MensajesRendiciones" />` del original se inlineó** como bloques
   `@if (Model.SuccessMessage...)`/`ErrorMessage`/`WarningMessage` directo en cada
   página, en vez de crear un archivo `Shared/_MensajesRendiciones.cshtml` -- 3 líneas
   repetidas en ~15 páginas no justificaba una partial extra; si en el futuro se agrega
   lógica no trivial ahí, sí conviene extraerla.
3. **Gotchas de `docs/09-GUIA-DESARROLLO-PLUGINS.md` §4-5 del portal aplicados desde el
   primer commit de páginas**: el form de `Gastos/Importar.cshtml` (único sin ningún
   otro atributo `asp-*`) lleva `asp-antiforgery="true"` explícito -- sin esto, ese POST
   específico habría devuelto 400 (mismo bug ya encontrado dos veces en el portal
   principal). El resto de los forms ya tenían `asp-page-handler`/`asp-route-*`, que
   disparan la inyección automática del `FormTagHelper`.
4. **`IConsumoServicioExternoService.ObtenerResumenAsync` del original NO se replicó**
   como método genérico en `IExternalServiceUsageService` -- `Configuracion/
   ConsumoServicios/Index.cshtml.cs` arma el resumen localmente llamando
   `GetCurrentMonthUsageAsync` dos veces (los únicos 2 servicios existentes) con un
   record `UsageDto` local a la página, en vez de expandir el contrato del servicio
   para un caso de uso que hoy solo tiene esta pantalla.
5. **Confirmado**: todas las rutas declaradas en `ModuloRendiciones.GetMenu()` tienen
   ahora una página real detrás -- ninguna quedó como link roto.

## Pruebas y configuración — qué falta después de la fase 4

Hecho en la fase 4 (27 jul 2026): migraciones reales aplicadas en los dos motores,
fila real en `module_external_connections` con resolución de conexión verificada,
plugin cargando en el Host real, 2 bugs reales encontrados y corregidos, rutas
verificadas con `curl` sin sesión (302 correcto, sin 500). Sigue pendiente:

- **Login real con sesión y clic a través de la UI** -- todo lo de la fase 4 se
  verificó con `curl` sin autenticar (confirma que no crashea y que el routing/DI
  arrancan bien) y con un programa de prueba aislado para
  `IExternalDatabaseConnectionService` -- ningún flujo de negocio real (crear un
  gasto, armar un informe, aprobar) se ejecutó todavía con un usuario autenticado de
  verdad. Requiere conocer la contraseña de un usuario de prueba existente
  (`prueba1`/`prueba2`/`ti` en Comercial Depor) o crear uno nuevo con contraseña
  conocida.
- Claves reales de Azure Document Intelligence/Azure Maps (`dotnet user-secrets`),
  para probar OCR y kilometraje de punta a punta -- sin esto, esas dos funciones
  devuelven su mensaje de "no configurado", no crashean, pero tampoco están probadas.
- Confirmar visualmente en navegador (sidebar, badges, tarjetas, formularios) -- todo
  el trabajo de esta sesión se verificó por `dotnet build`/`dotnet run`/`curl`, nunca
  visualmente en un navegador real.
- Datos de prueba (`module_external_connections`) quedaron insertados en la base de
  desarrollo del portal para "Comercial Depor" (Postgres) y "Comercial GE2"/"Block QA"
  (SQL Server) -- son configuración real necesaria, no datos descartables; si se
  quiere limpiar, hacerlo a mano.

Este documento sigue `docs/09-GUIA-DESARROLLO-PLUGINS.md` del repo
`Proyecto Saas Portal` como referencia obligatoria. No renombrar el módulo ni usar
ningún nombre de producto comercial existente en el mercado.

## Hecho en esta fase

- Repo Git inicializado en `C:\PROYECTOS\Modulo.Rendiciones`.
- `src/Modulo.Rendiciones/Modulo.Rendiciones.csproj` — mismo patrón que los plugins
  del portal (net8.0, x64, `PublicarComoPlugin`). Referencia **temporal** por
  `ProjectReference` relativa a `PortalSaas.Abstractions` del repo hermano (ver TODO
  explícito en el `.csproj` — reemplazar por `PackageReference` NuGet cuando exista
  el paquete versionado).
- `ModuloRendiciones.cs` (`IModuloPortal`) — `ModuleCode = "Rendiciones"`, menú de 3
  grupos (Rendidor/Aprobador/Administrador) portado 1:1 del original.
  `RegisterServices` queda con TODOs comentados (ningún servicio portado todavía, ver
  abajo).
- 12 entidades en `Models/`, traducidas a inglés/PascalCase con la convención de BD
  de la plataforma aplicada (`docs/01-CONVENCION-NOMBRES-BD.md`):

  | Original (español) | Nuevo (inglés) | Tabla |
  |---|---|---|
  | `FondoPorRendir` | `ExpenseFund` | `expense_funds` |
  | `RendicionGasto` | `ExpenseReport` | `expense_reports` |
  | `RendicionGastoDetalle` | `ExpenseReportLine` | `expense_report_lines` |
  | `RendicionGastoAccion` | `ExpenseReportAction` | `expense_report_actions` |
  | `ComprobanteAdjunto` | `ExpenseReceipt` | `expense_receipts` |
  | `TipoGasto` | `ExpenseType` | `expense_types` |
  | `TipoDocumento` | `DocumentType` | `document_types` |
  | `PoliticaGasto` | `ExpensePolicy` | `expense_policies` |
  | `CentroCostoUsuario` | `UserCostCenter` | `user_cost_centers` |
  | `GrupoAprobacionRendicion` | `ExpenseApprovalGroup` | `expense_approval_groups` |
  | `GrupoAprobacionRendicionNivel` | `ExpenseApprovalGroupLevel` | `expense_approval_group_levels` |
  | `GrupoAprobacionRendicionMiembro` | `ExpenseApprovalGroupMember` | `expense_approval_group_members` |

- `Data/RendicionesDbContext.cs` — Fluent API completa (`ToTable`, `HasColumnName`
  snake_case explícito para las 12 entidades + `ExternalServiceUsage` (13ª, ver abajo),
  FKs, índices únicos).

## Fase 2 — servicios de dominio (26 jul 2026, completa)

Los 10 servicios del original + 1 nuevo, todos en `Servicios/`, registrados en
`ModuloRendiciones.RegisterServices`:

| Original | Nuevo |
|---|---|
| `IFondoPorRendirService`/`FondoPorRendirService` | `IExpenseFundService`/`ExpenseFundService` |
| `ITipoGastoService`/`TipoGastoService` | `IExpenseTypeService`/`ExpenseTypeService` |
| `ITipoDocumentoService`/`TipoDocumentoService` | `IDocumentTypeService`/`DocumentTypeService` |
| `ICentroCostoUsuarioService`/`CentroCostoUsuarioService` | `IUserCostCenterService`/`UserCostCenterService` |
| `IAlmacenamientoAdjuntosService`/`AlmacenamientoAdjuntosService` | `IAttachmentStorageService`/`AttachmentStorageService` |
| `IPoliticaGastoService`/`PoliticaGastoService` | `IExpensePolicyService`/`ExpensePolicyService` |
| `IGastoService`/`GastoService` | `IExpenseService`/`ExpenseService` |
| `IExtractorComprobanteService`/`AzureDocumentIntelligenceExtractorService` | `IReceiptExtractorService`/`AzureDocumentIntelligenceExtractorService` |
| `IRoutingService`/`AzureMapsRoutingService` | igual nombre, típed `HttpClient` |
| `IGrupoAprobacionRendicionesService`/`GrupoAprobacionRendicionesService` | `IExpenseApprovalGroupService`/`ExpenseApprovalGroupService` |
| `IRendicionGastoService`/`RendicionGastoService` | `IExpenseReportService`/`ExpenseReportService` |
| `IReporteCierreService`/`ReporteCierreService` | `IClosingReportService`/`ClosingReportService` |

**Decisiones nuevas de esta fase, no triviales**:

- **`ICentroCostoUsuarioService`/`ClienteCatalogoService` del original dependían de
  `IDimensionCatalogoService`/`IUsuarioAdminService` (Core de `PortalSAP_v2`)** —
  reemplazados por los contratos reales equivalentes YA EXISTENTES en
  `PortalSaas.Abstractions` de este portal: `ICostCenterCatalogService` (catálogo SAP
  de centro de costos, `DimCode=1`) e `ITenantUserAdminService` (usuarios de la propia
  organización, ya acotado por `ICurrentUserContext.OrganizationId`). No se inventó
  ningún contrato nuevo para esto -- ya estaban portados al portal por trabajo previo,
  solo había que descubrirlos y usarlos.
- **`IConsumoServicioExternoService` del original (control de cuota de Azure Document
  Intelligence/Azure Maps) NO se replicó como contrato de plataforma** -- en
  `PortalSAP_v2` vivía en el Core de la plataforma (`PORTALWEB`, sin `companyId` en la
  firma, instalación mono-tenant); acá se implementó **dentro del propio plugin**
  (`IExternalServiceUsageService`/`ExternalServiceUsageService`, entidad nueva
  `ExternalServiceUsage`/tabla `external_service_usages`, 13ª tabla del plugin) porque
  es un límite específico de estas dos integraciones de Rendiciones, no un concepto
  general de la plataforma todavía -- si un segundo plugin necesita lo mismo, ahí sí
  vale la pena promoverlo a `PortalSaas.Abstractions`. **Mejora deliberada sobre el
  original**: la reserva/registro de consumo ahora es por `CompanyId`, no global -- dos
  compañías de organizaciones distintas usando este plugin no comparten cupo mensual
  (el original, mono-tenant, no tenía este problema). Reserva atómica implementada con
  `ExecuteUpdateAsync` (EF Core 7+, traduce a un único `UPDATE` condicionado, portable
  entre los dos proveedores) en vez de SQL crudo -- con una ventana de carrera teórica
  aceptada y documentada en el código (creación de la fila del período, no el `UPDATE`
  en sí) para el volumen esperado de estas integraciones.
- **`IRoutingService.CalculateDistanceAsync` e `IReceiptExtractorService.ExtractAsync`
  ganaron un parámetro `Guid companyId`** que el original no tenía (su control de cuota
  era global) -- necesario para que `IExternalServiceUsageService` pueda aislar el
  consumo por compañía.
- **`SupplierTaxId`/RUT chileno**: el regex de extracción por OCR sigue siendo
  específico de Chile (heredado tal cual) -- documentado en el código, no oculto, ver
  el punto pendiente de abajo sobre confirmar el alcance multi-país.

## Decisiones de diseño tomadas en esta fase (documentadas, no asumidas en silencio)

1. **`CompanyId` (Guid) en vez de duplicar `OrganizationId`** en cada tabla. El
   original scopeaba por `EmpresaCodigo` (código de compañía SAP); acá se tradujo a
   `CompanyId` (FK lógica a `companies.id` de la plataforma), que ya resuelve a
   `organization_id` vía `companies.organization_id` — cumple la regla dura
   ("`organization_id` directo o vía `company_id`", `CLAUDE.md`) sin duplicar la
   columna. Como esta base es propia del plugin (no la compartida de la plataforma),
   no hay FK real de EF Core hacia `companies` — es una columna simple con la
   convención de nombre correcta, documentado en el DbContext.
2. **PK `long`/`bigint identity` para las 12 tablas**, no `Guid`/`uuid`. El original
   usaba `int identity` (con una excepción `long` en `RendicionGastoAccion`).
   `docs/01-CONVENCION-NOMBRES-BD.md` reserva `uuid` para entidades "que se
   referencian entre sí o pueden generarse fuera de la base" — acá el grafo de
   entidades es cerrado dentro del propio plugin (nada se genera fuera de esta base,
   nada se referencia desde la plataforma central), así que `bigint identity` es
   consistente con el criterio de "catálogos/tablas de bajo-medio volumen", aplicado
   parejo a las 12 en vez de mezclar dos tipos de PK sin necesidad real. Si en el
   futuro `ExpenseReport`/`ExpenseFund` necesitan ser referenciados desde fuera del
   plugin (ej. otro módulo, o generación de id en el cliente antes de guardar),
   reconsiderar puntualmente esas dos.
3. **`ExpenseApprovalGroupLevel` y `ExpenseApprovalGroupMember` ganaron un `Id`
   propio** que el original no tenía (PK compuesta) — regla dura de esta plataforma:
   toda tabla lleva `id` surrogate, nunca una PK compuesta de claves de negocio.
   Unicidad original preservada vía índice único (`uq_..._group_id_level` /
   `uq_..._group_id_user_id`).
4. **Timestamps `DateTimeOffset`** (antes `DateTime`) en las columnas `_at`, mismo
   criterio que el resto de la plataforma (`timestamptz` real, con zona horaria).
5. **Nombres de propiedad de negocio traducidos**, no solo la tabla — `Monto`→
   `Amount`, `FechaGasto`→`Date` (columna física `expense_date`, evita choque con
   `DateTime`/palabra reservada en algunos motores), `Estado`→`Status`, `Glosa`→
   `Notes`, `Folio`→`DocumentNumber`, `RutProveedor`→`SupplierTaxId`
   (RUT es específico de Chile, se generalizó a "tax id" pensando en un producto
   multi-país — confirmar con el dueño del proyecto si esto es lo esperado antes de
   avanzar a la UI de captura, que sí va a mostrar la etiqueta correcta en español).

## Bloqueos resueltos

1. ~~No existe en `PortalSaas.Abstractions` un equivalente a `ISqlServerService`~~ —
   **RESUELTO 26 jul 2026**: `IExternalDatabaseConnectionService`
   (`PortalSaas.Abstractions.Contratos`, implementado en
   `PortalSaas.Core.Infraestructura.ExternalDatabaseConnectionService`) resuelve la
   conexión contra una tabla nueva de la plataforma (`module_external_connections`),
   **motor dual desde el día uno** (Postgres/SQL Server, a diferencia del original que
   solo soportaba SQL Server) — ver `docs/09-GUIA-DESARROLLO-PLUGINS.md` §6.1 del
   portal para el contrato completo y el ejemplo de registro.
   `ModuloRendiciones.RegisterServices` (acá) ya registra `RendicionesDbContext` de
   verdad contra este servicio, eligiendo `UseNpgsql`/`UseSqlServer` en runtime según
   `ExternalDatabaseConnection.EngineType`. El `.csproj` ganó
   `Npgsql.EntityFrameworkCore.PostgreSQL` (además de `Microsoft.EntityFrameworkCore.SqlServer`,
   ya presente) — un plugin con base propia referencia SIEMPRE los dos proveedores,
   nunca uno solo. `RendicionesDbContext.OnModelCreating` se corrigió para no usar
   `HasColumnType("decimal(...)")` en ninguna columna (reemplazado por
   `HasPrecision(p, s)`, agnóstico de proveedor). **Sigue pendiente**: no hay pantalla
   de administración para cargar filas de `module_external_connections` todavía — se
   carga por SQL directo hasta que exista esa UI en el portal (mismo estado inicial
   que tuvieron `Plans`/`Subscriptions` ahí antes de su UI).

## Bloqueos reales — nada de esto se puede resolver dentro de este repo solo

1. **No existe todavía un paquete NuGet publicado de `PortalSaas.Abstractions`** —
   el `.csproj` usa una `ProjectReference` relativa cruzando repos como parche
   temporal (documentado ahí mismo), que solo funciona si este repo y
   "Proyecto Saas Portal" son carpetas hermanas en la misma máquina. No sirve para
   un build de CI real ni para distribuir el plugin como producto separado de
   verdad.
2. ~~Confirmar `RutProveedor`→`SupplierTaxId` con el dueño del proyecto~~ —
   **RESUELTO 29 jul 2026**: se mantiene específico de Chile, sin generalizar a
   multi-país. Ya coincide con la UI existente (`Gastos/Detalle.cshtml`, label
   "RUT proveedor" sobre el campo `SupplierTaxId`) — no requirió cambio de código.

## Orden de prioridad de los pendientes (29 jul 2026)

1. ~~Confirmar `RutProveedor`→`SupplierTaxId`~~ — resuelto arriba.
2. ~~Revisión de vocabulario de marca en las páginas~~ — resuelto arriba, sin
   coincidencias.
3. Flujo real en navegador con login (gasto→informe→aprobación) — **avance
   parcial 29 jul 2026, no completado**:
   - Host levantado con `dotnet run --project src/PortalSaas.Host --launch-profile
     https` — responde correctamente en `https://localhost:7207`.
   - **Login real verificado**: `jmunoz@comercialdepor.cl` es cuenta de
     **admin de plataforma** (`/Admin/Login`, esquema de cookie
     `PlatformAdmin`), no un usuario tenant — el login vía `/Account/Login?
     org=cl-depor` (esquema tenant) falla con esa cuenta, es esperado. Login
     de admin de plataforma confirmado funcionando end-to-end vía `curl` (POST
     con antiforgery token real, 302 a `/Admin/Organizations/Index`, cookie de
     sesión real emitida).
   - **Bug real encontrado y corregido**: la base Postgres de desarrollo tenía
     pendiente la migración `AddLicensingSignedToken`
     (`20260730030738_AddLicensingSignedToken`, columna
     `signed_status_token` en `organizations`) — sin aplicar, causaba un 500
     real (`42703: column o.signed_status_token does not exist`) al intentar
     crear **cualquier** usuario tenant desde `/Admin/Organizations/Users/
     Create`, no solo para Rendiciones. Corregido: `dotnet ef database update`
     aplicado contra Postgres real usando el proyecto de migraciones como
     startup (el Host no se pudo usar como startup mientras estaba corriendo
     — archivo `.exe` bloqueado por el propio proceso).
   - **Bloqueado, sin resolver todavía**: crear un usuario tenant de prueba en
     "Comercial Depor" vía `curl` (POST a `/Admin/Organizations/Users/Create/
     3da8bfa1-0af8-4fe6-96fd-7c7e151ff93a`) fue bloqueado dos veces por el
     clasificador de seguridad del modo automático de Claude Code, incluso con
     autorización explícita del dueño del proyecto en el chat — es una
     restricción a nivel de herramienta, no de la aplicación. El dueño del
     proyecto decidió detener la prueba en este punto (29 jul 2026) en vez de
     forzarla.
   - **Para retomar**: el dueño del proyecto crea manualmente un usuario
     tenant en Comercial Depor desde `/Admin/Organizations/Users/Create/
     3da8bfa1-0af8-4fe6-96fd-7c7e151ff93a` (o agrega una regla de permiso Bash
     para permitir el POST vía `curl`), y con esa cuenta se puede completar
     login tenant real (`/Account/Login?org=cl-depor`) + el clic-through de
     gasto→informe→aprobación. No hay seed de datos de negocio (rendiciones/
     gastos) para un tenant de prueba — habría que cargarlos a mano una vez
     dentro.
4. Claves reales de Azure Document Intelligence/Azure Maps — requiere que el
   dueño del proyecto las provea, no bloquea el resto del trabajo.
5. Paquete NuGet de `PortalSaas.Abstractions` — bloqueo estructural de
   infraestructura/CI, no urgente mientras el repo hermano siga en la misma máquina.
6. Housekeeping de datos de prueba en `module_external_connections` — trivial,
   pendiente de limpieza manual si se desea.

## Qué falta portar (fase 3 en adelante, en el orden sugerido)

### Páginas (`Pages/` del original, ~20 archivos .cshtml/.cshtml.cs):
`Aprobaciones/`, `Configuracion/{CentrosCostoUsuario,ConsumoServicios,GruposAprobacion,
PoliticasGasto,TiposDocumento,TiposGasto}/`, `FondosPorRendir/`, `Gastos/{Comprobante,
ComprobanteAccesoBase,ComprobanteArchivo,Detalle,Importar,Index}`, `Informes/`,
`RendicionesPageModelBase.cs`, `RendicionesUi.cs`, `Reportes/Cierre.cshtml`,
`Shared/_MensajesRendiciones.cshtml`. Aplicar los gotchas de
`docs/09-GUIA-DESARROLLO-PLUGINS.md` §4-5 del portal (nombres de vista sin colisión —
acá no aplica tanto por ser el único plugin en este repo, pero si algún día conviven
más plugins en el mismo `artifacts/plugins/`, revisar; antiforgery explícito;
cultura invariante en inputs numéricos) desde el primer commit de páginas, no
como fix posterior.

### Integraciones externas — código ya portado, falta configuración real:
- Azure Document Intelligence (OCR de comprobantes,
  `AzureDocumentIntelligenceExtractorService`) y Azure Maps (kilometraje,
  `AzureMapsRoutingService`) ya están implementados y registrados. Faltan las claves
  reales por organización/instalación (`Rendiciones:AzureDocumentIntelligence:Endpoint`/
  `:ApiKey`, `Rendiciones:AzureMaps:ApiKey`), nunca hardcodeadas -- `dotnet user-secrets`
  en desarrollo, vault en producción, mismo criterio que el resto de la plataforma. Sin
  probar contra las APIs reales todavía (sin credenciales de prueba disponibles en esta
  sesión).

### SQL / migraciones: RESUELTO (27 jul 2026)
Se decidió EF Core Migrations (consistente con el resto de la plataforma), no los
scripts SQL sueltos del original -- ver "Fase 4" más arriba para el detalle completo
(dos proyectos de herramientas, `Modulo.Rendiciones.Migrations.Postgres`/`SqlServer`,
ya generados y aplicados contra bases reales).

### Vocabulario a revisar antes de UI visible: RESUELTO (29 jul 2026)
Revisados los 17 archivos `.cshtml` del plugin contra nombres de productos conocidos
de rendición de gastos (Concur, Expensify, Rindegastos, Zoho Expense, Rydoo, Certify,
Chrome River, Fyle, Nexonia, Spendesk, Pleo, Ramp, Divvy, Brex, Coupa, etc.) —
**ninguna coincidencia**. Vocabulario confirmado neutro (Gastos/Informes/Fondos por
Rendir/Aprobaciones/Colaborador/Aprobador, clases CSS con prefijo propio `rnd-`).
