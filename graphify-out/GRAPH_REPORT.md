# Graph Report - .  (2026-07-24)

## Corpus Check
- 172 files · ~50,937 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 981 nodes · 1628 edges · 103 communities (68 shown, 35 thin omitted)
- Extraction: 96% EXTRACTED · 4% INFERRED · 0% AMBIGUOUS · INFERRED: 72 edges (avg confidence: 0.8)
- Token cost: 164,467 input · 0 output

## Community Hubs (Navigation)
- Núcleo Auth/Datos/Admin (mixto)
- Límites de Contrato (IContractLimitService)
- Preferencias de Usuario
- Envío de Correo — Gmail/MIME
- Hash de Contraseñas (PBKDF2)
- Cifrado de Secretos (AES-256-GCM)
- Carga de Plugins (AssemblyLoadContext)
- Envío de Correo / Recuperación de Contraseña
- Configuración de Lanzamiento (launchSettings)
- Resultado de Autenticación
- Login de Administrador de Plataforma
- JWT de Cuenta de Servicio Google
- Decisión: Motor Dual PostgreSQL/SQL Server
- Entidades Company/Instance
- Tests de Autenticación de Admin
- Modelo Core Comercial (Tablas)
- Visión General del Proyecto / Huecos vs PortalSAP_v2
- Snapshot del Modelo EF (Migraciones)
- Autenticación / Acceso por Organización
- Factory de DbContext en Tiempo de Diseño
- Entidad OnPremiseLicense
- Entidad Subscription
- Convención de Nombres de BD
- Autenticación y Preferencias — Resumen
- Módulos Comerciales y Modelo de Negocio
- jQuery Validation Unobtrusive (JS)
- Páginas Home/Logout (Host)
- Página Crear Suscripción (code-behind)
- Páginas Error/Index (Host)
- Migraciones EF — Designer (PostgreSQL)
- Dependencias del Proyecto de Tests
- Página Editar Organización (code-behind)
- Reglas Duras y Próximos Pasos
- Proyecto de Migraciones PostgreSQL
- Proyecto de Migraciones SQL Server
- CLAUDE.md — Estructura y Licencias de Terceros
- Herramienta EmailSmokeTest
- Entidad Plan / Página Índice de Planes
- Entidad PlatformModule
- Página Crear Licencia (code-behind)
- Página Crear Usuario (code-behind)
- jQuery Validation Unobtrusive (minificado)
- Validación de Respuesta HTTP
- Proyecto Host (csproj)
- Página Crear Organización (code-behind)
- Página Editar Licencia (code-behind)
- Página Editar Suscripción (code-behind)
- Página Editar Plan (code-behind)
- Migración InitialCreate (SQL Server)
- Proyecto Core (csproj)
- Entidad Profile
- Entidad Menu
- Entidad MenuGroup
- Entidad Organization
- Migración InitialCreate (PostgreSQL)
- Migración AddAuthenticationAndPreferences
- Migración AddEmailSettings
- Migración AddOrganizationSlug
- Migración AddPlatformAdmins
- Migración FixCompanyOrganizationCascade
- Página Crear Plan (code-behind)
- Stub de HttpClientFactory (Tests)
- Proyecto Data (csproj)
- Proyecto Abstractions (csproj)
- Constantes PortalActions
- Entidad PlatformAdmin
- Migración InitialCreate — Designer (PostgreSQL)
- Migración AddEmailSettings — Designer
- Migración AddPlatformAdmins — Designer
- Migración FixCompanyOrganizationCascade — Designer
- Página Índice de Organizaciones (code-behind)
- Tests de Deserialización de ProviderConfig
- Vista Preferences
- Vista Editar Organización
- Vista Crear Licencia
- Vista Editar Licencia
- Vista Índice de Licencias
- Vista Crear Suscripción
- Vista Editar Suscripción
- Vista Índice de Suscripciones
- Vista Crear Usuario
- Vista Índice de Usuarios
- Vista Editar Plan
- Vista Error
- Vista Índice Raíz
- Vista Recuperar Contraseña
- Vista Login de Tenant
- Vista Logout de Tenant
- Vista Restablecer Contraseña
- Vista Login de Admin
- Vista Logout de Admin
- Vista Crear Organización
- Vista Índice de Organizaciones
- Vista Crear Plan
- Vista Índice de Planes
- Vista Home

