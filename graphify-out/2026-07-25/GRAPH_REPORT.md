# Graph Report - Proyecto Saas Portal  (2026-07-25)

## Corpus Check
- 1078 files · ~383,544 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1719 nodes · 2858 edges · 171 communities (114 shown, 57 thin omitted)
- Extraction: 97% EXTRACTED · 3% INFERRED · 0% AMBIGUOUS · INFERRED: 75 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `dc518cbb`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

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
- Estructura de la Solución (.sln)
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
- PortalSaas.Abstractions.Contratos
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
- 20260725001330_AddPlanToOnPremiseLicense.Designer.cs
- .OnModelCreating
- CancellationToken
- IReadOnlyCollection
- string
- ICollection
- Db
- Fact
- Org
- PortalSaas.Data.Entities
- EditModel
- Migration
- CreateModel
- CreateModel
- PluginLoadContext
- LoginModel
- Create.cshtml.cs
- Create.cshtml.cs
- HttpClientFactoryStub
- PortalActions
- SapEngineType.cs
- PortalSaas.Host.Pages.Account.SelectCompanyModel
- IndexModel
- Create.cshtml.cs
- ProviderConfigDeserializationTests
- IReadOnlyList<PortalSaas.Abstractions.Modelos.MenuNodeDto>
- PortalSaas.Abstractions.Modelos.MenuNodeDto
- CLAUDE.md — Proyecto Saas Portal
- PortalSaas.Core.Tests.csproj
- PortalSaas.Data.Migrations.SqlServer.csproj
- AdminPageModelBase
- Create.cshtml.cs
- DetailModel
- .SearchAsync
- Licencia MIT — jQuery (OpenJS Foundation)
- Licencia MIT — jquery-validation-unobtrusive (.NET Foundation)
- .ListAsync
- .ListAsync
- .CrearUsuarioAsync
- PortalSaas.sln
- .PrepareType
- PortalSaas.Tools.EmailSmokeTest.csproj
- PortalSaas.Host.ViewComponents
- .GetVisibleMenuAsync
- PortalSaas.Core.Tests.csproj
- SelectCompanyModel
- PluginLoadContext
- DocumentListViewModel.cs
- PortalSaas.Data.Migrations.SqlServer.csproj
- Instance
- CreateModel
- IndexModel
- PortalSaas.Abstractions.csproj
- SapSalesOrderHeader
- InputModel
- DocumentFormViewModel
- PortalActions
- PlatformAdmin
- Index.cshtml
- Default.cshtml
- _TabContent.cshtml
- _TabGeneral.cshtml
- PortalSaas.Abstractions.Componentes.DocumentFormViewModel

## God Nodes (most connected - your core abstractions)
1. `PortalSaasDbContext` - 61 edges
2. `PortalSaas.Abstractions.Modelos` - 60 edges
3. `PortalSaas.Abstractions.Contratos` - 56 edges
4. `PortalSaas.Data.Entities` - 47 edges
5. `PortalSaas.Data` - 40 edges
6. `Modelo Core — Capa Comercial` - 24 edges
7. `PortalSaas.Data.Entities` - 23 edges
8. `TenantUserAdminService` - 20 edges
9. `DetailModel` - 19 edges
10. `Organization` - 18 edges

## Surprising Connections (you probably didn't know these)
- `Historial de decisiones — previo a este repo (Proyecto Menoja)` --conceptually_related_to--> `referencia-original/ — copia de solo lectura de PortalSAP_v2 y WMS_Suite`  [INFERRED]
  docs/00-HISTORIAL-DECISIONES.md → ARCHITECTURE.md
- `Qué se reutiliza de PortalSAP_v2 y cómo (aislamiento de plugins, motores genéricos, HanaService)` --conceptually_related_to--> `PortalSAP_v2 — portal web ASP.NET Core 8 delante de SAP B1/HANA, modular monolith con plugins`  [INFERRED]
  ARCHITECTURE.md → docs/00-HISTORIAL-DECISIONES.md
- `Nombre de base portalsaas_saas_dev según convención de nombres físicos` --references--> `Patrón de nombre físico de base de datos: portalsaas_<scope>_<environment>`  [EXTRACTED]
  docker-compose.yml → docs/01-CONVENCION-NOMBRES-BD.md
- `DetailModel` --references--> `DocumentFormViewModel`  [EXTRACTED]
  plugins/Modulo.Ventas/Pages/SalesOrders/Detail.cshtml.cs → src/PortalSaas.Abstractions/Componentes/DocumentFormViewModel.cs
