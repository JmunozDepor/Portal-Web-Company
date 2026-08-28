# Guía de desarrollo de plugins — referente obligatorio

Estándar para **todo plugin nuevo** de esta plataforma, sea interno (vive en
`plugins/` de este mismo repo, ej. `Modulo.Ventas`/`Modulo.Compras`/
`Modulo.Inventario`/`Modulo.Administracion`) o externo (repo propio, compilado y
distribuido aparte, ej. `Modulo.Rendiciones`). Las reglas de esta página son las
mismas para los dos casos — un plugin externo no es una excepción a nada de lo de
acá, solo cambia dónde vive el código fuente y cómo llega el artefacto compilado
hasta `artifacts/plugins/`.

No duplica el detalle ya escrito en `CLAUDE.md` (§"Todo nace del documento padre",
§"Dirección de dependencias") ni en `docs/01-CONVENCION-NOMBRES-BD.md` — los
referencia. Esta guía es la checklist operativa para arrancar un plugin nuevo sin
tener que releer todo el historial de decisiones de `CLAUDE.md`.

## 1. Contrato de dependencias — la regla que no se negocia

- Un plugin **solo referencia `PortalSaas.Abstractions`**. Nunca `PortalSaas.Core`,
  nunca `PortalSaas.Host`, nunca otro plugin.
  - **Interno** (mismo repo/solución): `ProjectReference` a
    `src/PortalSaas.Abstractions/PortalSaas.Abstractions.csproj`.
  - **Externo** (repo propio): `PortalSaas.Abstractions` se consume como **paquete
    NuGet versionado** (`dotnet pack` desde este repo + feed local/privado o
    referencia por ruta a un `.nupkg`), nunca como `ProjectReference` cruzando
    repos — si el repo externo pudiera compilar sin acceso a este repo, referenciar
    el `.csproj` directo lo rompe. Versionar `PortalSaas.Abstractions` con SemVer
    real desde el día uno, un plugin externo fija la versión que soporta.
- Si el plugin necesita hablar con el SAP de la organización (HANA/SQL Server),
  usa los contratos ya definidos ahí: `IHanaService`, `ICurrentCompanyAccessor`,
  `ICurrentUserContext`. Si necesita su **propia** base de datos separada (patrón
  ya usado por `Modulo.SellOut`/`Modulo.GestionDistribucionGastos`/
  `Modulo.Rendiciones` del original), resuelve la connection string vía
  `IExternalDatabaseConnectionService` (`PortalSaas.Abstractions.Contratos`) —
  ver §6.1 para el detalle completo, incluida la regla dura de motor dual.
- Nunca acceder a `PortalSaasDbContext` directo desde un plugin — eso es
  exclusivo de `PortalSaas.Core`, un plugin pasa siempre por un contrato de
  `Abstractions`.

## 2. `IModuloPortal` — el punto de entrada

Todo plugin implementa `IModuloPortal`:

- `ModuleCode` (o `CodigoModulo` en el original) — identificador único, estable,
  nunca renombrado una vez publicado (es la clave de upsert de `MenuSyncService`
  y la que resuelve `organization_modules`/`platform_modules.code` para el
  filtrado de menú por módulos contratados, ver `docs/03-MODELO-CORE-COMERCIAL.md`
  y la entrega "Filtrado del árbol de menú" en `CLAUDE.md`).
- `RegisterServices(IServiceCollection)` — registra ahí toda su propia DI (nunca
  asumir algo ya registrado por el Host salvo lo que expone `Abstractions`).
- `GetMenu()` — define su árbol de menú propio (`MenuItemDefinition`), con
  `ParentCode` calificado `"OtroModulo.Codigo"` si necesita colgarse de un nodo de
  otro módulo ya cargado.

## 3. Estructura interna — motores genéricos de documento (si aplica)