## God Nodes (most connected - your core abstractions)
1. `PortalSaasDbContext` - 57 edges
2. `PortalSaas.Data.Entities` - 51 edges
3. `PortalSaas.Data` - 45 edges
4. `PortalSaas.Abstractions.Modelos` - 30 edges
5. `PortalSaas.Abstractions.Contratos` - 26 edges
6. `Organization` - 25 edges
7. `Modelo Core — Capa Comercial` - 25 edges
8. `CLAUDE.md — contexto persistente del repo Proyecto Saas Portal` - 17 edges
9. `PortalSaas.Core.Correo` - 14 edges
10. `PortalSaas.Core.Seguridad` - 14 edges

## Surprising Connections (you probably didn't know these)
- `4 límites de conectividad SAP al escalar a SaaS multiempresa (sesión SL en memoria, vecino ruidoso, red directa, TrustServerCertificate=true)` --semantically_similar_to--> `Reglas que no se negocian (organization_id, convención BD, secretos, TLS, tests, límites de plan en código)`  [INFERRED] [semantically similar]
  docs/00-HISTORIAL-DECISIONES.md → CLAUDE.md
- `Historial de decisiones — previo a este repo (Proyecto Menoja)` --conceptually_related_to--> `referencia-original/ — copia de solo lectura de PortalSAP_v2 y WMS_Suite`  [INFERRED]
  docs/00-HISTORIAL-DECISIONES.md → ARCHITECTURE.md
- `CLAUDE.md — contexto persistente del repo Proyecto Saas Portal` --conceptually_related_to--> `Licencia MIT — jQuery (OpenJS Foundation)`  [INFERRED]
  CLAUDE.md → src/PortalSaas.Host/wwwroot/lib/jquery/LICENSE.txt
- `Estructura de código real (src/PortalSaas.*, tests/, PortalSaas.Host)` --conceptually_related_to--> `Servicio Postgres 16 de desarrollo local (docker-compose)`  [INFERRED]
  CLAUDE.md → docker-compose.yml
- `Tabla platform_admins — operador de la plataforma, sin organization_id, email único global` --conceptually_related_to--> `Estado actual del proyecto (24 jul 2026) — pasos 1-4 hechos, backoffice admin, usuarios, planes`  [INFERRED]
  docs/03-MODELO-CORE-COMERCIAL.md → CLAUDE.md

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Esquema de la capa comercial (docs/03-MODELO-CORE-COMERCIAL.md)** — docs_03_modelo_core_comercial_platform_admins, docs_03_modelo_core_comercial_organizations, docs_03_modelo_core_comercial_plans, docs_03_modelo_core_comercial_subscriptions, docs_03_modelo_core_comercial_on_premise_licenses, docs_03_modelo_core_comercial_usage_metrics, docs_03_modelo_core_comercial_platform_modules [EXTRACTED 1.00]
- **Los 4 huecos reales para que PortalSAP_v2 sea vendible como producto** — docs_00_historial_decisiones_cuatro_huecos, architecture_cuatro_huecos, docs_00_historial_decisiones_hallazgos_seguridad_wms_suite, docs_00_historial_decisiones_plan_de_accion [EXTRACTED 1.00]
- **Flujo de envío de correo dual verificado end-to-end (Google Workspace/Microsoft 365)** — docs_06_autenticacion_y_preferencias_iemailsenderservice, docs_06_autenticacion_y_preferencias_microsoft365_provider, docs_06_autenticacion_y_preferencias_google_workspace_provider, docs_06_autenticacion_y_preferencias_bug_jsonpropertyname, tools_portalsaas_tools_emailsmoketest_readme_documento [EXTRACTED 1.00]

## Communities (103 total, 35 thin omitted)

### Community 0 - "Núcleo Auth/Datos/Admin (mixto)"
Cohesion: 0.05
Nodes (34): PortalSaas.Host.Pages.Admin.Organizations.Users, PortalSaas.Abstractions.Contratos, PortalSaas.Host.Pages.Account, PortalSaas.Data.Entities, PortalSaas.Host.Pages.Admin.Organizations.Licenses, PortalSaas.Core.Comercial, PortalSaas.Core.Tests, PortalSaas.Data (+26 more)

### Community 1 - "Límites de Contrato (IContractLimitService)"
Cohesion: 0.11
Nodes (23): InlineData, CancellationToken, Guid, Task, IContractLimitService, LimitCheckResult, CancellationToken, Guid (+15 more)

