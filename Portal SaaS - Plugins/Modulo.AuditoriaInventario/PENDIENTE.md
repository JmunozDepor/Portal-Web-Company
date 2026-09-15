# Pendiente — Modulo.AuditoriaInventario

Estado: **lógica de negocio real implementada (15 sep 2026)**. El plan
`docs/superpowers/plans/2026-09-14-auditoria-inventario-logica-negocio.md` (7
tareas) completó: hash de contraseña con sal para `CaptureUser`, motor de
diferencias (`DiferenciaEngine`), importador de Excel para congelados, mapeo SAP
+ gate de aprobación manual (`AjusteService`), y las 3 pantallas de administración
de catálogo (Sucursales/Sectores/Capturadores) que faltaban. Una revisión final de
todo el branch encontró y corrigió 2 fugas cross-tenant (login y aprobación de
ajustes sin filtro de compañía) más 3 gaps menores -- ver "Fuera de alcance" más
abajo para lo que quedó deliberadamente sin tocar. Compila y los 18 tests pasan en
todo el conjunto. Pusheado a `origin/main`.

## Corrección de arquitectura importante (14 sep 2026)

El scaffold original exponía `Api/V1` como MVC API Controllers dentro del plugin,
asumiendo que `PortalSaas.Host` descubría y enrutaba controllers de un plugin
cargado dinámicamente. **Se verificó contra el código real del Host y era
incorrecto**: `PluginManager.LoadModule` registra el assembly del plugin como
`AssemblyPart`/`CompiledRazorAssemblyPart` (Razor Pages sí se descubren), pero
`Program.cs` nunca llama `AddControllers()`/`MapControllers()` -- no hay pipeline de
MVC controllers activo. El patrón real que ya usa la plataforma para esto es
`Modulo.Wms` + `WmsInboundEndpoints.cs`: el **Host** mapea los endpoints con Minimal
API (`app.MapPost/MapGet`) y los resuelve contra una interfaz de
`PortalSaas.Abstractions`, cuya implementación real vive en el plugin y se
resuelve por DI en runtime (sin que el Host referencie el ensamblado del plugin en
tiempo de compilación).

Se corrigió siguiendo ese mismo patrón:
- `PortalSaas.Abstractions.Contratos.IAuditoriaInventarioApiService` (+ DTOs en
  `PortalSaas.Abstractions.Modelos.AuditoriaInventarioApiModels.cs`) -- el contrato.
- `Modulo.AuditoriaInventario.Servicios.AuditoriaInventarioApiService` -- la
  implementación real, registrada en `AuditoriaInventarioModule.RegisterServices`.
  Importante: este servicio **nunca usa** el `AuditoriaInventarioDbContext`
  registrado por `AddDbContext` (ese depende de `ICurrentCompanyAccessor`, que exige
  sesión de portal -- la PWA no tiene eso). En cambio arma un `DbContext` manual por
  `companyId` explícito vía `IExternalDatabaseConnectionService`, mismo patrón que
  `RendicionesReminderBackgroundService` (el otro caso ya existente en la
  plataforma de "código sin sesión HTTP de portal").
- `PortalSaas.Host/AuditoriaInventario/AuditoriaInventarioInboundEndpoints.cs` --
  mapea las rutas reales (`/api/auditoria-inventario/v1/...`), resuelve
  `Company.Code -> CompanyId` contra `PortalSaasDbContext` en el login (la PWA no
  conoce el `Guid` de la compañía, solo un código de negocio), y valida el token
  opaco a mano en el resto de las rutas.
- Registrado en `Program.cs` (`app.MapAuditoriaInventarioInboundEndpoints();`, junto
  a la línea equivalente de Wms) -- edición mínima y aditiva, sin tocar el resto del
  archivo (que tenía otros cambios en curso de otro trabajo, no relacionados).

La carpeta `Api/V1` original dentro del plugin (los MVC Controllers) se eliminó.

Plan y contexto completo de la decisión de arquitectura en
`docs/superpowers/plans/2026-09-14-auditoria-inventario-conteo-fisico.md` del
proyecto principal (`Proyecto Portal Web-Company`).

## Alcance confirmado por el dueño del proyecto

- Módulo **deliberadamente autocontenido**: no reutiliza
  `Modulo.Inventario.ProductMaster` ni `Modulo.ImportacionGenerica` -- maestro de
  producto, importador de congelados y cola de ajustes son propios de este módulo.
- `ModuleCode = "AuditoriaInventario"` -- confirmado, no cambiar.
- Ajustes hacia SAP: **base propia del plugin** (motor dual), nunca integración
  directa. Quién consume `sap_adjustment_queue_items` queda fuera del alcance de
  este proyecto.
- Aprobación manual **obligatoria** antes de encolar un ajuste hacia SAP -- nunca
  envío automático.