- `DetailModel` --references--> `ICustomerCatalogService`  [EXTRACTED]
  plugins/Modulo.Ventas/Pages/SalesOrders/Detail.cshtml.cs → src/PortalSaas.Abstractions/Contratos/ICustomerCatalogService.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Esquema de la capa comercial (docs/03-MODELO-CORE-COMERCIAL.md)** — docs_03_modelo_core_comercial_platform_admins, docs_03_modelo_core_comercial_organizations, docs_03_modelo_core_comercial_plans, docs_03_modelo_core_comercial_subscriptions, docs_03_modelo_core_comercial_on_premise_licenses, docs_03_modelo_core_comercial_usage_metrics, docs_03_modelo_core_comercial_platform_modules [EXTRACTED 1.00]
- **Los 4 huecos reales para que PortalSAP_v2 sea vendible como producto** — docs_00_historial_decisiones_cuatro_huecos, architecture_cuatro_huecos, docs_00_historial_decisiones_hallazgos_seguridad_wms_suite, docs_00_historial_decisiones_plan_de_accion [EXTRACTED 1.00]
- **Flujo de envío de correo dual verificado end-to-end (Google Workspace/Microsoft 365)** — docs_06_autenticacion_y_preferencias_iemailsenderservice, docs_06_autenticacion_y_preferencias_microsoft365_provider, docs_06_autenticacion_y_preferencias_google_workspace_provider, docs_06_autenticacion_y_preferencias_bug_jsonpropertyname, tools_portalsaas_tools_emailsmoketest_readme_documento [EXTRACTED 1.00]

## Communities (171 total, 57 thin omitted)

### Community 0 - "Núcleo Auth/Datos/Admin (mixto)"
Cohesion: 0.12
Nodes (9): PortalSaas.Data.Entities, PortalSaas.Core.Comercial, PortalSaas.Core.Tests, PortalSaas.Data, PortalSaas.Host.Pages.Admin.Organizations.Subscriptions, PortalSaas.Core.Usuarios, PortalSaas.Host.Comandos, PortalSaas.Core.Seguridad (+1 more)

### Community 1 - "Límites de Contrato (IContractLimitService)"
Cohesion: 0.13
Nodes (18): InlineData, CancellationToken, Guid, Task, IContractLimitService, CancellationToken, Guid, Task (+10 more)

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
Cohesion: 0.05
Nodes (28): byte, CancellationToken, Guid, Task, IEmailSenderService, CancellationToken, Guid, Task (+20 more)

### Community 6 - "Carga de Plugins (AssemblyLoadContext)"
Cohesion: 0.36
Nodes (6): ApplicationPartManager, Assembly, ILogger, IServiceCollection, List, PluginManager

### Community 7 - "Envío de Correo / Recuperación de Contraseña"
Cohesion: 0.22
Nodes (6): IPasswordResetService, IActionResult, InputModel, Task, InputModel, ResetPasswordModel

### Community 8 - "Configuración de Lanzamiento (launchSettings)"
Cohesion: 0.08
Nodes (25): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+17 more)

### Community 9 - "Resultado de Autenticación"
Cohesion: 0.19
Nodes (10): PortalSaas.Host.Pages.Admin.MenuGroups, Menu, InputModel, IActionResult, InputModel, List, PortalSaasDbContext, Task (+2 more)

### Community 10 - "Login de Administrador de Plataforma"
Cohesion: 0.15
Nodes (11): CancellationToken, Guid, Task, IAuthenticationService, Guid, AuthenticationResult, CancellationToken, Guid (+3 more)

### Community 11 - "JWT de Cuenta de Servicio Google"
Cohesion: 0.13
Nodes (12): Claims, Header, JsonElement, PrivateKeyPem, PublicKey, RSA, Signature, DateTimeOffset (+4 more)

### Community 12 - "Decisión: Motor Dual PostgreSQL/SQL Server"
Cohesion: 0.17
Nodes (12): Dos bases de datos, dos decisiones distintas (SAP del cliente vs. base propia de la plataforma), Nombre de base portalsaas_saas_dev según convención de nombres físicos, Servicio Postgres 16 de desarrollo local (docker-compose), Decisión original: PostgreSQL gestionado, único motor para la base propia de la plataforma, 4 límites de conectividad SAP al escalar a SaaS multiempresa (sesión SL en memoria, vecino ruidoso, red directa, TrustServerCertificate=true), Estrategia de aislamiento multi-tenant: schema compartido + organization_id obligatorio (default, no schema/base-per-tenant), Decisión revisada (24 jul 2026): motor dual PostgreSQL por defecto / SQL Server para on-premise existente, Detalle técnico del motor dual: sin defaults generados en la base, Guid/DateTimeOffset generados en C# (+4 more)

### Community 13 - "Entidades Company/Instance"
Cohesion: 0.20
Nodes (9): Guid, Company, DateTimeOffset, Guid, ICollection, Organization, DateTimeOffset, Guid (+1 more)

### Community 14 - "Tests de Autenticación de Admin"
Cohesion: 0.10
Nodes (20): AuditLog, Company, DbContext, DbSet, EmailSettings, Instance, MenuGroupItem, OrganizationModule (+12 more)

