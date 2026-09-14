# Pendiente — Modulo.AuditoriaInventario

Estado: **scaffold + API corregida + migraciones generadas (14 sep 2026)**. Compila
y los tests pasan en todo el conjunto (`Modulo.AuditoriaInventario`, los dos
proyectos de migraciones con migración `InitialCreate` real generada y compilando,
el proyecto de tests, y `PortalSaas.Host`/`PortalSaas.Abstractions` con los agregados
de este módulo). Lógica de negocio real (motor de diferencias, importador de Excel,
mapeo SAP) todavía no implementada.

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

1. ~~Confirmar que el Host descubre controllers MVC de un plugin~~ -- **resuelto
   (14 sep 2026)**: no los descubre, se corrigió al patrón Minimal API en el Host
   (ver sección de arriba). Sigue pendiente la verificación **end-to-end real**
   (Host corriendo, request real desde un cliente HTTP) -- lo de acá es
   compilación + lectura de código, no una prueba en caliente.
2. **Seguridad de la API**: `AuditoriaInventarioApiService.HashPassword` usa
   SHA-256 simple como placeholder -- reemplazar por un hasher con sal antes de
   cualquier ambiente real. El token opaco (`CaptureAuthToken`) es intencionalmente
   simple (no JWT) -- ver el comentario en `CaptureAuthToken.cs`. `ResolveTokenAsync`
   hoy recorre todas las compañías activas del módulo hasta encontrar el token
   (aceptable a la escala de "cantidad de compañías", revisar si no escala).
3. **Motor de diferencias** (`InventoryDifference`): falta el servicio que, al
   cerrar una sesión, cruza `InventoryCapture` agregado por sesión+sector+barcode
   contra `FrozenInventoryLine` del snapshot correspondiente y materializa el
   resultado (cantidad y monto, usando `FrozenInventoryLine.UnitCost`).
4. **Importador de congelados**: parseo de Excel (extraído del punto de venta)
   contra un `InventoryNumber`, con mapeo de columnas y validación -- hoy
   `Congelados/Index` solo lista lo ya cargado.
5. **Mapeo SAP**: agregar `CodigoSap`/campos equivalentes a `Branch`/`Product` (o
   una tabla de mapeo aparte) para poder completar `SapCompanyCode`/
   `SapWarehouseCode`/`SapMaterialCode` al aprobar un ajuste -- hoy
   `Ajustes/Index.OnPostAprobarAsync` solo cambia el estado, no genera la fila en
   `SapAdjustmentQueueItem` todavía.
6. ~~Migraciones reales~~ -- **hecho (14 sep 2026)**: `InitialCreate` generada y
   compilando en ambos motores. Falta aplicarlas contra un entorno de prueba real
   (`dotnet ef database update`, sin Postgres/SQL Server levantado en esta sesión)
   y confirmar el flujo de publicación hasta `artifacts/plugins/` del Host (ver
   `docs/09-GUIA-DESARROLLO-PLUGINS.md` §7 del portal).
7. **Catálogo de sectores/sucursales**: hoy no hay pantalla de administración para
   cargar `Branch`/`InventorySector`/`CaptureUser` -- solo el modelo y los
   endpoints de lectura para la PWA.
8. Sin probar de punta a punta: ningún flujo real con el Host corriendo, ninguna
   llamada real desde una PWA.
