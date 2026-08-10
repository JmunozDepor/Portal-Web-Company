# CLAUDE.md — Portal SaaS - Core (antes "Proyecto Saas Portal")

Contexto persistente para Claude Code en este repositorio. Para el razonamiento
completo (por qué, no solo qué) ver `ARCHITECTURE.md` y `docs/` — este archivo es el
resumen operativo y las reglas duras, no lo dupliques ahí.

**Antes de portar o consultar código de `PortalSAP_v2`/`WMS_Suite`**, usar la copia de
`referencia-original/` (no los repos reales en `C:\PROYECTOS\PortalSAP_v2` /
`C:\PROYECTOS\WMS_Suite`) — es de solo lectura, tomada el 24 jul 2026, ver
`ARCHITECTURE.md` §-1 para el detalle.

**Carpeta general del proyecto: `C:\PROYECTOS\Proyecto Portal Web-Company\`** — nombres
unificados bajo la convención `Portal SaaS - <rol>` (29 jul 2026, reordenamiento de
carpetas). Este repo es **`Portal SaaS - Core\`** (antes `Proyecto Saas Portal\` —
**si ves esa ruta vieja en un doc/config/atajo, está desactualizada, corregirla al
encontrarla**). Sus hermanos:
- **`Portal SaaS - Plugins\`** (antes `Portal SaaS-Plugins\`) — contenedor de los
  plugins **externos** del portal (repos propios, compilados aparte y copiados a
  `artifacts/plugins/` de este repo, ver `docs/09-GUIA-DESARROLLO-PLUGINS.md`),
  separados de `plugins/` (los internos, que sí viven dentro de este repo). Primer
  miembro: `Portal SaaS - Plugins\Modulo.Rendiciones\` -- **ruta real, corrige
  cualquier mención más abajo en este archivo a `C:\PROYECTOS\Modulo.Rendiciones` o a
  `Portal SaaS-Plugins\` (ubicaciones viejas, ya no existen ahí)**.
- **`Portal SaaS - Servicios SAP\`** (antes `Servicios SAP\`, ver la entrada
  correspondiente más abajo) — proyecto hermano independiente, sin dependencia
  cruzada de proyecto/solución con este repo pese a compartir el prefijo de nombre.
- **`Portal SaaS - Analisis Inicial\`** (antes `Proyecto Mejora Web\`) — análisis
  previo a la creación de este repo (evaluación CEO de `PortalSAP_v2`/`WMS_Suite`
  como producto SaaS/on-premise, ver `MEMORY.md`/`proyecto-menoja.md` ahí) —
  histórico, no área de desarrollo activo.

Al buscar o referenciar algo que podría vivir en un hermano (o agregarse ahí a
futuro), considerar esta carpeta general, no asumir que todo cuelga de este repo.

## Qué es esto

La vía de evolución de `PortalSAP_v2` hacia un producto vendible (SaaS + on-premise)
para cualquier empresa que use SAP Business One. Proyecto **paralelo**, no un fork:
`PortalSAP_v2` sigue operando para Comercial Depor sin interrupciones. Ver
`ARCHITECTURE.md` §0-2 para el detalle de qué se porta de ahí y qué no.

## Decisiones ya tomadas (no reabrir sin una razón nueva y explícita)

- **Arquitectura núcleo**: se reutiliza la de `PortalSAP_v2` — modular monolith,
  plugins reales en `AssemblyLoadContext` aislado, `IModuloPortal`, motores de
  documento genéricos, motor de aprobación configurable. **No reconstruir desde cero.**
- **Base propia de la plataforma: motor dual — PostgreSQL (SaaS/instalaciones nuevas)
  o SQL Server (on-premise que ya tiene SQL Server, ej. Comercial Depor en
  `sqlsap.cdepor.cl`)** — decisión revisada 24 jul 2026 (originalmente solo Postgres,
  ver `docs/00-HISTORIAL-DECISIONES.md`). El modelo EF Core (`src/PortalSaas.Data`) es
  el mismo para los dos motores, sin nada específico de proveedor — ver
  `docs/02-ARQUITECTURA-BASE-DE-DATOS.md` §1 y §7. La conexión hacia el SAP de cada
  organización (HANA/SQL Server) es un motor totalmente aparte, sin cambios respecto a
  `PortalSAP_v2` — no confundir los dos "SQL Server" (uno es la base propia de la
  plataforma, el otro es el SAP del cliente).
- **Nivel `organizations` por encima de `companies`**: toda la capa comercial (plan,
  suscripción, licencia, límites, medición de uso) cuelga de `organizations`, no de
  `companies`. Ver `docs/03-MODELO-CORE-COMERCIAL.md` para el modelo completo, y
  `docs/01-CONVENCION-NOMBRES-BD.md` para la convención de nombres (inglés, plural,
  formal — el idioma no es lo importante, la consistencia sí).
- **REGLA DURA (2026-08-08): todo plugin que necesite personalizar su comportamiento o
  su persistencia por el SAP/negocio del cliente lo hace por `CompanyId`, NUNCA por
  `OrganizationId` directo, y SIN fallback a un alcance más amplio.** Modelo real:
  `Organization` 1:N `Instance`, `Instance` 1:N `Company` (varias Company de la misma
  Organization pueden compartir una Instance física), `Company` N:1 `Instance`. Una
  Organization puede tener Companies con SAP completamente distintos (UDFs, series,
  almacenes, layouts de Excel) — cualquier tabla o resolución de conexión que se quede
  en `OrganizationId` termina mezclando configuración de compañías que en la práctica
  son SAP distintos. Aplica sin excepción a:
  - Tablas de configuración de un plugin (ej. `GenericImportConfig`,
    `GenericImportUserField` de `Modulo.ImportacionGenerica`, corregidas 2026-08-08 —
    antes colgaban de `OrganizationId`, ahora de `CompanyId`, mismo patrón que
    `UserMenuProfile`/`OrganizationDocumentPermission`).
  - Resolución de conexión a base de datos EXTERNA de un plugin
    (`IExternalDatabaseConnectionService.ResolveConnectionAsync`) — `companyId` es
    parámetro obligatorio, no nullable, y **no existe** un fallback a una fila global de
    la organización (`CompanyId IS NULL`). Existía antes (2026-07-26) y se eliminó a
    propósito: un plugin sin Company activa (`ICurrentCompanyAccessor.HasCompany`
    false) debe rechazar la operación con un mensaje claro, nunca degradar en silencio a
    un alcance más amplio. `Modulo.Rendiciones` y `Modulo.GestionDistribucionGastos`
    (`Portal SaaS - Plugins/`) ya siguen este patrón.
  - Cualquier plugin nuevo, interno o externo, que persista datos propios o resuelva una
    conexión — no hay excepción "el módulo no tiene concepto de compañía": si de verdad
    no lo tiene, es una señal de que no debería vivir bajo este modelo, no una razón
    para agregar un fallback a Organization.
  - Company ya resuelve a Organization (`Company.OrganizationId`) — nunca hace falta
    guardar ambos IDs en la misma tabla, alcanza con `CompanyId`.
  formal — el idioma no es lo importante, la consistencia sí).
- **`GestionDistribucionGastos` y `SellOut` NO se portan** — son desarrollo a medida
  de Comercial Depor (confirmado explícitamente por el dueño del proyecto), no
  funcionalidad de plataforma. Si un cliente nuevo necesita algo similar, se construye
  como módulo genérico configurable, nunca como copia con nombres distintos.
- **Proyecto hermano: `Servicios SAP`** (`C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Servicios SAP\`,
  creado 2026-07-29, carpeta renombrada el mismo día como parte del reordenamiento de
  nombres — ver "Carpeta general del proyecto" más arriba) — familia de Windows Services SAP standalone (sin UI, sin sesión
  HTTP), independientes de este proyecto pero preparados para integrarse. Primer
  miembro: `TransferenciaAutomatica`, portado del Windows Service legado de transferencia
  de stock entre bodegas de Comercial Depor, sin cambiar su lógica de negocio (mismas
  queries HANA, mismo stored procedure de asignación de bodegas, mismo mecanismo de UDF).
  No hay dependencia de proyecto/solución cruzada — comparten con este proyecto, vía
  **copia deliberada**, el cifrado de secretos (`ISecretoCifradoService`, AES-256-GCM) y
  el shape del connection string hacia el SAP del cliente (mismo criterio que
  `SapConnectionStringFactory`). El punto de integración futuro es que
  `ICompanyProvider` de `Servicios SAP` gane una implementación que lea
  `organizations`/`companies`/`instances` de este proyecto en vez de `appsettings.json` —
  hasta entonces, `Servicios SAP` corre completamente aparte. Ver
  `Servicios SAP/docs/00-VINCULO-CON-PORTAL-SAAS.md` para el razonamiento completo.

## Reglas que no se negocian

- **Toda tabla de negocio nueva lleva `organization_id`** (directo o vía `company_id`
  que resuelve a `organization_id`) desde el primer modelo — sin excepción, sin "se
  agrega en una migración después". Mismo criterio que ya probó `PortalSAP_v2` con
  `EMPRESA_CODIGO`, un nivel más arriba.
- **Convención de nombres de base de datos formal y obligatoria** (ver
  `docs/01-CONVENCION-NOMBRES-BD.md`): inglés, plural, `snake_case` en minúsculas,
  sin comillas, `id` surrogate siempre, FK `<entidad>_id`, timestamps `_at`,
  booleanos `is_`/`has_`, estados en columna `status`. Aplica igual en los dos
  motores (§ Decisiones ya tomadas).
- **Toda pantalla de administración de un catálogo/documento maestro (registro que el
  usuario crea explícitamente desde una UI del portal — planes, perfiles, grupos de
  menú, módulos, instancias, compañías, proveedores externos, tipos de gasto,
  políticas, etc.) debe ofrecer Crear + Editar + Eliminar, "si fuese el caso"** —
  es decir, salvo que una de las dos excepciones de abajo aplique explícitamente:
  - **Eliminar se reemplaza por desactivar (`is_active`/`status`) cuando el registro
    es HISTORIAL real** (una fila que representa algo que ya ocurrió — ej.
    `subscriptions`, `on_premise_licenses`, `audit_logs`) — borrarla destruiría
    trazabilidad real, no solo un dato de configuración.
  - **Eliminar se reemplaza por desactivar cuando el borrado en cascada alcanzaría
    datos de negocio de otro dueño** de forma no reversible ni acotable con un
    chequeo simple (ej. `organizations`/`users` — borrar una organización arrastra
    compañías, usuarios, suscripciones, licencias, conexiones de plugin; ya tienen
    su propio soft-delete vía `Organization.Status`/`User.IsActive`).
  - **En cualquier otro caso, Eliminar es borrado real** (no soft-delete disfrazado),
    pero SIEMPRE bloqueado con un mensaje claro si el registro tiene dependientes
    reales (otra tabla lo referencia con datos vivos) — nunca un borrado en cascada
    silencioso ni un `ON DELETE CASCADE` implícito sobre datos que el usuario no
    pidió borrar explícitamente. Ver `Pages/Admin/Plans/Index.cshtml.cs`,
    `Profiles/Index.cshtml.cs`, `MenuGroups/Index.cshtml.cs`,
    `PlatformModules/Index.cshtml.cs`, `Organizations/Instances/Index.cshtml.cs`,
    `Organizations/Companies/Index.cshtml.cs` (27 jul 2026) como los ejemplos de
    referencia del patrón: `[TempData] ErrorMessage` + `OnPostDeleteAsync` que
    primero valida dependientes (`AnyAsync` contra las tablas que lo referencian) y
    solo si no hay ninguno hace `Remove` + `SaveChangesAsync`, con
    `onsubmit="return confirm(...)"` en el botón. Aplica igual a cualquier motor
    genérico de documento futuro (Venta/Compra/Inventario y los que vengan) y a
    cualquier plugin nuevo — no es una regla exclusiva del backoffice de plataforma.
- **Todo catálogo compartido de SAP consumido desde un formulario (Artículo, Cliente,
  Proveedor, Almacén, Cuenta Mayor, Centro de Costos y cualquiera nuevo que se agregue)
  se busca con LIKE en vivo vía `wireCatalogSearch`, nunca con un `<select>` armado a
  mano en Razor** — la diferencia entre catálogos GRANDES (Artículo/Cliente/Proveedor,
  cientos de miles de filas posibles) y CHICOS (Almacén/Cuenta Mayor/Centro de Costos,
  decisión explícita del dueño del proyecto de que son acotados) es el valor de
  `minChars`, no el mecanismo -- los dos casos usan el mismo `<input list="...">` +
  `<datalist>` + `wireCatalogSearch`, nunca `<select asp-items="...">` con la lista
  completa armada en el servidor. Patrón único, portado primero en Artículo (27 jul
  2026) y replicado a Cliente/Proveedor/Almacén/Cuenta Mayor/Centro de Costos (mismo
  día) tanto en los campos de cabecera como en los de cada línea de detalle:
  - **Catálogos grandes** (Artículo/Cliente/Proveedor): `minChars: 2` (Artículo,
    mismo mínimo que ya usaba `PortalSAP_v2`) o `3` (Cliente/Proveedor, default del
    helper) — el handler AJAX devuelve `[]` si `text` viene vacío/blanco, **nunca**
    sirve el catálogo completo sin filtro.
  - **Catálogos chicos** (Almacén/Cuenta Mayor/Centro de Costos, Marca, Tipo de
    Gasto): `minChars: 0` — `wireCatalogSearch` precarga el `<datalist>` con una
    búsqueda de texto vacío apenas cablea el campo (así el navegador ya muestra el
    desplegable completo con solo hacer click/foco, sin escribir nada), y el handler
    AJAX correspondiente devuelve el listado COMPLETO cuando `text` viene vacío
    (`ListAsync(text, limit)` con `text` vacío ignora el `limit` y no filtra, ver
    `CatalogSqlHelper.BuildSearchFilter`) — el LIKE sigue activo apenas se tipea algo,
    solo cambia que el punto de partida (sin texto) ya muestra todo en vez de nada.
    **Nunca** aplicar `minChars: 0` a un catálogo grande — serviría el catálogo
    completo sin que el usuario pidiera nada, exactamente lo que esta regla prohíbe
    para esos.
  - Servidor: un handler AJAX `OnGetSearch<Catálogo>Async(string text, ...)` en el
    `DetailGeneric*ModelBase` del motor (o el `PageModel` que corresponda), que llama
    al método `ListAsync(searchText, limit)`/`SearchAsync(text, limit)` del
    `I*CatalogService` correspondiente con un `Limit` acotado (30 por convención, ver
    `CatalogSqlHelper.DefaultSearchLimit` -- ignorado por el helper cuando `text` es
    vacío, ver el punto de catálogos chicos arriba).
  - Cliente: `wwwroot/js/catalog-search.js`, único archivo compartido por los 3
    motores. Trae debounce (300ms) + el `minChars` configurable de arriba + manejo de
    error visible en consola (`console.error`, para que una falla de red o una
    excepción del handler AJAX no quede invisible como "la búsqueda no encuentra
    nada" sin ninguna pista) -- **cuidado real ya encontrado**: comparar
    `options.minChars || 3` trata `minChars: 0` como "no vino" (0 es falsy en JS) y
    cae siempre a 3, rompiendo el caso de catálogos chicos -- la comparación correcta
    es explícita contra `undefined`.
  - En modo solo-lectura (documento ya creado), un campo de CABECERA muestra
    "código — nombre" leyendo el nombre YA incluido en el DTO del documento (ej.
    `SalesDocumentDto.CustomerName`, que SAP ya trae en la cabecera) — nunca una
    consulta extra al catálogo. Un campo POR LÍNEA en solo-lectura muestra solo el
    código crudo (mismo criterio que ya tenía Artículo) — enriquecer cada línea con
    el nombre implicaría una consulta por línea, no vale la pena para una vista de
    solo lectura.
  - Aplica igual a los 3 motores genéricos de documento (regla de paridad) y a
    cualquier plugin nuevo que necesite elegir un valor de un catálogo SAP grande.
- **`src/PortalSaas.Data` nunca importa un paquete de proveedor** (Npgsql/SqlServer)
  ni nada que solo exista en un motor (`gen_random_uuid()`, `UseIdentityAlwaysColumn`,
  etc.) — eso rompería el punto entero del motor dual. Los valores por defecto
  (`Guid`, `DateTimeOffset`) se generan en C#, como inicializador de propiedad en la
  entidad, nunca con `HasDefaultValueSql`. Ver `docs/02-...md` §7.
- **Nunca credenciales en texto plano** en `appsettings.json` — `dotnet user-secrets`
  en desarrollo, vault en producción. Mismo estándar que `PortalSAP_v2`, sin
  excepciones ni "por ahora lo dejo así" (ver el incidente real de secretos expuestos
  en `WMS_Suite`, documentado en `docs/00-HISTORIAL-DECISIONES.md` — no se repite acá).
  Excepción explícita: `appsettings.Development.json` de los proyectos de migraciones
  SÍ se versiona, porque son credenciales de `docker-compose.yml`/desarrollo local, no
  secretos reales.
- **TLS obligatorio siempre**, sin excepción temporal — mismo motivo que arriba.
- **Ningún módulo comercial (plan/licencia/medición de uso/límites) se declara
  terminado sin tests.** A diferencia de `PortalSAP_v2` (2 archivos de test reales para
  ~400 nodos de `Core`), acá los tests no son deuda a pagar después: un error en
  licenciamiento o medición de uso tiene impacto de negocio directo (factura mal,
  cliente bloqueado por error, o al revés, un cliente sin pagar con acceso ilimitado).
- **Todo módulo nuevo declara explícitamente si es núcleo de plataforma (vendible a
  cualquier organización) o extensión específica de una organización puntual** — nunca
  ambiguo. Esta ambigüedad fue exactamente el problema detectado en
  `GestionDistribucionGastos`/`SellOut` dentro de `PortalSAP_v2`; no se repite acá.
- **Los límites de plan se hacen cumplir en código, no solo se documentan.** Ver
  `IContractLimitService` en `docs/03-MODELO-CORE-COMERCIAL.md` §5 — si no se puede
  verificar el límite, la operación se bloquea (falla hacia lo más estricto), nunca se
  deja pasar en silencio.
- **Dirección de dependencias**: idéntica a `PortalSAP_v2` — `Abstractions` la
  referencian todos, solo `Host` referencia `Core`, nadie referencia `Host`, un plugin
  nunca referencia a otro plugin. Si esto se va a romper, detenerse y avisar antes de
  continuar.
- **Todo nace del documento padre — los documentos hijo no se modifican.** Los tres
  motores genéricos de documento (`SalesDocumentService`/`PurchaseDocumentService`/
  `InventoryDocumentService`, y cualquier motor genérico futuro) comparten el mismo
  patrón a propósito, portado tal cual de `PortalSAP_v2` (`GenericoVenta`/
  `GenericoCompra`/`GenericoInventario`): un catálogo estático por tipo
  (`SalesDocumentTypeCatalog`/etc.) + un par de PageModel base por plugin
  (`IndexGeneric*ModelBase`/`DetailGeneric*ModelBase`, **sin métodos `virtual`, a
  propósito** — un subtipo concreto no puede sobreescribir `OnGetAsync`/`OnPostAsync`,
  solo puede declarar `Type`/`MenuCode`/`DocumentName`/`RouteBase`) + tabs compartidas
  en `Pages/Shared/` del mismo plugin, reusadas tal cual entre todos los subtipos. Un
  documento hijo nuevo (ej. un octavo tipo de Venta, o una tercera pantalla de
  Compras) se agrega con una línea al catálogo estático + una subclase de ~20 líneas —
  **nunca** copiando/reimplementando la lógica de listado/creación/validación.
- **Paridad entre los motores genéricos de documento** (Venta/Compra/Inventario, y
  cualquier motor genérico futuro) — regla explícita del dueño del proyecto,
  formulada igual en `referencia-original/PortalSAP_v2/CLAUDE.md` §"Paridad entre los
  motores genéricos de documento": **toda funcionalidad aplicada en cualquiera de los
  tres se debe considerar para los otros dos** — son la misma familia de formulario,
  no implementaciones independientes que coincidieron en parecerse. Lo único que los
  diferencia es la particularidad de cada uno (Inventario no tiene cliente/vendedor,
  Compras no tiene tabs Logística/Finanzas, Venta tiene 7 tipos contra 2 de los otros
  dos) — "considerar" no significa "aplicar literal sin pensar", significa **nunca
  dar un cambio por terminado sin preguntar explícitamente si corresponde también a
  los otros dos**, y si la respuesta es que no aplica, decir por qué (la
  particularidad concreta que lo justifica), no asumirlo en silencio. Esto rige en
  los dos sentidos:
  - **Hacia adelante**: cualquier pedido de cambio sobre uno de los tres motores (fix
    de bug, ajuste de UX, catálogo nuevo, campo nuevo) — preguntar antes de cerrar el
    cambio si corresponde replicarlo en los otros dos.
  - **Hacia atrás, contra el original**: cualquier funcionalidad que exista en
    `GenericoVenta`/`GenericoCompra`/`GenericoInventario` (`referencia-original/
    PortalSAP_v2`) y todavía no esté portada acá — el inventario vivo de esas
    brechas, motor por motor, está en `docs/08-BRECHA-FUNCIONAL-VS-PORTALSAP-V2.md`
    §1. Si se agrega algo ahí que hoy solo se investigó/portó para un motor,
    actualizar ese documento para reflejar el estado real en los tres, no dejarlo
    desactualizado.

## Dónde está cada cosa

- `ARCHITECTURE.md` — visión completa, qué se reutiliza de `PortalSAP_v2` y por qué.
- `docs/00-HISTORIAL-DECISIONES.md` — memoria completa del análisis previo a este repo
  (veredicto evolucionar-no-reconstruir, los 4 huecos reales, hallazgos de seguridad
  activos en `WMS_Suite`) — léelo antes de reabrir cualquiera de esas decisiones.
- `docs/01-CONVENCION-NOMBRES-BD.md` — convención formal de nombres de tabla/columna
  (inglés, plural, `snake_case`) que rige todo el esquema, en los dos motores.
- `docs/02-ARQUITECTURA-BASE-DE-DATOS.md` — decisión de motor dual (Postgres/SQL
  Server), hosting por etapa del lado Postgres, pooling, aislamiento multi-tenant.
- `docs/03-MODELO-CORE-COMERCIAL.md` — modelo completo de la capa comercial
  (`organizations`, `plans`, `subscriptions`, `on_premise_licenses`, `usage_metrics`)
  más el borrador de esquema.
- `docs/04-MODELO-COMERCIAL-NEGOCIO.md` — el planteamiento de negocio que llena esas
  tablas: mercado objetivo, canal de venta, qué se vende por módulo, cómo se cobra,
  tiers propuestos (Starter/Growth/Enterprise, a validar) y términos SaaS vs.
  on-premise. Precios reales y canal definitivo quedan pendientes de validar.
- `docs/05-RUNBOOK-PRODUCCION.md` — procedimiento paso a paso para desplegar la base
  motor SQL Server contra un servidor real (ej. `sqlsap.cdepor.cl`) — nadie lo ha
  ejecutado todavía, es la guía para cuando se decida hacerlo.
- `docs/06-AUTENTICACION-Y-PREFERENCIAS.md` — correo obligatorio, autenticación con
  bloqueo por intentos, recuperación de contraseña, preferencias personales, envío de
  correo dual (Google Workspace/Microsoft 365, `IEmailSenderService`), y qué falta a
  propósito (2FA real, verificación de correo, sesión, UI de administración).
- `docs/09-GUIA-DESARROLLO-PLUGINS.md` — referente obligatorio para todo plugin nuevo,
  interno (`plugins/` de este repo) o **externo** (repo propio, compilado aparte y
  copiado a `artifacts/plugins/`, ej. `Modulo.Rendiciones`) — contrato de dependencias
  (`Abstractions` como paquete NuGet si es externo), `IModuloPortal`, convención de
  nombres de vista para evitar colisión entre plugins (bug real ya encontrado),
  gotchas de formularios (antiforgery, cultura invariante en inputs numéricos),
  convención de BD aplicada también a bases propias de plugin, empaquetado
  (`PublicarComoPlugin`), autorización, y checklist final antes de dar un plugin por
  listo.
- `docs/08-BRECHA-FUNCIONAL-VS-PORTALSAP-V2.md` — auditoría (25/26 jul 2026) de qué le
  falta a este proyecto para tener paridad funcional con `PortalSAP_v2`: brechas
  puntuales en los 3 motores genéricos (tabs Logística/Finanzas, catálogos, líneas de
  Servicio, `CamposAdicionales`), los 3 importadores distintos del original (líneas
  CSV del formulario compartido Venta/Compra, versión propia de Inventario, y el
  `Modulo.ImportacionGenerica` masivo completo -- 0% portado, ≈5.760 líneas estimadas
  incluyendo 6-7 catálogos ausentes que bloquea), el `Modulo.Rendiciones`/"RindeGastos"
  (hallazgo: es plataforma genuina, no desarrollo a medida, pese a no estar
  documentado en el `CLAUDE.md` del original), el árbol de menús personalizable del
  original (`IMenuAdminService`, ≈1.255 líneas -- acá el menú ya es dinámico pero sin
  ninguna pantalla de personalización: no se puede renombrar/ocultar/reordenar un nodo
  de módulo ni crear una carpeta/página manual), y el requerimiento nuevo de
  importador de plugins (sin precedente en el original, converge con la brecha ya
  conocida de `organization_modules`).
- `referencia-original/PortalSAP_v2/`, `referencia-original/WMS_Suite/` — copia de solo
  lectura de los repos reales (sin `bin`/`obj`/`.vs`/`artifacts`/`graphify-out`/publish),
  para portar código sin tocar los sistemas en producción.
- `Lib/` — binarios de terceros que no son NuGet (25 jul 2026:
  `Sap.Data.Hana.Net.v8.0.dll`, cliente nativo de HANA para `PortalSaas.Core`,
  referenciado por `HintPath`, no lo resuelve `dotnet restore`).
- `plugins/Modulo.Administracion/` — primer plugin real cargado en runtime (25 jul
  2026), self-service de usuarios de la propia organización. Solo referencia
  `PortalSaas.Abstractions` (regla dura de plugins) — `ModuloAdministracion.cs`
  (`IModuloPortal`), `Pages/AdminPageModelBase.cs` (gate `IsAdmin`),
  `Pages/Usuarios/Index.cshtml(.cs)` + `Editar.cshtml(.cs)`. Build vía `dotnet build
  PortalSaas.sln` publica a `artifacts/plugins/Modulo.Administracion/1.0.0/` (target
  `PublicarComoPlugin` en su `.csproj`) — ver "Estado actual" para el detalle completo.

## Estructura de código real (24 jul 2026)

```
src/
├── PortalSaas.Abstractions/                 # Contratos + DTOs, sin lógica ni
│                                               dependencia de proveedor. Portado de
│                                               PortalSAP_v2 lo mínimo necesario hasta
│                                               ahora: IModuloPortal, MenuItemDefinition,
│                                               PortalActions, ISecretoCifradoService.
│                                               Nuevo (no existía en PortalSAP_v2):
│                                               IContractLimitService, LimitCheckResult,
│                                               IAuthenticationService+AuthenticationResult,
│                                               IPlatformAdminAuthenticationService,
│                                               IOrganizationAccessGateService,
│                                               IPasswordResetService,
│                                               IUserPreferenceService+UserPreferenceDto,
│                                               IEmailSenderService+EmailMessage.
│                                               Conector SAP (25 jul 2026, portado):
│                                               IHanaService, ISapConnectionProvider+
│                                               ISapSession, ICurrentCompanyAccessor,
│                                               ICurrentUserContext, SapEngineType.
│                                               ISapConnectionTestService+
│                                               SapConnectionTestResult -- nuevo, no
│                                               portado (no existía en PortalSAP_v2).
│                                               ITenantUserAdminService+DTOs (25 jul
│                                               2026, nuevo) -- self-service de usuarios
│                                               por organización, consumido por
│                                               plugins/Modulo.Administracion.
│                                               ICurrentUserContext ganó OrganizationId.
│                                               IMenuNavigationService+MenuNodeDto (25
│                                               jul 2026, nuevo) -- árbol de `menus` ya
│                                               filtrado/anidado para el sidebar del
│                                               shell de tenant. ICurrentCompanyAccessor
│                                               ganó HasCompany (evita depender de una
│                                               excepción para saber si hay compañía
│                                               activa en la sesión).
│                                               ISalesOrderService+DTOs,
│                                               ICustomerCatalogService,
│                                               IItemCatalogService,
│                                               IWarehouseCatalogService,
│                                               ISalesEmployeeCatalogService (25 jul
│                                               2026, nuevo) -- consumidos por
│                                               plugins/Modulo.Ventas, primer plugin
│                                               que habla con el SAP de la
│                                               organización. Nuevo namespace
│                                               Componentes/: DocumentListViewModel,
│                                               DocumentFormViewModel -- chrome
│                                               compartido "listado + documento con
│                                               tabs", portado de PortalSAP_v2.
├── PortalSaas.Data/                         # Entities/ + PortalSaasDbContext.
│                                               Agnóstico de proveedor -- SIN paquetes
│                                               de Npgsql/SqlServer/Design.
├── PortalSaas.Data.Migrations.PostgreSql/   # DesignTimeDbContextFactory (UseNpgsql)
│                                               + Migrations (InitialCreate,
│                                               AddAuthenticationAndPreferences,
│                                               AddEmailSettings, AddOrganizationSlug,
│                                               AddPlatformAdmins,
│                                               FixCompanyOrganizationCascade,
│                                               AddCoreMenuAndPermissions,
│                                               SeedFixedActions,
│                                               AddPlanToOnPremiseLicense) + su propio
│                                               appsettings.Development.json.
├── PortalSaas.Data.Migrations.SqlServer/    # ídem, UseSqlServer. InitialCreate
│                                               consolidado (24 jul 2026) -- las 5
│                                               migraciones anteriores nunca llegaron
│                                               a aplicarse contra una base real, se
│                                               regeneraron limpias; desde ahí sigue
│                                               igual que Postgres (mismo nombre en
│                                               los dos: FixCompanyOrganizationCascade,
│                                               AddCoreMenuAndPermissions,
│                                               SeedFixedActions,
│                                               AddPlanToOnPremiseLicense).
└── PortalSaas.Core/                         # Implementación real. AHORA x64 (ver
    │                                           "Cambio de plataforma de build" abajo).
    ├── Seguridad/
    │   ├── SecretoCifradoService.cs          #   AES-256-GCM, portado tal cual.
    │   ├── PasswordHasher.cs                 #   PBKDF2-SHA256, portado tal cual.
    │   ├── AuthenticationService.cs          #   Nuevo -- login + bloqueo por intentos.
    │   ├── PlatformAdminAuthenticationService.cs #   Nuevo -- login del administrador
    │   │                                            de plataforma (sin organización).
    │   ├── PasswordResetService.cs           #   Nuevo -- recuperación por correo.
    │   ├── CurrentCompanyAccessor.cs         #   25 jul 2026, portado de PortalSAP_v2
    │   │                                            (CurrentEmpresaAccessor).
    │   └── CurrentUserContext.cs             #   25 jul 2026, portado -- HasActionAsync
    │                                                reescrito contra PortalSaasDbContext.
    ├── Usuarios/UserPreferenceService.cs      #   Nuevo -- preferencias personales.
    ├── Correo/                                #   Nuevo -- envío de correo dual
    │   ├── EmailSenderService.cs              #     (Google Workspace/Microsoft 365).
    │   ├── Microsoft365EmailSender.cs         #     Graph API (client credentials).
    │   ├── GoogleWorkspaceEmailSender.cs      #     Gmail API (cuenta de servicio +
    │   │                                            delegación de dominio).
    │   ├── MicrosoftGraphPayloadBuilder.cs    #     Puro, sin HTTP (testeable).
    │   ├── GmailMessageBuilder.cs             #     Puro, sin HTTP (testeable).
    │   └── GoogleServiceAccountJwtBuilder.cs  #     Puro, sin HTTP (testeable).
    ├── Sap/                                   #   Nuevo (25 jul 2026), portado de
    │   ├── HanaService.cs                     #     PortalSAP_v2 -- ver la entrada
    │   ├── SapConnectionProvider.cs           #     "Conector SAP" en Estado actual
    │   ├── SapSession.cs                      #     para el detalle completo de qué
    │   ├── HanaToSqlServerTranslator.cs       #     se portó/adaptó/difirió.
    │   ├── SapConnectionStringFactory.cs      #     Extraído de HanaService, compartido
    │   │                                            con SapConnectionTestService.
    │   └── SapConnectionTestService.cs        #     Nuevo, no portado -- botón "Probar
    │                                                conexión" en Companies/Index.
    ├── Infraestructura/PluginLoadContext.cs   #   AssemblyLoadContext aislado, portado
    │              PluginManager.cs            #   tal cual (bug de orden de versión ya
    │                                            corregido); AHORA cableado en
    │                                            Program.cs (antes no lo estaba).
    │              MenuSyncService.cs          #   Nuevo -- upsert de `menus` desde
    │                                            IModuloPortal.GetMenu(), con la fase
    │                                            de desactivación de huérfanos.
    │              SapSessionCache.cs          #   25 jul 2026, portado -- Singleton,
    │                                            cachea sesión SL por Company.Id.
    │              RowReflectionMapper.cs      #   25 jul 2026, portado tal cual
    │                                            (MapeadorFilaReflection).
    │              MenuTreeHelper.cs           #   25 jul 2026, nuevo -- anida una
    │                                            lista plana de MenuNodeDto en árbol
    │                                            real vía ParentMenuId (no lista plana
    │                                            + Nivel como PortalSAP_v2 -- acá el
    │                                            sidebar usa `collapse` nativo de
    │                                            Bootstrap 5, cada contenedor debe
    │                                            envolver exactamente a sus hijos).
    │              MenuNavigationService.cs    #   25 jul 2026, nuevo -- implementa
    │                                            IMenuNavigationService, reescrito
    │                                            contra PortalSaasDbContext (no HANA).
    │                                            Máximo 2 queries reales (menús activos
    │                                            + ids asignados vía un join), el resto
    │                                            (expansión de ancestros, armado del
    │                                            árbol) corre en memoria -- sin N+1.
    ├── Comercial/ContractLimitService.cs       #   Implementación real de
    │              OrganizationAccessGateService.cs #   IContractLimitService.
    │                                             Nuevo -- gate subscriptions (saas) /
    │                                             on_premise_licenses (on_premise).
    ├── Administracion/TenantUserAdminService.cs #  25 jul 2026, nuevo -- self-service
    │                                             de usuarios de la propia organización,
    │                                             consumido por plugins/Modulo.Administracion.
    ├── Catalogos/                              #   25 jul 2026, nuevo -- Customer/Item/
    │              CustomerCatalogService.cs     #   Warehouse/SalesEmployeeCatalogService,
    │              ItemCatalogService.cs         #   consumidos por Modulo.Ventas. Item solo
    │              WarehouseCatalogService.cs    #   busca (SearchAsync), nunca lista completo
    │              SalesEmployeeCatalogService.cs #  (OITM real con decenas de miles de filas).
    └── Ventas/                                 #   25 jul 2026, nuevo -- primer plugin de
           SalesOrderService.cs                  #   negocio real (habla con el SAP de la
           SapSalesOrderModels.cs                #   organización). Tabla HANA "ORDR" +
                                                    recurso Service Layer "Orders" fijos (un
                                                    solo tipo de documento, no el motor
                                                    multi-tipo GenericoVenta de la
                                                    referencia). SapSalesOrderHeader/Line
                                                    (wire model, internal, nunca expuesto
                                                    fuera de Core) fija U_PortalUser (UDF de
                                                    trazabilidad) al crear.