### Community 2 - "Preferencias de Usuario"
Cohesion: 0.06
Nodes (33): CancellationToken, Guid, Task, IUserPreferenceService, UserPreferenceDto, CancellationToken, Guid, Task (+25 more)

### Community 3 - "Envío de Correo — Gmail/MIME"
Cohesion: 0.08
Nodes (24): EmailMessage, GmailMessageBuilder, CancellationToken, HttpClient, Task, GoogleWorkspaceEmailSender, GoogleWorkspaceProviderConfig, TokenResponse (+16 more)

### Community 4 - "Hash de Contraseñas (PBKDF2)"
Cohesion: 0.11
Nodes (19): Hash, IServiceProvider, Salt, int, PasswordHasher, CancellationToken, Guid, Task (+11 more)

### Community 5 - "Cifrado de Secretos (AES-256-GCM)"
Cohesion: 0.10
Nodes (13): byte, ISecretoCifradoService, CancellationToken, Guid, Task, int, SecretoCifradoService, Fact (+5 more)

### Community 6 - "Carga de Plugins (AssemblyLoadContext)"
Cohesion: 0.08
Nodes (20): ApplicationPartManager, AssemblyDependencyResolver, AssemblyLoadContext, AssemblyName, PortalSaas.Core.Infraestructura, Dictionary, HashSet, IReadOnlyDictionary (+12 more)

### Community 7 - "Envío de Correo / Recuperación de Contraseña"
Cohesion: 0.09
Nodes (19): CancellationToken, Guid, Task, IEmailSenderService, CancellationToken, Guid, Task, IPasswordResetService (+11 more)

### Community 8 - "Configuración de Lanzamiento (launchSettings)"
Cohesion: 0.08
Nodes (25): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+17 more)

### Community 9 - "Resultado de Autenticación"
Cohesion: 0.20
Nodes (10): Guid, AuthenticationResult, CancellationToken, Guid, Task, Db, Fact, Org (+2 more)

### Community 10 - "Login de Administrador de Plataforma"
Cohesion: 0.11
Nodes (14): PortalSaas.Host.Pages.Admin, CancellationToken, Task, IPlatformAdminAuthenticationService, int, PlatformAdminAuthenticationService, IActionResult, InputModel (+6 more)

### Community 11 - "JWT de Cuenta de Servicio Google"
Cohesion: 0.14
Nodes (12): Claims, Header, JsonElement, PrivateKeyPem, PublicKey, RSA, Signature, DateTimeOffset (+4 more)

### Community 12 - "Decisión: Motor Dual PostgreSQL/SQL Server"
Cohesion: 0.13
Nodes (18): Dos bases de datos, dos decisiones distintas (SAP del cliente vs. base propia de la plataforma), Decisión: motor dual PostgreSQL/SQL Server para la base propia de la plataforma, Nombre de base portalsaas_saas_dev según convención de nombres físicos, Servicio Postgres 16 de desarrollo local (docker-compose), Decisión original: PostgreSQL gestionado, único motor para la base propia de la plataforma, Patrón de nombre físico de base de datos: portalsaas_<scope>_<environment>, Decisión revisada (24 jul 2026): motor dual PostgreSQL por defecto / SQL Server para on-premise existente, Detalle técnico del motor dual: sin defaults generados en la base, Guid/DateTimeOffset generados en C# (+10 more)

### Community 13 - "Entidades Company/Instance"
Cohesion: 0.12
Nodes (15): Guid, Company, Guid, ICollection, IReadOnlyCollection, string, Instance, InstanceEngineType (+7 more)

### Community 14 - "Tests de Autenticación de Admin"
Cohesion: 0.31
Nodes (7): Admin, CancellationToken, Task, Db, Fact, Task, PlatformAdminAuthenticationServiceTests

### Community 15 - "Modelo Core Comercial (Tablas)"
Cohesion: 0.24
Nodes (15): Capa comercial — resumen (organizations por encima de companies), Regla: estados en columna literal status, con check de valores en inglés, Tabla companies — compañía SAP, ahora con organization_id obligatorio y code separado de la PK, Modelo Core — Capa Comercial, Tabla instances — servidor físico HANA/SQL de una organización, Tabla on_premise_licenses — estado comercial vigente en modo instalado (clave de activación, expiración), Tabla organizations — el cliente que paga, nivel nuevo por encima de companies, Evaluado y descartado (YAGNI): perfiles multi-organización — se mantiene 1 usuario = 1 organización (+7 more)