### Community 15 - "Modelo Core Comercial (Tablas)"
Cohesion: 0.20
Nodes (11): Próximos pasos sugeridos (§6, pasos 1-8), Patrón de nombre físico de base de datos: portalsaas_<scope>_<environment>, Un proyecto de migraciones EF Core por motor (PostgreSql/SqlServer), modelo único en PortalSaas.Data, IContractLimitService — hace cumplir en código los límites del plan (falla hacia lo más estricto), Paso 6: dar de alta organización y licencia on-premise a mano (sin UI de administración todavía), Paso 4: aplicar migraciones EF Core con dotnet-ef database update --connection, Paso 3: configurar connection string y Security:MasterSecretKey vía user-secrets/variable de entorno, Paso 2: crear base y usuario SQL Server dedicado (db_owner, principio de menor privilegio) (+3 more)

### Community 16 - "Visión General del Proyecto / Huecos vs PortalSAP_v2"
Cohesion: 0.22
Nodes (14): Los 4 huecos que PortalSAP_v2 no cubre para ser vendible, ARCHITECTURE.md — Documento de Arquitectura, Proyecto paralelo a PortalSAP_v2, no fork ni migración en caliente, referencia-original/ — copia de solo lectura de PortalSAP_v2 y WMS_Suite, Qué se reutiliza de PortalSAP_v2 y cómo (aislamiento de plugins, motores genéricos, HanaService), Los 4 huecos reales para ser vendible (capa comercial, núcleo/vertical, BD propia, higiene seguridad/tests), Historial de decisiones — previo a este repo (Proyecto Menoja), GestionDistribucionGastos — plugin vertical, desarrollo a medida de Comercial Depor (+6 more)

### Community 17 - "Snapshot del Modelo EF (Migraciones)"
Cohesion: 0.14
Nodes (7): PortalSaas.Data.Migrations.SqlServer.Migrations, ModelBuilder, InitialCreate, MigrationBuilder, ModelBuilder, AddPlanToOnPremiseLicense, AddPlanToOnPremiseLicense

### Community 18 - "Autenticación / Acceso por Organización"
Cohesion: 0.07
Nodes (25): Code, OriginModule, Guid, ICurrentCompanyAccessor, CancellationToken, Guid, Task, ICurrentUserContext (+17 more)

### Community 19 - "Factory de DbContext en Tiempo de Diseño"
Cohesion: 0.22
Nodes (5): PortalSaas.Data.Migrations.PostgreSql, PortalSaas.Data.Migrations.SqlServer, IDesignTimeDbContextFactory, DesignTimeDbContextFactory, DesignTimeDbContextFactory

### Community 20 - "Entidad OnPremiseLicense"
Cohesion: 0.29
Nodes (6): Guid, IActionResult, List, Organization, Task, IndexModel

### Community 21 - "Entidad Subscription"
Cohesion: 0.30
Nodes (9): Guid, IActionResult, InputModel, ISecretoCifradoService, MenuGroup, IndexModel, PermissionsModel, Task (+1 more)

### Community 22 - "Convención de Nombres de BD"
Cohesion: 0.15
Nodes (13): Regla: booleanos con prefijo is_/has_, Regla: dinero en columna _price/_amount + columna currency explícita, Convención de nombres — base de datos (inglés, plural, snake_case), Regla: estados en columna literal status, con check de valores en inglés, Regla: fechas/horas con sufijo _at, tipo timestamptz, Regla: inglés siempre, snake_case, todo minúsculas, sin comillas, Regla: llave primaria siempre id (uuid o bigint identity), nunca una clave de negocio como PK, Regla: FK <entidad_referenciada_singular>_id (+5 more)

### Community 23 - "Autenticación y Preferencias — Resumen"
Cohesion: 0.22
Nodes (13): Bug real: falta de [JsonPropertyName] causaba deserialización silenciosa a null de la config de proveedor (RSA.ImportFromPem falló varios pasos después), Autenticación, recuperación de contraseña y preferencias personales, Proveedor Google Workspace — Gmail API, cuenta de servicio con delegación de dominio, VERIFICADO end-to-end (24 jul 2026), IAuthenticationService — login con bloqueo por intentos (5), mensaje genérico anti-enumeración, IEmailSenderService — envío dual Google Workspace/Microsoft 365, email_settings 1:1 con organizations, IPasswordResetService — token SHA-256, expiración 1 hora, anti-enumeración deliberado, IUserPreferenceService — tabla user_preferences 1:1 con users (locale, timezone, theme, notificaciones), Proveedor Microsoft 365 — Microsoft Graph API, client_credentials, permiso de aplicación Mail.Send (sin verificar contra tenant real) (+5 more)