tests/
└── PortalSaas.Core.Tests/                    # xUnit + EF Core InMemory. AHORA x64
                                                 (referencia PortalSaas.Core). **96 tests,
                                                 todos en verde.** Incluye: builders
                                                 puros de correo (payload de Graph,
                                                 MIME de Gmail, JWT de cuenta de
                                                 servicio firmado y verificado con un
                                                 par RSA de prueba) y el despacho/
                                                 falla-cerrada de EmailSenderService.
                                                 Envío real por Gmail API VERIFICADO
                                                 de punta a punta contra un Workspace
                                                 real (24 jul 2026, entrega confirmada);
                                                 Microsoft Graph sigue sin verificar,
                                                 sin tenant de prueba disponible (ver
                                                 docs/06-...md §7).
```

Ver `docs/06-AUTENTICACION-Y-PREFERENCIAS.md` para el detalle completo de
autenticación/recuperación/preferencias/envío de correo (por qué el correo es
obligatorio, política de bloqueo, Google Workspace vs. Microsoft 365, qué falta a
propósito: 2FA real, verificación de correo).

```
src/PortalSaas.Host/                          # Primer ejecutable real del proyecto.
├── Program.cs                                 # DI de todo Core (motor dual, DOS
│                                                 esquemas de cookie -- tenant default
│                                                 + "PlatformAdmin" -- y el branch del
│                                                 comando `seed-admin`, ver abajo).
├── Comandos/PlatformAdminSeeder.cs             # `dotnet run -- seed-admin <correo>` --
│                                                 crea la primera cuenta de admin de
│                                                 plataforma, contraseña pedida por
│                                                 consola sin eco. No sobrescribe.
├── Pages/
│   ├── Index.cshtml(.cs)                      # Redirige según sesión.
│   ├── Account/                                # Login de TENANT (usuario dentro de
│   │   ├── Login.cshtml(.cs)                  #   una organización).
│   │   │                                        Slug + email/username + password.
│   │   ├── Logout.cshtml.cs
│   │   ├── ForgotPassword.cshtml(.cs)         # Junta IPasswordResetService +
│   │   │                                        IEmailSenderService -- flujo real.
│   │   ├── ResetPassword.cshtml(.cs)
│   │   └── SelectCompany.cshtml(.cs)          # Nuevo (25 jul 2026) -- segundo paso
│   │                                            del login, fija ICurrentCompanyAccessor
│   │                                            (claim CompanyId), solo si la
│   │                                            organización tiene compañías.
│   │                                            asp-antiforgery="true" agregado
│   │                                            (25 jul 2026, sidebar dinámico) -- el
│   │                                            <form method="post"> sin NINGÚN otro
│   │                                            atributo asp-* nunca tuvo el token
│   │                                            antiforgery inyectado (el FormTagHelper
│   │                                            solo lo agrega junto con asp-page/
│   │                                            asp-route-*/etc.) -- bug real, todo
│   │                                            envío devolvía 400, encontrado recién
│   │                                            al verificar el sidebar de punta a
│   │                                            punta con un usuario real.
│   ├── Home/
│   │   ├── Index.cshtml(.cs)                  # [Authorize] (esquema tenant).
│   │   └── Preferences.cshtml(.cs)            # [Authorize] IUserPreferenceService.
│   ├── Shared/Components/SidebarMenu/          # Nuevo (25 jul 2026) -- ViewComponent
│   │   ├── Default.cshtml                      #   del sidebar, ver ViewComponents/
│   │   └── _MenuNode.cshtml                     #   más abajo y "Sidebar dinámico" en
│   │                                              Estado actual para el detalle.
│   └── Admin/                                  # Backoffice del administrador de
│       │                                        plataforma -- actor nuevo, NO
│       │                                        pertenece a ninguna organización
│       │                                        (ver docs/03-...md §2). Esquema de
│       │                                        cookie "PlatformAdmin", sesión
│       │                                        totalmente aparte de /Account.
│       ├── _ViewStart.cshtml                  # Layout = "_AdminLayout" para toda
│       │                                        el área.
│       ├── Login.cshtml(.cs)                  # Solo correo+contraseña, sin slug --
│       │                                        el admin no pertenece a una org.
│       ├── Logout.cshtml(.cs)
│       ├── Organizations/                      # Alcance v1 (confirmado con el
│       │   ├── Index.cshtml(.cs)              #   dueño del proyecto): CRUD de
│       │   ├── Create.cshtml(.cs)              #   organizations, más gestión de
│       │   ├── Edit.cshtml(.cs)                #   users por organización (nuevo,
│       │   ├── Users/                          #   24 jul 2026) -- el admin de
│       │   │   ├── Index.cshtml(.cs)           #   plataforma crea el primer
│       │   │   ├── Create.cshtml(.cs)          #   usuario de cada cliente, no hay
│       │   │   │                                 autoregistro ni self-service
│       │   │   │                                 todavía.
│       │   │   └── Permissions.cshtml(.cs)     #   Nuevo (24 jul 2026) -- asigna
│       │   │                                     MenuGroups + Profile por nodo de
│       │   │                                     menú a un usuario, SIEMPRE por
│       │   │                                     Company (selector propio, la org
│       │   │                                     puede tener varias).
│       │   ├── Instances/                      # Nuevo (24 jul 2026) -- conexión
│       │   │   ├── Index.cshtml(.cs)           #   HANA/SQL Server del cliente
│       │   │   ├── Create.cshtml(.cs)          #   (Host/Port/EngineType/usuario
│       │   │   └── Edit.cshtml(.cs)            #   técnico). Clave cifrada
│       │   │                                     (ISecretoCifradoService),
│       │   │                                     write-only -- Edit la deja en
│       │   │                                     blanco para no cambiarla.
│       │   ├── Companies/                      # Nuevo (24 jul 2026) -- compañía/
│       │   │   ├── Index.cshtml(.cs)           #   schema SAP, cuelga de una
│       │   │   ├── Create.cshtml(.cs)          #   Instance de la misma org. Code
│       │   │   └── Edit.cshtml(.cs)            #   único GLOBAL (no por org, ver
│       │   │                                     PortalSaasDbContext). Mismo
│       │   │                                     patrón write-only de secreto.
│       │   │                                     Index tiene además (25 jul 2026)
│       │   │                                     el handler OnPostTestConnectionAsync
│       │   │                                     -- botón "Probar conexión" por fila,
│       │   │                                     ver ISapConnectionTestService.
│       │   ├── Subscriptions/                  # Asignar un plan a una
│       │   │   ├── Index.cshtml(.cs)           #   organización (historial de
│       │   │   ├── Create.cshtml(.cs)          #   subscriptions, no solo la
│       │   │   └── Edit.cshtml(.cs)            #   vigente) -- solo orgs modo "saas".
│       │   ├── Licenses/                       # Emitir licencia on-premise (nuevo,
│       │   │   ├── Index.cshtml(.cs)           #   24 jul 2026) -- solo orgs modo
│       │   │   ├── Create.cshtml(.cs)          #   "on_premise". Clave de activación
│       │   │   └── Edit.cshtml(.cs)            #   generada en servidor, nunca a mano.
│       │   └── EmailSettings/Index.cshtml(.cs) # Nuevo (25 jul 2026) -- 1:1 con
│       │                                          Organization, una sola página upsert
│       │                                          (no Create/Edit separadas). Secreto
│       │                                          del proveedor write-only, mismo
│       │                                          patrón que Instances/Companies.
│       ├── Plans/                              # Catálogo de planes (nuevo, 24 jul
│       │   ├── Index.cshtml(.cs)               #   2026) -- código, límites,
│       │   ├── Create.cshtml(.cs)              #   precio. Independiente de
│       │   └── Edit.cshtml(.cs)                #   organizations, referenciado
│       │                                         desde Subscriptions.
│       ├── Profiles/                            # Nuevo (24 jul 2026) -- CRUD de
│       │   ├── Index.cshtml(.cs)               #   Profile (GLOBAL a la
│       │   ├── Create.cshtml(.cs)              #   plataforma) + asignación de
│       │   └── Edit.cshtml(.cs)                #   PermissionAction (checkboxes,
│       │                                         catálogo fijo de 6) directo en
│       │                                         Edit -- sin página separada.
│       └── MenuGroups/                          # Nuevo (24 jul 2026) -- CRUD de
│           ├── Index.cshtml(.cs)               #   MenuGroup (GLOBAL) + asignación
│           ├── Create.cshtml(.cs)              #   de nodos Menu (checkboxes,
│           └── Edit.cshtml(.cs)                #   indentados por Level) directo
│                                                  en Edit. Vacío hasta que haya un
│                                                  plugin real cargado (`menus` sin
│                                                  filas todavía) -- estado vacío
│                                                  manejado explícitamente, no es
│                                                  un error.
└── appsettings.Development.json               # Database:Provider + ConnectionStrings,
                                                  igual criterio que los otros (dev-only,
                                                  versionado). Security:MasterSecretKey
                                                  SIEMPRE por user-secrets, nunca acá.
