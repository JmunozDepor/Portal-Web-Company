# Auditoría de Inventario — conteo físico móvil + portal

**Estado: scaffold + API corregida al patrón real del Host + migraciones generadas
(14 sep 2026).** Compila y los tests pasan en todo el conjunto (plugin, migraciones,
tests, y los agregados en `PortalSaas.Host`/`PortalSaas.Abstractions`). Se detectó y
corrigió un error de arquitectura real: la API para la PWA se había diseñado como
MVC Controllers dentro del plugin, pero el Host nunca activa el pipeline de MVC
(`AddControllers()`/`MapControllers()`) -- se corrigió al patrón real que ya usa
`Modulo.Wms` (Minimal API mapeada en el Host + contrato en `Abstractions`). Detalle
completo en `Portal SaaS - Plugins/Modulo.AuditoriaInventario/PENDIENTE.md`.

Lógica de negocio real (motor de diferencias, importador de congelados, mapeo SAP)
todavía no implementada -- ver PENDIENTE.md para el orden sugerido de las próximas
tareas.

**Goal:** Reemplazar el desarrollo abandonado en MAUI/Android (nunca se probó por
problemas de instalación en el equipo Datalogic Skorpio X5) por un sistema de
captura de inventario por código de barra (PWA, multi-dispositivo) más un módulo de
control/análisis dentro del Portal SaaS existente -- carga de maestro, sucursales,
congelados, diferencias capturado-vs-congelado (cantidad y monto) y ajustes hacia
SAP con aprobación manual obligatoria.

**Architecture:** Dos repos completamente separados (equipos distintos, pedido
explícito del dueño del proyecto), unidos por un único contrato versionado:

```
PWA de Captura (repo propio, TS+React, offline-first)
        │  único contrato: Api/V1 (HTTP/JSON)
        ▼
Modulo.AuditoriaInventario (plugin del Portal SaaS, repo propio)
  - Pages/          → control desde el portal (Sesiones, Congelados, Diferencias, Ajustes)
  - Api/V1/          → todo lo que necesita la PWA (auth, sync de maestro, sesiones, capturas)
  - Data/            → base propia del módulo, motor dual (Postgres/SQL Server)
        │  solo cuando un ajuste queda APPROVED
        ▼
sap_adjustment_queue_items (tabla de interfaz -- quién la consume queda fuera de este proyecto)
```

**Tech Stack:** Plugin: ASP.NET Core Razor Pages + MVC API Controllers (.NET 8), EF
Core motor dual (Postgres/SQL Server) vía `IExternalDatabaseConnectionService`,
xUnit + EF Core InMemory. PWA de captura (repo separado, sin empezar todavía):
TypeScript + React + Vite, Dexie (IndexedDB), Workbox (service worker/offline) --
elegido sobre Blazor WebAssembly por arranque en frío más rápido en hardware
rugerizado de gama media (Skorpio X5).

## Por qué PWA y no una app nativa (contexto de la decisión)

El intento anterior en .NET MAUI/Android nunca llegó a probarse por problemas de
instalación en el Skorpio X5 (probablemente bloqueo de sideloading/MDM propio de
equipos rugerizados). El propio código de ese intento (`ScannerService.cs`,
`MainActivity.cs`) ya trataba el modo teclado-wedge (HID) como la estrategia
universal y principal de lectura de código de barra -- ese modo es agnóstico de
plataforma (el navegador recibe las teclas igual que cualquier `input` con foco),
así que migrar a PWA no pierde compatibilidad con Zebra/Symbol ni Datalogic Skorpio
X5, y de paso elimina de raíz el problema de instalación que bloqueó el desarrollo
anterior (una PWA no se instala, es una URL, con opción de agregarla a pantalla de
inicio).

## Decisiones de alcance confirmadas por el dueño del proyecto

- **Separación total de repos/equipos**: la PWA de captura y
  `Modulo.AuditoriaInventario` nunca comparten código ni pipeline -- el único punto
  de contacto es `Api/V1`.
- **Módulo autocontenido**: no reutiliza `Modulo.Inventario.ProductMaster` ni
  `Modulo.ImportacionGenerica` (evaluado y descartado explícitamente) -- maestro de
  producto, importador de congelados y cola de ajustes son propios del módulo
  nuevo.
- **Multi-compañía**: resuelto por la plataforma (`company_id` + motor dual), no se
  modela una entidad `Empresa` propia del módulo.
- **Congelado**: se extrae del punto de venta y se carga manualmente vía Excel
  contra un Nro. de Inventario -- el parser es responsabilidad de este módulo (no
  se reutiliza `Modulo.ImportacionGenerica`).
- **Ajustes hacia SAP**: vía base propia del plugin (tabla de interfaz
  `sap_adjustment_queue_items`), nunca integración directa a SAP desde este
  módulo. **Aprobación manual obligatoria** antes de encolar -- nunca envío
  automático. Quién consume la cola queda fuera del alcance de este proyecto.
- **Toggle de validación contra maestro**: por sesión de conteo
  (`InventorySession.ValidateAgainstMaster`), el capturador puede desactivarlo.
- **Sector de captura**: catálogo (`InventorySector`: Sala de Venta, Bodega, etc.),
  no texto libre -- para poder agrupar el reporte de diferencias de forma
  confiable.
- **Trazabilidad de usuario**: cada captura individual registra quién escaneó
  (`InventoryCapture.CapturedByUserId`), no solo el responsable de la sesión.

## Dónde está el código

- Plugin: `Portal SaaS - Plugins/Modulo.AuditoriaInventario/` (repo externo al
  Core, mismo patrón que `Modulo.Wms`/`Modulo.Rendiciones` -- ver
  `docs/09-GUIA-DESARROLLO-PLUGINS.md` de `Portal SaaS - Core`).
- Contrato de la API: `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IAuditoriaInventarioApiService.cs`
  + `.../Modelos/AuditoriaInventarioApiModels.cs`.
- Endpoints HTTP reales (Minimal API, en el Host, no en el plugin):
  `Portal SaaS - Core/src/PortalSaas.Host/AuditoriaInventario/AuditoriaInventarioInboundEndpoints.cs`,
  mapeados desde `Program.cs` junto a `MapWmsInboundEndpoints()`.
- Estado y próximas tareas en detalle: `Portal SaaS - Plugins/Modulo.AuditoriaInventario/PENDIENTE.md`.
- PWA de captura: repo todavía no creado (pendiente, equipo mobile).

## Próximos pasos (ver PENDIENTE.md del plugin para el detalle completo)

1. Verificación end-to-end real de la API (Host corriendo + request HTTP real) --
   lo hecho hasta ahora es compilación correcta, no una prueba en caliente.
2. Reemplazar el hash de contraseña placeholder (`AuditoriaInventarioApiService.HashPassword`)
   antes de cualquier ambiente real.
3. Implementar el motor de diferencias (`InventoryDifference`), el importador de
   congelados (Excel) y el mapeo de códigos SAP para `SapAdjustmentQueueItem`.
4. Aplicar las migraciones EF Core ya generadas contra un entorno Postgres/SQL
   Server real (`dotnet ef database update`) y confirmar el flujo de publicación
   hasta `artifacts/plugins/` del Host.
5. Arrancar el repo de la PWA de captura (TS + React + Vite + Dexie + Workbox),
   consumiendo la API tal como está definida hoy
   (`POST /api/auditoria-inventario/v1/auth/login` con `CompanyCode`+`Username`+`Password`,
   resto de las rutas con `Authorization: Bearer {token}`).