### Community 24 - "Módulos Comerciales y Modelo de Negocio"
Cohesion: 0.22
Nodes (11): Tabla organization_modules — módulos contratados como add-on por organización, Tabla plan_modules — qué módulos incluye cada plan, Tabla plans — catálogo de planes/tiers: límites y precio, Tabla platform_modules — catálogo comercial de módulos vendibles, Canal de venta: directa vs. white-label/reventa vía partners SAP B1 (VARs) — pendiente de decidir, Modelo comercial de negocio, Mercado objetivo: empresas medianas SAP Business One en Chile/LatAm (USD 5M-200M facturación), Modelo de cobro híbrido: usuarios activos + empresas SAP conectadas + módulos contratados (+3 more)

### Community 25 - "jQuery Validation Unobtrusive (JS)"
Cohesion: 0.25
Nodes (7): IReadOnlyCollection, DateTimeOffset, Guid, Organization, OnPremiseLicense, OnPremiseLicenseStatus, string

### Community 26 - "Páginas Home/Logout (Host)"
Cohesion: 0.11
Nodes (26): DateTimeOffset, CancellationToken, Guid, IReadOnlyDictionary, IReadOnlyList, Task, ITenantUserAdminService, Guid (+18 more)

### Community 27 - "Página Crear Suscripción (code-behind)"
Cohesion: 0.29
Nodes (9): LeafMenuDto, MenuGroupOptionDto, PermissionsInputModel, Guid, IActionResult, ITenantUserAdminService, Task, EditarModel (+1 more)

### Community 28 - "Páginas Error/Index (Host)"
Cohesion: 0.11
Nodes (11): PortalSaas.Host.Pages, PortalSaas.Host.Pages.Home, PageModel, IActionResult, Task, LogoutModel, ILogger, ErrorModel (+3 more)

### Community 29 - "Migraciones EF — Designer (PostgreSQL)"
Cohesion: 0.15
Nodes (7): PortalSaas.Data.Migrations.PostgreSql.Migrations, ModelBuilder, AddAuthenticationAndPreferences, ModelBuilder, AddOrganizationSlug, ModelBuilder, FixCompanyOrganizationCascade

### Community 30 - "Dependencias del Proyecto de Tests"
Cohesion: 0.29
Nodes (6): DateTimeOffset, Guid, IReadOnlyCollection, string, Subscription, SubscriptionStatus

### Community 31 - "Página Editar Organización (code-behind)"
Cohesion: 0.11
Nodes (16): PortalSaas.Host.Pages.Admin.Organizations, IActionResult, IEnumerable, InputModel, PortalSaasDbContext, Task, CreateModel, InputModel (+8 more)

### Community 32 - "Reglas Duras y Próximos Pasos"
Cohesion: 0.32
Nodes (12): Capa comercial — resumen (organizations por encima de companies), Tabla companies — compañía SAP, ahora con organization_id obligatorio y code separado de la PK, Modelo Core — Capa Comercial, Tabla instances — servidor físico HANA/SQL de una organización, Tabla on_premise_licenses — estado comercial vigente en modo instalado (clave de activación, expiración), Tabla organizations — el cliente que paga, nivel nuevo por encima de companies, Evaluado y descartado (YAGNI): perfiles multi-organización — se mantiene 1 usuario = 1 organización, Tabla platform_admins — operador de la plataforma, sin organization_id, email único global (+4 more)

### Community 33 - "Proyecto de Migraciones PostgreSQL"
Cohesion: 0.17
Nodes (10): Microsoft.EntityFrameworkCore.Relational (8.0.8), net8.0, EFCore.NamingConventions (8.0.0), Microsoft.EntityFrameworkCore.Design (8.0.8), Microsoft.Extensions.Configuration.EnvironmentVariables (8.0.0), Microsoft.Extensions.Configuration.Json (8.0.1), Npgsql.EntityFrameworkCore.PostgreSQL (8.0.10), Microsoft.NET.Sdk (+2 more)

### Community 34 - "Proyecto de Migraciones SQL Server"
Cohesion: 0.21
Nodes (7): Modulo.Administracion, IEnumerable, IModuloPortal, IServiceCollection, MenuItemDefinition, ModuloAdministracion, ModuloVentas

### Community 35 - "CLAUDE.md — Estructura y Licencias de Terceros"
Cohesion: 0.16
Nodes (13): ISapConnectionProvider, Parameters, DateTime, SalesOrderDto, SalesOrderFilter, SalesOrderLineDto, SalesOrderListResult, SalesOrderSummaryDto (+5 more)

### Community 36 - "Herramienta EmailSmokeTest"
Cohesion: 0.20
Nodes (11): CancellationToken, IReadOnlyList, Task, ICustomerCatalogService, CustomerDto, CustomerFilter, CancellationToken, IHanaService (+3 more)

