# Modelo Core — Capa Comercial

Extiende el modelo ya probado de `PortalSAP_v2` (`PORTALWEB`) con el nivel que falta
para SaaS real: **`organizations`** — el cliente que paga, por encima de `companies`
(una compañía/schema SAP). Hoy en `PortalSAP_v2`, `EMPRESA` es la unidad más alta; un
cliente como Comercial Depor tiene dos filas de `EMPRESA` (DEPOR, DEPORQA) sin que
exista ningún nivel que las agrupe como "un mismo cliente". Ese nivel es el hueco que
cierra este modelo.

**Convención de nombres**: ver `docs/01-CONVENCION-NOMBRES-BD.md` — inglés, plural,
`snake_case`, `id` surrogate siempre, FK `<entidad>_id`, timestamps `_at`, booleanos
`is_`/`has_`, estados en columna `status`. Es una convención de datos, no de idioma de
producto — comentarios/UI siguen en español.

## 1. Principio rector

**Toda tabla de negocio nueva lleva `organization_id` desde el primer
`CREATE TABLE`, sin excepción** — mismo criterio que `PortalSAP_v2` ya aplicó con
`EMPRESA_CODIGO`, un nivel más arriba. `users` pertenece a una `organizations`, no
solo "existe"; `companies` pertenece a una `organizations`, no es un dato suelto del

**Evaluado y descartado por ahora (24 jul 2026): perfiles multi-organización** (un
usuario con una sola identidad accediendo a varias `organizations`, ej. un
partner/consultor administrando varios clientes desde una cuenta). Se decidió
mantener **1 usuario = 1 organización** hasta tener un caso real concreto —
mismo criterio "YAGNI" que ya aplicó el equipo en `WMS_Suite` para los adaptadores
formales de WMS. Si se necesita a futuro, el cambio real es: `users.email` pasaría de
único-por-organización a único-global, y `User.OrganizationId` se reemplazaría por una
tabla de acceso M:N (`user_organization_access`) — no es una extensión menor, hay que
diseñarla cuando exista el caso real, no antes. **Esto no es lo mismo que
`platform_admins`** (24 jul 2026): esa tabla no es un `user` con acceso a varias
organizaciones, es un actor fuera del concepto de organización por completo — el
operador de la plataforma, no un cliente. No reabre esta decisión.
portal.

## 2. Entidades nuevas (capa comercial — no existían en `PortalSAP_v2`)

| Tabla | Qué resuelve |
|---|---|
| `platform_admins` | El operador de la plataforma (Comercial Depor como vendedor, no como cliente) — administra todas las `organizations`. Sin `organization_id`: no es un perfil multi-organización (esa idea sigue descartada, ver más abajo), es un actor que no pertenece a ninguna organización. `email` único **global** (a diferencia de `users.email`), login separado (`/Admin/Login`, esquema de cookie propio `"PlatformAdmin"`, sin sesión compartida con `users`). Primera cuenta se crea con el comando `dotnet run --project src/PortalSaas.Host -- seed-admin <correo>` (pide la contraseña por consola, sin autoregistro). |
| `organizations` | El cliente que paga. Puede tener 1+ `companies` (compañías SAP). |
| `plans` | Catálogo de planes/tiers: límites y precio. |
| `platform_modules` | Catálogo comercial de módulos vendibles (equivalente a `MODULO_ORIGEN`, ahora con precio/inclusión por plan). |
| `plan_modules` | Qué módulos incluye cada plan. |
| `organization_modules` | Qué módulos tiene contratados una organización más allá de su plan base (add-ons). |
| `subscriptions` | Estado comercial vigente de una organización en modo SaaS (activa/vencida/cancelada, referencia a pasarela de pago). |
| `on_premise_licenses` | Estado comercial vigente en modo instalado (clave de activación, expiración, huella de instalación). |
| `usage_metrics` | Eventos de consumo (usuarios activos, documentos creados, transacciones) — base para alertar/bloquear límites y para facturación por consumo si el modelo comercial lo requiere. |

## 3. Entidades existentes, ahora colgando de `organizations`

| Tabla | Cambio respecto a `PortalSAP_v2` |
|---|---|
| `instances` | Gana `organization_id` — un servidor físico HANA/SQL pertenece a una organización (aunque varias `companies` de esa misma organización puedan compartirlo, igual que hoy). |
| `companies` | Gana `organization_id` obligatorio; el código SAP de la compañía pasa a ser columna `code` única, ya no la llave primaria (ver `docs/01-...md` §3). |
| `users` | Gana `organization_id` obligatorio; `username` pasa a ser único **dentro de la organización**, no global. |
| `menu_groups`, `menus`, `profiles`, `actions`, `profile_actions`, `user_menu_groups`, `user_menu_profiles`, `audit_logs`, `password_reset_tokens` | Mismo modelo que hoy — cuelgan de `companies`/`users`, que a su vez ya resuelven a `organizations`. No necesitan `organization_id` propio (evitar denormalizar donde no hace falta). |