### Community 16 - "Visión General del Proyecto / Huecos vs PortalSAP_v2"
Cohesion: 0.20
Nodes (15): Los 4 huecos que PortalSAP_v2 no cubre para ser vendible, ARCHITECTURE.md — Documento de Arquitectura, Proyecto paralelo a PortalSAP_v2, no fork ni migración en caliente, referencia-original/ — copia de solo lectura de PortalSAP_v2 y WMS_Suite, Qué se reutiliza de PortalSAP_v2 y cómo (aislamiento de plugins, motores genéricos, HanaService), Decisión: GestionDistribucionGastos y SellOut NO se portan (desarrollo a medida), Los 4 huecos reales para ser vendible (capa comercial, núcleo/vertical, BD propia, higiene seguridad/tests), Historial de decisiones — previo a este repo (Proyecto Menoja) (+7 more)

### Community 17 - "Snapshot del Modelo EF (Migraciones)"
Cohesion: 0.13
Nodes (8): PortalSaas.Data.Migrations.SqlServer.Migrations, ModelSnapshot, ModelBuilder, PortalSaasDbContextModelSnapshot, ModelBuilder, InitialCreate, ModelBuilder, PortalSaasDbContextModelSnapshot

### Community 18 - "Autenticación / Acceso por Organización"
Cohesion: 0.14
Nodes (11): CancellationToken, Guid, Task, CancellationToken, Guid, Task, IOrganizationAccessGateService, IActionResult (+3 more)

### Community 19 - "Factory de DbContext en Tiempo de Diseño"
Cohesion: 0.18
Nodes (9): PortalSaas.Data.Migrations.PostgreSql, PortalSaas.Data.Migrations.SqlServer, DbContext, DbSet, IDesignTimeDbContextFactory, DesignTimeDbContextFactory, DesignTimeDbContextFactory, ModelBuilder (+1 more)

### Community 20 - "Entidad OnPremiseLicense"
Cohesion: 0.15
Nodes (11): DateTimeOffset, Guid, IReadOnlyCollection, string, OnPremiseLicense, OnPremiseLicenseStatus, Guid, IActionResult (+3 more)

### Community 21 - "Entidad Subscription"
Cohesion: 0.15
Nodes (11): DateTimeOffset, Guid, IReadOnlyCollection, string, Subscription, SubscriptionStatus, Guid, IActionResult (+3 more)

### Community 22 - "Convención de Nombres de BD"
Cohesion: 0.17
Nodes (12): Regla: booleanos con prefijo is_/has_, Regla: dinero en columna _price/_amount + columna currency explícita, Convención de nombres — base de datos (inglés, plural, snake_case), Regla: fechas/horas con sufijo _at, tipo timestamptz, Regla: inglés siempre, snake_case, todo minúsculas, sin comillas, Regla: llave primaria siempre id (uuid o bigint identity), nunca una clave de negocio como PK, Regla: FK <entidad_referenciada_singular>_id, Regla: nombres de tabla en plural (organizations, plans, users) (+4 more)

### Community 23 - "Autenticación y Preferencias — Resumen"
Cohesion: 0.24
Nodes (12): Bug real: falta de [JsonPropertyName] causaba deserialización silenciosa a null de la config de proveedor (RSA.ImportFromPem falló varios pasos después), Autenticación, recuperación de contraseña y preferencias personales, Proveedor Google Workspace — Gmail API, cuenta de servicio con delegación de dominio, VERIFICADO end-to-end (24 jul 2026), IEmailSenderService — envío dual Google Workspace/Microsoft 365, email_settings 1:1 con organizations, IPasswordResetService — token SHA-256, expiración 1 hora, anti-enumeración deliberado, IUserPreferenceService — tabla user_preferences 1:1 con users (locale, timezone, theme, notificaciones), Proveedor Microsoft 365 — Microsoft Graph API, client_credentials, permiso de aplicación Mail.Send (sin verificar contra tenant real), 40 tests en tests/PortalSaas.Core.Tests, todos en verde (auth, reset, preferencias, builders de correo puros) (+4 more)

