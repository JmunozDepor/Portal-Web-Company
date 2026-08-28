# Catálogo general de conexiones externas por compañía — Diseño

Fecha: 2026-08-27
Estado: implementado (rama feature/catalogo-conexiones-externas, 2026-08-28)

## 1. Contexto y problema

Hoy las conexiones a bases de datos externas de los plugins se guardan en la tabla
`module_external_connections`, con una fila por `(companyId, moduleCode)`. Cada fila
repite host/puerto/base/usuario/secreto. Si varios módulos de la misma compañía
apuntan a la misma base, la configuración se duplica y hay que editarla en varios
lugares. Además el modelo solo contempla motores Postgres y SQL Server, y no tiene
lugar para conexiones que no son de base de datos (APIs HTTP).

El portal está incorporando administración self-service para el admin de cada
organización (área `/organizacion/*`, plugin `Modulo.Administracion`). La primera
pieza a resolver es la gestión de conexiones externas.

Este spec cubre **solo el catálogo de conexiones externas**. Las otras secciones
previstas para el self-service (Instancias, Correo, Permisos de documentos,
Licencia) van en specs posteriores.

## 2. Objetivo

Un catálogo general de conexiones externas **por compañía**, reutilizable por
cualquier módulo o servicio, sin nada hardcodeado. Cada compañía define sus
conexiones válidas una sola vez; cada módulo elige de ese catálogo qué conexión
usa y para qué propósito.

Entra en este spec:

1. Modelo de datos nuevo (`company_external_connections` + `company_module_connections`).
2. Migración de datos desde `module_external_connections` (desduplicando).
3. Cambio interno de `IExternalDatabaseConnectionService` para resolver contra el
   modelo nuevo, **manteniendo su firma pública**.
4. Refactor de la página `/Admin/Organizations/Companies/ExternalConnections/*`
   para operar contra el modelo nuevo.
5. Página self-service nueva `/organizacion/conexiones-externas` con selector de
   compañía.

Fuera de alcance:

- Migrar los canales HTTP del WMS (`IntegrationDefinition` tipo `WmsCloud` / `Sql`).
  El enum incluye `HttpApi` desde ya para no requerir otra migración después, pero
  ningún consumidor lo usa en esta iteración.
- CRUD de Compañías en el self-service (el alta sigue en `/Admin`). El selector de
  compañía del tenant es de solo lectura.
- Instancias, Correo, Permisos de documentos, Licencia self-service.

## 3. Enfoque

Enfoque A del brainstorming: la lógica vive en un servicio de `PortalSaas.Core`
detrás de un contrato en `PortalSaas.Abstractions`; el servicio recibe
`organizationId`/`companyId` explícitos. La página `/Admin` los pasa por ruta; la
página del plugin los resuelve de `ICurrentUserContext` / selector de compañía.
Una sola implementación de reglas, cifrado y "probar conexión". El plugin sigue
viendo únicamente `PortalSaas.Abstractions`.

## 4. Modelo de datos

> Convenciones del repo (verificadas en código): las tablas de infraestructura
> como `module_external_connections` usan PK `long` (bigint identity) y los
> "tipos" se modelan como **clase estática de constantes `string`** + check
> constraint, no como `enum` C#. El modelo nuevo sigue esa convención:
> `Id` es `long`; `Tipo` es `string` (`ExternalConnectionType.*`); el "engine"
> que hoy expone `ExternalDatabaseConnection.EngineType` sigue siendo el `string`
> `"postgres"` / `"sqlserver"` (se añade `"hana"`). No hay `enum`
> `ExternalDatabaseEngineType`: es `ExternalDatabaseEngineType` clase estática de
> constantes.

### 4.1 `company_external_connections`

Una fila por conexión válida de una compañía. **No lleva `organization_id`**: el
ámbito de organización se deriva vía `Company.OrganizationId` (regla dura del
proyecto — personalización por `CompanyId`, nunca por `OrganizationId` directo).

