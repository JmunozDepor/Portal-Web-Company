# CLAUDE.md — Proyecto Saas Portal

Contexto persistente para Claude Code en este repositorio. Para el razonamiento
completo (por qué, no solo qué) ver `ARCHITECTURE.md` y `docs/` — este archivo es el
resumen operativo y las reglas duras, no lo dupliques ahí.

**Antes de portar o consultar código de `PortalSAP_v2`/`WMS_Suite`**, usar la copia de
`referencia-original/` (no los repos reales en `C:\PROYECTOS\PortalSAP_v2` /
`C:\PROYECTOS\WMS_Suite`) — es de solo lectura, tomada el 24 jul 2026, ver
`ARCHITECTURE.md` §-1 para el detalle.

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
- **`GestionDistribucionGastos` y `SellOut` NO se portan** — son desarrollo a medida
  de Comercial Depor (confirmado explícitamente por el dueño del proyecto), no
  funcionalidad de plataforma. Si un cliente nuevo necesita algo similar, se construye
  como módulo genérico configurable, nunca como copia con nombres distintos.

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
- **Diferido a propósito (YAGNI)**: `ISqlServerService`/`SqlServerService` (bases SQL
  Server externas *no-SAP* de un plugin) -- no existe todavía el equivalente de
  `MODULO_INSTANCIA_EXTERNA` en este esquema ni un plugin real que lo necesite.

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

**Todavía no existe** (ver `ARCHITECTURE.md` §6, pasos 6-7): el resto del motor
`GenericoVenta` (Nota de Crédito/Facturas/Devoluciones -- de solo lectura en la
referencia), `GenericoCompra`/`GenericoInventario`, y el motor de aprobación -- ninguno
tiene todavía un consumidor real en este proyecto; generalizarlos ahora sería especular
sin necesidad concreta (mismo criterio que ya cerró el alcance de `Modulo.Ventas`).
Líneas de tipo Servicio en `Modulo.Ventas` tampoco existen todavía (ver el hallazgo de
diseño documentado arriba). El filtrado del árbol de menú por módulos contratados
(`organization_modules`) tampoco existe -- hoy el árbol de menú es el mismo para todas
las organizaciones sin relación con qué módulos tiene contratados cada una. Todas las
migraciones (comercial + núcleo) ya se probaron contra un motor real de desarrollo
(Postgres 16 en Docker, SQL Server 2022 Express local) -- lo que falta es correrlas
contra `sqlsap.cdepor.cl` en producción, ver `docs/05-RUNBOOK-PRODUCCION.md` para el
procedimiento completo cuando se decida desplegar ahí.

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
