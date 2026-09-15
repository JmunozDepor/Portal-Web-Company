# PWA de Captura de Inventario — Diseño

**Estado:** diseño aprobado, listo para plan de implementación.

**Contexto:** `Modulo.AuditoriaInventario` (plugin del Portal SaaS) ya expone el
contrato completo que esta PWA va a consumir — ver
`docs/superpowers/plans/2026-09-14-auditoria-inventario-conteo-fisico.md` para
el porqué de la decisión PWA (vs. el intento MAUI/Android abandonado por
problemas de instalación en Datalogic Skorpio X5) y el contrato ya
implementado:

- `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IAuditoriaInventarioApiService.cs`
- `Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/AuditoriaInventarioApiModels.cs`
- `Portal SaaS - Core/src/PortalSaas.Host/AuditoriaInventario/AuditoriaInventarioInboundEndpoints.cs`

Este documento diseña dos piezas nuevas:

1. **La PWA de captura** (repo nuevo, equipo mobile) — consume la API HTTP ya
   expuesta por el Host.
2. **Exportación manual + importación admin** — fallback para equipos que
   nunca logran conectividad; toca tanto la PWA (exportar) como el plugin
   `Modulo.AuditoriaInventario` (una página nueva para importar).

## Decisiones de alcance (confirmadas)