```

**Login resuelve la organización por `Slug`** (código corto, ej.
`comercial-depor`), campo nuevo en `organizations` — necesario porque
`username`/`email` son únicos solo DENTRO de una organización, no global (ver
`docs/03-...md` §1, nota sobre perfiles multi-organización evaluados y
descartados por ahora). **Esto es solo para el login de tenant** — el login de
administrador de plataforma (`/Admin/Login`) es un actor distinto y no pide
organización (ver `Pages/Admin/` arriba y `docs/03-...md` §2).

## Estado actual (24 jul 2026)

**Pasos 1-2 de `ARCHITECTURE.md` §6 — hecho, con motor dual, VERIFICADO contra los
dos motores reales (24 jul 2026).** Las tablas de la capa comercial + núcleo
(`docs/03-MODELO-CORE-COMERCIAL.md`) están aplicadas con éxito contra Postgres 16
(Docker) y SQL Server 2022 Express reales, 16 tablas en ambos. En el camino se
encontró y corrigió un bug real de compatibilidad entre motores: `companies` tenía
dos rutas de FK en cascada hacia `organizations` (directa, y vía `instances`) —
Postgres lo permite en silencio, SQL Server lo rechaza al crear la tabla (error 1785,
"may cause cycles or multiple cascade paths"). Corregido en `PortalSaasDbContext.cs`
(`Company.Organization` ahora `DeleteBehavior.Restrict`, ya alcanzable en cascada vía
`Instance`) — sin este fix el motor dual no funcionaba de verdad contra SQL Server
pese a compilar y generar migraciones sin error. **Generar la migración no prueba
nada; aplicarla contra el motor real sí** — antes de hoy solo se había hecho lo
primero.

**Paso 3 de `ARCHITECTURE.md` §6 — arrancado, parcial.** Se portaron de
`PortalSAP_v2` las piezas de `Abstractions`/`Core` que no dependen de HANA/SAP ni de
la tabla `menus` (todavía no existe): el contrato de plugin (`IModuloPortal`,
`MenuItemDefinition`), el cargador de plugins (`PluginLoadContext`/`PluginManager`,
con el bug de ordenamiento de versión de `PortalSAP_v2` ya corregido de una), el
cifrado de secretos (`SecretoCifradoService`, AES-256-GCM) y el hasher de contraseñas
(`PasswordHasher`, PBKDF2-SHA256). Se implementaron, nuevos (no existían en
`PortalSAP_v2`): `IContractLimitService`, autenticación con bloqueo por intentos
(`IAuthenticationService`), recuperación de contraseña por correo
(`IPasswordResetService`) y preferencias personales (`IUserPreferenceService`) — ver
`docs/06-AUTENTICACION-Y-PREFERENCIAS.md`. **45 tests reales pasando**
(`dotnet test`) — cumple la regla dura de este proyecto de no declarar un módulo
comercial terminado sin tests, a diferencia de la deuda de tests que sí tiene
`PortalSAP_v2`.

**Paso 4 de `ARCHITECTURE.md` §6 — hecho.** `PortalSaas.Host` arranca y sirve contra
Postgres real (Docker local, `docker compose up -d`): `/` redirige a
`/Account/Login` (302), `/Account/Login` renderiza (200), `/Home/Index` rechaza sin
sesión (302), y el flujo de administrador de plataforma (ver más abajo) se probó de
punta a punta con datos reales. El login POST de tenant, recuperación de contraseña
con correo real, y preferencias siguen sin probarse todavía (no hay un usuario de
organización de prueba creado aún, solo el administrador de plataforma).

**Backoffice de administrador de plataforma — nuevo (24 jul 2026), alcance v1.**
Actor nuevo que no existía en el modelo: `PlatformAdmin`, sin `organization_id`,
administra todas las `organizations` (no reabre "1 usuario = 1 organización" de
`docs/03-...md` §1 -- ver la nota agregada ahí mismo). Login propio en `/Admin/Login`
(solo correo+contraseña, sin slug), esquema de cookie `"PlatformAdmin"` separado del
de tenant (mismo proceso/`Host`, sesiones que nunca se pisan). Alcance de esta
entrega: CRUD de `organizations` únicamente (crear/listar/editar nombre, slug, país,
modo, estado) -- `plans`/`subscriptions`/`on_premise_licenses` ya existen como
entidades pero sin UI, quedan para una entrega posterior. Primera cuenta se crea con
`dotnet run --project src/PortalSaas.Host -- seed-admin <correo>` (sin autoregistro).
**45 tests pasando** (`PlatformAdminAuthenticationServiceTests` agregado, mismo
patrón que `AuthenticationServiceTests`). **Probado de punta a punta contra Postgres
real** (24 jul 2026): `seed-admin` creó la cuenta `jmunoz@comercialdepor.cl`, login
POST real en `/Admin/Login` funcionó, y la primera organización real
("Comercial Depor", slug `cl-depor`) se creó desde `/Admin/Organizations/Create` y
quedó persistida — confirmado con una consulta directa a la base. Se confirmó que
`/Account/*`/`/Home/*` siguen exactamente igual que antes (regresión cero).

**Gestión de usuarios por organización — nuevo (24 jul 2026).** No existía ninguna
forma de crear un `User` dentro de una `organization` (ni en el backoffice, ni
self-service). Se agregó `/Admin/Organizations/Users/Index` y `/Create` -- el admin
de plataforma crea usuarios para cualquier organización (usuario, correo,
contraseña, flag `IsAdmin` de la organización), mismo esquema `"PlatformAdmin"` y
mismas validaciones de unicidad dentro de la organización que ya exigía el modelo.
Con esto se creó el primer usuario real de "Comercial Depor" y se probó el login de
TENANT (`/Account/Login`, esquema de cookie default, resuelto por slug `cl-depor`)
de punta a punta contra Postgres real -- cierra el único hueco de runtime que
quedaba del Paso 4. `Security:MasterSecretKey` (requerido por
`SecretoCifradoService`, que la recuperación de contraseña toca en su cadena de DI)
también quedó seteado vía `dotnet user-secrets` para `PortalSaas.Host`.

**Recuperación de contraseña y preferencias — VERIFICADAS de punta a punta (24 jul
2026).** Sin `email_settings` configurado para "Comercial Depor" todavía, así que el
envío real de correo falla -- pero falla exactamente como está diseñado: en
silencio hacia el navegador (mismo mensaje siempre, anti-enumeración), con el error
solo en el log del servidor (`_logger.LogError` en `ForgotPassword.cshtml.cs`). El
token sí se genera y persiste (`password_reset_tokens`, hash SHA-256). Se probó
`ResetPasswordAsync` con un token válido inyectado directo en la base (mismo
algoritmo de hash que usa el servicio) -- la contraseña cambió de verdad, el login
con la clave vieja se rechazó, con la nueva funcionó. `IUserPreferenceService`
también verificado: `GET`/`POST` a `/Home/Preferences` persiste `theme`/`locale`/
`timezone`/`email_notifications_enabled` correctamente. **Con esto, todo el Paso 4
de `ARCHITECTURE.md` §6 queda probado en runtime contra una base real** -- ya no
queda ningún flujo de autenticación/preferencias sin verificar.

**Catálogo de planes + asignación a organización — nuevo (24 jul 2026).**
`/Admin/Plans` (CRUD de `plans`: código, nombre, límites de usuarios/compañías/
transacciones, precio, moneda, activo) y `/Admin/Organizations/Subscriptions`
(asigna un plan a una organización -- `subscriptions` es tabla de historial, no
un 1:1, así que Create agrega una fila nueva y Edit solo cambia estado/fecha de
fin/datos de pago de una fila existente, nunca el plan asignado). Mismo esquema
`"PlatformAdmin"` y mismo patrón que `Organizations`/`Users`. Probado de punta a
punta contra Postgres real con una cuenta de admin de prueba (creada y borrada
solo para el test, sin tocar la cuenta real): plan "Growth" creado, asignado a
"Comercial Depor" con estado `active`, editado a `past_due` con datos de pago --
todo persistido correctamente. **Nota de alcance**: a diferencia de
`AuthenticationService`/`PlatformAdminAuthenticationService`, estas páginas no
tienen tests xUnit dedicados -- es CRUD sin lógica de negocio computada (los
límites del plan todavía no se hacen cumplir en ningún lado; eso es trabajo de
`IContractLimitService`, que sí tiene tests, ver más abajo). Verificado solo con
las pruebas manuales de punta a punta de esta sesión. `on_premise_licenses` sigue
sin UI.

**Dos bugs reales encontrados y corregidos en esta misma sesión, ambos solo
visibles probando contra una base real (no en tests unitarios ni en compilación):**
1. `Subscriptions/Edit.cshtml.cs` usaba `DateTimeOffset.Parse` sobre la fecha de fin
   -- toma el offset de la zona horaria del servidor (Chile, `-04:00`), y Npgsql
   solo acepta escribir `timestamptz` con offset UTC (0). Corregido parseando como
   `DateOnly` y construyendo el `DateTimeOffset` con `TimeSpan.Zero` explícito.
2. **`Users/Create.cshtml.cs` no llamaba a `IContractLimitService.CheckUserLimitAsync`
   antes de crear el usuario** -- violaba directamente la regla dura "los límites de
   plan se hacen cumplir en código, no solo se documentan". Corregido: ahora
   bloquea la creación (mismo criterio "falla hacia lo más estricto") si no hay
   suscripción activa o si el límite del plan ya se alcanzó. Verificado de punta a
   punta: con un plan de límite 2 y una organización con 2 usuarios activos, el
   tercer intento se rechazó con el mensaje real del servicio ("Límite de usuarios
   del plan 'growth' alcanzado (2/2)."), sin crear la fila.

**Gate de acceso comercial + `on_premise_licenses` — nuevo (24 jul 2026).**
Se detectó que "Comercial Depor" (modo `on_premise`) no tenía NINGÚN gate comercial
activo -- no le corresponde `subscriptions` (eso es del modo `saas`, confirmado
contra `docs/03-...md` §5) y `on_premise_licenses` no tenía UI para crear la
primera licencia. Se agregó `IOrganizationAccessGateService`
(`PortalSaas.Core.Comercial`, con tests) -- según `organization.Mode`, exige
`subscriptions.status in (trial, active)` (saas) o
`on_premise_licenses.status = active` con `expires_at` futuro (on_premise); sin
fila de ninguna, deniega siempre (mismo criterio "falla hacia lo más estricto").
Se cablea en `/Account/Login` DESPUÉS de validar la contraseña (nunca antes, anti-
enumeración). `/Admin/Organizations/Licenses` (Index/Create/Edit, mismo patrón que
Subscriptions) -- la clave de activación la genera el servidor (32 bytes
aleatorios), nunca la elige el admin. En `Organizations/Index` el link muestra
"Licencia" o "Suscripción" según el modo de cada organización, para no poder
asignar el gate equivocado desde la UI. **Verificado de punta a punta contra
Postgres real**: login de `ti` se bloqueó ("Esta instalación no tiene una licencia
asignada") antes de emitir la licencia, y funcionó después de emitirla desde el
backoffice.

**Núcleo heredado de PORTALWEB (menú/perfiles/acciones/auditoría) — nuevo (24 jul
2026), portado.** Las 9 tablas (`menu_groups`, `menus`, `profiles`, `actions`,
`profile_actions`, `menu_group_items`, `user_menu_groups`, `user_menu_profiles`,
`audit_logs`) están modeladas, migradas y **aplicadas contra Postgres y SQL Server
reales** (25 tablas totales en cada motor). GLOBALES a la plataforma (sin
`organization_id` propio, ver docs/03-...md §3) -- el scope real por organización
lo dan `UserMenuGroup`/`UserMenuProfile` (usuario + compañía). Catálogo fijo de
`actions` sembrado vía `HasData` (VIEW/CREATE/EDIT/DELETE/APPROVE/EXPORT, mismos
valores que `PortalActions` en Abstractions). **Segundo bug real de rutas de
cascada encontrado y evitado esta vez ANTES de aplicar** (mismo error 1785 de SQL
Server que `Company.Organization`, ver más arriba): `UserMenuGroup.Company` y
`UserMenuProfile.Company` también son alcanzables en cascada por dos caminos
(directo y vía `User`) -- ambos configurados `DeleteBehavior.Restrict` de una,
igual que el FK autorreferencial `Menu.ParentMenu` (SQL Server rechaza cascada
autorreferencial directamente). `MenuSyncService`
(`PortalSaas.Core.Infraestructura`, con 7 tests) hace upsert de `menus` desde
`IModuloPortal.GetMenu()` de cada plugin cargado (clave `(OriginModule, Code)`,
resuelve `ParentCode` calificado como `"OtroModulo.Codigo"` para colgarse de un
nodo de otro módulo, desactiva nodos de un módulo que ya no está cargado) --
portado de PortalSAP_v2 con la misma fase de desactivación de huérfanos que ese
proyecto ya tuvo que corregir como bug real. **`PluginManager` queda cableado en
`Program.cs`** (antes no lo estaba, ni un solo `DiscoverAndLoad` se llamaba en
ningún lado) -- corre antes de `builder.Build()` (mismo motivo que
`ApplicationPartManager`: `IModuloPortal.RegisterServices` necesita
`IServiceCollection` mutable), con un `IServiceProvider` transitorio de un solo
uso para resolver `PortalSaasDbContext` y correr `MenuSyncService.SyncAsync`
después de cargar los plugins. **Probado de punta a punta**: con
`artifacts/plugins/` inexistente (no hay plugins reales en este proyecto
todavía), el Host arranca limpio, loguea la advertencia esperada, corre una
consulta SQL real contra `menus` (confirmado en el log), y el resto de la app
sigue sin regresión. **Alcance explícitamente NO cerrado en esta entrega** (para
no sobre-construir sin un plugin real que lo ejercite): el filtrado de
visibilidad de menú por módulos contratados de la organización
(`organization_modules`) -- hoy el árbol de menú es el mismo para todas las
organizaciones, sin relación todavía con qué módulos tiene contratados cada una.
La UI de administración de `menu_groups`/`profiles`/`actions` (asignar
perfiles/grupos a usuarios) se cerró en la entrega siguiente, ver abajo.

**UI de perfiles/permisos de menú + Instance/Company — nuevo (24 jul 2026).**
Cierra el hueco que dejó abierto la entrega anterior: hasta acá las tablas
núcleo (`profiles`/`actions`/`menu_groups`/`menus`) existían y se sincronizaban,
pero no había ninguna pantalla para asignarlas. Se agregó:
- `/Admin/Profiles` (CRUD de `Profile`, GLOBAL a la plataforma) con asignación de
  `PermissionAction` (checkboxes del catálogo fijo de 6) directo en `Edit` --
  sin página separada, mismo criterio que `MenuGroups`.
- `/Admin/MenuGroups` (CRUD de `MenuGroup`, GLOBAL) con asignación de nodos
  `Menu` (checkboxes indentados por `Level`) directo en `Edit`. Con `menus`
  todavía vacío (sin plugins reales cargados) muestra un estado vacío explícito
  en vez de una lista en blanco sin explicación.
- `/Admin/Organizations/Users/Permissions` -- asigna `UserMenuGroup` y
  `UserMenuProfile` (perfil por nodo de menú final) a un usuario. Ambas tablas
  llevan `CompanyId` (el acceso varía por compañía dentro de la misma
  organización), así que la página exige elegir una `Company` primero.

**Eso reveló que no existía NINGUNA UI para `instances`/`companies`** (la
conexión al SAP de cada organización) -- sin al menos una `Company` no hay
`CompanyId` que asignarle a un usuario. Se agregó también, mismo alcance:
- `/Admin/Organizations/Instances` (CRUD de `Instance` -- host/puerto/motor
  HANA o SQL Server/usuario técnico). Clave cifrada con
  `ISecretoCifradoService` (ya usado por `EmailSenderService`), patrón
  write-only: el campo de clave en `Edit` se deja en blanco para no cambiarla,
  igual que ya hacía `Licenses` con la clave de activación.
- `/Admin/Organizations/Companies` (CRUD de `Company`, cuelga de una
  `Instance` de la misma organización). `Code` es único GLOBAL, no por
  organización (así lo define `PortalSaasDbContext` desde el modelo original).
  Mismo patrón write-only para `IntegrationSecretKey`.

**Bug real encontrado y corregido en las 17 validaciones de nombre/código
duplicado de todo el backoffice** (14 archivos, 6 de entregas anteriores a
hoy): `ModelState.AddModelError(nameof(Input.X), mensaje)` usa como clave
solo `"X"`, no `"Input.X"` -- `nameof` de un acceso a miembro devuelve
únicamente el último identificador, nunca la ruta calificada. Como
`asp-validation-for="Input.X"` busca la clave `"Input.X"`, el mensaje nunca se
mostraba -- el duplicado SÍ se bloqueaba (`ModelState.IsValid` es `false` sin
importar la clave), pero el usuario no veía ningún motivo, solo el formulario
recargado en silencio. Nadie lo había notado porque las pruebas manuales de
sesiones anteriores probaron sobre todo el camino feliz. Corregido en los 14
archivos con `$"{nameof(Input)}.{nameof(Input.X)}"`. **Verificado de punta a
punta contra Postgres real**: antes del fix, crear una `Instance` duplicada
devolvía `field-validation-valid` (mensaje vacío); después del fix,
`field-validation-error` con el mensaje real.

**Hueco de `ContractLimitService` para organizaciones `on_premise` --
CORREGIDO (25 jul 2026).** El hueco detectado en la entrega anterior
(`Check*LimitAsync` solo miraba `Subscriptions`, así que una organización
`on_premise` bien configurada -- licencia, sin suscripción -- quedaba
bloqueada para siempre en `Users/Create` y cualquier otra operación con
límite) se cerró agregando `PlanId`/`Plan` a `OnPremiseLicense` (mismo patrón
que `Subscription.PlanId`, FK a `plans`). `ContractLimitService.GetActivePlanAsync`
ahora rama por `Organization.Mode` -- `saas` sigue mirando `Subscriptions`,
`on_premise` mira la licencia `Active` vigente (`ExpiresAt` futuro) más
reciente -- mismo criterio "por modo" que ya usaba
`IOrganizationAccessGateService` para el gate de acceso. `/Admin/Organizations/Licenses`
(`Create`/`Edit`) ahora exige elegir un `Plan` al emitir/editar una licencia
(antes no lo pedía). **Migración con backfill real**: la única licencia
existente (`Comercial Depor`) no tenía `plan_id` -- la migración
(`AddPlanToOnPremiseLicense`, los dos motores) agrega la columna NOT NULL y
hace `UPDATE ... SET plan_id = (plan más antiguo existente)` para las filas
previas a la migración misma, antes de crear el FK -- sin esto la migración
fallaba contra Postgres real (violación NOT NULL/FK) apenas se aplicaba
contra una base con datos, otro caso de "generar la migración no prueba
nada". Aplicada con éxito contra Postgres y SQL Server reales. 6 tests nuevos
(`ContractLimitServiceTests`, rama `on_premise`: sin licencia, licencia
vigente, límite alcanzado, revocada, expirada) -- **68/68 tests en verde**.
**Verificado de punta a punta contra Postgres real**: organización
`on_premise` de prueba + plan con `UserLimit=1` + licencia -- el primer
usuario se creó (antes esto SIEMPRE fallaba), el segundo se bloqueó con el
mensaje real del límite ("Límite de usuarios del plan 'e2e-op-1user'
alcanzado (1/1)."), datos de prueba borrados al terminar.

**Verificado de punta a punta contra Postgres real** (organización, instancia,
compañía y usuario de prueba, creados y borrados solo para esta verificación,
sin tocar datos reales): secretos cifrados en la base (no en texto plano),
patrón write-only confirmado (dejar la clave en blanco en `Edit` no la
cambia), asignación de `UserMenuGroup` persiste y se puede revertir
(desmarcar la casilla borra la fila), asignación de `ProfileAction` persiste.
**62/62 tests siguen en verde** -- sin tests xUnit nuevos dedicados (mismo
criterio que `Plans`/`Subscriptions`: es CRUD sin lógica de negocio computada,
la única lógica real -- cifrado de secretos -- ya la cubren los tests
existentes de `SecretoCifradoService`).

**Cómo generar/aplicar migraciones** (después de cambiar algo en
`src/PortalSaas.Data/Entities/` o `PortalSaasDbContext.cs`, regenerar **las dos**):
```
# PostgreSQL
dotnet tool run dotnet-ef migrations add <Nombre> \
  --project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj \
  --startup-project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj \
  --context PortalSaasDbContext

# SQL Server
dotnet tool run dotnet-ef migrations add <Nombre> \
  --project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj \
  --startup-project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj \
  --context PortalSaasDbContext
```
`dotnet ef database update` con los mismos flags `--project`/`--startup-project`
(sin `migrations add <Nombre>`) aplica la migración contra la base real.

**Cómo levantar el entorno de desarrollo Postgres** (tareas en `.vscode/tasks.json`,
Ctrl+Shift+P → "Run Task"):
```
docker compose up -d       # Postgres local (puerto 5432)
dotnet tool restore         # instala dotnet-ef (versionado en dotnet-tools.json)
```
La cadena de conexión de cada motor sale de su propio
`appsettings.Development.json` (`ConnectionStrings:Default`) — nunca hardcodeada en
C#. Son solo credenciales de desarrollo local (coinciden con `docker-compose.yml`),
la única excepción versionada a la regla de "nunca credenciales en texto plano".
Override posible vía variable de entorno `PORTALSAAS_ConnectionStrings__Default`.

**Comercial Depor no necesita instalar Postgres en ningún lado**: su instalación usa
el motor SQL Server (`Database:Provider = "sqlserver"`) contra la base que ya existe
en `sqlsap.cdepor.cl` — cero infraestructura nueva. Postgres es para la vía SaaS
(nube), cuando exista el primer cliente externo real.

**`.vscode/launch.json`**: ya tiene la configuración `"PortalSaas.Host"` (F5 lanza el
Host, abre el navegador en `/Account/Login` cuando el puerto queda listo), siguiendo
el mismo patrón que `referencia-original/PortalSAP_v2/.vscode/launch.json`.

**Conector SAP (`HanaService`/`SapConnectionProvider`/`CurrentCompanyAccessor`/
`CurrentUserContext`) — nuevo (25 jul 2026), portado de `PortalSAP_v2`.** Cierra
`ARCHITECTURE.md` §6 paso 5. Portado casi tal cual, ver el detalle de qué se portó, qué
se adaptó y qué se difirió a propósito (`ISqlServerService`) en el reporte de esta
entrega -- resumen:
- `IHanaService`/`HanaService` (`PortalSaas.Core.Sap`): wrapper multi-motor de SQL
  directo contra la compañía SAP activa -- HANA nativo (`Sap.Data.Hana`) o, si la
  `Instance` de la compañía es `sqlserver`, el mismo SQL en dialecto HANA traducido con
  `HanaToSqlServerTranslator` (portado tal cual de `TraductorSqlHanaASqlServer`, 5
  patrones exactos, con sus tests) y ejecutado con `Microsoft.Data.SqlClient`. A
  diferencia de `PortalSAP_v2` (resolvía `Company`/`Instance` con SQL crudo contra
  `PORTALWEB.EMPRESA`/`INSTANCIA` en HANA vía `EmpresaRepositorio`), acá esas dos tablas
  ya viven en la base propia de la plataforma -- se resuelven con una consulta EF Core
  directa contra `PortalSaasDbContext`, sin repositorio intermedio.
- `ISapConnectionProvider`/`SapConnectionProvider` + `ISapSession`/`SapSession`
  (adaptador de `B1SLayer.SLConnection`) + `ISapSessionCache`/`SapSessionCache`
  (`Singleton`, cachea la sesión de Service Layer por `Company.Id`): portados tal cual.
- `ICurrentCompanyAccessor`/`CurrentCompanyAccessor` (`PortalSaas.Core.Seguridad`):
  portado, renombrado "Empresa" -> "Company". Lee claims fijados en el login, compañía
  fija por sesión (cambiarla exige logout/login, igual que `PortalSAP_v2`).
- `ICurrentUserContext`/`CurrentUserContext`: contrato portado tal cual;
  `HasActionAsync` se **reescribió contra `PortalSaasDbContext`**
  (`UserMenuProfile`+`ProfileAction`+`Menu`+`PermissionAction`, ya modelados) en vez de
  SQL crudo a HANA -- la autorización del portal no debería depender de que el SAP del
  cliente esté disponible. `IsAdmin` bypasea todo, mismo criterio que
  `ES_ADMINISTRADOR` en el original.
- **Ya no diferido** -- resuelto 26 jul 2026 como `IExternalDatabaseConnectionService`,
  ver la entrega "Conexión a bases de datos externas de plugin" más abajo (primer
  consumidor real: `Modulo.Rendiciones`, repo externo).

**Hueco real encontrado y cerrado en el camino: el login de tenant nunca seleccionaba
compañía.** `Pages/Account/Login.cshtml.cs` resolvía `Organization`+`User` y entraba
directo -- sin esto `ICurrentCompanyAccessor` no tenía nada que resolver. Se agregó
`Pages/Account/SelectCompany.cshtml(.cs)`, segundo paso del login (solo si la
organización tiene 1+ `Companies` activas; si tiene 0, se entra directo, sin claim de
compañía -- las funciones que dependen de SAP simplemente no están disponibles). Fija
los claims `CompanyId`/`CompanyCode`/`CompanyDatabase`/`CompanyServiceLayerUrl`/
`CompanyCountry` recién ahí (no en `Login.cshtml.cs`, que ya firmó el `SignInAsync` con
`IsAdmin` pero sin compañía) -- si el usuario ya tiene el claim `CompanyId`, la página
redirige directo a `/Home/Index` sin dejar re-elegir, mismo criterio de
"cambiar de compañía exige logout/login" que `PortalSAP_v2`. Chequeo de acceso portado
tal cual: `User.IsAdmin` bypasea; si no, exige al menos una fila en `UserMenuGroup` o
`UserMenuProfile` para `(UserId, CompanyId)`, si no hay ninguna se rechaza.

**Cambio de plataforma de build a x64 (`PortalSaas.Core`/`Host`/`Core.Tests`).** El
cliente nativo de HANA (`Sap.Data.Hana.Net.v8.0.dll`, copiado a un `Lib/` nuevo en la
raíz del repo, referenciado por `HintPath` -- **no es NuGet**, requiere el cliente HANA
instalado en la máquina para conectar en runtime, ver
`C:\Program Files\sap\hdbclient\dotnetcore\v8.0\`) es x64-only, mismo criterio que
`PortalSAP_v2` (todos sus `.csproj` con dependencia nativa fijan
`PlatformTarget=x64`). Se fijó en los 3 `.csproj` + se actualizó `PortalSaas.sln`
(`ProjectConfigurationPlatforms`) para que **todas** las configuraciones de solución
(`Any CPU`/`x64`/`x86`) mapeen esos 3 proyectos a `x64` -- así `dotnet build`/`dotnet
test` sin flags extra siguen funcionando igual que antes. `Abstractions`/`Data`/
`Data.Migrations.*` quedan en `Any CPU`, no tocan HANA. **Verificado**: `dotnet build`
(0 warnings/errores, outputs en `bin\x64\Debug\net8.0\` para los 3 proyectos afectados)
y el Host arranca y sirve normal contra Postgres real con la referencia nativa cargada.

**`ISapConnectionTestService` — nuevo (25 jul 2026), agregado sobre la marcha.**
Sugerencia del dueño del proyecto al configurar 2 organizaciones demo reales
("Comercial Depor" contra un HANA real, "Comercial GE2" contra un SQL Server real):
un botón "Probar conexión" directo en `/Admin/Organizations/Companies` (`Index`), en
vez de una herramienta de consola aparte. `ISapConnectionTestService`/
`SapConnectionTestService` (`PortalSaas.Core.Sap`) prueba las dos rutas por
separado -- base directa (mismo camino que `HanaService`, factorizado en
`SapConnectionStringFactory` para no duplicarlo) y Service Layer (mismo camino que
`SapConnectionProvider`, sin pasar por el cache) -- **sin depender de
`ICurrentCompanyAccessor`** (recibe `companyId` explícito), porque el backoffice de
plataforma no tiene sesión de tenant. A diferencia de `HanaService`/
`SapConnectionProvider` (uso interno, nunca deben filtrar el detalle de una excepción a
un usuario final), acá el mensaje de error real SÍ se muestra tal cual -- es
exactamente lo que un administrador necesita para diagnosticar una compañía mal
configurada, y la pantalla es admin-only.

**Verificado de punta a punta contra los 2 ambientes demo reales que configuró el
dueño del proyecto, los dos motores -- ya no aplica el límite de "no hay HANA/SQL
Server de un SAP real disponible" (25 jul 2026):**
- **Comercial Depor (HANA real, `HW-DEPOR-HDB:30015`, compañía `DEPOR_QA`)**: base de
  datos **OK** y Service Layer **OK** desde el primer intento -- confirma que el
  cliente nativo de HANA (`Sap.Data.Hana.Net.v8.0.dll`) carga y conecta de verdad en
  runtime, no solo que compila.
- **Comercial GE2 (SQL Server real, compañía `BLOCK_QA`/`TEST_GE2`)**: primer intento
  -- Service Layer **OK**, base de datos **FALLÓ** (`Login failed for user
  'app_suc'`, el usuario técnico de la `Instance` sin acceso otorgado a `TEST_GE2` --
  exactamente el tipo de error de configuración que este botón está pensado para
  exponer, no un bug del conector). Corregidos los permisos SQL del lado del
  servidor demo, **re-verificado con éxito**: base de datos **OK** y Service Layer
  **OK**.

**Los dos ambientes demo reales (HANA y SQL Server) quedan confirmados end-to-end.**

**18 tests nuevos** (`HanaToSqlServerTranslatorTests`, portados de
`TraductorSqlHanaASqlServerTests`; `CurrentCompanyAccessorTests`;
`CurrentUserContextTests`, incluye que el acceso en una compañía no se filtra a otra
compañía de la misma organización) -- **86/86 tests en verde**. **Verificado de punta a
punta contra Postgres real** (organización, instancia, 2 compañías y 2 usuarios de
prueba -- uno sin acceso, uno admin -- creados y borrados solo para esto): login sin
compañías asignadas entra directo; con compañías, redirige a `SelectCompany`; usuario
sin `UserMenuGroup`/`UserMenuProfile` se rechaza con "No tienes acceso a esa
compañía."; con una fila de acceso real, funciona y el claim `CompanyId` queda fijo
(revisitar `SelectCompany` redirige derecho, no deja re-elegir); usuario `IsAdmin`
funciona sin necesitar ninguna fila de acceso.

**`Modulo.Administracion` -- primer plugin real cargado en runtime, nuevo (25 jul
2026).** Cierra `ARCHITECTURE.md` §6 paso 6 en la parte que faltaba probar: hasta acá
`PluginManager`/`PluginLoadContext`/`MenuSyncService` solo se habían verificado con
`artifacts/plugins/` vacío -- era la única pieza de la arquitectura de plugins que
seguía siendo solo teoría. `plugins/Modulo.Administracion/` (`ModuleCode =
"Administracion"`) es un self-service de **usuarios de la propia organización** para el
admin de un tenant (`ICurrentUserContext.IsAdmin`, esquema de cookie default/tenant, NO
`"PlatformAdmin"`) -- alcance deliberadamente reducido, decisión confirmada con el dueño
del proyecto vía pregunta explícita antes de portar nada: en `PortalSAP_v2` (mono-tenant)
este módulo cubría Usuarios/Grupos/Perfiles/Menús/Instancias/Empresas, pero acá
`Menu`/`MenuGroup`/`Profile` son catálogos **globales** de la plataforma (docs/03 §3) y
`Instance`/`Company` llevan credenciales técnicas de conexión SAP -- darle ese alcance
completo a cualquier admin de organización cliente permitiría editar catálogos
compartidos por TODAS las organizaciones o ver/rotar credenciales SAP ajenas. Grupos/
Perfiles/Instancias/Empresas siguen siendo exclusivos de `/Admin/*` (operador de
plataforma); este plugin solo LEE esos catálogos globales (para poder asignarlos a un
usuario por compañía vía `UserMenuGroup`/`UserMenuProfile`), nunca los escribe.

- **`ITenantUserAdminService`** (`PortalSaas.Abstractions.Contratos`, implementado en
  `PortalSaas.Core.Administracion.TenantUserAdminService` contra `PortalSaasDbContext`,
  registrado en el Host) -- mirror acotado a la organización actual
  (`ICurrentUserContext.OrganizationId`, propiedad nueva agregada a ese contrato,
  lee el claim `"OrganizationId"` ya fijado en el login) de la lógica ya probada en
  `/Admin/Organizations/Users/Create.cshtml.cs` y `Permissions.cshtml.cs` -- mismas
  reglas (límite de plan vía `IContractLimitService.CheckUserLimitAsync`, unicidad de
  username/email DENTRO de la organización) más una nueva, propia de self-service:
  **auto-bloqueo** -- un admin de organización no puede quitarse a sí mismo `IsAdmin`/
  `IsActive` ni eliminarse, para no dejar la organización sin ningún admin funcional.
  `SavePermissionsAsync`/`GetPermissionsAsync` validan que `userId`/`companyId`
  pertenezcan a la organización actual antes de tocar nada -- nunca confían en un id
  recibido de la UI.
- **Rutas bajo `/organizacion/usuarios`, NO `/admin/usuarios`** (a diferencia de la
  referencia) -- evita mezclar conceptualmente esta superficie de self-service de
  tenant con `/Admin/*` (backoffice del operador de plataforma), aunque el routing de
  ASP.NET Core sea case-insensitive. Al cerrar esta entrega no existía sidebar dinámico
  todavía (`_Layout.cshtml` era un navbar estático) -- se agregó a mano un link
  condicional "Administración" (`User.HasClaim("IsAdmin", "True")`) como único punto
  de entrada. **Ese link se eliminó en la entrega "Sidebar dinámico" (ver más abajo)**
  -- ahora aparece solo, vía el árbol real de `menus`.
- **Empaquetado de plugin verificado de punta a punta, no solo en teoría**: el target
  `PublicarComoPlugin` (`AfterTargets="Build"`, mismo patrón que `PortalSAP_v2`) copia
  el output del build a `artifacts/plugins/Modulo.Administracion/1.0.0/` -- la carpeta
  + nombre de DLL (`Modulo.Administracion.dll`) ES el manifiesto que `PluginManager`
  espera, sin archivo de manifiesto aparte. **Hueco real encontrado y cerrado**: sin
  configurar nada más, `Program.cs` busca por defecto en
  `AppContext.BaseDirectory/artifacts/plugins` (dentro de `bin\x64\Debug\net8.0\` del
  Host), una carpeta que nunca existe -- a diferencia de `PortalSAP_v2` (sube buscando
  el `.sln`), este proyecto simplificó esa resolución a propósito. Se fijó
  `Plugins:ArtifactsFolder` explícito en `appsettings.Development.json` del Host (dev-
  only, versionado, mismo criterio ya usado para `ConnectionStrings`) apuntando a
  `artifacts/plugins` en la raíz del repo.
- **Segundo hueco real encontrado durante la verificación E2E, no de diseño sino de
  comando de build**: `dotnet run --no-build` sobre `PortalSaas.Host.csproj` sin pasar
  por el `.sln` resuelve por defecto a `bin\Debug\net8.0\PortalSaas.Host.exe` (carpeta
  AnyCPU), NO a `bin\x64\Debug\net8.0\` -- con un `.exe` viejo todavía presente ahí de
  antes del cambio a x64, el Host arrancó cargando una copia **obsolesa** de
  `PortalSaas.Abstractions.dll` (sin los DTOs nuevos), y crasheó con
  `ReflectionTypeLoadException`/`TypeLoadException` al mapear las Razor Pages del
  plugin. Bin/obj limpiados por completo y reconstruidos vía `dotnet build
  PortalSaas.sln`; para levantar el Host manualmente contra el build x64 hay que
  ejecutar el `.dll` de esa carpeta directo (`dotnet
  bin/x64/Debug/net8.0/PortalSaas.Host.dll`), no `dotnet run --no-build` a secas.
- **Verificado de punta a punta contra Postgres real** (organización + plan + usuario
  admin de prueba, creados y borrados solo para esto, contraseña hasheada con el mismo
  PBKDF2-SHA256 de `PasswordHasher`): Host arrancó, logueó "Módulo Administracion
  v1.0.0 cargado (2 entradas de menú)" y sincronizó las 2 filas en `menus`
  (`origin_module = 'Administracion'`); login de tenant por slug funcionó, el link
  "Administración" apareció en el navbar; `/organizacion/usuarios` listó el usuario
  real; crear un segundo usuario con `plans.user_limit = 1` se bloqueó con el mensaje
  real del servicio ("...alcanzado (1/1)."); subiendo el límite a 2, la creación
  funcionó (redirect a la página de edición del usuario nuevo); el intento de
  auto-bloqueo (el propio admin tratando de sacarse `IsAdmin`) se rechazó con el
  mensaje real ("No podés quitarte a vos mismo el acceso de administrador ni
  desactivarte."). Aislamiento entre organizaciones verificado con **10 tests xUnit
  nuevos** (`TenantUserAdminServiceTests`, EF Core InMemory) en vez de manualmente --
  **96/96 tests en verde**: mismo username en dos organizaciones distintas no
  colisiona, `ListAsync` nunca devuelve usuarios de otra organización,
  `GetPermissionsAsync`/`SavePermissionsAsync` rechazan un `userId`/`companyId` que no
  pertenezca a la organización actual.

**Sidebar dinámico del shell de tenant — nuevo (25 jul 2026).** Cierra el hueco dejado
por `Modulo.Administracion`: `Pages/Shared/_Layout.cshtml` (shell de tenant, esquema de
cookie default -- `_AdminLayout.cshtml`/`/Admin/*` NO se tocó) pasó de un navbar
horizontal estático a un sidebar izquierdo + topbar real, con el árbol de `menus`
sincronizado renderizado de verdad -- cualquier plugin nuevo aparece solo, sin volver a
tocar `_Layout.cshtml`. Confirmado con el dueño del proyecto que el modelo de sidebar
de `PortalSAP_v2` (`referencia-original/`) no se debía perder, pero **sin** portar su
sistema completo de temas (`theme.css` de 1800 líneas + FontAwesome + selector de 5
temas) -- alcance acotado a estructura + comportamiento, sobre Bootstrap 5 (variables
`--bs-*`, ya usado en todo el proyecto) y **Bootstrap Icons** (vendored nuevo en
`wwwroot/lib/bootstrap-icons/`, mismo criterio manual que `bootstrap`/`jquery`, sin
`libman.json` en este repo) en vez de FontAwesome.

- **`IMenuNavigationService`/`MenuNavigationService`** (`PortalSaas.Core.Infraestructura`)
  -- árbol de `menus` ya filtrado y ANIDADO (no una lista plana con `Nivel` para
  indentar, como el `MenuNodoDto` original de `PortalSAP_v2`) para el usuario del
  request actual: administradores ven el árbol activo completo (mismo bypass que
  `HasActionAsync`); el resto, solo los nodos que su `MenuGroup` tenga asignado
  (`UserMenuGroup` por compañía) más sus carpetas ancestro (expandidas en memoria, para
  que la carpeta contenedora aparezca aunque no esté asignada ella misma). Sin compañía
  activa (`ICurrentCompanyAccessor.HasCompany`, propiedad nueva agregada al contrato),
  un usuario no-admin no puede tener ninguna fila de `UserMenuGroup` (siempre lleva
  `CompanyId`) -- el árbol queda vacío, mismo criterio ya establecido de "sin compañía,
  las funciones que dependen de eso simplemente no están disponibles". **Sin N+1**: como
  máximo 2 consultas reales sin importar el tamaño del árbol (menús activos + ids
  asignados vía un solo join) -- expansión de ancestros y armado del árbol
  (`MenuTreeHelper.BuildTree`) corren enteramente en memoria sobre la lista ya cargada.
- **Árbol anidado, no plano** -- decisión tomada explícitamente para esta entrega: el
  sidebar usa el componente `collapse` **nativo** de Bootstrap 5
  (`data-bs-toggle="collapse"` + `bootstrap.Collapse` de `bootstrap.bundle.min.js`, ya
  vendored), donde cada `<div class="collapse">` debe envolver exactamente a sus hijos
  -- no alcanza con una lista plana indentada por nivel (el truco de
  `data-grupos`/`[data-grupos~="id"]` de `sidebar.js` original, pensado para mostrar/
  ocultar con `style.display`, no aplica a un componente Bootstrap real). `MenuNodeDto`
  lleva `Children` (lista mutable, poblada por `MenuTreeHelper.BuildTree`) en vez de
  `Nivel`; `Pages/Shared/Components/SidebarMenu/_MenuNode.cshtml` se recorre a sí misma
  recursivamente (vía `<partial>`) para cualquier profundidad de árbol, sin límite de
  niveles.
- **`SidebarMenuViewComponent`** (`PortalSaas.Host.ViewComponents`, primer
  ViewComponent de este proyecto) -- inyecta `IMenuNavigationService`, expone la vista
  en `Pages/Shared/Components/SidebarMenu/Default.cshtml`, invocado desde
  `_Layout.cshtml` vía `@await Component.InvokeAsync("SidebarMenu")`.
- **`sidebar.js`** (nuevo, `wwwroot/js/`) -- NO oculta/muestra nada a mano: usa
  `bootstrap.Collapse.getOrCreateInstance(...).show()/hide()` y escucha
  `shown.bs.collapse`/`hidden.bs.collapse` solo para persistir en `localStorage` qué
  grupos quedaron expandidos (recarga de página los restaura). El chevron que rota
  no tiene clase propia -- CSS puro sobre `[aria-expanded="true"]`, que Bootstrap ya
  gestiona automáticamente en el toggle. Aparte, modo "solo iconos" del sidebar
  completo (`data-sidebar="collapsed"` en `<html>`, aplicado antes de pintar para evitar
  parpadeo, mismo truco que el selector de temas de `PortalSAP_v2` pero sin temas).
- **`sidebar.css`** (nuevo) -- adaptación de las ~430 líneas de `.app-shell`/`.sidebar`/
  `.topbar`/`.user-menu*` de `theme.css` (referencia) a variables de Bootstrap
  (`--bs-border-color`, `--bs-secondary-bg`, `--bs-primary`, etc.) en vez de las propias
  del sistema de temas original.
- **Dos bugs reales encontrados y corregidos en la verificación E2E, ninguno visible
  compilando ni en `dotnet test`:**
  1. `<partial name="_MenuNode" model="child" />` (sin ruta) tiraba
     `InvalidOperationException: The partial view '_MenuNode' was not found` apenas se
     invocaba el sidebar desde una página FUERA de `Pages/Shared/` (ej.
     `/Account/SelectCompany`) -- la búsqueda por nombre simple de `<partial>` dentro de
     la vista de un `ViewComponent` resuelve relativo a la página que lo invocó, no a la
     carpeta del `ViewComponent`. Corregido con ruta app-relativa explícita
     (`~/Pages/Shared/Components/SidebarMenu/_MenuNode.cshtml`) en los dos lugares que
     la referencian (`Default.cshtml` y la propia `_MenuNode.cshtml`, para su
     recursión).
  2. `Pages/Account/SelectCompany.cshtml` -- el `<form method="post">` del selector de
     compañía nunca tuvo NINGÚN atributo `asp-*`, así que el `FormTagHelper` nunca le
     inyectó el token antiforgery (solo lo hace junto con `asp-page`/`asp-route-*`/etc.)
     -- **cualquier envío real de ese formulario, con o sin este cambio de sidebar,
     devolvía 400** ("antiforgery token no encontrado"). Bug preexistente, no introducido
     por esta entrega, pero recién visible al hacer un POST real de punta a punta.
     Corregido agregando `asp-antiforgery="true"` explícito.
- **Verificado de punta a punta contra Postgres real** (organización "Comercial Depor",
  usuario admin real + un usuario no-admin de prueba creado/borrado solo para esto,
  mismo patrón de toda la sesión): login completo (incluido el paso `SelectCompany`, ya
  con el fix de antiforgery) muestra el árbol completo para el admin (bypass, sin
  necesitar ninguna fila de `UserMenuGroup`); un usuario no-admin con un `MenuGroup`
  vacío asignado entra pero ve el sidebar sin "Administración" (solo "Inicio"); agregando
  el nodo hoja "Usuarios" a ese mismo grupo (sin volver a loguear) el nodo aparece de
  inmediato junto con su carpeta "Administración" (expansión de ancestros, sin estar
  asignada ella misma). `id`/`href` de los `collapse` de Bootstrap coinciden 1:1 entre el
  toggle y su contenedor. `/Admin/Login` (`_AdminLayout.cshtml`) confirmado sin
  regresión. **96/96 tests siguen en verde** -- sin tests xUnit nuevos dedicados (mismo
  criterio que otras entregas de solo-rendering: la lógica de filtrado/expansión de
  `MenuNavigationService` no tiene condicionales de negocio nuevos que ameriten cobertura
  aparte de lo ya verificado manualmente end-to-end).

**UI de administración de `email_settings` — nuevo (25 jul 2026).** Hueco real
detectado al intentar recuperar la contraseña de un usuario real de "Comercial Depor":
el flujo de `/Account/ForgotPassword` fallaba en silencio (por diseño anti-enumeración)
porque no existía NINGUNA forma de cargar `email_settings` salvo SQL directo -- no
había pantalla de administración, pese a que `EmailSenderService`/`IEmailSenderService`
ya estaban completos y probados desde antes. Se agregó
`/Admin/Organizations/EmailSettings/Index` (`PortalSaas.Host.Pages.Admin.Organizations.EmailSettings`)
-- **una sola página que hace upsert**, no Create/Edit separadas como el resto del
backoffice, porque `email_settings` es 1:1 con `Organization` (no hay historial). Mismo
patrón write-only que `Instances`/`Companies` para el secreto: los campos de
credenciales del proveedor (Google Workspace: `ClientEmail`/`PrivateKeyPem`; Microsoft
365: `TenantId`/`ClientId`/`ClientSecret`) nunca se vuelven a mostrar una vez guardados
-- dejarlos en blanco al editar no toca la configuración cifrada existente; completar
CUALQUIER campo del proveedor seleccionado exige completar TODOS los de ese proveedor
(no se puede actualizar un campo suelto sin poder descifrar el resto del JSON). Cambiar
de proveedor siempre exige cargar la config nueva completa. El JSON armado usa records
locales (`GoogleWorkspaceConfigInput`/`Microsoft365ConfigInput`, con los mismos
`[JsonPropertyName]` camelCase exactos que ya esperan `GoogleWorkspaceEmailSender`/
`Microsoft365EmailSender` al descifrar) en vez de referenciar los tipos `internal` de
`PortalSaas.Core.Correo` -- evita agregar `InternalsVisibleTo("PortalSaas.Host")` solo
para esto. Link "Correo" agregado a `Organizations/Index.cshtml` junto a
Usuarios/Instancias/Compañías/Licencia-Suscripción.

**Dos bugs reales encontrados en la verificación E2E, ninguno visible compilando ni en
`dotnet test`:** (1) mismo problema que `SelectCompany.cshtml` (ver "Sidebar dinámico"
más arriba) -- el `<form method="post">` sin ningún otro atributo `asp-*` no dispara la
inyección automática del token antiforgery del `FormTagHelper`; corregido con
`asp-antiforgery="true"` explícito. (2) La carpeta de la página (`Pages/Admin/
Organizations/EmailSettings/`) coincide EXACTAMENTE con el nombre de la entidad
`PortalSaas.Data.Entities.EmailSettings` -- dentro del namespace generado
`...Organizations.EmailSettings`, una referencia sin calificar a `EmailSettings` como
tipo (ej. `new EmailSettings { ... }`) es ambigua con el namespace propio y no compila
(`CS0118`). Resuelto calificando esa única construcción con
`global::PortalSaas.Data.Entities.EmailSettings`.

**Verificado de punta a punta contra Postgres real** (admin de plataforma de prueba +
"Comercial Depor", credenciales de Google Workspace de prueba, creados y borrados solo
para esto): página vacía muestra "todavía no tiene un proveedor configurado"; guardar
con los campos de Google Workspace incompletos bloquea con los 2 mensajes reales;
guardando completo, `email_settings.encrypted_provider_config` queda cifrado (no
legible, no JSON plano) en la base; reabrir la página muestra los campos de credenciales
en blanco; editar solo `SenderDisplayName` (dejando los campos de Google en blanco) deja
`encrypted_provider_config` bit a bit intacto (mismo largo/prefijo). **96/96 tests siguen
en verde** -- sin tests xUnit nuevos dedicados (mismo criterio que otras entregas de
CRUD sin lógica de negocio computada, ya cubierto por los tests existentes de
`SecretoCifradoService` para el cifrado en sí).

**Comercial Depor configurado con credenciales reales de Google Workspace y VERIFICADO
de punta a punta (25 jul 2026)** -- cuenta de servicio real (`no-repli@portalweb-
503418.iam.gserviceaccount.com`, delegación de dominio para `no-reply@comercialdepor.cl`)
cargada vía la UI nueva. `/Account/ForgotPassword` real para un usuario real de la
organización: el log del Host confirma `POST oauth2.googleapis.com/token` → 200 (JWT de
la cuenta de servicio aceptado) y `POST gmail.googleapis.com/.../messages/send` → 200 --
el correo de recuperación llegó de verdad a la bandeja real (confirmado por el dueño del
proyecto, contenido exacto del template). **Ya no queda ninguna organización sin
recuperación de contraseña funcional** -- cierra el hueco detectado al reactivar el
usuario `ti` con un reset manual por SQL al principio de esta sesión.

**`Modulo.Ventas` — primer plugin de negocio real, habla de verdad con el SAP de una
organización (25 jul 2026).** Hasta esta entrega, todo lo construido operaba solo
contra la base propia de la plataforma (`Modulo.Administracion` incluido) -- el
conector SAP estaba verificado en aislamiento (botón "Probar conexión") pero ningún
plugin lo usaba todavía para una operación de negocio real. Se portó **Órdenes de
Venta (digitación directa)** desde `PortalSAP_v2` (`plugins/Modulo.Ventas` +
`GenericoVentaService`), con el alcance **deliberadamente recortado** respecto al
original (motor `GenericoVenta` con 7 tipos de documento):

- **Un solo tipo de documento** (`SalesOrder`) -- nada de la abstracción multi-tipo con
  diccionario de configuración por tipo; con un solo documento real esa capa no compra
  nada todavía (se generaliza cuando haya un segundo documento real, mismo criterio
  YAGNI que rige el resto del proyecto).
- **Solo líneas de Artículo** (sin Servicio) y **solo tabs General + Contenido** (sin
  Logística/Finanzas) -- evita portar 6 catálogos SAP más
  (`ICuentaContableCatalogoService`/`IDimensionCatalogoService`/`IEmpleadoCatalogoService`/
  `IShippingMethodCatalogService`/`IPaymentTermsCatalogService`/`IPriceListService`) que
  no hacen falta todavía. `DocumentFormViewModel.LogisticsView`/`AccountingView` en
  `null` oculta la tab -- es el mecanismo previsto, no un hack.
- **Sin importador CSV de líneas, sin toggle runtime de "permite crear"** -- ninguno de
  los dos existía tampoco como pantalla de administración en este proyecto.
- **Catálogos mínimos reales portados**: `ICustomerCatalogService` (OCRD),
  `IItemCatalogService` (OITM, **solo búsqueda en vivo, nunca listado completo** -- la
  tabla real de un cliente confirmó tener decenas de miles de artículos),
  `IWarehouseCatalogService` (OWHS), `ISalesEmployeeCatalogService` (OSLP).

**Convención de nombres**: inglés para todo el vocabulario de negocio nuevo
(`SalesOrder`, `Customer`, `Item`...), confirmado con el dueño del proyecto -- mismo
criterio que ya aplicó el resto de este proyecto. El nombre del plugin (`Modulo.Ventas`)
mantiene el patrón `Modulo.` + área en español, igual que `Modulo.Administracion` (es
convención de carpeta/proyecto, no vocabulario de negocio).

- **`PortalSaas.Abstractions/Componentes/`** (nuevo) -- `DocumentListViewModel`/
  `DocumentFormViewModel`, port directo del chrome compartido "SAP B1 Web Client"
  (listado con filtros+paginación, documento con tabs) de la referencia. Reusable por
  cualquier plugin futuro que necesite esta UI.
- **`SidebarMenuViewComponent`/`DocumentListViewComponent`/`DocumentFormViewComponent`**
  (`PortalSaas.Host.ViewComponents`) -- wrappers de 2 líneas, mismo patrón. Vistas en
  `Pages/Shared/Components/DocumentList|DocumentForm/Default.cshtml`, **sin ninguna
  clase de `theme.css`** (no existe en este proyecto) -- Bootstrap puro, mismo criterio
  que el sidebar dinámico.
- **`ISalesOrderService`/`SalesOrderService`** (`PortalSaas.Core.Ventas`) -- tabla HANA
  `"ORDR"` y recurso Service Layer `"Orders"` fijos (no diccionario de configuración).
  `SapSalesOrderHeader`/`SapSalesOrderLine` (wire model interno, nunca expuesto fuera de
  Core) fija `U_PortalUser` (UDF de trazabilidad) al crear.
- **`plugins/Modulo.Ventas/`** -- mismo patrón exacto que `Modulo.Administracion`
  (`.csproj` x64, única `ProjectReference` a Abstractions, target `PublicarComoPlugin`).
  `Pages/SalesOrders/Index` (listado) + `Detail` (crear/ver, sin `UpdateAsync` todavía --
  digitación directa, una vez creada la orden es de solo lectura vía el portal, editarla
  de verdad se hace directo en SAP). Búsqueda en vivo de artículos vía un handler AJAX
  (`OnGetSearchItemsAsync`) + `<datalist>` nativo, sin librería externa.

**Dos bugs reales encontrados y corregidos en la verificación, ninguno visible
compilando ni en `dotnet test`:**
1. `@for (var page = 1; page <= ...)` en `DocumentList/Default.cshtml` -- Razor
   interpreta el token literal `@page` como la **directiva** de página apenas lo ve en
   modo expresión dentro de markup, aunque sea solo el nombre de una variable de un
   `for`, no una referencia real a la directiva. Corregido renombrando la variable a
   `pageNumber` (y el helper `PageUrl` a `BuildPageUrl`, por consistencia).
2. Ninguno nuevo de antiforgery/resolución de partials en esta entrega -- las lecciones
   de las dos entregas anteriores (`~/Pages/...` explícito para partials cruzando
   ensamblados, `asp-antiforgery="true"` explícito en formularios sin otro atributo
   `asp-*`) ya se aplicaron desde el primer intento.

**Verificado de punta a punta contra los 2 ambientes demo reales, no solo compilando**:
- **Comercial GE2 (SQL Server, compañía BLOCK_QA)**: listado real -- muestra órdenes
  reales existentes en el SAP del cliente (clientes reales, totales reales en formato
  local, estado Abierto/Cerrado real, `LEFT JOIN "OSLP"` para el vendedor). Búsqueda en
  vivo de artículos contra el catálogo real (decenas de miles de SKU reales, ej.
  zapatillas Vans). **Creación real de una Orden de Venta nueva** (cliente real,
  artículo real, almacén real) -- `POST` a Service Layer devolvió un `DocEntry` real
  (611, siguiente al último existente), la orden se pudo releer (`GET Orders(611)`,
  status "Abierto", cliente/artículo/N° referencia correctos) y aparece en el listado.
  El UDF `U_PortalUser` no bloqueó la creación (ya existía en el ambiente o SAP lo
  ignoró en silencio por no ser un campo reconocido -- de cualquier modo, no hubo que
  crearlo a mano para esta prueba). **Queda una Orden de Venta real (DocEntry 611,
  N° referencia "TEST-CLAUDE-E2E") en el ambiente demo BLOCK_QA** -- no se intentó
  borrar (no hay flujo seguro de cancelación construido todavía); si se quiere limpiar,
  hacerlo directo en SAP.
- **Comercial Depor (HANA, compañía DEPOR_QA)**: `HanaConnection.Open()` falló con
  timeout de red real (`hw-depor-hdb:30015` no respondió) -- confirma que el código
  llega correctamente hasta el intento de conexión real (mismo camino que ya prueba
  `SapConnectionTestService`), pero el servidor demo HANA no estaba alcanzable en el
  momento de la prueba (VPN/red, no un bug de este plugin) -- pendiente reintentar.
- **Autorización**: un usuario admin (`IsAdmin`) ve/crea sin necesitar ninguna fila de
  `UserMenuProfile` (bypass, mismo criterio de siempre). Un usuario no-admin con acceso
  a la compañía pero SIN perfil para el menú `Ventas.ordenes` fue bloqueado
  (`HasActionAsync` en falso → `Forbid()` → redirect a `/Account/Login`, mismo
  comportamiento ya usado por `Modulo.Administracion`).

**96/96 tests siguen en verde** -- sin tests xUnit nuevos dedicados (mismo criterio que
otras entregas de esta sesión: la lógica nueva es CRUD/mapeo sin condicionales de
negocio complejos, ya verificado manualmente end-to-end contra los 2 SAP reales).

**Bug real encontrado y corregido probando `Modulo.Ventas` con el dueño del proyecto en
vivo (25 jul 2026) -- crash en TODO submit del formulario de Orden de Venta.**
`DetailModel.LineInput.Quantity`/`DiscountPercent` (`plugins/Modulo.Ventas/Pages/SalesOrders/Detail.cshtml.cs`)
eran `decimal` no-nullable, pero `_TabContent.cshtml` siempre renderiza `Lines.Count + 9`
filas en blanco (para no necesitar un botón "Agregar línea" con JS, ver el comentario en
ese archivo) -- esas filas mandan `""` para esos dos campos. El model binding de
ASP.NET Core rechaza un string vacío contra un `decimal` no-nullable con el mensaje
genérico `"The value '' is invalid."`, **antes** de que corra el código de
`OnPostAsync` que descarta las líneas sin `ItemCode` -- por eso aparecía un error por
cada campo numérico vacío de las filas extra, sin importar que las líneas reales
estuvieran bien cargadas. Corregido: los dos campos pasan a `decimal?` (mismo criterio
que ya tenía `UnitPrice`), con `?? 0` al construir el `SalesOrderLineDto` y
`line.Quantity is null or <= 0` en la validación. **Verificado de punta a punta contra
Comercial GE2 (SQL Server, BLOCK_QA) real**: con el fix, la búsqueda en vivo de
artículos funcionó (catálogo real) y se creó la **Orden de Venta N° 612** de verdad en
SAP -- confirma que el resto del flujo (catálogos, `SalesOrderService.CreateAsync`,
autorización) seguía intacto, el único problema era este bug de binding.

**Segundo hallazgo, no es un bug de código sino del comando usado para levantar el
Host manualmente**: `dotnet <ruta>/PortalSaas.Host.dll` ejecutado desde la raíz del
repo (en vez de `dotnet run`/F5) crashea con `"Falta ConnectionStrings:Default en la
configuración"` aunque `appsettings.Development.json` exista y esté completo --
`WebApplicationBuilder` resuelve el content root contra el **directorio de trabajo
actual**, no contra la carpeta del ensamblado, así que busca `appsettings.Development.json`
en la raíz del repo (donde no existe) en vez de en `bin/x64/Debug/net8.0/`. Solución:
pararse **dentro** de esa carpeta antes de ejecutar (`cd
src/PortalSaas.Host/bin/x64/Debug/net8.0 && dotnet ./PortalSaas.Host.dll`) con
`ASPNETCORE_ENVIRONMENT=Development` seteado -- ahí sí carga el archivo correcto. Ya
documentado acá para no repetir el diagnóstico la próxima vez que haga falta levantar
el Host fuera de `dotnet run`/F5.

**Actualización 26 jul 2026: líneas de tipo Servicio YA se portaron para Venta y
Compra** (no para Inventario, no aplica -- ver la entrega "Rediseño visual + brechas
funcionales" más abajo para el detalle real). El hallazgo de diseño original que sigue
abajo se conserva tal cual porque documenta el mapeo de campos SAP investigado en su
momento contra la referencia -- la implementación real terminó usando ese mismo mapeo
(AccountCode/CostingCode, DocType de cabecera), con Dimensión2/Dimensión3 deliberadamente
fuera de alcance (a diferencia de lo que sigue describiendo abajo como "opcionales
pendientes de portar").

**Pendiente real, NO portado a propósito en esta entrega: líneas de tipo Servicio.**
El dueño del proyecto pidió confirmar primero cómo lo resolvía `PortalSAP_v2` antes de
decidir si portarlo ahora -- investigado contra `referencia-original/PortalSAP_v2`
(nunca el repo real). Hallazgo clave: **el toggle Artículo/Servicio NO es por línea, es
por documento completo** -- SAP B1 no permite mezclar líneas de Artículo y Servicio en
la misma Orden de Venta (`DocType` es un campo de **encabezado**:
`dDocument_Items`/`dDocument_Service`, resuelto en el original por
`GenericoVentaService.ResolverDocType` mirando el `Tipo` de la primera línea, con el
comentario "el portal siempre digita documentos homogéneos"). El selector real vive en
el tab General (`_TabGeneral.cshtml` del original, un `<select>` a nivel de
documento), no en cada fila -- cada fila del original solo lleva un
`<input type="hidden" class="tipo-linea-valor">` sincronizado con ese selector global
(hay un bug ya corregido ahí documentado: si ese input queda deshabilitado o fuera del
POST, una línea de Servicio se guarda en silencio como Artículo).
Campos SAP reales por tipo (`GenericoVentaService.ArmarLineasNuevas`): Artículo ->
`ItemCode`+`WarehouseCode`; Servicio -> `AccountCode` (Cuenta Mayor) +
`CostingCode`/`CostingCode2`/`CostingCode3` (Centro de Costos obligatorio,
Dimensión2/3 opcionales), sin almacén. Dos catálogos SAP nuevos que haría falta portar:
`ICuentaContableCatalogoService` (`SELECT "AcctCode","AcctName" FROM "OACT"`) e
`IDimensionCatalogoService` (`SELECT "PrcCode","PrcName" FROM "OPRC" WHERE "DimCode" =
:dimCode AND "Locked" = 'N'` -- el mapeo de `DimCode` es específico del ambiente
original, 1=Centro de Costo/2=Marca/5=Tipo de Gasto, habría que reconfirmarlo contra
Comercial Depor/GE2 antes de portar, no asumirlo). Validación condicional también
documentada en el original (`DetalleGenericoVentaModelBase.ValidarLinea`): Servicio no
pide Almacén pero sí Descripción+Cuenta Mayor+Centro de Costos; Artículo exige
Artículo+Almacén. Se deja acá el diseño completo para no tener que
re-investigarlo cuando se decida portarlo -- decisión explícita del dueño del proyecto
de cerrar esta entrega solo con líneas de Artículo (mismo criterio YAGNI del resto del
proyecto).

**Nota (26 jul 2026): el párrafo "Todavía no existe" de arriba quedó desactualizado por
las entregas `feat(motores-genericos)` (`3194eea`) y `docs(gobernanza)` (`a400082`)** --
`GenericoCompra`/`GenericoInventario` **sí existen** hoy (`Modulo.Compras`/
`Modulo.Inventario`, motor de Venta generalizado a 7 tipos), esta sección nunca se
actualizó para reflejarlo cuando se agregaron. Queda el texto original tal cual arriba
por trazabilidad histórica del alcance con el que se cerró `Modulo.Ventas`, pero para el
estado real de los 3 motores genéricos ver la entrega "Motores genéricos de documento"
más abajo. Lo que sigue siendo cierto sin cambios: líneas de tipo Servicio, motor de
aprobación, importador CSV, override de "permite crear" por organización, y el filtrado
del árbol de menú por `organization_modules` -- ninguno existe todavía.

## Motores genéricos de documento (Venta 7 tipos, Compra, Inventario) -- 25/26 jul 2026

Generaliza el motor de Venta (antes solo Orden de Venta hardcodeada, ver la entrega
anterior) al patrón diccionario-por-tipo del original -- `ISalesDocumentService` +
`SalesDocumentTypeCatalog`, 7 tipos (`SalesOrder`/`CreditNote`/`CustomerInvoice`/
`ReserveInvoice`/`ReturnRequest`/`Return`/`Receipt`). Agrega `Modulo.Compras`
(`PurchaseQuotation`/`PurchaseOrder`) y `Modulo.Inventario`
(`InventoryTransferRequest`/`StockTransfer`) con el mismo patrón: catálogo estático por
tipo, PageModel base compartido por plugin (`IndexGeneric*ModelBase`/
`DetailGeneric*ModelBase`, sin métodos `virtual` a propósito), tabs reusadas entre
subtipos. Alcance recortado igual que `Modulo.Ventas`: sin importador CSV, sin motor de
aprobación, sin líneas de Servicio, sin override de "permite crear" por organización.

**Auditoría de código contra la referencia (pre-E2E, 26 jul 2026)** -- antes de probar
Compra/Inventario contra SAP real, se releyó línea por línea `GenericoCompraService.cs`/
`GenericoCompraSapModels.cs`/`GenericoInventarioService.cs`/
`GenericoInventarioSapModels.cs` en `referencia-original/PortalSAP_v2` contra el código
propio. Inventario ya cumplía las 3 reglas duras confirmadas en la entrega anterior
(`StockTransferLines` no `DocumentLines`, sin `DocDueDate`, almacén origen/destino por
línea) -- sin cambios. **Compra tenía un bug real, no detectado hasta esta auditoría**:
faltaba el campo `RequriedDate` de cabecera (nombre real de Service Layer, SAP tiene un
typo documentado -- letras invertidas respecto a `RequiredDate`, que sí es el nombre
correcto por línea) -- sin este campo, **Service Layer rechaza la creación de toda Oferta/
Pedido de Compra** con `"Specify the required date [OINV.ReqDate]"`, aunque `DocDueDate`
ya viniera seteado (error real ya documentado en el original, causa raíz no obvia --
confirmado con un log de diagnóstico ahí, no una suposición). Corregido en
`SapPurchaseDocumentModels.cs` (`RequriedDate` en cabecera, `RequiredDate` en línea) y
`PurchaseDocumentService.CreateAsync` (ambos se postean con el mismo valor de
`DocDueDate`, igual criterio que el original -- "el portal no captura una fecha distinta
por línea"). De paso se encontró que `PurchaseDocumentDto` no tenía `TaxDate` (el
original captura 3 fechas de cabecera -- `DocDate`/`DocDueDate`/`TaxDate` -- en los dos
motores, Venta ya las tenía las 3, Compra solo 2) -- agregado a `PurchaseDocumentDto`,
`SapPurchaseDocumentHeader`, `PurchaseDocumentService` y al formulario
(`DetailGenericPurchaseDocumentModelBase`/`_TabGeneral.cshtml`, mismas 3 columnas y
mismas etiquetas que ya usa Venta) -- regla de paridad entre motores del `CLAUDE.md`
aplicada hacia adentro del propio proyecto, no solo contra el original.

**Hallazgo de diseño, no corregido, dejado documentado para decisión explícita**: el
catálogo de Compra (`PurchaseDocumentTypeCatalog`) deja `PurchaseOrder` (Pedido) con
`DefaultCanCreate = true` -- en el original ese tipo es `PermiteCrear = false`, nace
**siempre** de un Copy-From automático de una Oferta aprobada, reforzado además por un
Transaction Notification a nivel de base de datos SAP que rechaza cualquier Pedido que
no venga de ese Copy-From. Acá se dejó en `true` a propósito, documentado ya en el
comentario del catálogo ("sin el flujo de aprobación que en el original generaba el
Pedido a partir de la Oferta") -- es coherente con no haber portado el motor de
aprobación todavía, pero implica un riesgo concreto para la verificación E2E pendiente:
**si el SAP real de Comercial GE2/Comercial Depor tiene ese Transaction Notification
instalado, crear un Pedido directo desde el portal será rechazado por SAP** aunque el
JSON esté bien formado -- confirmar esto antes de darlo por probado, no asumir que un
`POST` exitoso a `PurchaseOrders` en un ambiente demo sin ese control significa que
funcionará igual en un ambiente con aprobación real configurada. (Al revés, la
inconsistencia encontrada entre la prosa del `CLAUDE.md` del original -- que dice
Traslado/`StockTransfer` es "solo lectura" -- y su código real
(`GenericoInventarioService.cs`, `PermiteCrear = true` para los 2 tipos, con un
comentario explícito de que el cliente pidió digitación en blanco igual) se resolvió a
favor del código: nuestro `InventoryDocumentTypeCatalog` ya tenía `true` para los 2
tipos, coincide con el comportamiento real, no hace falta cambiar nada ahí.)

Generado `payloads_prueba_sap.json` (raíz del repo, no versionado a propósito -- es un
scratch de desarrollo) con los payloads reales `POST` de los 4 documentos (Oferta/
Pedido de Compra, Solicitud de Traslado/Traslado) ya reflejando los mapeos corregidos,
para probar manualmente contra Service Layer con Postman/curl antes o en paralelo a la
verificación E2E vía la UI.

**Fix visual de la entrega anterior, reforzado**: `.document-list-table` en
`site.css` tenía `width: auto; min-width: 50%` -- el `min-width: 50%` todavía dejaba
huecos grandes en tablas con pocas columnas de contenido corto (Inventario, o Notas de
Crédito con pocas filas) en pantallas anchas, porque fuerza un ancho mínimo como
fracción del contenedor en vez de dejar que el ancho salga solo del contenido real.
Quitado el `min-width`, queda solo `width: auto`. **Verificado por inspección de
código/markup y `dotnet build`, NO en navegador** (sin herramienta de captura de
pantalla disponible en esta sesión) -- confirmar visualmente contra `/ventas/notas-
credito` con datos reales antes de dar el punto por cerrado del todo.

**Checklist de migración de producción actualizado**: `docs/05-RUNBOOK-PRODUCCION.md`
quedó escrito contra un estado más viejo del esquema ("una sola migración,
`InitialCreate`, 11 tablas") -- hoy son 4 migraciones del lado SQL Server (`InitialCreate`
consolidado con 15 tablas, `AddCoreMenuAndPermissions` con 9 más, `SeedFixedActions`
solo datos, `AddPlanToOnPremiseLicense`) / 9 del lado Postgres, 24 tablas de negocio
totales. Se agregó `docs/05B-CHECKLIST-MIGRACION-SQLSAP.md` (no se reescribió el 05,
sus pasos 1-3/6 de alta de base/usuario/secretos/organización siguen vigentes tal
cual) con la lista real de migraciones, el registro de las 3 incompatibilidades
Postgres/SQL Server ya encontradas y corregidas en el modelo (rutas de cascada
múltiples -- error SQL Server 1785, `NOT NULL`+FK sobre tabla con datos sin backfill
previo, `DateTimeOffset` con offset no-UTC contra Npgsql), caveats generales a vigilar
en migraciones futuras (precisión de `decimal` sin especificar, mismo criterio de
"nunca `HasDefaultValueSql`"), y los pasos + queries de verificación post-aplicación
contra `sqlsap.cdepor.cl` cuando se decida ejecutarlo -- **sigue sin ejecutarse
todavía**, es guía, no un registro de que ya se hizo.

**`dotnet build PortalSaas.sln` en 0 advertencias/0 errores y las 96 pruebas de
`PortalSaas.Core.Tests` en verde después de cada cambio de esta entrega** -- sin tests
xUnit nuevos dedicados (mismo criterio que otras entregas de esta sesión: los fixes son
de mapeo/payload contra Service Layer, no lógica de negocio nueva con condicionales
propios). **Pendiente real, no cerrado en esta entrega**: la verificación E2E de Compra
e Inventario contra SAP real (Comercial GE2/Comercial Depor) -- esta sesión fue
auditoría de código "pre-E2E" explícitamente (sin acceso a los ambientes SAP reales
desde este entorno), no reemplaza probar la creación real de una Oferta de Compra y una
Solicitud de Traslado de punta a punta, con especial atención al hallazgo de
`PurchaseOrder`/Transaction Notification de arriba.

## Rediseño visual + 3 brechas del backlog funcional -- 26 jul 2026

Entrega grande, cuatro piezas independientes en el mismo pase: identidad visual nueva y
tres ítems de `docs/08-BRECHA-FUNCIONAL-VS-PORTALSAP-V2.md`. `dotnet build` en 0
advertencias/0 errores y `dotnet test` en verde después de cada pieza (**107/107** al
cierre, 11 tests nuevos: 4 de `OrganizationDocumentPermissionServiceTests` + 6 de
`ModuleAccessServiceTests`, más uno que ya contaba). Sin verificación E2E contra SAP
real ni en navegador real en esta entrega (mismas limitaciones que la auditoría
anterior) -- ver el detalle de qué falta confirmar en cada pieza.

**1. Identidad visual corporativa** (`site.css`/`sidebar.css`) -- paleta del sistema
anterior aplicada vía variables `--bs-*` (Bootstrap 5.1 vendored acá SÍ consume
`var(--bs-primary-rgb)` en `.bg-primary`/`.text-primary`/`.table`, pero **no** en
`.btn-primary`/`.page-link`/navbar -- confirmado leyendo `bootstrap.min.css` directo,
no asumido -- esos quedaron con reglas explícitas aparte). Azul institucional
`#0A2540` en la topbar del shell de tenant y en el navbar de `/Admin/*`
(`.navbar.bg-dark`, que Bootstrap 5.1 compila a un gris fijo sin variable), encabezados
de `.document-list-table` y `.btn-primary` (con `border-radius: 6px` + transición);
gris de fondo `#F4F6F9`/texto `#1E293B` vía `--bs-body-bg`/`--bs-body-color`; verde
`#10B981`/ámbar `#F59E0B` vía `--bs-success-rgb`/`--bs-warning-rgb` (recogidos gratis
por los badges de estado ya existentes, `bg-success`/`bg-warning`, sin tocarlos). El
`min-width: 50%` de la entrega anterior (`.document-list-table`) se sacó del todo --
seguía dejando huecos grandes en tablas angostas en pantallas anchas, el ancho debe
salir solo del contenido. **Sin verificación visual en navegador** (sin herramienta de
captura de pantalla en esta sesión) -- confirmar contra el shell de tenant y
`/Admin/Organizations/Index` reales antes de darlo por cerrado del todo.

**2. Líneas de tipo Servicio en Venta y Compra** (`DocumentLineType`, nuevo enum en
`Abstractions/Modelos/`) -- **no en Inventario**, decisión explícita: `OWTQ`/`OWTR`
(traslado entre almacenes) no tienen concepto de línea de Servicio en SAP, solo mueven
artículos físicos -- la regla de paridad entre motores dice "considerar", no "aplicar
literal sin pensar", y acá la particularidad real de Inventario (sin cliente/vendedor,
tampoco tipo de línea) la justifica. Campos reales confirmados contra
`referencia-original/PortalSAP_v2` (`GenericoVentaSapModels.cs`/
`GenericoCompraSapModels.cs`): `ItemType` ("itItems"/"itService") por línea,
`AccountCode`/`CostingCode` (Cuenta Mayor/Centro de Costos, solo Servicio),
`DocType` ("dDocument_Items"/"dDocument_Service") de CABECERA -- SAP no permite mezclar
Artículo y Servicio en el mismo documento. **Diferencia deliberada de diseño respecto al
original**: acá el tipo se elige una sola vez por documento (`Input.LineType`, un único
`<select>` en la tab General) y se aplica igual a todas las líneas al construir el DTO
en `OnPostAsync` -- el original sincroniza un campo oculto por línea vía JS
(`aplicarTipoDocumento` en `_TabContenido.cshtml`, con un bug ya corregido ahí de
líneas que se guardaban mal si el campo quedaba deshabilitado); acá no hace falta JS
nuevo porque no hay "Agregar línea" dinámico (mismo criterio ya establecido: filas
extra en blanco pre-renderizadas). Las columnas de Cuenta Mayor/Centro de Costos quedan
SIEMPRE visibles en la tabla de líneas (sin JS de mostrar/ocultar, a diferencia del
original) -- el servidor ignora los campos que no correspondan al tipo elegido, mismo
criterio de simpleza que el resto del proyecto. Dos catálogos nuevos, ambos
`PortalSaas.Core.Catalogos/`: `IGeneralLedgerAccountCatalogService` (OACT completo, sin
paginar -- plan de cuentas típicamente acotado, a diferencia de Artículo) e
`ICostCenterCatalogService` (OPRC filtrado a `DimCode = 1`, **advertencia ya heredada de
la referencia**: ese mapeo de DimCode es específico del ambiente original -- 1=Centro de
Costo/2=Marca/5=Tipo de Gasto -- reconfirmar contra cada organización real antes de
asumir que aplica igual). Dimensión 2/Dimensión 3 explícitamente NO portadas (opcionales
en el original, YAGNI). Bug real evitado de una: `SalesDocumentLineDto`/
`PurchaseDocumentLineDto` generalizaron `ItemCode`/`WarehouseCode` a nullable (antes
`string` no-nullable) -- una línea de Servicio real sin esto habría fallado el binding
igual que el bug de `Quantity`/`DiscountPercent` ya documentado en la entrega de
`Modulo.Ventas`, evitado acá por diseñarlo nullable desde el principio, no encontrado
después. **Sin verificar contra SAP real** -- ni el mapeo de campos ni el DimCode.

**3. Override de "permite crear documento" por organización**
(`OrganizationDocumentPermission`, tabla nueva `organization_document_permissions` --
migración `AddOrganizationDocumentPermissions` generada y **aplicada con éxito contra
Postgres y SQL Server reales de desarrollo**, no solo generada) -- reemplaza el flag fijo
`DefaultCanCreate` de `SalesDocumentTypeCatalog`/`PurchaseDocumentTypeCatalog`/
`InventoryDocumentTypeCatalog` por una evaluación dinámica
(`IOrganizationDocumentPermissionService.IsCreateAllowedAsync(engine, documentType,
defaultValue)`, `PortalSaas.Core.Comercial`) consultada desde `CanCreateAsync` de los 3
motores. Sin fila de override para (organización, engine, tipo), se usa el
`defaultValue` del catálogo -- nunca al revés, una organización sin configuración
explícita no queda más permisiva que el default (mismo criterio "falla hacia lo más
estricto" del resto del proyecto). Vive en la base propia de la plataforma
(`organizations`), no en el SAP del cliente -- a diferencia del original
(`PERMITE_CREAR_DOCUMENTO` en HANA), la config de plataforma no debería depender de que
el SAP del cliente esté disponible (ya establecido en `PurchaseDocumentTypeCatalog.cs`
antes de esta entrega). **Pendiente real, no cerrado acá**: no hay ninguna pantalla de
administración para cargar estas filas todavía -- hoy solo se puede insertar a mano
(SQL directo), mismo estado inicial que tuvieron `Plans`/`Subscriptions` antes de que
esa UI se construyera en una entrega posterior. 4 tests xUnit
(`OrganizationDocumentPermissionServiceTests`, EF Core InMemory con un
`ICurrentUserContext` fijo -- default sin override, override en `true`/`false`,
aislamiento entre organizaciones).

**4. Filtrado del árbol de menú por módulos contratados** (`IModuleAccessService`/
`ModuleAccessService`, `PortalSaas.Core.Comercial`) -- cierra el hueco documentado desde
la entrega del sidebar dinámico ("el árbol de menú es el mismo para todas las
organizaciones sin relación con qué módulos tiene contratados"). `MenuNavigationService`
filtra `Menu.OriginModule` contra los `PlatformModule.Code` contratados por la
organización (unión de módulos `IsCore=true` + los del plan activo vía `PlanModule`,
misma resolución "por modo" -- `Subscription`/`OnPremiseLicense` -- que
`IContractLimitService`, copia deliberada más chica, no una dependencia compartida + los
add-ons propios vía `OrganizationModule`, tabla que ya existía sin ningún consumidor
real hasta ahora) -- **antes** del bypass de administrador, a propósito: es un gate
comercial (qué pagó la organización), no de autorización, así que un admin de la
organización tampoco debería ver un módulo no contratado. Un `Menu.OriginModule` que no
aparezca en ningún `PlatformModule.Code` (ej. `"Administracion"`, núcleo de plataforma,
no vendible aparte) **no está gateado** -- se trata como parte del core, nunca se oculta
por esto. **Hueco real encontrado al implementar, no un bug de código sino de estado de
datos**: `platform_modules`/`plan_modules`/`organization_modules` no tienen NINGÚN dato
cargado todavía (no hay UI de administración para ninguna de las 3, y ningún seed) --
con el catálogo vacío, `GetCatalogedModuleCodesAsync().Count == 0` y
`MenuNavigationService` **no filtra nada** (chequeo explícito antes de siquiera resolver
los contratados, evita una consulta de más en el caso común de hoy). Esto significa que
la lógica de filtrado está completa y probada, pero **sin efecto visible todavía** hasta
que exista una pantalla de administración para cargar el catálogo de módulos vendibles y
asociarlos a planes/organizaciones -- mismo estado que el punto 3 de arriba, ambos
convergen en el mismo hueco: falta UI de administración para la capa
`platform_modules`/`plan_modules`/`organization_modules` completa, no solo el
consumo. 6 tests xUnit (`ModuleAccessServiceTests`: módulo core siempre incluido, módulo
del plan activo incluido, módulo no contratado ni como add-on excluido, add-on propio
incluido, add-on de otra organización no filtra, resolución on-premise vía licencia).

**Actualización, misma noche**: la pantalla de administración que faltaba para los
puntos 3 y 4 **ya se construyó** -- ver la entrega "UI de administración de módulos y
permisos de documento" más abajo. Verificación E2E contra SAP real de las líneas de
Servicio (punto 2) y verificación visual en navegador del rediseño (punto 1) siguen sin
hacerse.

## UI de administración de módulos y permisos de documento -- 26 jul 2026 (mismo día)

Cierra el hueco que dejó abierta la entrega anterior: `platform_modules`/
`plan_modules`/`organization_modules`/`organization_document_permissions` existían con
lógica real de consumo (`IModuleAccessService`/`IOrganizationDocumentPermissionService`)
pero sin ninguna forma de cargarse salvo SQL directo. `dotnet build` en 0/0 y
**107/107 tests siguen en verde** (sin tests nuevos dedicados -- CRUD/checkboxes sin
lógica de negocio computada, mismo criterio que otras pantallas de solo-administración
de esta sesión, ej. `Plans`/`Subscriptions`).

- **`/Admin/PlatformModules`** (Index/Create/Edit, mismo patrón exacto que
  `/Admin/Profiles`) -- CRUD del catálogo comercial (`Code`/`Name`/`IsCore`). El `Code`
  debe coincidir con el `OriginModule`/`ModuloPortal.ModuleCode` real del plugin (ej.
  "Ventas", "Compras", "Inventario", "Administracion") para que
  `MenuNavigationService` tenga algo que filtrar -- documentado en la propia pantalla
  (estado vacío explícito).
- **`/Admin/Plans/Edit`** -- ganó una sección "Módulos incluidos" (checkboxes,
  `PlanModule`), mismo patrón que `Profiles/Edit` asignando `ProfileAction`. Los
  módulos `IsCore=true` se muestran marcados y deshabilitados (informativo, no se
  postean -- ya están incluidos siempre vía `ModuleAccessService` sin necesidad de una
  fila en `PlanModule`).
- **`/Admin/Organizations/Modules`** (una sola página, patrón `EmailSettings`) --
  add-ons de `OrganizationModule` por organización, más allá de lo que ya incluye su
  plan. Muestra "Ya incluido (core o plan actual)" sin checkbox para los que ya
  aplican vía `IModuleAccessService.GetContractedModuleCodesAsync` menos los add-ons
  propios (para no doble-contar), y checkbox editable solo para lo que sí se puede
  agregar como extra.
- **`/Admin/Organizations/DocumentPermissions`** (una sola página) -- override de
  `OrganizationDocumentPermission` por organización, un `<select>` de 3 estados (Usar
  default/Permitir siempre/Bloquear siempre) por cada combinación real de
  (Engine, DocumentType) -- construida siempre desde
  `SalesDocumentTypeCatalog`/`PurchaseDocumentTypeCatalog`/`InventoryDocumentTypeCatalog.Entries`
  (nunca tipeada a mano), para no poder desincronizarse si se agrega un tipo de
  documento nuevo. "Usar valor por defecto" borra el override existente en vez de
  guardar un valor redundante.
- Los 3 formularios nuevos sin ningún otro atributo `asp-*` llevan
  `asp-antiforgery="true"` explícito -- lección ya aprendida dos veces en este
  proyecto (`SelectCompany.cshtml`/`EmailSettings/Index.cshtml`, ver más arriba): el
  `FormTagHelper` de ASP.NET Core solo se activa sobre un `<form>` si tiene al menos
  un atributo `asp-action`/`asp-page`/`asp-route-*`/`asp-antiforgery`/etc. -- un
  `<form method="post">` a secas queda como HTML literal, sin token antiforgery, y
  cualquier POST real devuelve 400. **Nota para revisar más adelante, no corregida
  acá**: varias pantallas de administración previas a esta sesión (`Profiles/Edit`,
  `MenuGroups/Edit`, etc.) tienen el mismo `<form method="post">` sin
  `asp-antiforgery` explícito -- podrían tener este mismo bug latente, nunca
  confirmado con un POST real; no se tocaron en esta entrega por estar fuera de
  alcance (no son parte de lo pedido), pero es una duda real abierta, no una
  suposición descartada.

**Sin verificar contra Postgres/SQL Server real en runtime** -- a diferencia de otras
entregas de esta sesión (migraciones sí aplicadas contra bases reales), esta UI
específica solo se verificó por build + revisión de código, sin levantar el Host y
probar un POST real de punta a punta (mismo motivo que las verificaciones visuales
pendientes de arriba -- sin navegador disponible en esta sesión, y sin credenciales de
administrador de plataforma ya sembradas en este entorno).

## Catálogos prerrequisito del Módulo Importador Genérico -- 26 jul 2026 (mismo día)

Antes de encarar `Modulo.ImportacionGenerica` en sí (0% portado, ~5.760 líneas
estimadas, ver `docs/08-BRECHA-FUNCIONAL-VS-PORTALSAP-V2.md` §2), se releyó esa
sección en detalle y se decidió con el dueño del proyecto avanzar primero en los
catálogos/servicios prerrequisito que el importador necesita y que hoy no existían --
**sin ningún consumidor real todavía** (ningún plugin los usa hasta que se porte el
importador), quedan listos para esa entrega futura. `dotnet build` en 0/0, **107/107
tests siguen en verde** (sin tests nuevos dedicados -- mismo criterio que el resto de
los catálogos de `PortalSaas.Core.Catalogos`, ninguno tiene tests xUnit propios en este
proyecto, se verifican E2E contra SAP real cuando tienen consumidor, cosa que estos
todavía no tienen).

**5 de los 7 prerrequisitos identificados en `docs/08` §2.3 ya están portados** (2 ya
existían de la entrega de líneas de Servicio de esta misma noche):
- **`IItemCrossReferenceService`** (`PortalSaas.Core.Catalogos`, portado de
  `IParidadCatalogoService`) -- paridad SKU-cliente ↔ `ItemCode` sobre el recurso
  estándar de Service Layer `AlternateCatNum`. A diferencia de los demás catálogos de
  este namespace (solo lectura vía `IHanaService`), este necesita crear/borrar filas
  -- usa `ISapConnectionProvider`/`ISapSession` (Service Layer), no HANA directo.
  `SyncAsync` reemplaza toda la paridad del cliente de una (borra + crea), mismo
  criterio que la referencia -- evita diffing, volumen chico por cliente.
- **`IPriceListService`** (portado de `IListaPrecioService`) -- precio de un artículo
  en una lista de precios puntual (`ITM1`), con variante batch (`GetPricesAsync`) para
  no consultar una vez por fila al importar un archivo con muchas líneas.
- **`IBusinessPartnerDefaultsService`** (portado de `ISocioNegocioDefaultsService`) --
  Vendedor/lista de precio que SAP ya tiene configurados nativamente para un socio de
  negocio (`OCRD.SlpCode`/`ListNum`), lectura directa a HANA. El precio en sí **nunca**
  se calcula a mano con esto -- es solo referencia informativa, Service Layer asigna
  el precio real de la lista del socio al postear un documento sin `UnitPrice`
  explícito (mismo criterio ya establecido en `SalesDocumentService`/
  `PurchaseDocumentService`).
- **`ODataFilterHelper`** (`PortalSaas.Core.Sap`, `internal`) -- portado tal cual,
  construye filtros `$filter` de Service Layer escapando comillas simples (mismo
  criterio de "nunca concatenar sin escapar" que ya rige el SQL de HANA, adaptado a
  OData porque no soporta parámetros bindeados). Prerrequisito de
  `IItemCrossReferenceService` (filtra `AlternateCatNum` por `CardCode`/`Substitute`).

**Quedan 2 de los 7, ambos más grandes y transversales, no atacados en esta
entrega**: `SapCamposAdicionalesHelper.Aplanar`/`EsNombreReservado` (aplanar
`AdditionalFields` dentro del JSON al postear a Service Layer, más el guardrail que
bloquea que un campo de usuario pise un nombre reservado del payload) y agregar
`AdditionalFields` a los DTOs de los 3 motores de documento (`SalesDocumentDto`/
`PurchaseDocumentDto`/`InventoryDocumentDto`) -- sin estos dos, aunque el importador
en sí se porte, no soportaría campos de usuario/UDF dinámicos, que es justamente la
funcionalidad que más lo diferencia de un importador CSV simple. `docs/08` §2.3
actualizado con el detalle línea por línea de qué quedó portado y qué falta.

**Nota de alcance heredada de la entrega anterior, sigue vigente**: `ICostCenterCatalogService`
solo cubre `DimCode=1` (Centro de Costos) -- Dimensión2/Dimensión3 (`CostingCode2`/
`CostingCode3`) no están portadas, mismo criterio YAGNI. El importador en sí, cuando se
porte, va a necesitar esas dos dimensiones si se quiere paridad completa con el
original (líneas de Servicio con Dimensión2/3, ver el hallazgo de diseño ya
documentado) -- otro prerrequisito a tener en cuenta, no cerrado acá.

## Rediseño visual con valores reales extraídos de PortalSAP_v2 -- 26 jul 2026 (mismo día)

El dueño del proyecto mostró dos capturas (Órdenes de Venta acá vs. en la instalación
real de Comercial Depor) y pidió extraer los valores reales de diseño del proyecto
anterior (`referencia-original/PortalSAP_v2/src/PortalSAP.Host/wwwroot/css/theme.css`,
1802 líneas) en vez de inventar una paleta nueva. `dotnet build` en 0/0, **107/107
tests siguen en verde** (CSS/markup, sin lógica nueva).

**Hallazgo real antes de tocar nada**: `theme.css` HOY (tal como está commiteado en la
referencia) **no reproduce exactamente** lo que se ve en la captura de Comercial Depor
-- el propio archivo dice en un comentario que su paleta "fue reemplazada por la
paleta nueva provista por el cliente", y en la versión actual `.sidebar`/`.topbar` usan
`var(--card-bg)` (blanco en el tema "claro", el default), no un azul sólido. La
instalación real de Comercial Depor de la captura corre un build más viejo
(`build 26.7.21.1`, visible en el pie del sidebar) con la paleta anterior, ya
reemplazada en el código fuente que quedó como referencia. Se documenta esto en vez de
fingir una coincidencia exacta que no existe en el archivo real -- los valores usados
abajo son reales (extraídos de `theme.css`), pero reaplicados a `.sidebar`/`.topbar` en
vez de tomados literal de esas dos reglas (que hoy son blancas).

- **`--bs-primary` pasó de `#0a2540` (inventado en la entrega de rediseño anterior) a
  `#1e3a5f`** -- es el valor real de `--accent-2` del tema "claro" de `theme.css`
  (línea 32), el tono oscuro que ese archivo ya usa para el hover de `--accent` y el
  extremo del gradiente de la pantalla de login. `--radius-sm: 6px` (línea 125) y
  `--card-border: #cbd1db` (línea 28) también son valores reales de ahí, agregados como
  variables nuevas en `site.css`.
- **`.sidebar` pasó de fondo claro a `var(--bs-primary)` sólido** con todo el texto/
  hover/activo recalculado para fondo oscuro (blanco/`rgba(255,255,255,N)`) -- mismo
  criterio visual que la captura, aplicado sobre la estructura real ya existente
  (`.sidebar-nav`/`.sidebar-nav-grupo`/`.sidebar-brand-mark`, portados en la entrega
  "Sidebar dinámico"), no una reescritura desde cero.
- **`.filter-card`** (nuevo en `site.css`, usado por
  `Pages/Shared/Components/DocumentList/Default.cshtml`) -- portado tal cual de
  `.doc-list-filtros` (`theme.css:1438`): mismo padding (14px), mismo borde/radio.
  Reemplaza el `<form class="row g-2 mb-3 align-items-end">` anterior (grid de
  Bootstrap sin caja visual) por un contenedor con borde real, igual que la
  referencia -- se ve en TODOS los listados de documento (Venta/Compra/Inventario),
  no solo Órdenes de Venta, porque todos comparten el mismo componente.
- **`.document-list-table-wrapper`** (nuevo, reemplaza a `.table-responsive` en el
  mismo archivo) -- portado de `.doc-list-tabla-wrapper` (`theme.css:1510`): caja con
  borde/radio alrededor de la tabla, encabezado `position: sticky` adentro (antes el
  header solo tenía el fondo azul, sin sticky), altura acotada (`max-height: 65vh`)
  con scroll propio -- mismo criterio que la referencia, el filtro queda siempre
  afuera de la caja que scrollea. Fila con hover sutil
  (`color-mix(in srgb, var(--bs-primary) 6%, transparent)`), portado de
  `.doc-list-fila:hover` (`theme.css:1503`, con `--accent` en vez de `--bs-primary`,
  mismo concepto).

**Sin verificar visualmente en navegador** -- mismo motivo que toda la sesión (sin
herramienta de captura de pantalla disponible acá), la única confirmación es
`dotnet build`/revisión de código contra los valores reales extraídos. Alcance acotado
a lo que pidió la captura (shell + Órdenes de Venta, que comparte componente con el
resto de los listados de documento) -- **no** se tocaron las tablas de `/Admin/*`
(backoffice, `Pages/Admin/**/Index.cshtml`), que siguen con `<table class="table
table-striped">` liso, fuera del alcance de esta comparación.

