# Diseño — Página "Configuración del Servicio" (Modulo.Wms)

Fecha: 2026-08-14
Estado: aprobado, pendiente de implementación

## Contexto

`ARQUITECTURA.md` documenta que, tras completar "Mapeo de Campos", las
páginas Razor pendientes del plugin son "Configuración del Servicio" y
"Estado del Servicio" (hoy solo existen como entradas de menú en
`ModuloWms.GetMenu()`, sin contenido detrás). Este spec cubre exclusivamente
"Configuración del Servicio" — la página de "Estado del Servicio" (heartbeat)
tiene su propio ciclo de diseño aparte.

Tiene un precedente real en producción: `WmsPortal.Web` →
`Admin/ServiceConfig` (tabla HANA `INT_SERVICE_CONFIG`), que en el original
combina configuración + una tabla de heartbeat en la misma página. Acá se
mantienen separadas (decisión de este spec, ver abajo).

## Objetivo

CRUD sobre `wms_oracle_service_configs`, portando la whitelist fija de
`WMS_Suite.ServiceConfigKeys.Labels` (~30 `ConfigKey` con su label legible y
un hint del tipo esperado: `true/false`, número entero, texto/URL). Mismo
patrón server-rendered ya validado y verificado E2E en "Mapeo de Campos".

## Decisiones de este diseño

- **Página separada de "Estado del Servicio"**, aunque el original las
  combina. Ya existen como dos entradas de menú distintas en
  `ModuloWms.GetMenu()` — cada página con una responsabilidad clara
  (Configuración = CRUD de parámetros; Estado = solo lectura de heartbeat,
  spec aparte).
- **`ConfigKey` por dropdown fijo**, no texto libre — mismo criterio que
  `MapperKey` en Mapeo de Campos. Las ~30 claves conocidas se mueven de
  `WMS_Suite/WmsPortal.Core/Models/ServiceConfigModels.cs`
  (`ServiceConfigKeys.Labels`) a `PortalSaas.Abstractions` como
  `WmsServiceConfigKeys`, junto a `WmsFieldMapperKeys` ya existente.
  Deliberadamente **no** incluye credenciales (`SapSettings:UserName`,
  `WmsIntegration:User/Pass`, etc. — quedan solo en env/vault del servidor
  donde corre `WmsSapIntegration.Service`) ni rutas de filesystem del host —
  mismo criterio ya documentado en el original.
- **`ConfigKey` bloqueado en edición.** Es la identidad lógica de la fila
  (`UNIQUE(company_id, config_key)`), igual que `MapperKey`/`FieldName` en
  Mapeo de Campos. Solo `ConfigValue` y `IsActive` son editables en una fila
  existente; para cambiar el parámetro hay que crear uno nuevo y desactivar
  el anterior.
- **Sin validación de tipo sobre `ConfigValue`.** Es texto libre, igual que
  el original — el hint (`true/false`, número entero, texto) es solo
  informativo bajo el campo; la interpretación real del tipo es
  responsabilidad de `WmsSapIntegration.Service` al leerlo, no de esta
  página.
- **Soft state, no delete** — Activar/Desactivar, mismo patrón que Mapeo de
  Campos.
- **Scoping por `ICurrentCompanyAccessor.CompanyId`**, `UpdatedBy` desde
  `ICurrentUserContext.Username` automático — igual que Mapeo de Campos.
- **Estilo: clases del Portal ya existentes**, sin CSS propio.

## Arquitectura

```
Modulo.Wms/
  Services/
    IServiceConfigService.cs
    ServiceConfigService.cs      -- capa sobre WmsDbContext.ServiceConfigs
  Pages/
    ConfiguracionServicio/
      Index.cshtml
      Index.cshtml.cs
```

- `IServiceConfigService`: `ListAllAsync(companyId, ct)`,
  `CreateAsync(companyId, configKey, configValue, isActive, updatedBy, ct)`,
  `UpdateAsync(id, companyId, configValue, isActive, updatedBy, ct)` — mismas
  firmas y mismo criterio que `IFieldMappingService`. Valida
  `ConfigKey` contra `WmsServiceConfigKeys.Labels.ContainsKey(...)` en
  `CreateAsync`, pre-check de duplicado vía `AnyAsync` + catch de
  `DbUpdateException` acotado a violación de unicidad real
  (`Npgsql.PostgresException.SqlState == "23505"` /
  `Microsoft.Data.SqlClient.SqlException.Number is 2601 or 2627`) — mismo
  patrón ya corregido en `FieldMappingService`.
- `ModuloWms.RegisterServices` agrega
  `services.AddScoped<IServiceConfigService, ServiceConfigService>()`.
- Ruta `/wms/configuracion-servicio` — ya reservada en
  `ModuloWms.GetMenu()`.
- `WmsServiceConfigKeys` (nueva clase en `PortalSaas.Abstractions.Modelos`,
  junto a `WmsFieldMapperKeys`): diccionario
  `IReadOnlyDictionary<string, (string Label, string Hint)>`, puerto directo
  de `ServiceConfigKeys.Labels`.

## UI

Tabla: Parámetro (label + `ConfigKey` en `<code>` debajo) | Valor | Activo |
Actualizado | Acciones.

- **Filas existentes:** Parámetro como texto plano (no editable). Valor y
  Activo editables inline vía `form="config-{id}"` → `OnPostGuardarAsync`.
- **Activar/Desactivar:** botones POST a handlers dedicados, mismo patrón
  que Mapeo de Campos.
- **Nuevo parámetro:** formulario aparte debajo de la tabla — `<select>` de
  las ~30 claves fijas (label + `ConfigKey` real, para poder correlacionar
  directo con el `appsettings.json` del servicio), input de Valor con el
  hint del tipo esperado mostrado como texto de ayuda bajo el campo
  (actualizado por JS al cambiar la selección, o renderizado server-side
  para la clave por defecto — implementación decide cuál es más simple sin
  JS custom), checkbox Activo.
- **Caja informativa fija** arriba de la tabla, portada del original:
  aclara que estos valores solo tienen efecto si la instancia real de
  `WmsSapIntegration.Service` de esa compañía tiene
  `"ConfigSource": "Database"` en su propio `appsettings.json` (default es
  `"File"`) — el Portal no tiene forma de saber qué modo usa la instancia
  real corriendo en el servidor.

## Validación y errores

- `[Required]` en `ModelState` para Valor en el formulario de creación.
- Mismo patrón de errores que Mapeo de Campos: `try/catch` alrededor de
  create/update, `ErrorMessage` vía `GetErrorMessage(ex)`.

## Fuera de alcance

- Tabla de heartbeat (queda exclusivamente en "Estado del Servicio", spec
  aparte).
- Validación de tipo real sobre `ConfigValue` (texto libre).
- Escritura de `config_source_effective` (responsabilidad de
  `WmsSapIntegration.Service`, ya documentado como pendiente en
  `ARQUITECTURA.md`).