### Community 37 - "Entidad Plan / Página Índice de Planes"
Cohesion: 0.11
Nodes (21): CancellationToken, Db, Fact, ICollection, LimitCheckResult, Org, Plan, Guid (+13 more)

### Community 38 - "Entidad PlatformModule"
Cohesion: 0.38
Nodes (6): DateTimeOffset, Guid, ICollection, OrganizationModule, PlanModule, PlatformModule

### Community 39 - "Página Crear Licencia (code-behind)"
Cohesion: 0.29
Nodes (8): Guid, IActionResult, InputModel, List, Organization, SelectListItem, Task, CreateModel

### Community 40 - "Página Crear Usuario (code-behind)"
Cohesion: 0.18
Nodes (9): CancellationToken, Task, IPlatformAdminAuthenticationService, int, PlatformAdminAuthenticationService, IActionResult, InputModel, Task (+1 more)

### Community 41 - "jQuery Validation Unobtrusive (minificado)"
Cohesion: 0.50
Nodes (3): List, Task, IndexModel

### Community 42 - "Validación de Respuesta HTTP"
Cohesion: 0.33
Nodes (4): HttpResponseMessage, CancellationToken, Task, HttpResponseValidation

### Community 43 - "Proyecto Host (csproj)"
Cohesion: 0.09
Nodes (19): HanaCommand, Regex, SqlCommand, CancellationToken, IReadOnlyList, Task, IHanaService, CancellationToken (+11 more)

### Community 44 - "Página Crear Organización (code-behind)"
Cohesion: 0.12
Nodes (15): PortalSaas.Host.Pages.Admin.Profiles, PermissionAction, IActionResult, InputModel, PortalSaasDbContext, Task, CreateModel, InputModel (+7 more)

### Community 45 - "Página Editar Licencia (code-behind)"
Cohesion: 0.31
Nodes (7): IActionResult, InputModel, List, Organization, SelectListItem, Task, EditModel

### Community 46 - "Página Editar Suscripción (code-behind)"
Cohesion: 0.47
Nodes (4): IActionResult, InputModel, Task, EditModel

### Community 47 - "Página Editar Plan (code-behind)"
Cohesion: 0.12
Nodes (13): PortalSaas.Host.Pages.Admin.Plans, IActionResult, InputModel, PortalSaasDbContext, Task, CreateModel, InputModel, IActionResult (+5 more)

### Community 49 - "Proyecto Core (csproj)"
Cohesion: 0.15
Nodes (12): PortalSaas.Data, B1SLayer (2.1.4), EFCore.NamingConventions (8.0.0), Microsoft.Data.SqlClient (5.2.2), Microsoft.EntityFrameworkCore (8.0.8), Microsoft.EntityFrameworkCore.SqlServer (8.0.8), Microsoft.Extensions.Http (8.0.1), Npgsql.EntityFrameworkCore.PostgreSQL (8.0.10) (+4 more)

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
Cohesion: 0.27
Nodes (8): SelectListItem, Guid, IActionResult, IEnumerable, InputModel, List, Task, CreateModel

### Community 61 - "Estructura de la Solución (.sln)"
Cohesion: 0.25
Nodes (5): PortalSaas.Host.Pages.Account, InputModel, InputModel, Guid, InputModel

### Community 62 - "Stub de HttpClientFactory (Tests)"
Cohesion: 0.09
Nodes (20): PortalSaas.Host.Pages.Admin.Organizations.Instances, Guid, IActionResult, IEnumerable, InputModel, ISecretoCifradoService, Organization, PortalSaasDbContext (+12 more)

### Community 63 - "Proyecto Data (csproj)"
Cohesion: 0.31
Nodes (7): Admin, CancellationToken, Task, Db, Fact, Task, PlatformAdminAuthenticationServiceTests

### Community 64 - "Proyecto Abstractions (csproj)"
Cohesion: 0.09
Nodes (27): Error, Organization, CancellationToken, Guid, Task, ISapConnectionTestService, SapConnectionTestResult, Company (+19 more)

### Community 66 - "Entidad PlatformAdmin"
Cohesion: 0.10
Nodes (20): Func, SemaphoreSlim, CancellationToken, Task, ISapConnectionProvider, ISapSession, Guid, SLConnection (+12 more)

### Community 70 - "Migración FixCompanyOrganizationCascade — Designer"
Cohesion: 0.22
Nodes (5): ModelSnapshot, ModelBuilder, PortalSaasDbContextModelSnapshot, ModelBuilder, PortalSaasDbContextModelSnapshot

### Community 71 - "Página Índice de Organizaciones (code-behind)"
Cohesion: 0.50
Nodes (3): List, Task, IndexModel

### Community 73 - "PortalSaas.Abstractions.Contratos"
Cohesion: 0.11
Nodes (7): PortalSaas.Abstractions.Contratos, Modulo.Ventas, PortalSaas.Abstractions.Modelos, PortalSaas.Core.Ventas, PortalSaas.Core.Infraestructura, PortalSaas.Core.Catalogos, InputModel