Si el plugin nuevo es un motor de documento (vende/compra/mueve algo, con
listado + formulario con tabs), sigue el patrón ya establecido por
`SalesDocumentService`/`PurchaseDocumentService`/`InventoryDocumentService`:
catálogo estático por tipo + PageModel base **sin métodos `virtual`** (un
subtipo concreto solo declara `Type`/`MenuCode`/`DocumentName`/`RouteBase`) +
tabs compartidas en `Pages/Shared/` del mismo plugin. Ver `CLAUDE.md`
§"Todo nace del documento padre" para el detalle completo — no es exclusivo de
plugins de venta/compra/inventario, cualquier plugin nuevo con esa forma
(listado + documento con tabs) debería reusar `DocumentListViewModel`/
`DocumentFormViewModel` de `PortalSaas.Abstractions/Componentes/` en vez de
reinventar el chrome.

Si el plugin **no** tiene esa forma (ej. `Modulo.Administracion`, self-service
simple; o `Modulo.Rendiciones`, con su propio dominio de fondos/rendiciones/
aprobaciones), no fuerces el patrón — usalo solo donde encaje de verdad.

## 4. Nombres de rutas de vista — evitar colisión entre plugins

**Bug real ya encontrado** (ver `CLAUDE.md` §"Crash real de producción: colisión
de rutas de vista entre plugins"): Razor cachea vistas compiladas por **ruta
string global**, sin importar de qué assembly de plugin vienen. Si dos plugins
tienen cada uno `Pages/Shared/_TabGeneral.cshtml` en la misma ruta virtual, el
motor de vistas puede resolver la vista de OTRO plugin en runtime — crash con el
modelo equivocado, sin error en build ni en tests.

**Regla obligatoria**: cualquier vista parcial compartida dentro de un plugin
lleva un sufijo único del propio módulo en el nombre de archivo
(`_TabGeneralVentas.cshtml`, no `_TabGeneral.cshtml`) — nunca un nombre genérico
que otro plugin podría reusar igual.

## 5. Formularios — dos gotchas ya confirmados, aplican siempre

- **Antiforgery**: un `<form method="post">` sin ningún otro atributo `asp-*`
  (`asp-page`/`asp-route-*`/etc.) **no** dispara la inyección automática del
  token antiforgery del `FormTagHelper` — todo POST real devuelve 400. Agregar
  siempre `asp-antiforgery="true"` explícito si el form no tiene ya otro
  atributo `asp-*`.
- **Cultura invariante en `<input type="number">`**: nunca escribir
  `value="@algunDecimal"` sin `.ToString(CultureInfo.InvariantCulture)` — la
  cultura del servidor (`es-*`) renderiza `.` como separador de miles, HTML5
  exige formato invariante, y el navegador descarta el `value` inválido **en
  silencio** (campo se ve vacío, sin error). Aplica a cualquier campo numérico
  editable; texto de solo lectura para el usuario (ej. totales) sí puede/debe
  usar la cultura del servidor (`ToString("N2")`), esa regla es al revés.

## 6. Base de datos — convención obligatoria, sin excepción para plugins externos

Ver `docs/01-CONVENCION-NOMBRES-BD.md` completo. Resumen aplicado a plugins:

- Inglés, plural, `snake_case`, sin comillas — **igual en la base compartida de
  la plataforma que en cualquier base propia de un plugin** (`Modulo.Rendiciones`
  y cualquier otro con connection string propia). No hay excepción por ser
  "la base de un plugin externo".
- Toda tabla de negocio nueva lleva `organization_id` (directo o vía
  `company_id` que resuelve a `organization_id`, mismo criterio que
  `CLAUDE.md`) desde el primer `CREATE TABLE` — aunque el plugin resuelva su
  connection string por compañía SAP, la fila de negocio lleva la columna
  igual (consistencia + soporte futuro de una sola base de plugin sirviendo a
  más de una organización).
- PK siempre `id` (`uuid` para entidades referenciadas entre sí, `bigint
  identity` para catálogos/logs) — nunca una clave de negocio como PK.
- FK `<entidad_singular>_id`, timestamps `_at`, booleanos `is_`/`has_`, estados
  en columna `status` con `check`.

### 6.1 Motor dual — obligatorio también para la base propia de un plugin

Esta plataforma soporta **PostgreSQL (SaaS) y SQL Server (on-premise)** como
motor de la base propia (ver `docs/02-ARQUITECTURA-BASE-DE-DATOS.md`). Esa
misma regla aplica a **cualquier base de datos externa que un plugin necesite
para sí mismo** — nunca asumir un solo motor, ni siquiera "total, esto es la
base de un plugin, no la de la plataforma". Un plugin no sabe de antemano si
la organización que lo usa es una instalación SaaS (Postgres) o on-premise
(SQL Server, ej. Comercial Depor).

**`IExternalDatabaseConnectionService`** (`PortalSaas.Abstractions.Contratos`,
implementado en `PortalSaas.Core.Infraestructura.ExternalDatabaseConnectionService`)
resuelve esto — primer consumidor real: `Modulo.Rendiciones`. Contrato:

```csharp
Task<ExternalDatabaseConnection> ResolveConnectionAsync(
    string moduleCode, Guid organizationId, Guid? companyId, CancellationToken ct = default);
```

`ExternalDatabaseConnection` trae `EngineType` (`"postgres"` | `"sqlserver"`,
ver `ExternalDatabaseEngineType`) + `ConnectionString` ya armado — el plugin
decide en **runtime**, no en tiempo de compilación, con cuál proveedor de EF
Core registrar su `DbContext`:

```csharp
services.AddDbContext<MiPluginDbContext>((sp, options) =>
{
    var userContext = sp.GetRequiredService<ICurrentUserContext>();
    var companyAccessor = sp.GetRequiredService<ICurrentCompanyAccessor>();
    var externalDb = sp.GetRequiredService<IExternalDatabaseConnectionService>();
    var companyId = companyAccessor.HasCompany ? companyAccessor.CompanyId : (Guid?)null;
    var connection = externalDb
        .ResolveConnectionAsync(ModuleCode, userContext.OrganizationId, companyId)
        .GetAwaiter().GetResult();

    switch (connection.EngineType)
    {
        case ExternalDatabaseEngineType.Postgres: options.UseNpgsql(connection.ConnectionString); break;
        case ExternalDatabaseEngineType.SqlServer: options.UseSqlServer(connection.ConnectionString); break;
        default: throw new InvalidOperationException($"Motor no soportado: '{connection.EngineType}'.");
    }
});
```

Reglas duras que se derivan de esto:

- El `.csproj` del plugin referencia **los dos** paquetes de proveedor de EF
  Core (`Microsoft.EntityFrameworkCore.SqlServer` **y**
  `Npgsql.EntityFrameworkCore.PostgreSQL`), nunca uno solo.
- El `DbContext` propio del plugin nunca usa `HasColumnType("decimal(...)")`
  ni ningún tipo SQL específico de un proveedor — usar `HasPrecision(p, s)`
  (agnóstico, EF Core lo traduce a `decimal`/`numeric` según corresponda),
  mismo criterio que ya exige `PortalSaasDbContext` para la base compartida
  (nada de `gen_random_uuid()`/`HasDefaultValueSql`/tipos de un solo motor).
- La conexión se resuelve contra `ModuleExternalConnection` (tabla
  `module_external_connections`, base propia de la plataforma) — fila puntual
  por `(organization_id, company_id, module_code)` o fila global de la
  organización (`company_id IS NULL`) como fallback. Se administra hoy solo
  por acceso directo a la base (sin pantalla de `/Admin/*` todavía — mismo
  estado inicial que tuvieron `Plans`/`Subscriptions` antes de su UI).

## 7. Empaquetado y publicación — mismo target en interno y externo

Todo `.csproj` de plugin lleva el target `PublicarComoPlugin`
(`AfterTargets="Build"`, mismo patrón que `Modulo.Ventas`/
`Modulo.Administracion`) que copia el output a
`<nombre-plugin>/<version>/` — esa carpeta + el nombre de la DLL **es** el
manifiesto que `PluginManager` espera, sin archivo de manifiesto aparte.

- **Interno**: el target escribe directo a
  `artifacts/plugins/<NombrePlugin>/<version>/` de este repo (build normal,
  `dotnet build PortalSaas.sln`).
- **Externo**: el target escribe a una carpeta de salida local del repo
  externo; el artefacto (`<NombrePlugin>/<version>/*.dll` + dependencias) se
  **copia manualmente** (o vía pipeline propio, si se arma después) a
  `artifacts/plugins/<NombrePlugin>/<version>/` de este portal. El portal no
  necesita saber que el origen es un repo distinto — desde `PluginManager` es
  exactamente igual a cualquier otro plugin.
- Si el plugin tiene una dependencia nativa (x64-only, tipo el cliente HANA),
  fijar `PlatformTarget=x64` en el `.csproj`, mismo criterio que
  `PortalSaas.Core`/`Host`.
- `Plugins:ArtifactsFolder` en `appsettings.Development.json` del Host controla
  dónde busca `PluginManager` — confirmar que apunta a la carpeta correcta
  antes de dar una integración por probada.

## 8. Autorización

Un plugin nunca decide autorización por sí mismo con lógica propia — siempre
vía `ICurrentUserContext.HasActionAsync`/`IsAdmin` (`Abstractions`), que ya
resuelve `UserMenuProfile`+`ProfileAction`+`Menu`+`PermissionAction` contra la
base de la plataforma. `IsAdmin` bypasea todo. Sin acceso, `Forbid()` — nunca
ocultar en silencio sin also bloquear server-side (el menú se filtra, pero el
handler igual debe verificar).

## 9. Checklist antes de dar un plugin nuevo por listo

- [ ] Solo referencia `PortalSaas.Abstractions` (paquete NuGet si es externo).
- [ ] `IModuloPortal.ModuleCode` único, estable, sin coincidir con otro plugin.
- [ ] Ninguna vista parcial compartida con nombre genérico (sufijo de módulo
      en el archivo).
- [ ] Todo `<form method="post">` sin otro `asp-*` lleva
      `asp-antiforgery="true"` explícito.
- [ ] Todo `value="@decimal"` de un `<input type="number">` usa
      `CultureInfo.InvariantCulture`.
- [ ] Toda tabla nueva (compartida o de base propia del plugin) sigue
      `docs/01-CONVENCION-NOMBRES-BD.md`, con `organization_id`.
- [ ] Si el plugin tiene base propia: referencia los DOS proveedores de EF
      Core (SqlServer + Npgsql), resuelve el motor en runtime vía
      `IExternalDatabaseConnectionService`, y su `DbContext` no usa ningún
      tipo SQL específico de un solo proveedor (`HasPrecision`, no
      `HasColumnType("decimal(...)")`).
- [ ] Target `PublicarComoPlugin` presente y probado (carpeta +
      DLL en `artifacts/plugins/` reconocida por `PluginManager`).
- [ ] `[Authorize]` en el/los PageModel base del plugin -- no hay convención global
      `AuthorizeFolder` en el Host, sin esto un request anónimo llega directo al
      handler (bug real encontrado en `Modulo.Rendiciones`: crash 500 en vez de
      redirect 302 a login, porque el DbContext del plugin exige un claim de sesión
      que no existe).
- [ ] Autorización vía `ICurrentUserContext`, nunca lógica propia.
- [ ] Si aplica motor de documento: sin métodos `virtual` en el PageModel base,
      catálogo estático por tipo.
- [ ] Vocabulario visible/interno sin coincidir con marca comercial existente
      en el mercado (aplica en particular a `Modulo.Rendiciones`).