| Columna (entidad / BD) | Tipo | Notas |
|---|---|---|
| `Id` / `id` | `long` (bigint identity, PK) | |
| `CompanyId` / `company_id` | Guid (FK `companies`, `DeleteBehavior.Restrict`) | obligatorio |
| `Nombre` / `nombre` | string(100) | único por `CompanyId` (índice único `uq_company_external_connections_company_nombre`) |
| `Tipo` / `tipo` | string(20) (`ExternalConnectionType.*`) + check constraint | `db_postgres`, `db_sqlserver`, `db_hana`, `http_api` |
| `Host` / `host` | string(200) null | host de BD; null para `http_api` |
| `BaseUrl` / `base_url` | string(500) null | URL base de API; null para `db_*` |
| `Port` / `port` | int null | solo `db_*` |
| `DatabaseName` / `database_name` | string(100) null | solo `db_*` |
| `TechnicalUsername` / `technical_username` | string(100) null | usuario técnico / usuario Basic |
| `TechnicalSecretKey` / `technical_secret_key` | string(500) null | write-only, AES-256-GCM vía `ISecretoCifradoService` |
| `ConfiguracionExtra` / `configuracion_extra` | `text` / `nvarchar(max)` null | parámetros específicos del tipo/módulo (timeouts, `ClientEnvCode`, etc.) |
| `IsActive` / `is_active` | bool (default true) | |
| `CreatedAt` / `created_at`, `UpdatedAt` / `updated_at` | timestamptz / datetimeoffset | inicializados en C# |

Reglas de validación (en el servicio + DataAnnotations en `ExternalConnectionEditModel`):

- `Nombre` `[Required]`/`[StringLength(100)]` y único por compañía.
- `Tipo` `[Required]`; `Host`/`BaseUrl`/`DatabaseName`/`TechnicalUsername`
  `[StringLength]`, `Port` `[Range(1, 65535)]`.
- Según `Tipo` (solo en el servicio, no expresable en anotaciones):
  - `db_*`: `Host`, `Port`, `DatabaseName`, `TechnicalUsername` obligatorios.
  - `http_api`: `BaseUrl` obligatorio.
- `TechnicalSecretKey` nunca se devuelve en DTOs de lectura. Se escribe solo si el
  formulario envía un secreto nuevo (write-only); si viene vacío en edición, se
  conserva el existente.
- Toda entidad leída/escrita se valida contra la `CompanyId` del contexto (que ya
  resuelve a la organización) antes de operar (defensa en profundidad).

### 4.2 `company_module_connections` (binding)

Qué conexión usa cada módulo/propósito en cada compañía. **Sin `organization_id`**
(mismo criterio que 4.1).

| Columna (entidad / BD) | Tipo | Notas |
|---|---|---|
| `Id` / `id` | `long` (bigint identity, PK) | |
| `CompanyId` / `company_id` | Guid (FK `companies`, `DeleteBehavior.Restrict`) | |
| `ModuleCode` / `module_code` | string(50) | código del módulo (`"Wms"`, …) |
| `Purpose` / `purpose` | string(50) (default `"Default"`) | permite varias conexiones por módulo |
| `ConnectionId` / `connection_id` | `long` (FK `company_external_connections`, `DeleteBehavior.Restrict`) | |
| `UpdatedAt` / `updated_at` | timestamptz / datetimeoffset | |

Índice único `uq_company_module_connections_company_module_purpose`
`(CompanyId, ModuleCode, Purpose)`; índice `ix_company_module_connections_connection_id`.

`Purpose` es un string libre declarado por cada módulo (ver 4.3). Ejemplo: WMS
podría exponer `Default` (su base propia) y `SapSource` (base SAP de origen).

### 4.3 Declaración de propósitos por módulo

`IModuloPortal` gana un miembro opcional para declarar qué conexiones necesita:

```csharp
// PortalSaas.Abstractions
public interface IModuloPortal
{
    // ...
    IReadOnlyList<ExternalConnectionRequirement> ExternalConnectionRequirements => Array.Empty<ExternalConnectionRequirement>();
}

public sealed record ExternalConnectionRequirement(
    string Purpose,                 // "Default", "SapSource", ...
    string DisplayName,             // "Base de datos del módulo"
    ExternalConnectionKind Kind,    // Database | HttpApi
    bool Required);

public enum ExternalConnectionKind { Database, HttpApi }
```

Módulos que no lo implementan se asumen con un único requisito implícito
`Purpose = "Default"`, `Kind = Database`, `Required = true` (comportamiento
equivalente al actual). La UI de binding se arma a partir de estos requisitos +
`PluginManager.ModulosCargados`.

## 5. Contratos y servicios

### 5.1 `PortalSaas.Abstractions`