### Community 82 - "Vista Índice de Usuarios"
Cohesion: 0.29
Nodes (4): PortalSaas.Host.Pages.Admin.Organizations.Companies.IndexModel, PortalSaas.Host.Pages.Admin.Organizations.EmailSettings.IndexModel, PortalSaas.Host.Pages.Admin.Organizations.Users.IndexModel, route:{organizationId:guid}

### Community 104 - ".OnModelCreating"
Cohesion: 0.15
Nodes (10): PortalSaas.Host.Pages.Admin.Organizations.Licenses, Modulo.Ventas.Pages.SalesOrders, PortalSaas.Abstractions.Componentes, Microsoft.AspNetCore.Mvc.Rendering, LineInput, DateOnly, FilterInput, InputModel (+2 more)

### Community 112 - "PortalSaas.Data.Entities"
Cohesion: 0.12
Nodes (10): PortalSaas.Host.Pages.Admin.Organizations.Users, PortalSaas.Core.Administracion, PortalSaas.Host.Pages.Admin.Organizations.Companies, PortalSaas.Core.Sap, PortalSaas.Data.Entities, PortalSaas.Host.Pages.Home.PreferencesModel, InputModel, InputModel (+2 more)

### Community 113 - "EditModel"
Cohesion: 0.21
Nodes (11): Guid, IActionResult, InputModel, ISecretoCifradoService, List, Organization, PortalSaasDbContext, SelectListItem (+3 more)

### Community 114 - "Migration"
Cohesion: 0.50
Nodes (3): Migration, MigrationBuilder, InitialCreate

### Community 115 - "CreateModel"
Cohesion: 0.24
Nodes (10): Guid, IActionResult, InputModel, ISecretoCifradoService, List, Organization, PortalSaasDbContext, SelectListItem (+2 more)

### Community 116 - "CreateModel"
Cohesion: 0.27
Nodes (8): IContractLimitService, Guid, IActionResult, InputModel, Organization, PortalSaasDbContext, Task, CreateModel

### Community 117 - "PluginLoadContext"
Cohesion: 0.14
Nodes (13): HashSet, ICurrentCompanyAccessor, long, PortalSaasDbContext, List, MenuNodeDto, CancellationToken, IReadOnlyList (+5 more)

### Community 118 - "LoginModel"
Cohesion: 0.22
Nodes (7): IAuthenticationService, IOrganizationAccessGateService, IActionResult, InputModel, PortalSaasDbContext, Task, LoginModel

### Community 120 - "Create.cshtml.cs"
Cohesion: 0.22
Nodes (8): AdminPageModelBase, IReadOnlyList, Guid, IActionResult, ITenantUserAdminService, Task, IndexModel, TenantUserDto

### Community 122 - "HttpClientFactoryStub"
Cohesion: 0.13
Nodes (6): PortalSaas.Core.Correo, IHttpClientFactory, HttpClient, HttpClientFactoryStub, Fact, ProviderConfigDeserializationTests

### Community 123 - "PortalActions"
Cohesion: 0.29
Nodes (6): Modulo.Administracion.Pages.Usuarios, Dictionary, List, InputModel, PermissionsInputModel, InputModel

### Community 127 - "IndexModel"
Cohesion: 0.25
Nodes (5): PortalSaas.Host.Pages.Admin, InputModel, IActionResult, Task, LogoutModel

### Community 128 - "Create.cshtml.cs"
Cohesion: 0.40
Nodes (4): 07 — Theming Visual por Tenant (pendiente de implementar), Decisiones ya cerradas (no reabrir sin razón nueva), Qué es, Reglas de compatibilidad para módulos nuevos (mientras esta feature no se implementa)

### Community 129 - "ProviderConfigDeserializationTests"
Cohesion: 0.20
Nodes (10): FilterInput, CancellationToken, IActionResult, ICurrentUserContext, string, Task, IndexModel, CancellationToken (+2 more)

### Community 133 - "CLAUDE.md — Proyecto Saas Portal"
Cohesion: 0.20
Nodes (9): CLAUDE.md — Proyecto Saas Portal, Decisiones ya tomadas (no reabrir sin una razón nueva y explícita), Dónde está cada cosa, Estado actual (24 jul 2026), Estilo de código, Estructura de código real (24 jul 2026), graphify, Qué es esto (+1 more)

### Community 134 - "PortalSaas.Core.Tests.csproj"
Cohesion: 0.29
Nodes (4): IEnumerable, IServiceCollection, IModuloPortal, MenuItemDefinition

### Community 135 - "PortalSaas.Data.Migrations.SqlServer.csproj"
Cohesion: 0.40
Nodes (4): PortalSaas.Host.Pages.Admin.Organizations.EmailSettings, GoogleWorkspaceConfigInput, InputModel, Microsoft365ConfigInput