## Diagnóstico forense del "azul plano" reportado tras el rediseño -- 26 jul 2026 (mismo día)

El dueño del proyecto reportó ver el sidebar/tablas en azul brillante plano pese al
rediseño de la entrega anterior, y pidió diagnóstico con evidencia antes de tocar
código -- sospecha inicial: `_Layout.cshtml.css` (CSS isolation de Razor) ganándole la
cascada a `site.css`/`sidebar.css`.

**Causa raíz real, confirmada con evidencia -- NO es la que se sospechaba**:
1. `site.css`/`sidebar.css` en disco **ya tenían** el rediseño completo y correcto
   (`--bs-primary: #1e3a5f`, `.sidebar` sólido, `.filter-card`/
   `.document-list-table-wrapper`) -- confirmado leyendo el contenido completo de los
   dos archivos, no el resumen de `CLAUDE.md`. La entrega anterior sí se guardó en
   disco (nunca se había dicho lo contrario, solo que no se había verificado en
   navegador).
2. `_Layout.cshtml.css` **sí existe** -- scaffold default de `dotnet new` con
   `.btn-primary { background-color: #1b6ec2; }`, presente desde el commit inicial,
   nunca limpiado. Se compila de verdad (`obj/.../PortalSaas.Host.styles.css`,
   `.btn-primary[b-v7lcklutb0]`), pero **`grep` sobre todo `**/*.cshtml` confirma que
   ningún `<link>` referencia ese bundle** -- el navegador nunca lo descarga. Y aunque
   se sirviera, `_Layout.cshtml` no tiene ningún elemento `.btn-primary` en su propio
   marcado (el scope de Razor CSS Isolation no se propaga a `@RenderBody()`) -- la
   regla es código muerto por partida doble, nunca fue la causa. **Se eliminó
   igual** (`rm src/PortalSaas.Host/Pages/Shared/_Layout.cshtml.css`) por higiene --
   scaffold sin ningún consumidor real, no como parte del fix.