### Community 24 - "Módulos Comerciales y Modelo de Negocio"
Cohesion: 0.22
Nodes (11): Tabla organization_modules — módulos contratados como add-on por organización, Tabla plan_modules — qué módulos incluye cada plan, Tabla plans — catálogo de planes/tiers: límites y precio, Tabla platform_modules — catálogo comercial de módulos vendibles, Canal de venta: directa vs. white-label/reventa vía partners SAP B1 (VARs) — pendiente de decidir, Modelo comercial de negocio, Mercado objetivo: empresas medianas SAP Business One en Chile/LatAm (USD 5M-200M facturación), Modelo de cobro híbrido: usuarios activos + empresas SAP conectadas + módulos contratados (+3 more)

### Community 25 - "jQuery Validation Unobtrusive (JS)"
Cohesion: 0.22
Nodes (4): escapeAttributeValue(), onError(), onReset(), validationInfo()

### Community 26 - "Páginas Home/Logout (Host)"
Cohesion: 0.20
Nodes (6): PortalSaas.Host.Pages.Home, PageModel, IActionResult, Task, LogoutModel, IndexModel

### Community 27 - "Página Crear Suscripción (code-behind)"
Cohesion: 0.27
Nodes (8): SelectListItem, Guid, IActionResult, IEnumerable, InputModel, List, Task, CreateModel

### Community 28 - "Páginas Error/Index (Host)"
Cohesion: 0.22
Nodes (5): PortalSaas.Host.Pages, ILogger, ErrorModel, IActionResult, IndexModel

### Community 29 - "Migraciones EF — Designer (PostgreSQL)"
Cohesion: 0.22
Nodes (5): PortalSaas.Data.Migrations.PostgreSql.Migrations, ModelBuilder, AddAuthenticationAndPreferences, ModelBuilder, AddOrganizationSlug

### Community 30 - "Dependencias del Proyecto de Tests"
Cohesion: 0.22
Nodes (8): coverlet.collector (6.0.0), Microsoft.NET.Test.Sdk (17.8.0), xunit (2.5.3), xunit.runner.visualstudio (2.5.3), net8.0, Microsoft.EntityFrameworkCore.InMemory (8.0.8), Microsoft.Extensions.Configuration (8.0.0), Microsoft.NET.Sdk

### Community 31 - "Página Editar Organización (code-behind)"
Cohesion: 0.28
Nodes (7): Guid, IActionResult, IEnumerable, InputModel, Task, EditModel, InputModel

### Community 32 - "Reglas Duras y Próximos Pasos"
Cohesion: 0.25
Nodes (8): Próximos pasos sugeridos (§6, pasos 1-8), Dos bugs reales encontrados y corregidos en sesión (DateTimeOffset.Parse offset Chile; límite de usuarios no verificado), Estado actual del proyecto (24 jul 2026) — pasos 1-4 hechos, backoffice admin, usuarios, planes, Reglas que no se negocian (organization_id, convención BD, secretos, TLS, tests, límites de plan en código), 4 límites de conectividad SAP al escalar a SaaS multiempresa (sesión SL en memoria, vecino ruidoso, red directa, TrustServerCertificate=true), Pooling de conexiones — lección aprendida de HanaService/SapConnectionProvider de PortalSAP_v2 (sin pooling), IContractLimitService — hace cumplir en código los límites del plan (falla hacia lo más estricto), IAuthenticationService — login con bloqueo por intentos (5), mensaje genérico anti-enumeración

### Community 33 - "Proyecto de Migraciones PostgreSQL"
Cohesion: 0.25
Nodes (7): net8.0, EFCore.NamingConventions (8.0.0), Microsoft.EntityFrameworkCore.Design (8.0.8), Microsoft.Extensions.Configuration.EnvironmentVariables (8.0.0), Microsoft.Extensions.Configuration.Json (8.0.1), Npgsql.EntityFrameworkCore.PostgreSQL (8.0.10), Microsoft.NET.Sdk

### Community 34 - "Proyecto de Migraciones SQL Server"
Cohesion: 0.25
Nodes (7): net8.0, EFCore.NamingConventions (8.0.0), Microsoft.EntityFrameworkCore.Design (8.0.8), Microsoft.EntityFrameworkCore.SqlServer (8.0.8), Microsoft.Extensions.Configuration.EnvironmentVariables (8.0.0), Microsoft.Extensions.Configuration.Json (8.0.1), Microsoft.NET.Sdk