## 4. Esquema (PostgreSQL, primer corte — a refinar antes de implementar)

```sql
create extension if not exists pgcrypto; -- gen_random_uuid()

-- ---------------------------------------------------------------------------
-- platform_admins: operador de la plataforma, sin organization_id -- administra
-- todas las organizations, no pertenece a ninguna. Email único GLOBAL (a
-- diferencia de users.email).
-- ---------------------------------------------------------------------------
create table platform_admins (
    id                      uuid primary key default gen_random_uuid(),
    email                   varchar(150) not null unique,
    password_hash           varchar(300) not null,
    password_salt           varchar(100) not null,
    is_active               boolean not null default true,
    is_locked               boolean not null default false,
    failed_login_attempts   integer not null default 0,
    last_login_at           timestamptz,
    created_at              timestamptz not null default now()
);

-- ---------------------------------------------------------------------------
-- organizations: el cliente que paga. Nivel nuevo por encima de "companies".
-- ---------------------------------------------------------------------------
create table organizations (
    id              uuid primary key default gen_random_uuid(),
    legal_name      varchar(200) not null,
    tax_id          varchar(20),
    country         varchar(10) not null,
    mode            varchar(20) not null default 'saas'
                        check (mode in ('saas', 'on_premise')),
    status          varchar(20) not null default 'trial'
                        check (status in ('trial', 'active', 'suspended', 'cancelled')),
    created_at      timestamptz not null default now()
);

-- ---------------------------------------------------------------------------
-- plans: catálogo de planes/tiers comerciales.
-- ---------------------------------------------------------------------------
create table plans (
    id                          bigint generated always as identity primary key,
    code                        varchar(30) not null unique,
    name                        varchar(100) not null,
    user_limit                  integer,  -- null = ilimitado
    company_limit                integer,
    monthly_transaction_limit    integer,
    monthly_price                numeric(12, 2),
    currency                     varchar(3) not null default 'CLP',
    is_active                    boolean not null default true
);

-- ---------------------------------------------------------------------------
-- platform_modules: catálogo comercial de módulos vendibles.
-- ---------------------------------------------------------------------------
create table platform_modules (
    id          bigint generated always as identity primary key,
    code        varchar(50) not null unique,   -- equivalente a CodigoModulo/MODULO_ORIGEN
    name        varchar(100) not null,
    is_core     boolean not null default false -- true = incluido en todo plan
);

create table plan_modules (
    plan_id     bigint not null references plans(id),
    module_id   bigint not null references platform_modules(id),
    primary key (plan_id, module_id)
);

create table organization_modules (
    organization_id     uuid not null references organizations(id),
    module_id           bigint not null references platform_modules(id),
    contracted_at        timestamptz not null default now(),
    primary key (organization_id, module_id)
);

-- ---------------------------------------------------------------------------
-- subscriptions: estado comercial vigente en modo SaaS.
-- ---------------------------------------------------------------------------
create table subscriptions (
    id                          bigint generated always as identity primary key,
    organization_id             uuid not null references organizations(id),
    plan_id                     bigint not null references plans(id),
    status                      varchar(20) not null
                                    check (status in ('trial', 'active', 'past_due', 'cancelled')),
    started_at                  timestamptz not null default now(),
    ended_at                    timestamptz,
    payment_provider             varchar(30),   -- 'stripe' | 'flow' | 'transbank' | null (on-premise)
    external_payment_reference   varchar(100)   -- id de cliente en el proveedor -- NUNCA datos de tarjeta acá
);

-- ---------------------------------------------------------------------------
-- on_premise_licenses: estado comercial vigente en modo instalado.
-- ---------------------------------------------------------------------------
create table on_premise_licenses (
    id                      bigint generated always as identity primary key,
    organization_id         uuid not null references organizations(id),
    activation_key          varchar(100) not null unique,
    installation_fingerprint varchar(200),
    issued_at               timestamptz not null default now(),
    expires_at              timestamptz not null,
    status                  varchar(20) not null default 'active'
                                check (status in ('active', 'revoked', 'expired'))
);

-- ---------------------------------------------------------------------------
-- instances / companies: mismo modelo de PortalSAP_v2, ahora colgando de organizations.
-- ---------------------------------------------------------------------------
create table instances (
    id                      bigint generated always as identity primary key,
    organization_id         uuid not null references organizations(id),
    name                    varchar(50) not null,
    host                    varchar(200) not null,
    port                    integer not null default 30015,
    engine_type             varchar(20) not null check (engine_type in ('hana', 'sqlserver')),
    technical_username      varchar(100) not null,
    technical_secret_key    varchar(200) not null, -- cifrado AES-256-GCM, igual que PortalSAP_v2
    is_active               boolean not null default true,
    unique (organization_id, name)
);

create table companies (
    id                          uuid primary key default gen_random_uuid(),
    organization_id             uuid not null references organizations(id),
    instance_id                 bigint not null references instances(id),
    code                        varchar(20) not null unique, -- código SAP de la compañía (ej. "DEPOR") -- ver docs/01 §3
    name                        varchar(100) not null,
    database_name               varchar(50) not null,
    service_layer_url           varchar(300) not null,
    integration_username         varchar(100) not null,
    integration_secret_key       varchar(200) not null,
    country                      varchar(10) not null,
    is_active                    boolean not null default true
);

-- ---------------------------------------------------------------------------
-- users: mismo modelo de PortalSAP_v2, ahora con organization_id obligatorio.
-- ---------------------------------------------------------------------------
create table users (
    id                          uuid primary key default gen_random_uuid(),
    organization_id             uuid not null references organizations(id),
    username                     varchar(100) not null,
    email                        varchar(150),
    password_hash                varchar(300) not null,
    password_salt                varchar(100) not null,
    is_admin                     boolean not null default false,
    is_active                    boolean not null default true,
    is_locked                    boolean not null default false,
    failed_login_attempts        integer not null default 0,
    last_login_at                timestamptz,
    created_at                   timestamptz not null default now(),
    is_email_confirmed           boolean not null default false,
    has_two_factor_enabled       boolean not null default false,
    two_factor_method             varchar(20),
    two_factor_secret             varchar(200),
    unique (organization_id, username) -- unico DENTRO de la organizacion, no global
);

-- ---------------------------------------------------------------------------
-- usage_metrics: eventos de consumo, base para limites y facturacion por uso.
-- ---------------------------------------------------------------------------
create table usage_metrics (
    id                  bigint generated always as identity primary key,
    organization_id     uuid not null references organizations(id),
    company_id          uuid references companies(id),
    metric_name         varchar(50) not null,  -- 'active_user' | 'document_created' | 'sap_transaction'
    value                numeric(18, 2) not null default 1,
    period               char(6) not null,       -- 'YYYYMM'
    recorded_at           timestamptz not null default now()
);

create index ix_usage_metrics_period on usage_metrics (organization_id, period, metric_name);

-- Nota: menu_groups, menus, profiles, actions, profile_actions, menu_group_items,
-- user_menu_groups, user_menu_profiles, audit_logs -- PORTADAS (24 jul 2026, ver
-- CLAUDE.md "Núcleo heredado de PORTALWEB"), mismo shape que
-- PortalSAP_v2/db/hana/002_crear_tablas.sql, ver src/PortalSaas.Data/Entities/ para
-- el DDL real generado (no se repite acá para no duplicar lo que ya está en el
-- código -- tabla de equivalencia de nombres en docs/01-...md §12).
-- password_reset_tokens también portada, ver docs/06-AUTENTICACION-Y-PREFERENCIAS.md.
```

## 5. Reglas de negocio que el código debe validar (no solo el esquema)

- **`IContractLimitService`** (a construir): antes de crear un `users` o una
  `companies`, o de aceptar un documento nuevo, se consulta el límite vigente del
  `plans` de la organización contra `usage_metrics`/conteo real — el límite se
  **hace cumplir**, no es solo informativo. Mismo criterio de "falla hacia lo más
  estricto" que ya usa `PortalSAP_v2` en el motor de aprobación (si la condición
  falla, exige aprobación en vez de autoaprobar en silencio) — acá, si no se puede
  verificar el límite, se bloquea la operación en vez de dejarla pasar.
- **Modo on-premise**: una sola fila de `organizations` (la instalación local), sin
  `subscriptions` — el gate de acceso es `on_premise_licenses.status = 'active'` y
  `expires_at > now()`, verificado al iniciar el Host, no solo al loguearse.
- **Modo SaaS**: el gate es `subscriptions.status in ('trial', 'active')` — igual
  principio, verificado en el middleware de autorización existente de
  `PortalSAP_v2` (el mismo punto donde hoy se chequea `ES_ADMINISTRADOR`).
