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
│                                               SeedFixedActions) + su propio
│                                               appsettings.Development.json.
├── PortalSaas.Data.Migrations.SqlServer/    # ídem, UseSqlServer. InitialCreate
│                                               consolidado (24 jul 2026) -- las 5
│                                               migraciones anteriores nunca llegaron
│                                               a aplicarse contra una base real, se
│                                               regeneraron limpias; desde ahí sigue
│                                               igual que Postgres (mismo nombre en
│                                               los dos: FixCompanyOrganizationCascade,
│                                               AddCoreMenuAndPermissions,
│                                               SeedFixedActions).
└── PortalSaas.Core/                         # Implementación real.
    ├── Seguridad/
    │   ├── SecretoCifradoService.cs          #   AES-256-GCM, portado tal cual.
    │   ├── PasswordHasher.cs                 #   PBKDF2-SHA256, portado tal cual.
    │   ├── AuthenticationService.cs          #   Nuevo -- login + bloqueo por intentos.
    │   ├── PlatformAdminAuthenticationService.cs #   Nuevo -- login del administrador
    │   │                                            de plataforma (sin organización).
    │   └── PasswordResetService.cs           #   Nuevo -- recuperación por correo.
    ├── Usuarios/UserPreferenceService.cs      #   Nuevo -- preferencias personales.
    ├── Correo/                                #   Nuevo -- envío de correo dual
    │   ├── EmailSenderService.cs              #     (Google Workspace/Microsoft 365).
    │   ├── Microsoft365EmailSender.cs         #     Graph API (client credentials).
    │   ├── GoogleWorkspaceEmailSender.cs      #     Gmail API (cuenta de servicio +
    │   │                                            delegación de dominio).
    │   ├── MicrosoftGraphPayloadBuilder.cs    #     Puro, sin HTTP (testeable).
    │   ├── GmailMessageBuilder.cs             #     Puro, sin HTTP (testeable).
    │   └── GoogleServiceAccountJwtBuilder.cs  #     Puro, sin HTTP (testeable).
    ├── Infraestructura/PluginLoadContext.cs   #   AssemblyLoadContext aislado, portado
    │              PluginManager.cs            #   tal cual (bug de orden de versión ya
    │                                            corregido); AHORA cableado en
    │                                            Program.cs (antes no lo estaba).
    │              MenuSyncService.cs          #   Nuevo -- upsert de `menus` desde
    │                                            IModuloPortal.GetMenu(), con la fase
    │                                            de desactivación de huérfanos.
    └── Comercial/ContractLimitService.cs      #   Implementación real de
                   OrganizationAccessGateService.cs #   IContractLimitService.
                                                  Nuevo -- gate subscriptions (saas) /
                                                  on_premise_licenses (on_premise).

tests/
└── PortalSaas.Core.Tests/                    # xUnit + EF Core InMemory. **62 tests,
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
│   │   └── ResetPassword.cshtml(.cs)
│   ├── Home/
│   │   ├── Index.cshtml(.cs)                  # [Authorize] (esquema tenant).
│   │   └── Preferences.cshtml(.cs)            # [Authorize] IUserPreferenceService.
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
│       │   │   └── Create.cshtml(.cs)          #   usuario de cada cliente, no hay
│       │   │                                     autoregistro ni self-service
│       │   │                                     todavía.
│       │   ├── Subscriptions/                  # Asignar un plan a una
│       │   │   ├── Index.cshtml(.cs)           #   organización (historial de
│       │   │   ├── Create.cshtml(.cs)          #   subscriptions, no solo la
│       │   │   └── Edit.cshtml(.cs)            #   vigente) -- solo orgs modo "saas".
│       │   └── Licenses/                       # Emitir licencia on-premise (nuevo,
│       │       ├── Index.cshtml(.cs)           #   24 jul 2026) -- solo orgs modo
│       │       ├── Create.cshtml(.cs)          #   "on_premise". Clave de activación
│       │       └── Edit.cshtml(.cs)            #   generada en servidor, nunca a mano.
│       └── Plans/                              # Catálogo de planes (nuevo, 24 jul
│           ├── Index.cshtml(.cs)               #   2026) -- código, límites,
│           ├── Create.cshtml(.cs)              #   precio. Independiente de
│           └── Edit.cshtml(.cs)                #   organizations, referenciado
│                                                  desde Subscriptions.
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
no sobre-construir sin un plugin real que lo ejercite): UI de administración de
`menu_groups`/`profiles`/`actions` (asignar perfiles/grupos a usuarios), y el
filtrado de visibilidad de menú por módulos contratados de la organización
(`organization_modules`) -- hoy el árbol de menú es el mismo para todas las
organizaciones, sin relación todavía con qué módulos tiene contratados cada una.

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

**Todavía no existe** (ver `ARCHITECTURE.md` §6, paso 6): el conector SAP
(`HanaService`/`SapConnectionProvider`/traductor) y los motores genéricos de
documento (`GenericoVenta`/`Compra`/`Inventario`) — son SAP-específicos y no
tienen todavía un consumidor real en este proyecto (ningún plugin real cargado
todavía); portarlos ahora sería especular sin necesidad concreta. Las tablas
núcleo (`menus`/`profiles`/`actions`/...) y su sincronización SÍ están listas
(ver más arriba) — lo que falta ahí es la UI de administración
(`menu_groups`/`profiles`/asignación a usuarios) y el filtrado por módulos
contratados, no la base. Todas las migraciones (comercial + núcleo) ya se
probaron contra un motor real de desarrollo (Postgres 16 en Docker, SQL Server
2022 Express local, 24 jul 2026) — lo que falta es correrlas contra
`sqlsap.cdepor.cl` en producción, ver `docs/05-RUNBOOK-PRODUCCION.md` para el
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