### Community 35 - "CLAUDE.md — Estructura y Licencias de Terceros"
Cohesion: 0.33
Nodes (7): Decisión: reutilizar arquitectura núcleo de PortalSAP_v2 (modular monolith, AssemblyLoadContext), CLAUDE.md — contexto persistente del repo Proyecto Saas Portal, Estructura de código real (src/PortalSaas.*, tests/, PortalSaas.Host), Decisión: nivel organizations por encima de companies, Licencia MIT — jQuery (OpenJS Foundation), Licencia MIT — jquery-validation (Jörn Zaefferer), Licencia MIT — jquery-validation-unobtrusive (.NET Foundation)

### Community 36 - "Herramienta EmailSmokeTest"
Cohesion: 0.29
Nodes (6): Microsoft.Extensions.DependencyInjection (8.0.1), net8.0, Microsoft.EntityFrameworkCore.InMemory (8.0.8), Microsoft.Extensions.Configuration (8.0.0), Microsoft.Extensions.Http (8.0.1), Microsoft.NET.Sdk

### Community 37 - "Entidad Plan / Página Índice de Planes"
Cohesion: 0.29
Nodes (5): ICollection, Plan, List, Task, IndexModel

### Community 38 - "Entidad PlatformModule"
Cohesion: 0.38
Nodes (6): DateTimeOffset, Guid, ICollection, OrganizationModule, PlanModule, PlatformModule

### Community 39 - "Página Crear Licencia (code-behind)"
Cohesion: 0.43
Nodes (5): Guid, IActionResult, InputModel, Task, CreateModel

### Community 40 - "Página Crear Usuario (code-behind)"
Cohesion: 0.43
Nodes (5): Guid, IActionResult, InputModel, Task, CreateModel

### Community 41 - "jQuery Validation Unobtrusive (minificado)"
Cohesion: 0.38
Nodes (3): f(), p(), u()

### Community 42 - "Validación de Respuesta HTTP"
Cohesion: 0.33
Nodes (4): HttpResponseMessage, CancellationToken, Task, HttpResponseValidation

### Community 43 - "Proyecto Host (csproj)"
Cohesion: 0.33
Nodes (5): Microsoft.NET.Sdk.Web, net8.0, EFCore.NamingConventions (8.0.0), Microsoft.EntityFrameworkCore.SqlServer (8.0.8), Npgsql.EntityFrameworkCore.PostgreSQL (8.0.10)

### Community 44 - "Página Crear Organización (code-behind)"
Cohesion: 0.33
Nodes (5): IActionResult, IEnumerable, InputModel, Task, CreateModel

### Community 45 - "Página Editar Licencia (code-behind)"
Cohesion: 0.47
Nodes (4): IActionResult, InputModel, Task, EditModel

### Community 46 - "Página Editar Suscripción (code-behind)"
Cohesion: 0.47
Nodes (4): IActionResult, InputModel, Task, EditModel

### Community 47 - "Página Editar Plan (code-behind)"
Cohesion: 0.47
Nodes (4): IActionResult, InputModel, Task, EditModel

### Community 48 - "Migración InitialCreate (SQL Server)"
Cohesion: 0.50
Nodes (3): Migration, MigrationBuilder, InitialCreate

### Community 49 - "Proyecto Core (csproj)"
Cohesion: 0.40
Nodes (4): Microsoft.EntityFrameworkCore (8.0.8), net8.0, Microsoft.Extensions.Http (8.0.1), Microsoft.NET.Sdk

### Community 50 - "Entidad Profile"
Cohesion: 0.40
Nodes (4): ProfileAction, ICollection, UserMenuProfile, Profile

### Community 51 - "Entidad Menu"
Cohesion: 0.40
Nodes (4): ICollection, MenuGroupItem, UserMenuProfile, Menu

### Community 52 - "Entidad MenuGroup"
Cohesion: 0.40
Nodes (4): ICollection, MenuGroupItem, MenuGroup, UserMenuGroup

### Community 53 - "Entidad Organization"
Cohesion: 0.60
Nodes (4): IReadOnlyCollection, string, OrganizationMode, OrganizationStatus

### Community 60 - "Página Crear Plan (code-behind)"
Cohesion: 0.40
Nodes (4): IActionResult, InputModel, Task, CreateModel

### Community 62 - "Stub de HttpClientFactory (Tests)"
Cohesion: 0.50
Nodes (3): IHttpClientFactory, HttpClient, HttpClientFactoryStub

