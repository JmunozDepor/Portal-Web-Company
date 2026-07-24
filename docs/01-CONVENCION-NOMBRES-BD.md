# Convención de nombres — base de datos

Estándar obligatorio para **toda tabla y campo nuevo** en el Postgres de la
plataforma (`docs/02-ARQUITECTURA-BASE-DE-DATOS.md`). Reemplaza por completo la
convención española/HANA de `PortalSAP_v2` (`"MAYUSCULAS_CON_GUION_BAJO"`) — acá no
hay legado que respetar, así que se define bien desde el primer `CREATE TABLE`.
Referencia de estilo: nomenclatura tipo Stripe/API SaaS americana (`customers`,
`subscriptions`, `invoices`) — inglés, plural, `snake_case`, sin comillas.

**Esto es una convención de datos, no de idioma de producto**: comentarios, mensajes
de log y texto de UI siguen en español (igual que `PortalSAP_v2`, ver `CLAUDE.md`
§Estilo de código). Solo cambian los identificadores de base de datos.

**Nota sobre el idioma de los identificadores**: lo que importa acá es la
**formalidad y consistencia** de la convención — reglas explícitas y parejas en toda
la base, no dejadas a criterio de quien escribe cada tabla — no el idioma en sí. Se
eligió inglés porque es el estándar de facto en APIs/esquemas SaaS de referencia
(Stripe, etc.) y evita mezclar dos idiomas en el mismo esquema, pero la regla dura es
"una sola convención formal, aplicada sin excepciones", no "inglés obligatorio". Si
en el futuro se decide español, se aplican exactamente las mismas reglas de esta
página (plural, `snake_case`, `id` siempre, sufijo `_at`/`_id`/`status`/`is_`) solo
traducidas — nunca una mezcla de ambas dentro de la misma tabla.

## 1. Idioma y casing

- **Inglés siempre**, sin excepciones ni mezcla con español.
- `snake_case`, todo minúsculas, sin comillas dobles (evita el dolor de
  case-sensitivity que sí tiene HANA con identificadores entre comillas).

## 2. Nombres de tabla: **plural**

`organizations`, `plans`, `subscriptions`, `companies`, `users` — no singular
(`organization`, `plan`). Es la convención que ya reconoce cualquiera que haya visto
una API de Stripe/similar, y evita además choques con palabras reservadas de SQL
(`user` es palabra reservada en Postgres; `users` no).

## 3. Llave primaria: siempre `id`

- **`uuid`** (`default gen_random_uuid()`) para entidades de negocio/multi-tenant que
  se referencian entre sí o pueden generarse fuera de la base (`organizations`,
  `users`, `companies`).
- **`bigint generated always as identity`** para catálogos, tablas de bajo volumen o
  de log/auditoría (`plans`, `platform_modules`, `subscriptions`,
  `on_premise_licenses`, `usage_metrics`, `audit_logs`).
- **Nunca una clave de negocio como llave primaria** (a diferencia de `PortalSAP_v2`,
  donde `EMPRESA.CODIGO` es PK directa) — toda clave de negocio externa (ej. el
  código de compañía SAP) vive en una columna `code` separada, única e indexada, no
  como PK. Permite cambiar el código de negocio sin tocar ninguna FK.

## 4. Llaves foráneas: `<entidad_referenciada_singular>_id`

`organization_id`, `plan_id`, `module_id`, `company_id`, `instance_id`, `user_id`,
`menu_id`, `profile_id`, `action_id` — singular aunque la tabla que referencian sea
plural (`organizations.id` → columna `organization_id` en cualquier tabla que la
referencie).

## 5. Booleanos: prefijo `is_`/`has_`

`is_active`, `is_admin`, `is_core`, `is_locked`, `is_used`, `is_email_confirmed`,
`has_two_factor_enabled` — nunca un adjetivo suelto (`activo`, `bloqueado`).

## 6. Fechas/horas: sufijo `_at`

`created_at`, `updated_at`, `expires_at`, `issued_at`, `occurred_at`, `recorded_at`,
`last_login_at` — nunca `fecha_x`. Tipo `timestamptz` siempre (con zona horaria).

## 7. Estados: columna literal `status`, con `check` de valores en inglés

Nunca `estado`. Ejemplo: `status varchar(20) not null check (status in ('trial',
'active', 'suspended', 'cancelled'))`.

## 8. Dinero: columna `_price`/`_amount` + columna `currency` explícita

`monthly_price numeric(12,2)`, `currency varchar(3) not null default 'CLP'` —
siempre juntas, nunca un monto sin su moneda al lado.

## 9. Tablas de unión (N:N): `<tabla1>_<tabla2>`

`plan_modules`, `organization_modules`, `profile_actions`, `menu_group_items`,
`user_menu_groups`, `user_menu_profiles` — nombre compuesto de las dos entidades,
nunca un genérico `_detail`/`_xref`.

## 10. Convención de índices y constraints