3. **Causa real encontrada**: había un proceso `PortalSaas.Host` corriendo en vivo
   (`dotnet run --project src/PortalSaas.Host`, arrancado horas antes en la misma
   sesión) -- casi con certeza la pestaña del navegador donde se tomaron las capturas
   quedó abierta desde ANTES de que el rediseño se guardara en disco, mostrando el
   HTML viejo con los `href` versionados (`asp-append-version`) apuntando al hash
   anterior de `site.css`/`sidebar.css`. `wwwroot` se sirve en vivo desde el
   proyecto fuente en `dotnet run` (no hace falta rebuild para que un cambio de CSS
   se refleje), así que un simple refresh de esa pestaña contra el mismo proceso ya
   debería haber mostrado la paleta nueva -- el "bug" no estaba en el código.

**Archivos que cambiaron en esta entrega** (además de la eliminación de arriba):
- `plugins/Modulo.Ventas|Compras|Inventario/Pages/Shared/_TabContent.cshtml` -- las 3
  tablas de líneas de detalle ganaron la clase `line-items-table` (paridad obligatoria
  entre los 3 motores, ver regla dura del proyecto).
- `src/PortalSaas.Host/wwwroot/css/site.css` -- nueva clase `.line-items-table`,
  portada de `.admin-table` (`theme.css:1184`, tratamiento MÁS discreto que
  `.document-list-table` a propósito: la referencia usa un estilo distinto para la
  grilla de líneas dentro de un documento vs. el listado principal -- encabezado
  mayúscula chico gris sin relleno de color, borde inferior por fila).