```
Contratos/ICompanyExternalConnectionService.cs
Modelos/ExternalConnectionDto.cs          // sin SecretCifrado
Modelos/ExternalConnectionEditModel.cs    // con secreto write-only
Modelos/ModuleConnectionBindingDto.cs
Modelos/ExternalConnectionType.cs         // enum
Modelos/ExternalConnectionRequirement.cs  // record + ExternalConnectionKind
Modelos/ConnectionTestResultDto.cs        // { bool Ok; string? Error; long? ElapsedMs }
```

`ICompanyExternalConnectionService` (todos los métodos reciben `organizationId` y
`companyId` explícitos):

- `Task<IReadOnlyList<ExternalConnectionDto>> ListAsync(orgId, companyId, ct)`
- `Task<ExternalConnectionDto?> GetAsync(orgId, companyId, id, ct)`
- `Task<long> CreateAsync(orgId, companyId, ExternalConnectionEditModel m, ct)`
- `Task UpdateAsync(orgId, companyId, id, ExternalConnectionEditModel m, ct)`
- `Task DeleteAsync(orgId, companyId, id, ct)` — falla si hay bindings que la usan
- `Task<ConnectionTestResultDto> TestAsync(orgId, companyId, id, ct)`
- `Task<IReadOnlyList<ModuleConnectionBindingDto>> ListBindingsAsync(orgId, companyId, ct)`
- `Task SetBindingAsync(orgId, companyId, moduleCode, purpose, connectionId, ct)`
- `Task ClearBindingAsync(orgId, companyId, moduleCode, purpose, ct)`

### 5.2 `PortalSaas.Core`

- `Core/Administracion/CompanyExternalConnectionService.cs` — implementa el
  contrato contra `PortalSaasDbContext`, reutiliza `ISecretoCifradoService`.
- `TestAsync`: abre conexión real según `Tipo`:
  - `DbPostgres` → `NpgsqlConnection`
  - `DbSqlServer` → `Microsoft.Data.SqlClient.SqlConnection`
  - `DbHana` → conector HANA existente (`Sap.Data.Hana`, ya referenciado por Core)
  - `HttpApi` → `HttpClient` GET/HEAD a `BaseUrl` con timeout corto
  Devuelve `ConnectionTestResultDto` con `Error` = mensaje de la excepción
  (nunca incluye el secreto).
- Registro DI en `PortalSaas.Host/Program.cs`.

### 5.3 `IExternalDatabaseConnectionService` (runtime, sin cambio de firma)

`ResolveConnectionAsync(moduleCode, companyId)` pasa a:

1. Buscar `company_module_connections` por `(companyId, moduleCode, Purpose="Default")`.
   Overload nuevo `ResolveConnectionAsync(moduleCode, companyId, purpose)` para
   módulos que necesitan varias; el existente delega con `purpose="Default"`.
2. Cargar la `company_external_connections` referenciada, descifrar secreto, armar
   `ConnectionString` según `Tipo`.
3. Mapear `Tipo` → `ExternalDatabaseEngineType` que ya devuelve el contrato
   (`DbPostgres`→`Postgres`, `DbSqlServer`→`SqlServer`). `DbHana` extiende el enum
   `ExternalDatabaseEngineType` con `Hana`; consumidores que no lo soporten
   lanzan su `default` como hoy.
4. Si no hay binding o la conexión está `Activo=false`: misma excepción "conexión
   externa no configurada" que hoy.

## 6. Migración

Migración EF `AddCompanyExternalConnections` (proyectos Postgres y SqlServer).

Esquema: crear las dos tablas nuevas.

Datos (en la misma migración o script idempotente posterior):

1. Por cada fila de `module_external_connections`, agrupar por
   `(company_id, engine_type, host, port, database)` y crear **una**
   `company_external_connections`:
   - `Tipo` = `db_postgres` / `db_sqlserver` según `engine_type` (una fila con
     `engine_type` no soportado se omite con `WARN`, nunca aborta el backfill)
   - copiar host/port/database/username/secreto tal cual (el secreto ya está
     cifrado con el mismo `ISecretoCifradoService`, se copia el blob)
   - `Nombre` = `"{moduleCode}"` de la primera fila, o `"{host}/{database}"` si
     colisiona; garantizar unicidad por compañía con sufijo incremental
   - sin `organization_id` (se deriva de `Company.OrganizationId`)
2. Por cada fila original crear un `company_module_connections` con
   `Purpose="Default"` apuntando a la conexión creada/compartida.