`pk_<tabla>` (implícito con `primary key`), `fk_<tabla>_<referenciada>`,
`uq_<tabla>_<columnas>`, `ix_<tabla>_<columnas>` — mismo hábito que ya usaba
`PortalSAP_v2` (`FK_.../UQ_.../IX_...`), en minúsculas y sin comillas.

## 11. Sin abreviaturas salvo estándares universales

Deletrear las palabras completas (`description`, no `desc`; `configuration`, no
`cfg`) — únicas abreviaturas aceptadas: `id`, `url`, `sql`, `ip`.

## 12. Tabla de equivalencia — modelo español (borrador anterior) → inglés (definitivo)

| Español (docs/03, versión anterior) | Inglés (definitivo) |
|---|---|
| `organizacion` | `organizations` |
| `plan` | `plans` |
| `modulo_plataforma` | `platform_modules` |
| `plan_modulo` | `plan_modules` |
| `organizacion_modulo_contratado` | `organization_modules` |
| `suscripcion` | `subscriptions` |
| `licencia_on_premise` | `on_premise_licenses` |
| `instancia` | `instances` |
| `empresa` | `companies` |
| `usuario` | `users` |
| `medicion_uso` | `usage_metrics` |
| `grupo_menu` | `menu_groups` |
| `menu` | `menus` |
| `perfil` | `profiles` |
| `accion` | `actions` |
| `perfil_accion` | `profile_actions` |
| `usuario_grupo_menu` | `user_menu_groups` |
| `grupo_menu_detalle` | `menu_group_items` |
| `usuario_menu_perfil` | `user_menu_profiles` |
| `log_auditoria` | `audit_logs` |
| `token_recuperacion` | `password_reset_tokens` |

Ver `docs/03-MODELO-CORE-COMERCIAL.md` para el esquema completo ya reescrito con esta
convención.

## 13. Nomenclatura del nombre físico de la base de datos

Esta sección es distinta a las anteriores: no son nombres de tabla/columna dentro de
una base, es el **nombre de la base de datos misma** (`CREATE DATABASE ...`) — importa
porque, a diferencia de la vía SaaS (una sola base compartida), cada instalación
on-premise (motor dual, ver `docs/02-ARQUITECTURA-BASE-DE-DATOS.md` §1) crea su propia
base física, potencialmente en un servidor que ya aloja otras bases del cliente (ej.
`sqlsap.cdepor.cl`, que hoy también tiene `CLDEPORFIN`/`CLSELLOUT`/`CLINV`) — el nombre
tiene que ser inconfundible ahí.

**Patrón obligatorio**: `portalsaas_<scope>_<environment>`

- `portalsaas` — prefijo fijo, igual en todos lados. Es lo que identifica la base como
  "de esta plataforma" ante cualquier DBA que mire la lista de bases de un servidor
  compartido.
- `<scope>` —
  - `saas` para la única base compartida multi-tenant que hospeda la plataforma (la
    vía SaaS en la nube — un solo `organization_id` no implica una base nueva, ver
    `docs/02-...md` §4, aislamiento por columna, no por base).
  - `<client_slug>` — código corto del cliente (`snake_case`, derivado del nombre
    legal, ej. `comercial_depor`) para una instalación on-premise dedicada a un solo
    cliente (una `organizations` por instalación, ver `docs/03-...md` §5).
- `<environment>` — `dev` | `qa` | `prod`. Mismo criterio que ya usa el propio cliente
  hoy en SAP (`DEPOR` producción / `DEPORQA` testing, ver
  `referencia-original/PortalSAP_v2/ARCHITECTURE.md` §10) — un entorno nuevo del mismo
  cliente es una base nueva con este mismo sufijo, no una base "de prueba" suelta sin
  convención.

**Ejemplos:**

| Base | Qué es |
|---|---|
| `portalsaas_saas_dev` | Desarrollo local de la plataforma SaaS compartida, motor Postgres (`docker-compose.yml`). |
| `portalsaas_saas_staging` | Ambiente de prueba de la plataforma SaaS compartida, antes de producción. |
| `portalsaas_saas_prod` | La base SaaS multi-tenant real, sirviendo a todas las organizaciones. |
| `portalsaas_onpremise_dev` | Desarrollo local del patrón on-premise, motor SQL Server (genérico, no un cliente real todavía). |
| `portalsaas_comercial_depor_prod` | Instalación on-premise dedicada de Comercial Depor, en `sqlsap.cdepor.cl` (motor SQL Server). |
| `portalsaas_comercial_depor_qa` | Ambiente de prueba de esa misma instalación on-premise, si se necesita. |

**Nunca** un nombre de base genérico (`portalsaas`, `db`, `test`) ni improvisado
(`portalsaas_dev` sin `scope`, que fue el nombre usado antes de formalizar esto) —
corregido en `docker-compose.yml` y los `appsettings.Development.json` de los dos
proyectos de migraciones para seguir este patrón desde ahora.