- Multi-compañía: resuelto por la plataforma (`company_id` + motor dual vía
  `IExternalDatabaseConnectionService`), no se modela una entidad `Empresa` propia.

## Hecho en este scaffold

- Estructura completa del plugin siguiendo `docs/09-GUIA-DESARROLLO-PLUGINS.md` del
  portal: `Modulo.AuditoriaInventario` (Razor Pages + API), proyectos de migración
  separados por motor (`.Migrations.Postgres` / `.Migrations.SqlServer`), proyecto
  de tests (xUnit + EF Core InMemory).
- `AuditoriaInventarioModule : IModuloPortal` con menú (`Sesiones`, `Congelados`,
  `Diferencias`, `Ajustes`) y `RegisterServices` resolviendo el `DbContext` vía
  `IExternalDatabaseConnectionService` (motor dual, `CompanyId` obligatorio).
- Modelo de datos completo (`Data/AuditoriaInventarioDbContext.cs`): `Product`,
  `Branch`, `InventorySector`, `CaptureUser`, `InventorySession`,
  `InventoryCapture`, `FrozenInventorySnapshot`, `FrozenInventoryLine`,
  `InventoryDifference`, `InventoryAdjustment`, `SapAdjustmentQueueItem`,
  `CaptureAuthToken`. `InventorySession`/`InventoryCapture` usan `Guid` generado en
  el cliente (offline-safe); el resto usa `bigint identity`.
- Páginas de portal: `Sesiones` (listado real, filtro por estado),
  `Congelados` (listado real de lo cargado, carga de Excel pendiente),
  `Diferencias` (listado real de `InventoryDifference`, motor de cálculo
  pendiente), `Ajustes` (listado real + aprobar/rechazar funcionando end-to-end
  contra la base).
- API para la PWA de captura (repo separado), corregida al patrón real de la
  plataforma (Minimal API en el Host, ver sección de arriba): login (token
  opaco contra `Company.Code`), sync paginado de productos/sucursales/sectores
  (keyset pagination por `Id`), upsert de sesión idempotente por `Guid`, batch de
  capturas idempotente por `Guid`.
- **Migraciones EF Core reales generadas** (`InitialCreate`) para ambos motores,
  vía `DesignTimeDbContextFactory` en cada proyecto de migración (mismo patrón que
  Rendiciones) -- compilan, no aplicadas todavía contra una base real (sin entorno
  Postgres/SQL Server levantado en esta sesión).
- Tests: 3 smoke tests del modelo (`Id` generado en cliente, unicidad de barcode
  por compañía, distinción `InMaster` null vs. false).

## Pendiente — próximos pasos, en orden sugerido

1. Verificación **end-to-end real** (Host corriendo, request real desde un
   cliente HTTP) -- sigue sin hacerse; lo hecho hasta ahora es compilación +
   tests unitarios/InMemory, no una prueba en caliente contra Postgres/SQL Server
   reales ni contra la PWA (que tampoco existe todavía como repo).
2. ~~Seguridad de la API~~ -- **hecho (14 sep 2026)**: `PasswordHasher` (PBKDF2 +
   sal, mismo algoritmo que `PortalSaas.Core.Seguridad.PasswordHasher`) reemplazó
   el placeholder SHA-256. El token opaco (`CaptureAuthToken`) sigue siendo
   intencionalmente simple (no JWT) -- eso no cambió. `ResolveTokenAsync` sigue
   recorriendo todas las compañías activas (sin cambios, aceptable a la escala
   actual).
3. ~~Motor de diferencias~~ -- **hecho (14 sep 2026)**: `DiferenciaEngine`
   (`Servicios/DiferenciaEngine.cs`) cruza `InventoryCapture` agregado por
   sesión+sector+barcode contra `FrozenInventoryLine` del snapshot
   correspondiente al cerrar una sesión (`UpsertSesionAsync`), materializa
   `InventoryDifference`.
4. ~~Importador de congelados~~ -- **hecho (14 sep 2026)**: `CongeladoExcelParser`
   (ExcelDataReader, mapeo por columna A/B/C) + formulario de carga en
   `Congelados/Index`, con validación por fila y acumulación de errores sin
   abortar el archivo completo.
5. ~~Mapeo SAP~~ -- **hecho (14 sep 2026)**: `Branch.SapCompanyCode`/
   `SapWarehouseCode` y `Product.SapMaterialCode` agregados; `AjusteService`
   resuelve el mapeo y genera la fila en `SapAdjustmentQueueItem` al aprobar
   (único punto de escritura en esa cola, con scope de compañía verificado).
   **Pero ver el punto 9 más abajo**: hoy no hay forma de llegar a un ajuste
   `PROPOSED` desde la UI, así que este flujo sigue siendo inalcanzable en la
   práctica.