3. Si dos filas del mismo `(company_id, module_code)` resolvieran a conexiones
   distintas (no debería ocurrir con el modelo actual, pero se valida): tomar la
   primera y escribir `WARN` en el log de migración con ambos ids.
4. `module_external_connections` se conserva en esta migración (no se elimina)
   para permitir rollback; se marca como obsoleta y se elimina en un spec
   posterior una vez verificado en las 4 bases.

Aplicar contra las 4 bases reales de `172.16.122.171` (bases Postgres operativas)
tras validar en local. Confirmar si alguna queda pendiente.

## 7. UI

### 7.1 `/Admin/Organizations/Companies/ExternalConnections/*` (refactor)

- `Index` (`?companyId=`): lista de `company_external_connections` de la compañía
  + sección de bindings por módulo/propósito. Handlers `OnPostTestConnectionAsync`,
  `OnPostDeleteAsync`, `OnPostSetBindingAsync` delegan en el servicio con
  `organizationId`/`companyId` de ruta.
- `Create` / `Edit`: formulario con `Tipo` y campos condicionados por tipo;
  secreto write-only; `ConfiguracionExtra` como editor clave/valor simple.
- Sin cambio funcional visible respecto a hoy salvo el `Tipo` y los bindings.

### 7.2 `/organizacion/conexiones-externas` (nueva, plugin `Modulo.Administracion`)

- `Pages/ConexionesExternas/Index.cshtml(.cs)` y `Editar.cshtml(.cs)`, heredan
  `AdminPageModelBase` (cookie de tenant + `Forbid()` si `!IsAdmin`).
- Selector de compañía: dropdown de solo lectura con las compañías de
  `ICurrentUserContext.OrganizationId` (nuevo método de lectura mínima, o
  `ITenantUserAdminService` si ya expone compañías). Parámetro `?companyId=`.
- Misma UI que 7.1 pero tomando `organizationId` del contexto y `companyId` del
  selector. Patrón visual "ListadoOrganizacion".
- Nodo de menú en `ModuloAdministracion.GetMenu()`: "Conexiones externas",
  `PageRoute = "/organizacion/conexiones-externas"`, `Order = 6`, icono
  `bi-plug` (o similar). La subnav horizontal genérica lo recoge solo.
- `MenuSyncService` sincroniza el nodo a la tabla `menus` como con los demás.

## 8. Manejo de errores

- Fallo de "probar conexión": `MensajeError` (TempData) con el texto de la
  excepción de conexión, sin el secreto. `ConnectionTestResultDto.Error`.
- `DeleteAsync` con bindings activos: `MensajeError` "La conexión está en uso por
  N módulo(s)" y no borra.
- Validación de campos por tipo: se muestra en el formulario (ModelState).
- Binding a una conexión inexistente o de otra compañía: rechazado por el
  servicio con excepción de validación.

## 9. Pruebas

xUnit, patrón `TenantUserAdminServiceTests`:

- Scoping: `ListAsync`/`GetAsync` no devuelven conexiones de otra organización ni
  de otra compañía.
- Create/Update: secreto write-only (no se pierde al editar sin reenviar secreto;
  DTO de lectura no expone `SecretCifrado`).
- Validación por `Tipo` (falta `Host` en `DbPostgres`, falta `BaseUrl` en
  `HttpApi`).
- `Nombre` duplicado por compañía → error.
- Binding: `SetBindingAsync` crea y actualiza; `ClearBindingAsync`; unicidad
  `(CompanyId, ModuleCode, Purpose)`.
- `DeleteAsync` bloqueado si hay binding.
- Resolver runtime: `ResolveConnectionAsync(moduleCode, companyId)` devuelve la
  conexión del binding `Default`; overload con `purpose`; excepción si no hay
  binding.
- Migración: caso de desduplicación (2 filas mismo host/db → 1 conexión, 2
  bindings) y caso de conflicto (log `WARN`, toma la primera).

Objetivo: suite verde (hoy 183/0).

## 10. Riesgos

- **Datos migrados sin consumidor**: mitigado porque el resolver runtime entra en
  el mismo spec.
- **`ExternalDatabaseEngineType.Hana`**: revisar todos los `switch` sobre ese enum
  en Core y plugins; los que no soporten HANA deben mantener su `default` con
  excepción clara.
- **Doble fuente de verdad temporal**: `module_external_connections` sigue en la
  base tras la migración; ningún código la lee después del refactor. Eliminarla en
  spec posterior.