**El proceso que bloqueaba la compilación** (PID de `PortalSaas.Host`, el mismo que
servía la página con los estilos viejos) se detuvo para poder correr `dotnet build`
limpio -- es el mismo proceso que había que reiniciar de todos modos para ver el
rediseño reflejado, así que detenerlo no fue una acción aparte del diagnóstico, era
parte de la solución real (**hay que volver a levantar el Host y refrescar el
navegador para confirmar visualmente** -- sigue sin verificarse con una captura
nueva, misma limitación de toda la sesión, sin herramienta de navegador disponible
acá).

`dotnet build` en 0 advertencias/0 errores, **107/107 tests siguen en verde** (CSS +
wrapping de markup existente, sin lógica nueva). No se tocó `_AdminLayout.cshtml` ni
`Pages/Admin/**` (fuera de alcance, confirmado). No se agregó ningún hex nuevo fuera de
las variables `--bs-*`/`--card-*` ya definidas -- `.line-items-table` reusa
`--card-border` existente, con un solo color nuevo puntual (`#6b7280`, gris de texto
muted para el encabezado de esa tabla, valor real de `theme.css` línea 30
`--text-muted` del tema "claro") documentado acá en vez de dejarlo sin explicar.

## Auditoría de paridad visual completa (A/B/C) -- 26 jul 2026 (mismo día)

Tercera pasada de rediseño en el mismo día -- esta vez con la paleta completa
prevalidada por el dueño del proyecto (theme.css ya releído línea por línea, sin
volver a extraer) y **revierte una decisión de la entrega anterior**: sidebar/topbar
vuelven a blanco (`--card-bg`), el navy (`#1e3a5f`) queda como acento (botones,
activo, hover, degradado), no como fondo sólido -- la entrega de hace unas horas se
había guiado por la captura de producción vieja de Comercial Depor, ya reemplazada en
el `theme.css` real. No reabrir esto de nuevo sin pedirlo explícito.

**Corrección a una premisa del pedido**: se afirmó que `site.css` era "un placeholder
de una línea" -- releído completo antes de tocar nada, tenía 198 líneas con las 2
entregas anteriores ya aplicadas. La documentación de `CLAUDE.md` sobre el estado del
archivo era correcta; lo que faltaba eran valores puntuales (paleta incompleta, un par
de decisiones a revertir), no el archivo entero.

### Tabla brecha-por-brecha (Fase 0, antes de escribir CSS)

| Pieza | ¿Clase existía? | ¿Valor coincidía? | ¿Markup la usaba? |
|---|---|---|---|
| A. Paleta | Parcial (`--bs-primary`/`--card-border`/`--radius-sm`) | ❌ Faltaban `--text-muted`/`--accent`/`--accent-soft`/`--shadow-card`/`--bs-danger`/`--input-bg`/`--card-bg`; `--bs-primary` en rol de fondo, no de acento | — |
| B.1 sidebar/topbar | Sí | ❌ Sólido navy (decisión a revertir); activo/hover en `rgba(255,255,255,N)` en vez de `color-mix` | Sí |
| B.2 `.filter-card` | Sí | ⚠️ Fondo hardcodeado `#ffffff` en vez de `var(--input-bg)` | Sí |
| B.3 `.document-list-table-wrapper` | Sí | ❌ `max-height: 65vh` (debía ser 60vh); `thead th` con relleno azul (debía ser `var(--card-bg)` fijo) | Sí |
| C.1-2 `.form-row`/`.form-group` (grilla densa) | ❌ No existían | — | ❌ Los 3 `_TabGeneral.cshtml` en `row`/`col-md-*` crudo -- causa real de "las líneas del detalle se ven planas" |
| C.3 `.doc-tab-seccion` | ❌ No existía | — | ❌ Ningún wrapper de sección |
| C.4 `.doc-columnas-2` | ❌ No existía | — | Sin consumidor hoy (ningún tab necesita 2 columnas independientes todavía) |
| C.5 `.doc-tabs`/`.doc-tab-btn` | ❌ No existían | — | `DocumentForm/Default.cshtml` en `nav-tabs` crudo de Bootstrap, sin sticky |
| C.6 `.doc-badge-*` | ❌ No existían | — | Usaba `badge bg-success`/`bg-secondary` armado en C# (`StatusClass`) |
| C.7 `.doc-resumen-total` | ❌ No existe la funcionalidad | — | Sin desglose de totales en ningún documento (ver `docs/08` §1.1) -- requiere datos nuevos, no solo CSS |

Grep `007bff|0d6efd|0A2540|rgba(0, *123` sobre `wwwroot`: cero resultados fuera de
`bootstrap.min.css` (vendored) y un comentario de texto en `site.css` (no una regla
viva) -- confirmado, sin azul hardcodeado activo.

### Qué se corrigió, por pieza

- **(A)** `site.css :root` reescrito con las 17 variables de la paleta validada
  completa (antes solo 8). `--bs-success`/`--bs-warning` pasaron de valores
  provisorios (`#10b981`/`#f59e0b`) a los reales del tema "claro"
  (`#2d7d4e`/`#d4880e`). `.btn-primary` hover pasó de un hex inventado (`#16293f`) a
  `var(--accent)` (rol real "acento hover/secundario" de la paleta); estado activo
  usa `color-mix(in srgb, var(--bs-primary) 80%, black)` en vez de un hex nuevo --
  cero hex fuera de las variables definidas, tal como pide la regla dura de
  `07-THEMING-TENANT-PENDIENTE.md`.
- **(B)** `.sidebar`/`.topbar` vuelven a `var(--card-bg)` con borde `var(--card-border)`;
  activo/hover de `.sidebar-nav a` recalculados a `color-mix(...16%/10%...)` con texto/
  inset en `var(--bs-primary)`, valores exactos pedidos. `.document-list-table thead th`
  perdió el relleno azul (ahora `var(--card-bg)` fijo, sticky se mantiene).
  `.document-list-table-wrapper` a `max-height: 60vh`. `.filter-card` a
  `var(--input-bg)`. `.user-menu-panel` (dropdown de usuario) pasó de
  `var(--bs-body-bg)` (ahora gris de página, se veía mal ahí) a `var(--card-bg)` +
  `var(--shadow-card)` -- ajuste no pedido explícitamente pero necesario para que el
  dropdown siga siendo una tarjeta blanca real tras el cambio de `--bs-body-bg`.
- **(C)** `DocumentForm/Default.cshtml`: clases `doc-tabs`/`doc-tab-btn` agregadas
  junto a `nav-tabs`/`nav-link` (conviven, Bootstrap no se quita); `doc-status-badge`
  agregada junto al `badge @Model.StatusClass` que ya arma el PageModel -- **cero
  cambios en ningún `.cs`**, todo el remapeo de color es CSS puro vía selector
  compuesto (`.doc-status-badge.bg-success`, etc.). Los 3 `_TabGeneral.cshtml`
  (Ventas/Compras/Inventario, paridad obligatoria) migraron de `row`/`col-md-*` a
  `doc-tab-seccion` > `form-row` > `form-group` -- cada campo pasa a ser una celda
  con borde/fondo propio (`8px` de radio, `var(--card-border)`), label 11px arriba,
  valor 13.5px abajo, tal como se pidió. `.doc-columnas-2`/`.doc-resumen-total` se
  dejaron definidos en CSS sin consumidor real (no hay tab con 2 columnas
  independientes ni desglose de totales en ningún documento todavía) -- agregarlos al
  markup ahora habría sido maquetar sobre datos que no existen.

### Efecto lateral no pedido, documentado por transparencia

`--bs-body-bg`/`--bs-body-color` son variables de Bootstrap consumidas globalmente por
`body{}` -- al vivir en `site.css` (compartido por `_Layout.cshtml` Y
`_AdminLayout.cshtml`), el cambio de fondo de página (`#f4f6f9`→`#e4e7ec`) y de color
de texto (`#1e293b`→`#1a1a1a`) también alcanza al backoffice de administración, aunque
no se tocó ningún archivo de `Pages/Admin/**`. Es una diferencia sutil (dos tonos de
gris/negro casi idénticos), pero se documenta explícito para no ocultarlo -- mismo
criterio que ya aplica `.navbar.bg-dark` (también comparte variable con el tenant).

`dotnet build` en 0 advertencias/0 errores, **107/107 tests en verde**. Sin cambios en
ningún `.cs` (confirmado por revisión de cada diff antes de compilar). **Sin
verificación visual en navegador** -- mismo motivo de toda la sesión (sin herramienta
de captura de pantalla disponible acá); no hay ningún proceso `PortalSaas.Host`
corriendo en este momento para siquiera intentar una inspección de DOM. Recomendado:
levantar el Host, loguearse, y confirmar contra DevTools que `.sidebar`/`.topbar`
computan `#ffffff` y que un botón `.btn-primary` computa `#1e3a5f` antes de dar este
punto por cerrado del todo.

## El problema real no era solo color -- estructura, bug de paginación y selector de tema faltante (26 jul 2026, mismo día)

El dueño del proyecto comparó dos capturas reales lado a lado (la instalación de
Comercial Depor vs. `localhost:5271/ventas/ordenes`) y señaló 3 síntomas: la paleta de
colores "disponible" (4 opciones) no aparecía en ningún lado, la distribución de las
grillas de Órdenes de Venta era distinta al original, y los colores seguían sin
coincidir. Pidió ir a la causa real con el código completo de
`referencia-original/PortalSAP_v2` disponible, no seguir ajustando valores de CSS a
ciegas. Se leyó `DocumentListViewComponent`/`_Index.cshtml`/`_Layout.cshtml`/
`doc-list.js`/`theme-switcher.js` reales del original antes de tocar nada -- 3
hallazgos, ninguno era solo una diferencia de color:

1. **"La paleta de colores... deben ser 4" -- selector de tema, no una paleta fija.**
   El original tiene 4 temas reales (Claro/Oscuro/Teal/Violeta) con 4 botones-swatch
   en el dropdown de usuario (`.theme-row`, `theme-switcher.js`, persistencia por
   `localStorage`) -- esta entrega solo tenía la paleta "claro" fija, sin ningún
   selector. **Corregido**: los 4 bloques `[data-theme="..."]` completos (valores
   reales de `theme.css` líneas 21-122, incluidos `--menu-color-1..6` por tema) +
   `theme-switcher.js` portado tal cual + 4 swatches en `_Layout.cshtml`
   `.user-menu-panel`. Script en `<head>` aplica el tema guardado antes de pintar
   (mismo truco que ya usaba el colapso del sidebar).
2. **"Distribución de las grillas distinta" -- confirmado, la estructura real difiere,
   no solo el CSS.** `DocumentList/Default.cshtml` de la referencia envuelve TODO
   (título + filtro + tabla + paginación) en una sola tarjeta (`<div class="card-ps
   doc-list">`) con bordes anidados reales (el filtro y la tabla SÍ llevan su propio
   borde cada uno, aunque estén dentro de la tarjeta exterior -- confirmado releyendo
   `theme.css` dos veces porque la primera pasada asumió mal que había que quitarlos).
   La fila de la tabla es clicable COMPLETA vía `data-url-detalle` +
   `doc-list.js` (no un link en la primera celda), con una columna final de chevron
   (`»`) para ir al detalle. La paginación es "« Anterior / Página X de Y / Siguiente
   »" + contador de registros arriba de la tabla, no un paginador numerado. El
   encabezado de la tabla es mayúscula/chico/gris (`.admin-table`, LA MISMA clase que
   usan las líneas de detalle de un documento) -- nunca tuvo relleno de color, ese
   invento fue de una entrega anterior de esta sesión. **Corregido**: `DocumentList/
   Default.cshtml` reescrito con esta estructura real; `doc-list.js` portado;
   `.card-ps`/`.doc-list`/`.doc-list-header`/`.doc-list-contador`/
   `.doc-list-paginacion`/`.doc-list-col-detalle` nuevos en `site.css`. Iconos de
   categoría raíz del sidebar (Ventas/Compras/Inventario/etc.) ahora ciclan
   `--menu-color-1..6` por color (antes monocromos) -- portado a
   `Pages/Shared/Components/SidebarMenu/Default.cshtml`/`_MenuNode.cshtml` vía
   `ViewData` (limpiado antes de recursar a hijos, para que un subgrupo anidado no
   herede el chip de su carpeta raíz) -- sin tocar `MenuNodeDto`/`MenuTreeHelper`.
3. **Bug real encontrado en el camino, no cosmético: la paginación nunca funcionó.**
   `IndexGeneric*ModelBase.OnGetAsync` (los 3 motores) llamaba
   `_documents.ListAsync(Type, filter, ct: ct)` **sin pasar `page`/`pageSize`** -- el
   paginador numerado de la entrega anterior generaba links `?page=2`, pero
   ningún `[BindProperty]` los leía, así que TODO click en una página distinta de la 1
   seguía devolviendo la página 1 con 25 filas fijas, en los 3 motores, desde que se
   creó el listado genérico. **Corregido**: `PageNumber`/`PageSize` agregados como
   `[BindProperty(SupportsGet = true, Name = "page"/"pageSize")]` en los 3
   `IndexGeneric*ModelBase.cs`, pasados a `ListAsync` y al `DocumentListViewModel`
   (`CurrentPage`/`PageSize`) -- el modelo (`DocumentListViewModel.TotalPages`, etc.)
   ya tenía todo lo necesario, no hizo falta tocarlo. Se evitó nombrar la propiedad
   `Page` (colisiona con `PageModel.Page()`, `CS0108`, un `return Page();` a centímetros
   de distancia en el mismo archivo -- se detectó por el warning del compilador, no a
   simple vista).

