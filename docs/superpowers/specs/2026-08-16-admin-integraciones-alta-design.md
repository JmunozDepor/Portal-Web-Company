# Diseño: Alta/edición de IntegrationDefinition desde Admin

Fecha: 2026-08-16
Estado: Aprobado para implementación

## Contexto

El motor genérico de integración (`PortalSaas.Integrations`) ya funciona de punta a
punta: `IntegrationSyncHostedService` ejecuta filas `IntegrationDefinition` activas,
`Admin/Integraciones/Index` las lista y permite "Ejecutar ahora", `Bitacora.cshtml`
muestra el historial de corridas. El piloto WMS↔SAP (Modulo.Wms) ya tiene los 4
readers/writers y el conector `WmsCloudConnector` construidos y probados con fakes.

**El hueco real**: no existe ninguna pantalla para crear o editar filas
`IntegrationDefinition` — solo se pueden listar y ejecutar las que ya existen en la
base. Para poder operar el motor de integración (incluido probar la conexión real
Oracle WMS Cloud del piloto WMS) hace falta poder darlas de alta desde la UI, sin
tocar la base a mano.

## Objetivo

Página nueva de alta (`Admin/Integraciones/Nuevo`) y edición (reutilizando la misma
página con un `id` en la ruta) para `IntegrationDefinition`, con manejo correcto del
secreto cifrado (`ConectorConfigCifrado`).

## Diseño

### 1. Modelo — sin cambios

`IntegrationDefinition` (`PortalSaas.Data.Entities.Integraciones`) ya tiene todos los
campos necesarios: `Nombre`, `CompanyId`, `ModuloOrigen`, `EntidadNegocio`,
`ConectorTipo` (`Sap`/`Rest`/`Archivo`/`WmsCloud`), `ConectorConfigCifrado`,
`Direccion` (`Subida`/`Bajada`/`Ambas`), `Activo`, `ProgramacionCron`, `NextRunAt`. No
se agrega ni modifica ninguna columna.

### 2. Página `Admin/Integraciones/Nuevo`

Ruta `/admin/integraciones/nuevo/{id:guid?}` (el mismo `.cshtml`/`.cshtml.cs` sirve
alta y edición, `id` ausente = alta). Campos:

- `Nombre` (texto, requerido).
- `CompanyId` — selector de compañía (mismo patrón de dropdown ya usado en otras
  páginas de `Admin`, poblado desde `PortalSaasDbContext.Companies`).
- `ModuloOrigen` (texto, ej. `"Wms"`).
- `EntidadNegocio` (texto, ej. `"SapWms.Item.Subida"` — valor libre, el motor lo usa
  para hacer match contra `IIntegrationEntityReader`/`Writer.EntidadNegocio`, no hay
  catálogo cerrado de valores).
- `ConectorTipo` — `<select>` con las 4 opciones del enum.
- `Direccion` — `<select>` con las 3 opciones del enum.
- `Activo` (checkbox).
- `ProgramacionCron` (texto opcional — no usado por el motor actual basado en
  `NextRunAt`, se mantiene solo como campo informativo ya existente en el modelo).

**Bloque de configuración del conector**, condicional por `ConectorTipo` (mostrar/
ocultar con JS simple al cambiar el `<select>`, mismo patrón que el toggle
kilometraje/monto normal en `Modulo.Rendiciones/Pages/Gastos/Detalle.cshtml`):

- **`Sap`**: un campo `TipoEntidad` (texto libre — valores usados hoy:
  `Items`/`Stores`/`Picking`/`InboundTraslado`, ver `SapDocumentConnector.PullAsync`).
- **`WmsCloud`**: `ApiUrl`, `Usuario`, `Clave` (`<input type="password">`),
  `ClientEnvCode`, `ParentCompanyCode`.
- **`Rest`/`Archivo`**: sin conector implementado todavía en el motor — el bloque
  queda vacío/deshabilitado por ahora (fuera de alcance, no hay `IIntegrationConnector`
  registrado para esos tipos aún).

Al enviar el formulario:
- Arma el JSON correspondiente al `ConectorTipo` elegido (`{"TipoEntidad": "..."}`
  para `Sap`, o el record de `WmsCloudConnector.WmsCloudConfig` para `WmsCloud`) y lo
  cifra con `ISecretoCifradoService.Encrypt` antes de guardarlo en
  `ConectorConfigCifrado`. Nunca se persiste JSON sin cifrar.
- **Edición, campo `Clave` (write-only)**: al entrar a editar una fila `WmsCloud`
  existente, el campo `Clave` se muestra vacío (mismo criterio documentado en
  `ISecretoCifradoService`: "self-service... sin volver a mostrarlo"). Si al guardar
  el campo sigue vacío, se conserva la clave cifrada ya guardada (se descifra la fila
  actual, se toma su `Clave`, y se vuelve a cifrar con los demás campos actualizados);
  si el admin tipeó algo, se usa ese valor nuevo.
- Los demás campos del bloque de config (`ApiUrl`, `Usuario`, etc., y `TipoEntidad`
  para `Sap`) sí se muestran precargados al editar — no son secretos.

### 3. `Admin/Integraciones/Index.cshtml`

Se agrega un link "+ Nueva integración" hacia `Admin/Integraciones/Nuevo`, y cada fila
de la tabla existente gana un link "Editar" hacia `Admin/Integraciones/Nuevo/{id}` —
mismo patrón `admin-link-action` ya usado en otras páginas de administración del
proyecto (ver `Modulo.Rendiciones`).

### 4. Autorización

Misma política que el resto de `Admin/Integraciones/*`:
`[Authorize(AuthenticationSchemes = "PlatformAdmin")]`.

## Fuera de alcance

- Conectores `Rest`/`Archivo` (no implementados en el motor todavía).
- Programación real vía `ProgramacionCron` (el motor usa `NextRunAt` fijado por
  "Ejecutar ahora"; el campo cron existe en el modelo pero ningún proceso lo lee hoy —
  no se cambia ese comportamiento en esta entrega).
- Alta/edición de `IntegrationFieldMapping` (mapeo de campos por integración) — no
  existe hoy ninguna UI para eso tampoco, pero es una pieza separada, no bloquea poder
  crear la `IntegrationDefinition` en sí.
- Validación de que `EntidadNegocio` coincide con un reader/writer realmente
  registrado — el motor ya falla de forma clara en tiempo de ejecución si no hay
  match (bitácora), no se duplica esa validación en la UI.

## Testing

- Unit test del helper de armado/cifrado de config (si se extrae como servicio
  separado) o, si la lógica queda en el page model, un test de integración liviano
  contra `PortalSaasDbContext` InMemory verificando: alta cifra correctamente, edición
  con `Clave` vacía conserva la clave anterior, edición con `Clave` nueva la reemplaza.
- Verificación manual: crear las 8 filas para DEPORTEST (Items/Sucursales/Órdenes/
  Ingresos × Bajada/Subida) con credenciales reales de Oracle WMS Cloud, ejecutar cada
  una desde `Index`, confirmar en `Bitacora` que la corrida fue exitosa.