6. ~~Migraciones reales~~ -- **hecho contra producción real (15 sep 2026)**: las 3
   migraciones (`InitialCreate`, `AddCaptureUserPasswordSalt`,
   `AddSapMappingColumns`) se aplicaron con `dotnet ef database update` contra una
   base nueva, propia del módulo, creada en el Postgres real de producción
   (`Host=172.16.122.171;Port=5432;Database=ps_comdepor_ai`, mismo servidor que
   `ps_comdepor`/`portalsaas_saas_prod`, usuario técnico `admin_saas`) -- **13
   tablas confirmadas** (12 + `__EFMigrationsHistory`). Sigue pendiente:
   - Confirmar el flujo de publicación hasta `artifacts/plugins/` del Host (ver
     `docs/09-GUIA-DESARROLLO-PLUGINS.md` §7 del portal) -- la base ya existe,
     pero el plugin en sí todavía no se copió/cargó contra un Host real.
   - **Registrar la conexión en la plataforma** para que el módulo resuelva contra
     `ps_comdepor_ai` en runtime -- `CompanyId` real de Comercial Depor
     confirmado: `46326209-2ccb-4423-9bbb-b3c6a9fc4569` (tabla `companies` de
     `ps_comdepor`, no de `portalsaas_saas_prod` -- la instancia on-premise tiene
     su propio catálogo de compañías/organizaciones, autocontenido). Falta crear
     la fila en `company_external_connections` (Nombre sugerido:
     "AuditoriaInventario", Tipo `db_postgres`, Host `172.16.122.171`, Port
     `5432`, DatabaseName `ps_comdepor_ai`, TechnicalUsername `admin_saas`) +
     el binding en `company_module_connections` (ModuleCode `AuditoriaInventario`,
     Purpose `Default`) -- vía `/Admin/Organizations/Companies/
     46326209-2ccb-4423-9bbb-b3c6a9fc4569/ExternalConnections/Create` del Host
     que sirve esa instancia on-premise (login `PlatformAdmin`), NO escribiendo
     `company_external_connections`/`company_module_connections` a mano: esa
     página cifra `TechnicalSecretKey` con la `MasterSecretKey` real, evitando
     manejarla fuera de la app.
   - **Nota de arquitectura descubierta en este despliegue**: `module_external_
     connections` (mencionada en sesiones anteriores) es una tabla LEGACY, sin
     lector de runtime desde hace tiempo -- el modelo real es
     `company_external_connections` + `company_module_connections`, resuelto por
     `ExternalDatabaseConnectionService`. No usar la tabla legacy para nada nuevo.
7. ~~Catálogo de sectores/sucursales~~ -- **hecho (14 sep 2026)**: pantallas de
   administración para `Branch` (Sucursales), `InventorySector` (Sectores) y
   `CaptureUser` (Capturadores), con drawer + alta/edición/baja, todas con scope
   de compañía verificado (incluye 2 fixes de tenant-isolation encontrados en
   revisión: unicidad de código/usuario al editar, y validación de que el
   `BranchId` elegido pertenezca a la compañía actual).
8. Sin probar de punta a punta: ningún flujo real con el Host corriendo, ninguna
   llamada real desde una PWA (mismo punto que el 1).
9. **Nuevo -- falta el flujo "proponer ajuste desde una diferencia"**: nada en el
   portal crea hoy un `InventoryAdjustment` en estado `PROPOSED` a partir de una
   `InventoryDifference` -- sin esta pantalla, la cola de aprobación/mapeo SAP del
   punto 5 es funcionalmente inalcanzable desde la UI. Encontrado en la revisión
   final del plan de la Tarea 4-7 (15 sep 2026).

## Fuera de alcance — revisión final del plan (14 sep 2026)

La revisión final de todo el branch del plan
`docs/superpowers/plans/2026-09-14-auditoria-inventario-logica-negocio.md` (7
tareas) encontró dos gaps que se dejan deliberadamente sin tocar en esta ronda de
fixes -- necesitan su propia tarea futura:

1. **No existe el flujo "proponer ajuste desde una diferencia"**: nada en el portal
   crea hoy un `InventoryAdjustment` en estado `PROPOSED` -- la cola de
   aprobación/mapeo SAP de `Ajustes/Index` (Tarea 4 del plan) es funcionalmente
   inalcanzable desde la UI hasta que exista esa pantalla.
2. Dos consultas sin scope por `CompanyId`, **preexistentes** (no introducidas por
   este plan, ninguna tarea las tocó) y marcadas como deuda técnica conocida, fuera
   de alcance de esta ronda de fixes:
   `AuditoriaInventarioApiService.UploadCapturasAsync` escribe `SessionId`/
   `SectorId` provistos por el cliente sin validar que le pertenezcan a la
   compañía, y el camino CREATE de `UpsertSesionAsync` (el de UPDATE sí valida)
   acepta `request.BranchId` sin validarlo contra la compañía del caller.