### Community 63 - "Proyecto Data (csproj)"
Cohesion: 0.50
Nodes (3): Microsoft.EntityFrameworkCore.Relational (8.0.8), net8.0, Microsoft.NET.Sdk

### Community 64 - "Proyecto Abstractions (csproj)"
Cohesion: 0.50
Nodes (3): Microsoft.Extensions.DependencyInjection.Abstractions (8.0.2), net8.0, Microsoft.NET.Sdk

### Community 65 - "Constantes PortalActions"
Cohesion: 0.50
Nodes (3): IReadOnlyCollection, string, PortalActions

### Community 66 - "Entidad PlatformAdmin"
Cohesion: 0.50
Nodes (3): DateTimeOffset, Guid, PlatformAdmin

### Community 71 - "Página Índice de Organizaciones (code-behind)"
Cohesion: 0.50
Nodes (3): List, Task, IndexModel

## Knowledge Gaps
- **123 isolated node(s):** `net8.0`, `Microsoft.Extensions.DependencyInjection.Abstractions (8.0.2)`, `Microsoft.NET.Sdk`, `TokenResponse`, `TokenResponse` (+118 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **35 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `PortalSaasDbContext` connect `Factory de DbContext en Tiempo de Diseño` to `Núcleo Auth/Datos/Admin (mixto)`, `Límites de Contrato (IContractLimitService)`, `Preferencias de Usuario`, `Envío de Correo — Gmail/MIME`, `Hash de Contraseñas (PBKDF2)`, `Cifrado de Secretos (AES-256-GCM)`, `Envío de Correo / Recuperación de Contraseña`, `Resultado de Autenticación`, `Login de Administrador de Plataforma`, `Entidades Company/Instance`, `Tests de Autenticación de Admin`, `Autenticación / Acceso por Organización`, `Entidad OnPremiseLicense`, `Entidad Subscription`, `Página Crear Suscripción (code-behind)`, `Página Editar Organización (code-behind)`, `Entidad Plan / Página Índice de Planes`, `Entidad PlatformModule`, `Página Crear Licencia (code-behind)`, `Página Crear Usuario (code-behind)`, `Página Crear Organización (code-behind)`, `Página Editar Licencia (code-behind)`, `Página Editar Suscripción (code-behind)`, `Página Editar Plan (code-behind)`, `Página Crear Plan (code-behind)`, `Entidad PlatformAdmin`, `Página Índice de Organizaciones (code-behind)`?**
  _High betweenness centrality (0.164) - this node is a cross-community bridge._
- **Why does `PortalSaas.Data` connect `Núcleo Auth/Datos/Admin (mixto)` to `Migración InitialCreate — Designer (PostgreSQL)`, `Migración AddEmailSettings — Designer`, `Migración AddPlatformAdmins — Designer`, `Migración FixCompanyOrganizationCascade — Designer`, `Snapshot del Modelo EF (Migraciones)`, `Factory de DbContext en Tiempo de Diseño`, `Migraciones EF — Designer (PostgreSQL)`?**
  _High betweenness centrality (0.132) - this node is a cross-community bridge._
- **Why does `PortalSaas.Data.Entities` connect `Núcleo Auth/Datos/Admin (mixto)` to `Preferencias de Usuario`, `Envío de Correo — Gmail/MIME`, `Entidad PlatformAdmin`, `Entidad Plan / Página Índice de Planes`, `Entidad PlatformModule`, `Entidades Company/Instance`, `Entidad Profile`, `Entidad Menu`, `Entidad OnPremiseLicense`, `Entidad MenuGroup`, `Entidad Organization`, `Entidad Subscription`?**
  _High betweenness centrality (0.093) - this node is a cross-community bridge._
- **What connects `net8.0`, `Microsoft.Extensions.DependencyInjection.Abstractions (8.0.2)`, `Microsoft.NET.Sdk` to the rest of the system?**
  _126 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Núcleo Auth/Datos/Admin (mixto)` be split into smaller, more focused modules?**
  _Cohesion score 0.052125100240577385 - nodes in this community are weakly interconnected._
- **Should `Límites de Contrato (IContractLimitService)` be split into smaller, more focused modules?**
  _Cohesion score 0.10621942697414395 - nodes in this community are weakly interconnected._
- **Should `Preferencias de Usuario` be split into smaller, more focused modules?**
  _Cohesion score 0.06292517006802721 - nodes in this community are weakly interconnected._