- Ubicación: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/` — carpeta
  hermana al plugin, dentro del monorepo actual. Repo Git propio, sin
  dependencia de código con el plugin (solo el contrato HTTP).
- Despliegue: fuera de alcance por ahora. Esta iteración entrega la app
  corriendo en dev (`npm run dev` / build local) contra el Host local. IIS,
  dominio, HTTPS real y la instalación en los Skorpio X5 quedan para cuando el
  equipo mobile tome el proyecto.
- Alcance funcional: flujo completo offline-first (login, sync de maestro,
  sesiones, captura con cola offline, sync en batch) — no una versión
  reducida online-only.
- Identificación: login manual en cada sesión de uso (CompanyCode + Username +
  Password), sin configuración fija de compañía por dispositivo.

## Stack

Vite + React + TypeScript, `vite-plugin-pwa` (Workbox) para el service
worker/shell offline, Dexie para IndexedDB, Vitest para tests. Sin librería
de estado (Zustand/Redux) ni UI kit — pantallas simples, prioridad a bundle
chico y arranque en frío rápido en hardware rugerizado de gama media (mismo
motivo por el que se descartó Blazor WebAssembly en el plan original).

## Estructura del proyecto

```
Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/
├── package.json
├── vite.config.ts          (vite-plugin-pwa configurado acá)
├── tsconfig.json
├── src/
│   ├── api/                 cliente HTTP tipado (1 función por endpoint) + DTOs
│   ├── db/                  esquema Dexie + accessors tipados por tabla
│   ├── sync/                motor de sync (maestro, sesiones, capturas) + export/import
│   ├── features/
│   │   ├── auth/            pantalla Login
│   │   ├── sesiones/        lista + alta + cierre de sesión
│   │   └── captura/         pantalla de captura por sector
│   ├── App.tsx, main.tsx, router
└── tests/                    Vitest
```

## Contrato consumido (referencia, ya implementado — no se toca)

| Método | Ruta | Body/Query | Respuesta |
|---|---|---|---|
| POST | `/api/auditoria-inventario/v1/auth/login` | `{CompanyCode, Username, Password}` | `{Token, ExpiresAt, DisplayName}` o 401 |
| GET | `/api/auditoria-inventario/v1/maestro/productos?afterId=N` | Bearer | `{Items:[{Id,Barcode,ProductCode,Description,Brand,Line}], HasMore}` |
| GET | `/api/auditoria-inventario/v1/maestro/sucursales` | Bearer | `[{Id,BranchCode,Name}]` |
| GET | `/api/auditoria-inventario/v1/maestro/sectores?branchId=N` | Bearer | `[{Id,Name}]` |
| POST | `/api/auditoria-inventario/v1/sesiones` | Bearer, `{Id(guid),BranchId,InventoryNumber,StartedAt,Status,ValidateAgainstMaster}` | 200 vacío |
| POST | `/api/auditoria-inventario/v1/capturas/batch` | Bearer, `[{Id(guid),SessionId,SectorId,Barcode,ProductCode?,Quantity,InMaster?,CapturedAt}]` | `{processed:N}` |

`Status` de sesión es `"ACTIVE"` (default) o `"CLOSED"`. Todo `Id` de sesión y
captura se genera en el cliente (GUID) — el servidor es idempotente por ese
Id, tanto en el upload automático como en la importación manual.

## Modelo de datos local (Dexie)

- `authConfig` (fila única): `{ token, expiresAt, displayName, companyCode }`
- `productos`: `{ id, barcode (índice), productCode, description, brand, line }`
- `sucursales`: `{ id, branchCode, name }`
- `sectores`: `{ id, branchId, name }` — el DTO del servidor no trae
  `branchId`; el cliente lo agrega al cachear (se pide un branchId a la vez).
- `sesiones`: `{ id(guid), branchId, inventoryNumber, startedAt, status,
  validateAgainstMaster, syncStatus: 'pending'|'synced'|'error', lastError? }`
- `capturas`: `{ id(guid), sessionId, sectorId, barcode, productCode?,
  quantity, inMaster?, capturedAt, syncStatus: 'pending'|'synced'|'error',
  lastError? }`

Sin tabla `outbox` genérica: `syncStatus` vive directo en `sesiones`/
`capturas`. Toda alta es local-primero; la sincronización es un barrido
posterior sobre las filas `pending`.

## Pantallas y flujo

1. **Login** — CompanyCode + Username + Password → `POST auth/login` →
   guarda `{token, expiresAt, displayName, companyCode}` en `authConfig`.
   401 → mensaje de error, sin guardar nada.
2. **Sync inicial de maestro** — tras login, si `productos`/`sucursales`
   están vacíos, pagina `GET maestro/productos` por `afterId` (empezando en
   0) hasta `HasMore=false`, y trae `sucursales` completo. Progreso visible
   ("Sincronizando productos... N traídos").
3. **Sesiones** — lista local (`sesiones` de Dexie). Acción "Nueva sesión":
   form con Sucursal (de `sucursales` cacheadas), Nro. de Inventario (texto
   libre), toggle `ValidateAgainstMaster` → crea fila local `ACTIVE`/
   `pending` con `id` GUID nuevo, `startedAt=now`.
4. **Captura** (dentro de una sesión abierta) — elegir Sector: si no está
   cacheado para ese `branchId`, pedir `GET maestro/sectores?branchId=X` y
   guardar en `sectores` local. Input de barcode siempre enfocado
   (auto-refocus tras cada commit y en `visibilitychange`); el commit del
   scanner se detecta por `keydown Enter` (perfil estándar keyboard-wedge
   con sufijo CR). Si `ValidateAgainstMaster`, se busca el barcode en
   `productos` local (lookup por índice, sin red) y se completa
   `productCode`/`inMaster` automáticamente; si no está y la validación
   está activa, se muestra advertencia pero se permite capturar igual
   (`inMaster=false`). Cantidad editable, default 1. Lista de capturas de la
   sesión+sector visible debajo, editable/borrable mientras `syncStatus`
   sea `pending` (una vez `synced` no se edita localmente — habría que
   corregir del lado del portal).
5. **Cerrar sesión** — pasa `status='CLOSED'`, vuelve a `syncStatus='pending'`
   para reenviar el upsert con el nuevo estado.
6. **Indicador de sync** (visible en todas las pantallas post-login) —
   contador de `sesiones`+`capturas` pendientes/en error, punto online/
   offline (`navigator.onLine` + eventos `online`/`offline`), botón
   "Sincronizar ahora".
7. **Exportar pendientes** (ver sección siguiente) y **Logout** (borra
   `authConfig`; mantiene el resto de los datos locales — maestro y
   pendientes — para no perder capturas sin sincronizar).

## Motor de sync

- **Disparadores:** evento `online`, intervalo cada 30s mientras
  `navigator.onLine`, botón manual.
- **Orden:** `sesiones` con `syncStatus='pending'` primero (una llamada
  `POST /sesiones` por fila), luego `capturas` con `syncStatus='pending'`
  agrupadas en lotes de 100 vía `POST /capturas/batch`.
- **Resultado por fila/lote:**
  - Éxito (2xx) → `syncStatus='synced'`.
  - Error de red/timeout → se mantiene `pending`, se reintenta en el
    siguiente tick sin intervención.
  - Error 4xx del servidor → `syncStatus='error'` + `lastError` con el
    mensaje; sin reintento automático (evita loop infinito contra datos
    inválidos). Visible en la UI para que el usuario edite o borre esa fila.
  - `401` en cualquier llamada → se asume token vencido: se detiene el loop
    de sync, se limpia `authConfig.token` y se redirige a Login. Los datos
    `pending`/`error` NO se borran — quedan esperando el próximo login para
    reintentar.

## Exportación manual + importación admin (fallback sin conectividad)

**En la PWA — botón "Exportar pendientes":** junta todas las filas
`sesiones`+`capturas` con `syncStatus != 'synced'` y genera un único JSON:

```json
{
  "exportedAt": "2026-09-15T14:30:00Z",
  "companyCode": "DEPOR",
  "deviceLabel": "texto libre, opcional, tipeado por el capturador",
  "sesiones": [ { "Id": "...", "BranchId": 1, "InventoryNumber": "...", "StartedAt": "...", "Status": "ACTIVE", "ValidateAgainstMaster": true } ],
  "capturas": [ { "Id": "...", "SessionId": "...", "SectorId": 1, "Barcode": "...", "ProductCode": null, "Quantity": 1, "InMaster": true, "CapturedAt": "..." } ]
}
```

`sesiones`/`capturas` usan exactamente el shape de `CaptureSesionUpsert`/
`CaptureItemDto` — el mismo JSON que ya viajaría por la API. Se descarga vía
`Blob` + `<a download>` con nombre `conteo_{companyCode}_{timestamp}.json`.
Exportar **no** marca nada como `synced` — es una copia, no un ack; si el
equipo recupera señal después, el sync automático manda lo mismo y el
servidor lo deduplica por `Id` (GUID).

**En el portal — página nueva `Pages/ImportarConteos/Index.cshtml`** dentro
de `Modulo.AuditoriaInventario`:

- Sube uno o varios archivos `.json` a la vez (input `multiple`).
- Por archivo: deserializa, valida estructura mínima (presencia de
  `sesiones`/`capturas`, tipos correctos) y por cada sesión+sus capturas
  llama **la misma lógica de servicio** que usan los endpoints HTTP
  (`IAuditoriaInventarioApiService.UpsertSesionAsync` /
  `.UploadCapturasAsync`, inyectado en la página como cualquier otro
  servicio del plugin) — pasando el `CompanyId` de `_currentCompany` (la
  empresa actual del admin logueado en el portal), **nunca** un CompanyId
  leído del archivo, para que no se puedan inyectar datos de otra compañía
  manipulando el JSON. `deviceLabel`/`companyCode` del archivo son solo
  informativos para el reporte, no se usan para autorizar nada.
- Reporta por archivo: sesiones importadas, capturas procesadas, errores
  fila a fila (una captura o sesión inválida no aborta el resto del
  archivo).
- Idempotente por el mismo motivo que el sync automático: reimportar un
  archivo ya sincronizado actualiza en vez de duplicar.
- Entrada nueva en `AuditoriaInventarioModule.GetMenu()` ("Importar
  Conteos", icono a elegir, junto a Sesiones/Diferencias/Ajustes).

Esto no agrega tablas nuevas al plugin ni cambia el contrato HTTP — es
puramente cliente (export) + una página Razor más que reusa lógica existente
del lado del servidor.

## Manejo de errores (resumen transversal)

- Fallas de red en cualquier sync → no se pierde nada, la fila sigue
  `pending` y se reintenta sola.
- `401` → logout forzado preservando datos locales sin sincronizar.
- Error de validación del servidor (4xx) en una fila → `error` visible,
  corregible/borrable a mano, no bloquea el resto de la cola.
- Storage/quota de IndexedDB: fuera de alcance explícito — el volumen
  esperado (unos pocos miles de productos, capturas de una sesión de
  conteo) está lejos de los límites típicos de IndexedDB en navegadores
  modernos.

## Testing

Vitest sobre lógica pura, sin UI:

- Motor de sync: batching, marcado `pending→synced`, manejo de error 4xx
  vs. error de red, no pérdida de datos ante fallas simuladas (mock
  `fetch`).
- Paginación de maestro (`afterId`/`HasMore`) contra un mock de varias
  páginas.
- Parsing del commit de scanner (detección de `Enter`, no confundir con
  tipeo manual accidental).
- Generación y (re)lectura del JSON de exportación (round-trip).

Sin E2E ni tests de integración contra un Host real en esta iteración — la
validación es correr la app en dev contra el Host local, acorde a lo
decidido para este alcance.

Del lado del plugin (`Modulo.AuditoriaInventario`, xUnit, ya existente):
tests de la nueva página de importación cubriendo el caso feliz, un archivo
con estructura inválida, una fila con datos inválidos dentro de un archivo
válido, y el caso de reimportar un archivo ya sincronizado (no duplica).

## Fuera de alcance (explícito)

- Despliegue real (IIS, dominio, HTTPS, instalación en los Skorpio X5).
- Resolución de conflictos entre ediciones concurrentes del mismo registro
  (no existe ese caso de uso: cada captura es de un solo capturador, un
  dispositivo).
- El flujo de "proponer ajuste desde una diferencia" en el portal (gap ya
  documentado en `PENDIENTE.md` del plugin, independiente de este diseño).