`dotnet build` en 0 advertencias/0 errores, **107/107 tests en verde**. Cambios reales
en `.cs` esta vez (a diferencia de la entrega anterior) -- acotados a los 3
`IndexGeneric*ModelBase.cs` (agregar 2 propiedades de paginación + pasarlas a
`ListAsync`/al view model), nada de lógica de negocio nueva. **Sin verificación visual
en navegador** -- mismo motivo de toda la sesión.

## Crash real de producción: colisión de rutas de vista entre plugins + 2 bugs más -- 26 jul 2026 (mismo día)

El dueño del proyecto probó la entrega anterior contra los 2 ambientes demo reales y
reportó un crash real (`InvalidOperationException`, no cosmético) más otros 3 problemas
en el mismo mensaje. `dotnet build` en 0/0, **107/107 tests en verde** después de cada
fix. Los 3 motores genéricos de documento (Venta/Compra/Inventario) quedaron afectados
por igual -- regla de paridad aplicada a los 3 sin preguntar, porque los 3 comparten
exactamente el mismo patrón que causaba el bug.

**1. Causa raíz real del crash, no la que se sospechaba primero.** El mensaje de error
exacto: *"The model item passed into the ViewDataDictionary is of type
'Modulo.Ventas.Pages.SalesOrders.DetailModel', but this ViewDataDictionary instance
requires a model item of type 'Modulo.Compras.Pages.DetailGenericPurchaseDocumentModelBase'"*.
La sospecha inicial (mía) fue el cambio reciente de `SidebarMenu/Default.cshtml`
(`new ViewDataDictionary(ViewData)`) -- descartada al revisar el mensaje con cuidado: el
tipo "requerido" es `DetailGenericPurchaseDocumentModelBase` (Compras), no `MenuNodeDto`
(el modelo de esa vista), así que el sidebar no tiene nada que ver. **Causa real,
confirmada leyendo `PluginManager.LoadModule`**: los 3 plugins (`Modulo.Ventas`/
`Modulo.Compras`/`Modulo.Inventario`) tenían cada uno su propio
`Pages/Shared/_TabGeneral.cshtml`/`_TabContent.cshtml` en la **misma ruta virtual
exacta** (`~/Pages/Shared/_TabGeneral.cshtml`), cada uno compilado en un assembly de
plugin distinto pero registrado bajo el mismo `ApplicationPartManager`
(`partManager.ApplicationParts.Add(new CompiledRazorAssemblyPart(assembly))`, una vez
por plugin). El motor de vistas de Razor cachea vistas compiladas por ruta STRING,
agregando TODOS los `ApplicationPart` registrados -- con 3 assemblies distintos
aportando una vista a la misma ruta, `Html.PartialAsync("~/Pages/Shared/_TabGeneral.cshtml",
Model.Model)` puede resolver a la vista de **otro** plugin (cualquiera haya "ganado" la
ruta en el caché), pasándole un modelo del tipo equivocado -- de ahí el crash exacto
reportado (una página de Ventas resolviendo la vista de Compras). Esto llevaba latente
desde que se creó `Modulo.Compras` (portado el mismo patrón exacto de `Modulo.Ventas`
sin darse cuenta de la colisión de ruta), no algo introducido en la entrega anterior.

**Corregido**: los 6 archivos (`_TabGeneral.cshtml`/`_TabContent.cshtml` × 3 plugins)
renombrados a nombres únicos por motor (`_TabGeneralVentas.cshtml`/
`_TabContentVentas.cshtml`, `...Compras.cshtml`, `...Inventario.cshtml`), y las 3
`DetailGeneric*ModelBase.BuildDocumentViewModel()` actualizadas para apuntar a la ruta
nueva. Cada archivo sigue declarando `@model` de SU PROPIO `DetailGeneric*ModelBase`
(sin cambios ahí) -- el fix es puramente de ruta, no de contenido. **Nota de alcance**:
`Pages/_ViewImports.cshtml` de cada plugin también colisiona por ruta, pero eso se
resuelve en tiempo de COMPILACIÓN dentro del `.csproj` de cada plugin (no hay lookup en
tiempo de ejecución vía `Html.PartialAsync` de por medio), así que no es el mismo tipo de
riesgo -- no se tocó.

**2. "También tiene problema de paginación" -- bug real distinto, no el mismo de la
entrega anterior.** La entrega anterior ya había corregido que `page`/`pageSize` nunca
se leían server-side; ese fix seguía funcionando. El bug real encontrado acá: **"Volver"
desde el detalle de un documento siempre volvía a la página 1 del listado**, sin
importar en qué página estuviera el usuario al hacer clic en una fila. Causa:
`DetailGeneric*ModelBase.BuildDocumentViewModel()` fijaba `BackUrl = RouteBase` (fijo,
sin querystring) en los 3 motores; `IndexGeneric*ModelBase` tampoco propagaba la
página/filtros actuales al armar cada `DetailUrl`. **Corregido**: `IndexGeneric*ModelBase.OnGetAsync`
arma `returnUrl = Uri.EscapeDataString(Request.Path + Request.QueryString)` y lo agrega a
cada `DetailUrl` (`{RouteBase}/{DocEntry}?returnUrl=...`); `DetailGeneric*ModelBase` gana
una propiedad `[BindProperty(SupportsGet = true, Name = "returnUrl")] public string?
ReturnUrl` y `BackUrl = Url.IsLocalUrl(ReturnUrl) && ReturnUrl is not null ? ReturnUrl :
RouteBase` -- mismo patrón anti-open-redirect que ya usaba `Account/Login.cshtml.cs`/
`SelectCompany.cshtml.cs` (`Url.IsLocalUrl`), no inventado acá.

**3. "El ancho de la grilla no se ajusta al ancho de la página... debe ser responsive"
-- bug real de una entrega anterior de esta misma sesión, nunca verificado en
navegador.** `site.css` tenía `.document-list-table { width: auto; ... }`, agregado en
la entrega de rediseño con la justificación (nunca verificada) de que evitaba "huecos"
en tablas angostas -- confirmado contra el archivo real (`theme.css:1184`,
`.admin-table { width: 100% }`) que esa premisa era incorrecta: el valor real siempre
fue `100%`. Con `width: auto` la tabla se encogía a su contenido en vez de llenar
`.document-list-table-wrapper`/`.card-ps`, exactamente el síntoma reportado ("no se
ajusta al ancho de la página"). **Corregido**: `width: 100%` (vuelve al valor real y al
default de Bootstrap). Se agregó además una regla responsive nueva, sin equivalente
previo (`.form-row`/`.doc-columnas-2` son grids de 2 columnas fijas que no se apilaban en
viewport angosto) -- `@media (max-width: 767.98px) { .form-row, .doc-columnas-2 {
grid-template-columns: 1fr; } }`.

**4. Directiva reconfirmada, no un bug nuevo: todo el diseño de un motor vive en su
formulario general, los documentos hijo heredan.** El dueño del proyecto lo reiteró
explícitamente después de este round -- ya era la arquitectura vigente (`DetailGeneric*ModelBase.cs`
+ `_TabGeneral*`/`_TabContent*` compartidos por plugin + `DocumentForm/Default.cshtml`,
sin métodos `virtual`), y los 3 fixes de esta entrega se hicieron exactamente ahí (la
clase base de cada motor, nunca en un subtipo concreto como `SalesOrders/Detail.cshtml.cs`)
-- se preserva sin cambios adicionales.

**Sin verificación visual en navegador** -- mismo motivo de toda la sesión (sin
herramienta de captura de pantalla disponible acá). El fix del crash (punto 1) sí queda
verificado por LECTURA DIRECTA del mecanismo de registro de `ApplicationPart`
(`PluginManager.cs`) contra el mensaje de excepción real reportado, no por suposición.

## Bug real de despliegue local: Host corriendo desde la carpeta AnyCPU vieja, no x64 -- 26 jul 2026 (mismo día)

El dueño del proyecto reportó "el botón avanzar página sigue sin funcionar" probando
`?page=2` directo en la URL, pese al fix de paginación de la entrega anterior. Antes de
tocar código de nuevo, se verificó el proceso realmente corriendo: `Get-CimInstance
Win32_Process` mostró un `dotnet.exe` sirviendo
`src\PortalSaas.Host\bin\Debug\net8.0\PortalSaas.Host.dll` -- la carpeta **AnyCPU**, no
`bin\x64\Debug\net8.0\` (la que genera `dotnet build PortalSaas.sln`, ver "Cambio de
plataforma de build a x64" más arriba). Mismo gotcha ya documentado antes en esta
sesión para `dotnet run --no-build`, pero esta vez afectando una prueba real del dueño
del proyecto: ese `.dll` de `bin\Debug\net8.0\` es un artefacto huérfano de antes de la
migración a x64 (o de un `dotnet run` sin pasar por el `.sln`), nunca recompilado desde
entonces -- **ninguno de los fixes de esta sesión (ni de sesiones anteriores) estaba
reflejado ahí**. No era el código el que seguía roto, era el binario que se estaba
probando. Corregido deteniendo ese proceso y relanzando con
`dotnet run --project src/PortalSaas.Host --launch-profile https` (resuelve x64
correctamente solo). **Recomendación para no repetir esto**: levantar el Host siempre
vía `dotnet run --project src/PortalSaas.Host` (F5/VS Code ya lo hace bien) o, si hace
falta el `.dll` directo, pararse dentro de `bin\x64\Debug\net8.0\` -- nunca ejecutar un
`.dll` de `bin\Debug\net8.0\` a secas (carpeta AnyCPU, no la que compila el proyecto).

## Paridad Modulo.Ventas vs. el original: filtros/columnas del listado + fecha por defecto -- 26 jul 2026 (mismo día)

El dueño del proyecto pidió comparar explícitamente `Modulo.Ventas` (acá) contra
`referencia-original/PortalSAP_v2/plugins/Modulo.Ventas` (el original) y cerrar la
brecha en UN solo motor primero, antes de replicar a Compras/Inventario -- confirmado
releyendo `IndexGenericoVentaModelBase.cs`/`GenericoVentaService.ListarAsync` reales del
original, no de memoria. Tres brechas reales encontradas:

1. **Faltaban 3 filtros completos** (`CustomerName`/`Cliente`, `CustomerReferenceNumber`/
   `N.° ref. cliente`, `SalesEmployeeName`/`Vendedor`) -- el listado acá solo tenía
   Cliente(código)/N° documento/fechas. Agregados a `SalesDocumentFilter`
   (`PortalSaas.Abstractions`), `SalesDocumentService.BuildWhereClause` y al
   `FilterInput`/`Filters` de `IndexGenericSalesDocumentModelBase.cs`, mismo orden que el
   original (N.° Cliente, Cliente, N.° ref. cliente, N.° documento, Fecha desde, Fecha
   hasta, Vendedor). El filtro de Vendedor exigió agregar el mismo `LEFT JOIN "OSLP"` al
   `COUNT(*)` que ya tenía el `SELECT` paginado -- sin eso, "invalid column" apenas se
   usara ese filtro (el alias `s` no existía en esa consulta).
2. **El comportamiento de los filtros de texto no coincidía** -- el original hace `LIKE`
   parcial case-insensitive (`UPPER(...) LIKE UPPER(:x)`) en TODO campo de texto,
   incluido `DocNum` (buscar "123" encuentra "51230", porque es
   `TO_VARCHAR(o."DocNum") LIKE :x`, no comparación numérica exacta) -- acá
   `CustomerCardCode`/`DocNum` eran `=` exacto (`DocNum` además era `int?`, no `string?`).
   Corregido: `DocNum` pasó a `string?` en `SalesDocumentFilter`/`FilterInput`, todos los
   filtros de texto ahora arman `UPPER(...) LIKE UPPER(:x)` con `%x%`. `FechaHasta`
   también estaba mal: acá comparaba `<= dateTo` a las 00:00:00 (excluía documentos del
   mismo día "hasta" creados después de medianoche); el original usa límite EXCLUSIVO del
   día siguiente (`< dateTo.AddDays(1)`) -- mismo criterio corregido acá.
3. **Faltaban 2 columnas y una estaba mal combinada** -- el original muestra `DocEntry`
   como primera columna y `Cliente`(código)/`Nombre cliente` como columnas SEPARADAS más
   `Sucursal entrega` (SAP `Address2` de cabecera, sin equivalente acá todavía); esta
   entrega tenía `Cliente` como una sola celda combinada (`"{code} — {name}"`) y no
   mostraba `DocEntry` ni `Sucursal entrega`. Agregado `DeliveryAddress` a
   `SalesDocumentSummaryDto` (mapea `Address2`), columnas reordenadas a la paridad exacta
   del original: DocEntry, N° documento, Cliente, Nombre cliente, Sucursal entrega,
   Fecha, N.° ref. cliente, Total, Estado, Vendedor.
4. **Filtro de fecha por defecto (ayer→hoy) faltaba en los 3 motores** -- el original
   fija `FechaDesde ??= Hoy.AddDays(-1)` / `FechaHasta ??= Hoy` en el primer ingreso sin
   filtro explícito, para no traer todo el histórico de una sola vez -- acá no existía en
   ninguno de los 3 (`IndexGeneric*ModelBase.OnGetAsync`). Agregado a los 3 (Venta/Compra/
   Inventario, pedido explícito del dueño del proyecto de que corra en los 3, no solo en
   Venta) -- único cambio de esta entrega que sí tocó Compras/Inventario, el resto
   (filtros/columnas nuevos) quedó **acotado a Venta a propósito**, como se pidió, hasta
   validar el resultado antes de replicar.

**Alcance explícito**: NO se tocó el Detalle de un documento individual (`SalesOrders/
Detail.cshtml`/`DetailGenericSalesDocumentModelBase`) en esta entrega -- lo que el dueño
del proyecto describió como "en el detalle se muestra..." es la lista de 10 columnas del
LISTADO (fila por documento), no el formulario de un documento individual; confirmado
contra `Columnas` de `IndexGenericoVentaModelBase.cs` del original, coincide 1:1 con lo
pedido.

`dotnet build` 0/0, **107/107 tests siguen en verde** (cambios de mapeo/filtro SQL y de
listado, sin lógica de negocio nueva con condicionales propios que ameriten tests
dedicados). Host relanzado con el build nuevo (`https://localhost:7207`) para que el
dueño del proyecto pueda reprobar `?page=2` y los filtros nuevos contra un ambiente SAP
real. **Pendiente explícito, no cerrado acá**: replicar las mismas 3 brechas de filtros/
columnas a Compras/Inventario -- decisión deliberada de esperar a que Venta se valide
primero (mismo criterio ya usado para las 3 fases del motor genérico en su momento).

## Bug real: migración `AddLicensingSignedToken` sin aplicar en Postgres dev (29 jul 2026)

Encontrado mientras se probaba el login/flujo real del plugin Rendiciones (ver
`PENDIENTE.md` de `Portal SaaS - Plugins\Modulo.Rendiciones`), pero es un bug de
plataforma, no del plugin: la base Postgres de desarrollo no tenía aplicada la
migración `20260730030738_AddLicensingSignedToken` (columna `signed_status_token` en
`organizations`) -- causaba un 500 real (`42703: column o.signed_status_token does not
exist`) al intentar crear **cualquier** usuario tenant desde
`/Admin/Organizations/Users/Create`, para cualquier organización, no solo Comercial
Depor. **Corregido**: `dotnet ef database update --project
src\PortalSaas.Data.Migrations.PostgreSql --startup-project
src\PortalSaas.Data.Migrations.PostgreSql` (usando el propio proyecto de migraciones
como startup -- el `PortalSaas.Host` no sirve como startup mientras el proceso está
corriendo, el `.exe` queda bloqueado por el propio proceso). Aplicar el mismo update
contra la base SQL Server de desarrollo si se usa esa organización para pruebas --
no verificado en esta sesión (solo se tocó Postgres, la base real de Comercial Depor).

## Causa raíz real de "nada de lo de hoy funciona" -- `.vscode/launch.json` apuntaba al build AnyCPU viejo -- 26 jul 2026 (mismo día)

El dueño del proyecto siguió reportando "la paginación sigue sin funcionar" incluso con
el Host relanzado a mano correctamente. Antes de seguir revisando lógica de binding,
se revisó CÓMO arranca el Host cuando el dueño del proyecto presiona F5 -- ahí estaba
el problema real, no en el código de paginación.