### Community 136 - "AdminPageModelBase"
Cohesion: 0.25
Nodes (5): Modulo.Administracion.Pages, Exception, ICurrentUserContext, PageHandlerExecutingContext, AdminPageModelBase

### Community 138 - "DetailModel"
Cohesion: 0.26
Nodes (8): JsonResult, CancellationToken, IActionResult, ICurrentUserContext, string, Task, DetailModel, route:/ventas/ordenes/{id}

### Community 139 - ".SearchAsync"
Cohesion: 0.17
Nodes (10): CancellationToken, IReadOnlyList, Task, IItemCatalogService, ItemDto, CancellationToken, IHanaService, IReadOnlyList (+2 more)

### Community 142 - ".ListAsync"
Cohesion: 0.17
Nodes (10): CancellationToken, IReadOnlyList, Task, ISalesEmployeeCatalogService, SalesEmployeeDto, CancellationToken, IHanaService, IReadOnlyList (+2 more)

### Community 143 - ".ListAsync"
Cohesion: 0.17
Nodes (10): CancellationToken, IReadOnlyList, Task, IWarehouseCatalogService, WarehouseDto, CancellationToken, IHanaService, IReadOnlyList (+2 more)

### Community 144 - ".CrearUsuarioAsync"
Cohesion: 0.38
Nodes (5): Db, Fact, Org, Task, AuthenticationServiceTests

### Community 145 - "PortalSaas.sln"
Cohesion: 0.17
Nodes (7): Modulo.Administracion, PortalSaas.Core, PortalSaas.Data.Migrations.PostgreSql, PortalSaas.Data.Migrations.SqlServer, PortalSaas.Host, PortalSaas.Core.Tests, PortalSaas.Tools.EmailSmokeTest

### Community 146 - ".PrepareType"
Cohesion: 0.24
Nodes (8): DbDataReader, IReadOnlyDictionary, IsSimpleType, Properties, PropertyInfo, RowReflectionMapper, Type, UnderlyingType

### Community 147 - "PortalSaas.Tools.EmailSmokeTest.csproj"
Cohesion: 0.18
Nodes (9): Microsoft.Extensions.DependencyInjection (8.0.1), Microsoft.Extensions.DependencyInjection.Abstractions (8.0.2), net8.0, Microsoft.NET.Sdk, net8.0, Microsoft.EntityFrameworkCore.InMemory (8.0.8), Microsoft.Extensions.Configuration (8.0.0), Microsoft.Extensions.Http (8.0.1) (+1 more)

### Community 148 - "PortalSaas.Host.ViewComponents"
Cohesion: 0.25
Nodes (5): PortalSaas.Host.ViewComponents, DocumentFormViewComponent, IViewComponentResult, DocumentListViewComponent, ViewComponent

### Community 149 - ".GetVisibleMenuAsync"
Cohesion: 0.25
Nodes (7): IViewComponentResult, CancellationToken, IReadOnlyList, Task, IMenuNavigationService, Task, SidebarMenuViewComponent

### Community 150 - "PortalSaas.Core.Tests.csproj"
Cohesion: 0.22
Nodes (8): coverlet.collector (6.0.0), Microsoft.EntityFrameworkCore.InMemory (8.0.8), Microsoft.Extensions.Configuration (8.0.0), Microsoft.NET.Test.Sdk (17.8.0), xunit (2.5.3), xunit.runner.visualstudio (2.5.3), net8.0, Microsoft.NET.Sdk

### Community 151 - "SelectCompanyModel"
Cohesion: 0.36
Nodes (6): IActionResult, InputModel, List, PortalSaasDbContext, Task, SelectCompanyModel

### Community 152 - "PluginLoadContext"
Cohesion: 0.25
Nodes (5): AssemblyDependencyResolver, AssemblyLoadContext, AssemblyName, Assembly, PluginLoadContext

### Community 153 - "DocumentListViewModel.cs"
Cohesion: 0.36
Nodes (7): IReadOnlyList, DocumentListColumn, DocumentListFilter, DocumentListFilterOption, DocumentListRow, DocumentListViewModel, FilterFieldType

### Community 154 - "PortalSaas.Data.Migrations.SqlServer.csproj"
Cohesion: 0.25
Nodes (7): net8.0, EFCore.NamingConventions (8.0.0), Microsoft.EntityFrameworkCore.Design (8.0.8), Microsoft.EntityFrameworkCore.SqlServer (8.0.8), Microsoft.Extensions.Configuration.EnvironmentVariables (8.0.0), Microsoft.Extensions.Configuration.Json (8.0.1), Microsoft.NET.Sdk

### Community 155 - "Instance"
Cohesion: 0.29
Nodes (6): Guid, ICollection, IReadOnlyCollection, string, Instance, InstanceEngineType

### Community 156 - "CreateModel"
Cohesion: 0.33
Nodes (5): IActionResult, InputModel, PortalSaasDbContext, Task, CreateModel

### Community 157 - "IndexModel"
Cohesion: 0.33
Nodes (5): Guid, IActionResult, List, Task, IndexModel

### Community 159 - "SapSalesOrderHeader"
Cohesion: 0.50
Nodes (4): DateTime, List, SapSalesOrderHeader, SapSalesOrderLine

### Community 160 - "InputModel"
Cohesion: 0.50
Nodes (4): LineInput, DateOnly, List, InputModel

### Community 162 - "PortalActions"
Cohesion: 0.50
Nodes (3): IReadOnlyCollection, string, PortalActions

### Community 163 - "PlatformAdmin"
Cohesion: 0.50
Nodes (3): DateTimeOffset, Guid, PlatformAdmin

## Knowledge Gaps
- **181 isolated node(s):** `Qué es esto`, `Decisiones ya tomadas (no reabrir sin una razón nueva y explícita)`, `Reglas que no se negocian`, `Dónde está cada cosa`, `Estructura de código real (24 jul 2026)` (+176 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **57 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `PortalSaas.Abstractions.Contratos` connect `PortalSaas.Abstractions.Contratos` to `Núcleo Auth/Datos/Admin (mixto)`, `Entidad PlatformAdmin`, `Cifrado de Secretos (AES-256-GCM)`, `.OnModelCreating`, `Proyecto Host (csproj)`, `PortalSaas.Data.Entities`, `Autenticación / Acceso por Organización`, `PortalSaas.Host.ViewComponents`, `HttpClientFactoryStub`, `Estructura de la Solución (.sln)`, `IndexModel`?**
  _High betweenness centrality (0.148) - this node is a cross-community bridge._
- **Why does `PortalSaas.Abstractions.Modelos` connect `PortalSaas.Abstractions.Contratos` to `Núcleo Auth/Datos/Admin (mixto)`, `Límites de Contrato (IContractLimitService)`, `Preferencias de Usuario`, `Envío de Correo — Gmail/MIME`, `PortalSaas.Core.Tests.csproj`, `Login de Administrador de Plataforma`, `.SearchAsync`, `.ListAsync`, `.ListAsync`, `Páginas Home/Logout (Host)`, `PortalActions`, `CLAUDE.md — Estructura y Licencias de Terceros`, `Herramienta EmailSmokeTest`, `Estructura de la Solución (.sln)`, `Proyecto Abstractions (csproj)`, `.OnModelCreating`, `PortalSaas.Data.Entities`, `PluginLoadContext`, `HttpClientFactoryStub`, `SapEngineType.cs`?**
  _High betweenness centrality (0.119) - this node is a cross-community bridge._
- **Why does `PortalSaasDbContext` connect `Tests de Autenticación de Admin` to `Núcleo Auth/Datos/Admin (mixto)`, `Límites de Contrato (IContractLimitService)`, `Preferencias de Usuario`, `Hash de Contraseñas (PBKDF2)`, `Cifrado de Secretos (AES-256-GCM)`, `Resultado de Autenticación`, `Login de Administrador de Plataforma`, `.CrearUsuarioAsync`, `Factory de DbContext en Tiempo de Diseño`, `Entidad OnPremiseLicense`, `Entidad Subscription`, `jQuery Validation Unobtrusive (JS)`, `IndexModel`, `Entidad Plan / Página Índice de Planes`, `Página Crear Licencia (code-behind)`, `Página Crear Usuario (code-behind)`, `jQuery Validation Unobtrusive (minificado)`, `Página Crear Organización (code-behind)`, `Página Editar Licencia (code-behind)`, `Página Editar Suscripción (code-behind)`, `Entidad Profile`, `Entidad MenuGroup`, `Página Crear Plan (code-behind)`, `Proyecto Data (csproj)`, `Página Índice de Organizaciones (code-behind)`?**
  _High betweenness centrality (0.099) - this node is a cross-community bridge._
- **What connects `Qué es esto`, `Decisiones ya tomadas (no reabrir sin una razón nueva y explícita)`, `Reglas que no se negocian` to the rest of the system?**
  _183 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Núcleo Auth/Datos/Admin (mixto)` be split into smaller, more focused modules?**
  _Cohesion score 0.12063492063492064 - nodes in this community are weakly interconnected._
- **Should `Límites de Contrato (IContractLimitService)` be split into smaller, more focused modules?**
  _Cohesion score 0.13174603174603175 - nodes in this community are weakly interconnected._
- **Should `Preferencias de Usuario` be split into smaller, more focused modules?**
  _Cohesion score 0.06292517006802721 - nodes in this community are weakly interconnected._