**`.vscode/launch.json`** (`configurations[0].program`) apuntaba a
`${workspaceFolder}/src/PortalSaas.Host/bin/Debug/net8.0/PortalSaas.Host.dll` -- la
carpeta **AnyCPU**, la misma que ya se identificó como stale en la entrega anterior
("Bug real de despliegue local"), pero esta vez el problema no era un proceso viejo
corriendo por accidente: **estaba escrito en la configuración de lanzamiento misma**.
El `preLaunchTask` ("build", `tasks.json`) sí corre `dotnet build PortalSaas.sln`
completo (x64 correcto, publica los plugins a `artifacts/plugins/` bien) -- pero
después de compilar, F5 SIEMPRE lanzaba el `.dll` de la carpeta AnyCPU, un artefacto
congelado desde antes de la migración a x64 que ningún comando actual vuelve a
escribir (confirmado: `dotnet build PortalSaas.sln` solo genera
`bin\x64\Debug\net8.0\` para `Host`/`Core`/`Tests`/plugins, nunca `bin\Debug\net8.0\`).
**Esto significa que absolutamente ninguno de los fixes de esta sesión (ni de sesiones
anteriores) se vio jamás probando con F5** -- el dueño del proyecto llevaba quién sabe
cuánto tiempo viendo siempre el mismo build viejo sin importar cuánto código se
corrigiera.

**Corregido**: `program` en `launch.json` ahora apunta a
`bin/x64/Debug/net8.0/PortalSaas.Host.dll`. Se borró además
`src/PortalSaas.Host/bin/Debug/` por completo (carpeta ignorada por git, contenido
100% regenerable) para que no quede la carpeta vieja ahí tentando a ejecutarla de
nuevo por error -- verificado con un `dotnet build PortalSaas.sln` después del borrado
que esa carpeta **no vuelve a crearse** (0/0, solo escribe en `bin\x64\`). `cwd` de
`launch.json` (`src/PortalSaas.Host/`) ya estaba bien -- ahí vive
`appsettings.Development.json`, así que la resolución del content root nunca fue el
problema, solo el `.dll` que se ejecutaba.

**Para confirmar de una vez que esto era la causa real y no una capa más de síntoma**:
el dueño del proyecto debe volver a probar F5 ahora -- si `?page=2` sigue sin traer
resultados distintos después de este fix, recién ahí hay que sospechar de la lógica de
binding de `PageNumber`/`Filter` en sí (revisada por lectura de código y parece
correcta: `[BindProperty(SupportsGet = true, Name = "page")]` sobre un `int`, sin
conflicto aparente con el fallback de prefijo vacío de `Filter`), no antes.

## La causa real de la paginación: "page" es una route-value reservada de Razor Pages -- 26 jul 2026 (mismo día)

Con el Host relanzado correctamente (fix de `launch.json` de la entrega anterior
verificado por el dueño del proyecto -- F5 sí levantaba el build nuevo), la
paginación seguía sin funcionar (`?page=2` quedaba pegado en la página 1). Esta vez
la causa no era el proceso ni el build -- era un bug real de ASP.NET Core, **NO
detectable por lectura de código, solo por prueba empírica**.

**Cómo se aisló**: se armó una app Razor Pages mínima nueva (`dotnet new webapp`,
retargeteada a `net8.0` para igualar el proyecto real) fuera de este repo, con un
`PageModel` que replica EXACTAMENTE el mismo patrón de
`IndexGenericSalesDocumentModelBase` (`[BindProperty(SupportsGet = true)] Filter` +
`[BindProperty(SupportsGet = true, Name = "page")] int PageNumber`), sin SAP/DB/
autenticación de por medio -- para poder probar `curl` directo sin sesión y aislar
la variable real. Resultado, confirmado con 4 rondas de prueba:
- `?page=2` con el modelo completo (Filter + PageNumber): `PageNumber` queda en 1.
- `?page=2` con un modelo que SOLO tiene `PageNumber` (sin `Filter`): **también**
  queda en 1 -- descarta que `Filter`/el fallback de prefijo vacío tuviera algo que
  ver.
- Se imprimió `Request.Query["page"]` directo (sin pasar por binding): devuelve
  `"2"` correctamente -- el valor SÍ llega en el query string, el problema es
  específicamente el binding de `[BindProperty(Name = "page")]`.
- Se cambió `Name = "page"` a `Name = "pageNum"` (mismo patrón exacto, sin tocar
  nada más) y `?pageNum=2` **bindeó perfecto al toque**.

**Causa raíz real**: Razor Pages usa internamente una route-value reservada llamada
literalmente **`"page"`** (`RouteData.Values["page"]`) para registrar qué archivo
`.cshtml` resolvió la ruta actual (la usa, entre otras cosas, `asp-page` para
generar links). El `CompositeValueProvider` de ASP.NET Core consulta la ROUTE DATA
antes que el QUERY STRING -- cualquier `[BindProperty(Name = "page")]` queda
tapado por esa route-value interna, **sin ninguna excepción ni warning visible**:
el binder simplemente no encuentra un valor numérico válido ahí y el `int` se queda
en su default. Ni la documentación de ASP.NET Core ni el compilador avisan de esto
-- es un choque de nombres puramente interno, invisible a menos que se pruebe
empírico como se hizo acá.

**Corregido en los 3 motores genéricos** (regla de paridad, mismo bug afecta a los
3 por igual): `[BindProperty(SupportsGet = true, Name = "page")]` →
`Name = "pageNumber"` en los 3 `IndexGeneric*ModelBase.cs`
(`IndexGenericSalesDocumentModelBase`/`IndexGenericPurchaseDocumentModelBase`/
`IndexGenericInventoryDocumentModelBase`), y `DocumentList/Default.cshtml`
(`BuildPageUrl`) actualizado a generar/filtrar la misma query key nueva
(`pageNumber=`, no `page=`). La variable LOCAL `pageNumber` de `BuildPageUrl` ya se
llamaba así por otra razón (evitar que Razor interprete el token `@page` como
directiva de página, ver el comentario de la entrega de esa vista) -- ahora
coincide también con la query key real, no es casualidad de nombres.

`dotnet build` 0/0, **107/107 tests siguen en verde**. App de prueba aislada
descartada al terminar (vivía fuera del repo, en el scratchpad de la sesión).
**Este fix debería resolver la paginación de una vez** -- a diferencia de las 2
entregas anteriores (que corrigieron problemas reales pero distintos: el código
nunca leía `page`/`pageSize`, y después el Host servía un build viejo), esta vez la
causa se aisló con evidencia empírica reproducible, no por lectura de código ni
suposición.

## Resumen total del documento (Modulo.Ventas) -- 26 jul 2026 (mismo día)

El dueño del proyecto confirmó que la paginación ya funciona, y pidió portar el
bloque "Resumen total" (Total antes del descuento/Descuento/Gastos adicionales/
Impuesto/Total del documento) que se ve al final del tab Contenido en el original,
con la MISMA lógica -- mostró una captura del original como referencia de diseño
(no una captura de este proyecto). Portado tal cual de
`DetalleGenericoVentaModelBase`/`_TabContenido.cshtml`
(`referencia-original/PortalSAP_v2`), acotado a `Modulo.Ventas` por ahora (mismo
criterio "un motor primero" de toda esta sesión).

**Cálculo, portado exacto** (`DetailGenericSalesDocumentModelBase.cs`, nuevas
propiedades computadas): `TotalBeforeDiscount` = suma de `Quantity * UnitPrice` de
las líneas del formulario; `Discount` = suma de `Quantity * UnitPrice *
DiscountPercent / 100`; `AdditionalExpenses` fijo en `0` (el original tampoco lo
calcula, sin tracking de gastos adicionales en ningún lado); `DocumentTotal` = el
`DocTotal` real de SAP si el documento ya existe, si no cae al subtotal
(`TotalBeforeDiscount - Discount`); `Tax` = lo que queda entre `DocumentTotal` y el
subtotal-menos-descuento -- por diseño da `0` mientras se crea un documento nuevo
(sin `DocTotal` todavía) y solo muestra el impuesto real una vez que el documento
existe en SAP y trae su propio `DocTotal`. Ninguna de estas 5 cifras se lee de SAP
directo (no hay `DiscSum`/`VatSum` en el DTO) -- se infieren algebraicamente de las
líneas + el total real, exactamente como lo hace el original.

**Markup**: bloque nuevo en `_TabContentVentas.cshtml`, después de la tabla de
líneas, usando las clases `.doc-resumen-total`/`.doc-resumen-total-fila`/
`.doc-resumen-total-final` que ya existían en `site.css` sin consumidor real desde
la entrega de rediseño visual. Se agregó una clase nueva,
`.doc-resumen-total-titulo` (portada de `.admin-subtitulo`, `theme.css:1174`, con
nombre propio en vez del genérico original -- acá no hay una clase "admin-subtitulo"
reusada en ningún otro lado). **Diferencia deliberada respecto al original**: sin el
JS de recálculo en vivo al tipear Cantidad/Precio/%Dto (`_TabContenido.cshtml` lo
tiene, actualiza los `<span>` por `id` en cada `input`/`change`) -- este proyecto no
tiene "Agregar línea" dinámico (filas pre-renderizadas, ver el comentario de
`_TabContentVentas.cshtml`), y el resumen ya se recalcula solo con cada
`OnGetAsync`/`OnPostAsync` normal del servidor; agregar JS de recálculo en vivo acá
sería una mejora no pedida sobre un patrón que este proyecto decidió no tener.

`dotnet build` 0/0, **109/109 tests en verde** (propiedades derivadas puras sin
condicionales de negocio nuevos, mismo criterio que otras entregas de esta sesión
sin tests xUnit dedicados). **Pendiente explícito, no cerrado acá**: replicar el
mismo resumen a `Modulo.Compras` (Compras también tiene precio/descuento por línea,
aplica igual) -- **no** a `Modulo.Inventario` (traslados no tienen precio ni
descuento, `.doc-resumen-total` no tendría qué mostrar ahí, mismo criterio de
paridad "considerar, no aplicar literal" ya establecido en `CLAUDE.md`). Sin
verificación visual en navegador -- mismo motivo de toda la sesión.

## Bug real: Cantidad/Precio/%Desc se veían vacíos en un documento existente -- cultura del servidor rompe `<input type="number">` -- 26 jul 2026 (mismo día)

El dueño del proyecto compartió una captura real de la Orden de Venta N° 612 (la
misma creada end-to-end contra Comercial GE2 en una entrega anterior): Artículo/
Descripción/Almacén/Cuenta Mayor se veían bien, pero **Cantidad, Precio Unitario y
% Desc. se veían completamente vacíos** -- pese a que el "Resumen total" (agregado
en la entrega anterior) mostraba cifras reales y correctas (1.000,00 antes del
descuento, 190,00 de impuesto, 1.190,00 total). Esa contradicción (el cálculo server-
side ve los valores, el input no los muestra) fue la pista real: el dato SÍ llega,
el problema es específicamente cómo se escribe en el HTML.

**Causa raíz confirmada**: `_TabContentVentas.cshtml` escribía
`value="@line.UnitPrice"`/`value="@(line.Quantity...ToString())"` **sin
`CultureInfo` explícito** -- `decimal.ToString()` sin argumentos usa la cultura del
**hilo actual**, que por defecto toma la cultura del sistema operativo del
servidor (casi seguro `es-CL`/`es-*` acá, no invariante). Para un valor como
`29652`, esa cultura renderiza `"29.652"` (`.` como separador de MILES, no
decimal) -- un `value` **inválido** para `<input type="number">` (el estándar
HTML5 exige formato invariante: `.` solo como separador decimal, sin separador de
miles). Los navegadores **descartan en silencio** un `value` inválido en un input
numérico -- sin error de consola, sin warning, el campo simplemente queda vacío.
El cálculo del "Resumen total" nunca pasó por esto (opera sobre los `decimal`
crudos, nunca los convierte a string), por eso mostraba las cifras reales mientras
los inputs de esas mismas líneas se veían en blanco.

**Corregido en los 3 motores** (mismo bug, mismo patrón exacto en los 3 --
`_TabContentVentas.cshtml`/`_TabContentCompras.cshtml`/`_TabContentInventario.cshtml`):
`value="@line.UnitPrice?.ToString(CultureInfo.InvariantCulture)"` y los dos
ternarios de Cantidad/%Desc con `.Value.ToString(CultureInfo.InvariantCulture)`
en la rama no-vacía. `@using System.Globalization` agregado a los 3 archivos.
**Nota importante, no se tocó**: el "Resumen total" (`.ToString("N2")`, texto de
solo lectura, no un `<input>`) sigue usando la cultura del servidor a propósito --
ahí SÍ es lo correcto/deseado (un usuario chileno espera ver "1.000,00", no
"1000.00"); la regla no es "nunca usar cultura del servidor", es específicamente
"nunca escribir un `decimal` con cultura no-invariante dentro de un
`value="..."` de `<input type="number">`", que es un requisito del estándar HTML,
no una preferencia de formato.

**Se aprovechó el mismo pase para cerrar 2 pendientes explícitos de la entrega
anterior** (pedido del dueño del proyecto: "aplica todos los cambios aplicados a
Modulo de Venta hacia Compra e Inventario"):
- **Resumen total portado a `Modulo.Compras`** (`DetailGenericPurchaseDocumentModelBase.cs`
  + `_TabContentCompras.cshtml`, mismas 5 propiedades/mismo markup que Ventas) --
  Compras también tiene precio/descuento por línea, aplica igual. **No** se portó a
  `Modulo.Inventario` (sin precio/descuento en traslados, ya documentado en la
  entrega anterior por qué no aplica ahí).
- Verificado que `page`→`pageNumber` y el fix de `returnUrl`/`BackUrl` (entregas
  anteriores) ya estaban consistentes en los 3 motores -- sin cambios adicionales
  necesarios ahí.

`dotnet build` 0/0, **109/109 tests en verde**. Sin verificación visual en
navegador -- mismo motivo de toda la sesión, aunque esta vez la causa se identificó
con alta confianza por la contradicción lógica entre el cálculo server-side (ve los
valores reales) y el render (los pierde) más el conocimiento del requisito de
formato de `<input type="number">` del estándar HTML5, no por prueba empírica como
el bug de paginación.

## Paridad de listado Compra/Inventario vs. Venta -- 26 jul 2026 (mismo día)

Pedido explícito del dueño del proyecto: "aplica todas las correcciones hechas en
generico_venta para generico_compra y generico_inventario". Cierra la brecha que la
entrega "Paridad Modulo.Ventas vs. el original" había dejado deliberadamente acotada
a Venta -- confirmado releyendo `IndexGenericoCompraModelBase.cs`/
`GenericoCompraService.ListarAsync` e `IndexGenericoInventarioModelBase.cs`/
`GenericoInventarioService.ListarAsync` reales del original antes de tocar nada, no
copiado mecánicamente de Venta (regla de paridad del `CLAUDE.md`: "considerar" no es
"aplicar literal sin pensar").

**Compras** -- el original (`FiltroGenericoCompra`) tiene los mismos 5 filtros que
Venta salvo Vendedor (Compra no tiene ese concepto): `ProveedorCardCode`/
`ProveedorNombre`/`NumAtCard`/`DocNum`/fechas, mismo comportamiento LIKE parcial
case-insensitive + `DocNum` como string + `FechaHasta` exclusiva del día siguiente
confirmado línea por línea contra `GenericoCompraService.cs:139-167`. Columnas del
listado original: DocEntry, N° documento, Proveedor, Nombre proveedor (columnas
SEPARADAS, no combinadas), Fecha, N.° ref. proveedor, Total, Estado -- sin "Sucursal
entrega" (Compra no tiene `Address2` en el listado original, a diferencia de Venta).
Aplicado:
- `PurchaseDocumentFilter` (Abstractions) ganó `SupplierName`/
  `SupplierReferenceNumber`; `DocNum` pasó de `int?` a `string?`.
- `PurchaseDocumentService.BuildWhereClause` reescrito con el mismo patrón LIKE
  parcial + `DocDate < :dateTo` (día siguiente) que `SalesDocumentService`.
- `IndexGenericPurchaseDocumentModelBase`: columna `Id` (DocEntry) primero,
  Proveedor/Nombre proveedor separados (antes `$"{Code} — {Name}"` combinado, mismo
  bug que tenía Venta antes de su propia entrega de paridad), filtros reordenados
  igual que el original, `FilterFieldType.Number` de `DocNum` pasó a `Text`.

**Inventario** -- el original (`FiltroGenericoInventario`) SÍ tiene filtros de socio
de negocio/almacén destino de cabecera (`SocioNegocioCardCode`/`SocioNegocioNombre`/
`AlmacenDestinoCodigo`) porque su `OWTQ`/`OWTR` real tiene esos campos -- **nuestro
motor no los tiene a propósito** (ver `InventoryDocumentDto`, decisión ya tomada:
sin cliente/vendedor, almacén origen/destino es por línea, no de cabecera), así que
esos 3 filtros del original **no se portan** -- no hay campo equivalente que
filtrar, portarlos habría sido inventar una feature nueva (socio de negocio
opcional en cabecera), no una corrección de paridad. Lo que sí es una corrección de
paridad real, aplicado:
- `InventoryDocumentFilter.DocNum` pasó de `int?` a `string?` (LIKE parcial, mismo
  criterio que los otros 2 motores).
- `InventoryDocumentService.BuildWhereClause`: mismo fix `DocDate < :dateTo` (día
  siguiente) + `DocNum` como `TO_VARCHAR(...) LIKE`.
- `IndexGenericInventoryDocumentModelBase`: columna `Id` (DocEntry) agregada
  primero (el original la tiene, acá no existía en absoluto), filtro `DocNum` de
  `FilterFieldType.Number` a `Text`.

`dotnet build` 0/0, **109/109 tests siguen en verde** (cambios de filtro/columna sin
lógica de negocio nueva, mismo criterio que la entrega de Venta). Sin verificación
visual en navegador -- mismo motivo de toda la sesión. **Pendiente explícito, fuera
de alcance a propósito**: el "Resumen total" (ya portado a Venta y Compra en la
entrega anterior) no aplica a Inventario -- sigue sin precio/descuento por línea,
razón ya documentada.

## Conexión a bases de datos externas de plugin -- 26 jul 2026

Resuelve el hueco documentado hasta hoy como "diferido a propósito (YAGNI)"
(`ISqlServerService`/bases SQL Server externas no-SAP de un plugin) — surgió como
prerrequisito real al extraer `Modulo.Rendiciones` (rendición de gastos corporativos,
nombre interno que evita cualquier marca comercial existente en el mercado) como
plugin **externo** (repo propio, `Portal SaaS - Plugins\Modulo.Rendiciones` — ruta
histórica al momento de esta entrega: `C:\PROYECTOS\Modulo.Rendiciones`, ya movida,
ver "Carpeta general del proyecto" más arriba —, compilado aparte y
copiado a `artifacts/plugins/` de este portal, ver `docs/09-GUIA-DESARROLLO-PLUGINS.md`).
A diferencia del original (`ISqlServerService` de `PortalSAP_v2`, solo SQL Server),
acá es **motor dual desde el día uno** (Postgres/SQL Server, mismo criterio que la base
propia de la plataforma) — un plugin no puede asumir qué motor tiene la organización
que lo usa.

- **`ModuleExternalConnection`** (`PortalSaas.Data.Entities`, tabla nueva
  `module_external_connections`, migrada y **aplicada con éxito contra Postgres y SQL
  Server reales de desarrollo**) -- fila por `(organization_id, company_id, module_code)`
  con `engine_type`/`host`/`port`/`database_name`/`technical_username`/
  `technical_secret_key` (cifrado AES-256-GCM, mismo patrón write-only que
  `Instance`/`Company`). `company_id` nullable: fila puntual por compañía o fila global
  de la organización (fallback). FK a `Company` con `DeleteBehavior.Restrict` (mismo
  motivo que `Company.Organization`: ya alcanzable en cascada vía `Organization`, un
  segundo camino en cascada rompe SQL Server con el error 1785 ya conocido de otras
  entregas). **Sin pantalla de administración todavía** -- se carga hoy por acceso
  directo a la base, mismo estado inicial que tuvieron `Plans`/`Subscriptions` antes de
  su UI; queda pendiente para cuando haga falta de verdad (`Modulo.Rendiciones` todavía
  no tiene servicios ni páginas portados, ver su propio `PENDIENTE.md`).
- **`IExternalDatabaseConnectionService`** (`PortalSaas.Abstractions.Contratos`,
  implementado en `PortalSaas.Core.Infraestructura.ExternalDatabaseConnectionService`)
  -- `ResolveConnectionAsync(moduleCode, organizationId, companyId?)` devuelve
  `ExternalDatabaseConnection` (`EngineType` + `ConnectionString` ya armado, motor
  resuelto según la fila encontrada). Sin caché (a diferencia del `SqlServerService`
  del original, que cacheaba 10 minutos) -- primer consumidor real, YAGNI hasta que el
  volumen lo justifique. `PortalSaas.Core` ganó una referencia nueva a `Npgsql` (el
  driver base, no el provider de EF Core -- ese solo vive en
  `PortalSaas.Data.Migrations.PostgreSql`) para poder armar el connection string
  Postgres con `NpgsqlConnectionStringBuilder`, mismo criterio dual que ya tenía
  `Microsoft.Data.SqlClient` para la rama SQL Server.
- **Consumo real en `Modulo.Rendiciones`** (repo externo): su `.csproj` referencia los
  DOS proveedores de EF Core (`Microsoft.EntityFrameworkCore.SqlServer` +
  `Npgsql.EntityFrameworkCore.PostgreSQL`) y `ModuloRendiciones.RegisterServices`
  registra su `RendicionesDbContext` eligiendo `UseNpgsql`/`UseSqlServer` en runtime
  según `ExternalDatabaseConnection.EngineType` -- nunca fijo en el código del plugin.
  Su `DbContext` no usa `HasColumnType("decimal(...)")` en ninguna columna (reemplazado
  por `HasPrecision(p, s)`, agnóstico de proveedor) -- mismo criterio que
  `PortalSaasDbContext` ya exige para la base compartida. **Regla nueva agregada a
  `docs/09-GUIA-DESARROLLO-PLUGINS.md` §6.1**: cualquier plugin con base propia debe
  seguir este mismo patrón de motor dual, sin excepción -- no asumir que "es la base de
  un plugin, no la de la plataforma" habilita fijar un solo proveedor.
- **Verificado**: `dotnet build PortalSaas.sln` en 0/0, **109/109 tests siguen en
  verde** (sin tests nuevos dedicados -- resolución de connection string sin lógica de
  negocio computada, mismo criterio que otras entregas de conexión/credenciales de esta
  sesión). Migraciones aplicadas de verdad contra Postgres y SQL Server locales de
  desarrollo (no solo generadas). `Modulo.Rendiciones` (repo externo) compila 0/0 contra
  el `IExternalDatabaseConnectionService` real -- ya no queda ningún bloqueo para que su
  `RendicionesDbContext` se registre de verdad; el trabajo pendiente ahí es la fase de
  servicios/páginas (ver su propio `PENDIENTE.md`), no infraestructura de conexión.

## Bug real de plataforma: `PluginLoadContext` duplicaba ensamblados del framework compartido -- 27 jul 2026

Encontrado recién al verificar `Modulo.Rendiciones` (repo externo) cargando de verdad
en `PluginManager`, contra el Host real -- **afecta a cualquier plugin futuro con
dependencias NuGet propias**, no es específico de Rendiciones, así que el fix va acá
(`PortalSaas.Core`), no en el plugin.

**Síntoma real**: `PluginManager` logueaba `"El assembly Modulo.Rendiciones no
implementa IModuloPortal, se carga solo como vistas"` y el Host crasheaba con
`ReflectionTypeLoadException` al mapear Razor Pages -- causa raíz:
`System.TypeLoadException: Method 'RegisterServices' ... does not have an
implementation`.

**Causa real**: `Modulo.Rendiciones.csproj` tiene `CopyLocalLockFileAssemblies=true`
(necesario para que los providers de EF Core -- SqlServer/Npgsql -- estén físicamente
al lado del `.dll` cuando `PluginManager` lo carga desde una carpeta suelta, mismo
motivo ya documentado para `Modulo.SellOut`/`Modulo.GestionDistribucionGastos` en el
original). Ese flag copia TODAS las dependencias transitivas del plugin a su propia
carpeta -- incluida `Microsoft.Extensions.DependencyInjection.Abstractions.dll`
(arrastrada por los providers de EF Core), que normalmente debería venir del framework
compartido (`FrameworkReference Include="Microsoft.AspNetCore.App"`). `PluginLoadContext`
(`PortalSaas.Core.Infraestructura`) solo forzaba a compartir `PortalSaas.Abstractions`
con el `Default` context -- con la copia duplicada de `DependencyInjection.Abstractions`
en su propia carpeta, el plugin terminaba con un tipo `IServiceCollection` "distinto"
(misma forma, identidad de ensamblado diferente) al que usa el Host, y el runtime
rechazaba `RegisterServices(IServiceCollection)` como si no tuviera implementación.
**Ningún plugin anterior lo había disparado** porque ninguno traía dependencias NuGet
propias con este patrón de despliegue -- `Modulo.Rendiciones` es el primero.

**Corregido en `PluginLoadContext.Load`**: antes de resolver un ensamblado desde la
carpeta propia del plugin, se chequea si ya hay un ensamblado con ese nombre cargado en
`AssemblyLoadContext.Default` (el Host) -- si lo hay, se delega ahí (`return null`),
igual criterio que ya existía para `PortalSaas.Abstractions`, generalizado a cualquier
ensamblado. Esto es seguro porque para el momento en que `PluginManager` carga plugins,
el Host ya cargó todo lo que compone su propio proceso (DI, logging, EF Core base,
etc.) -- solo dependencias verdaderamente privadas del plugin (ej. `Azure.AI.
DocumentIntelligence`, providers específicos de EF Core que el Host no usa) siguen
resolviéndose desde la carpeta del plugin, sin cambios ahí.

**Verificado de punta a punta contra el Host real** (no solo `dotnet build`): con el
fix, `Módulo Rendiciones v1.0.0 cargado (15 entradas de menú)` -- coincide exactamente
con las 15 entradas declaradas en `ModuloRendiciones.GetMenu()` (1 raíz + 3 grupos + 11
hojas). **107→109 tests de `PortalSaas.Core.Tests` siguen en verde** (sin tests nuevos
dedicados -- es un fix de resolución de ensamblados en tiempo de carga, no lógica de
negocio testeable con EF Core InMemory).

## Bug real en `Modulo.Rendiciones`: sin `[Authorize]`, un request anónimo crasheaba con 500 -- 27 jul 2026

Encontrado en la misma verificación E2E: `curl` sin sesión a `/rendiciones/gastos`
devolvía `500` en vez del `302` a `/Account/Login` esperado. Causa: `RendicionesPageModelBase`
(y `ComprobanteAccesoBase`) no llevaban `[Authorize]` -- a diferencia de
`Modulo.Ventas`/`Modulo.Administracion`, que sí lo tienen en su PageModel base
respectivo (no hay una convención global `AuthorizeFolder` en `Program.cs` del Host,
cada plugin es responsable de anotar su propia base). Sin `[Authorize]`, el middleware
de autorización dejaba pasar el request anónimo directo al handler, que crasheaba
porque `RendicionesDbContext` exige `ICurrentUserContext.OrganizationId` (lee un claim
que no existe sin sesión) para resolver su connection string. **Corregido** (en el repo
externo, no acá) agregando `[Authorize]` a las dos clases base de página del plugin --
verificado de nuevo con `curl`: las 5 rutas probadas devuelven `302` sin sesión.
**Nota para `docs/09-GUIA-DESARROLLO-PLUGINS.md`**: agregar esto al checklist -- no
hay enforcement automático de autenticación para plugins, cada uno debe anotar su
propio PageModel base con `[Authorize]` explícitamente.

## Bug real de plataforma: `_Layout.cshtml` no renderizaba la sección `"Styles"` -- 27 jul 2026

Encontrado en la verificación E2E en navegador de `Modulo.Rendiciones` (usuario de
prueba real, `demo_rendiciones`/Comercial Depor): `/rendiciones/gastos` crasheaba con
`InvalidOperationException: The following sections have been defined but have not
been rendered ... 'Styles'`. Causa: `src/PortalSaas.Host/Pages/Shared/_Layout.cshtml`
nunca tuvo un `@await RenderSectionAsync("Styles", ...)` -- ningún plugin anterior
había necesitado `@section Styles { ... }` en su `<head>` (los CSS de plugin, cuando
existían, se referenciaban distinto). `Modulo.Rendiciones` es el primer plugin que usa
ese patrón (`wwwroot/css/rendiciones.css` propio, `ServesOwnWwwRoot=true`). **Corregido**
agregando `@await RenderSectionAsync("Styles", required: false)` al final del `<head>`
-- aditivo, `required: false` no rompe ninguna página existente que no defina la
sección. Verificado reiniciando el Host real y confirmando que
`/rendiciones/gastos` ya no crashea.

## Bug real de plataforma: `ServesOwnWwwRoot` nunca estaba implementado -- 27 jul 2026

Encontrado en la misma verificación E2E de `Modulo.Rendiciones`: `~/css/rendiciones.css`
devolvía 404 en silencio (el `<link>` fallido no genera ningún error visible más allá
de que el diseño no cambia). Causa: `IModuloPortal.ServesOwnWwwRoot` y
`PluginManager.AssembliesConWwwRootPropio` existían desde el portado inicial ("el host
futuro lo usará para servir su wwwroot embebido"), pero **nada en `Program.cs` los
consumía todavía** -- ningún plugin anterior había declarado `ServesOwnWwwRoot = true`
con contenido real, así que nunca se notó. **Corregido**: `Program.cs` ahora registra
un `app.UseStaticFiles(...)` adicional por cada assembly en
`AssembliesConWwwRootPropio`, con `FileProvider = new
ManifestEmbeddedFileProvider(assembly, "wwwroot")`.

**Segundo detalle real, no obvio**: el manifiesto embebido (`GenerateEmbeddedFilesManifest`)
conserva `"wwwroot"` como carpeta raíz dentro de sus rutas (`wwwroot/css/x.css`) -- a
diferencia del wwwroot físico del propio Host, donde esa carpeta nunca aparece en la
URL porque es la raíz que se le pasa al file provider por fuera. El primer intento
(`new ManifestEmbeddedFileProvider(assembly)`, sin segundo parámetro) seguía dando 404
-- hace falta el overload con el segundo parámetro `"wwwroot"` para acotar el provider
a esa subcarpeta y que `~/css/rendiciones.css` resuelva. `PortalSaas.Host.csproj` ganó
la referencia a `Microsoft.Extensions.FileProviders.Embedded` (antes solo la tenían
los plugins que la necesitaban para SÍ MISMOS empaquetar su wwwroot, nunca el Host
para consumirla).

**Verificado con `curl` real contra el Host corriendo**: `GET /css/rendiciones.css` →
`200`, 17.891 bytes (el contenido real del CSS, no una página de error). Sin
verificación visual en navegador todavía.

## Densidad visual estándar (regla dura, 27 jul 2026)

Pedido explícito del dueño del proyecto: los formularios se veían "muy grandes" en los
motores genéricos de documento -- la corrección **no es puntual de un módulo, es una
regla de diseño para todo el proyecto**. Todo formulario cabecera/detalle de cualquier
módulo (backoffice de plataforma en `/Admin/*`, self-service de organización en
`/organizacion/*`, y los 3 motores genéricos de documento Venta/Compra/Inventario)
**debe compartir la misma densidad, tipografía, color y comportamiento** -- nunca un
tamaño de fuente, padding, buscador o layout propio por módulo nuevo.

- **Fuente única de la densidad: `site.css`**, bloque "Compactación global" (justo
  después de los 4 `[data-theme]` y antes de `.btn-primary`) -- redefine
  `.form-control`/`.form-select`/`.btn`/`.table`/`.card-ps`/`.h3`/`.h5` una sola vez
  para TODO el proyecto (Bootstrap 5.1 vendored es demasiado espaciado por defecto
  para una grilla de datos tipo SAP B1). Un módulo nuevo **nunca** define su propio
  tamaño de fuente/padding para un input, botón o tabla -- hereda esto automáticamente
  con clases estándar de Bootstrap (`.form-control`, `.btn`, `.table`), igual que ya
  hereda el tema activo (`[data-theme]`) sin hacer nada.
- **Grilla de campos de un documento**: `.doc-tab-seccion .form-row .form-group`
  (celda con borde/fondo propio, label 10px arriba/valor 12.5px abajo) es el único
  patrón para "cabecera de documento" -- ver Modulo.Ventas/Compras/Inventario
  `_TabGeneral*.cshtml` como referencia. No inventar un layout de campos alternativo.
- **Tabla de líneas de detalle**: `.line-items-table` (encabezado mayúscula chico
  gris, sin relleno de color) + `document-lines-editor.js` (+ Agregar línea/Quitar/
  Vaciar todas las líneas/Descargar plantilla CSV/Importar CSV, recálculo en vivo del
  Resumen total) + `catalog-search.js` (buscador `data-catalogo-buscador`-like contra
  un handler AJAX, delegado en `document` para que también funcione en filas
  agregadas dinámicamente) son el único patrón para "detalle de líneas" -- portados
  primero en Modulo.Ventas, replicados literal (mismos nombres de clase/función,
  solo cambia el prefijo del id) en Compras/Inventario. Un motor de documento futuro
  reusa los mismos 2 scripts, no escribe los suyos.
- **Bloqueo de campos por tipo de línea** (Artículo/Servicio, Venta/Compra): mismo
  mecanismo (`wireDocumentLinesEditor({ lineType: {...} })`, clases
  `.line-item-only`/`.line-service-only`) en los dos motores que tienen el concepto
  -- Inventario no lo tiene (traslados no distinguen Artículo/Servicio, la
  particularidad real que sí justifica no aplicar esto ahí, ver la regla de paridad
  más abajo).
- Antes de agregar CSS/JS nuevo para una pantalla de formulario, **primero revisar si
  ya existe la clase/función equivalente acá** -- la señal de que algo está mal es
  escribir un `padding`/`font-size` a mano en un `.cshtml` nuevo en vez de reusar una
  clase ya definida en `site.css`.
- **Tabs de sección de un documento (`.doc-tabs`/`.doc-tab-btn`)**: pill switcher
  segmentado (contenedor con fondo tenue + borde, cada tab una pastilla; la activa
  se pinta con `--accent` sólido y texto `--on-accent`) -- no volver al subrayado
  plano ni inventar otro estilo de tabs por módulo.
- **Botones outline (`.btn-outline-secondary`/`.btn-outline-danger`/
  `.btn-outline-primary`)**: fondo tenue de color (8%) en reposo, fill sólido +
  sombra de acento en hover -- nunca transparente puro con solo un borde (look de
  "link con borde" descartado explícitamente). `.btn`/`.btn-sm` llevan
  `border-radius`/`font-weight: 600`/transición ya definidos globalmente, no
  redeclarar esto por botón.
- **Barra de acciones de una tabla de líneas (`.admin-row-actions`)**: `display:flex`
  + `gap: 8px` -- cualquier grupo de botones de acciones nuevo (no solo líneas de
  documento) reusa esta clase en vez de depender del espaciado por defecto entre
  elementos inline.

## Estilo de código

- Comentarios, mensajes de log y texto de UI: **en español** (mismo criterio que
  `PortalSAP_v2`).
- Nombres de tablas/columnas: `snake_case` minúsculas, sin comillas, en los dos
  motores (ver `docs/01-CONVENCION-NOMBRES-BD.md`).
- No hay CI configurado todavía — no asumir que hooks de pre-commit corren nada.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
