# Bases Postgres operativas — servidor `172.16.122.171`

Registro vivo de las bases Postgres reales que hoy reciben datos de producción o
testing, para no dejar ninguna desactualizada al aplicar una migración nueva. Antes
de este documento, estos nombres no estaban registrados en ningún lado del repo —
solo existían en el servidor real, y el nombre real está en **minúscula** (Postgres
pliega a minúscula cualquier identificador sin comillas al crearlo) — no confundir
con `PS_COMDEPOR`/`PS_CENTRAL` en mayúscula, que son nombres del SQL Server de SAP
(`sqlsap.cdepor.cl`), un servidor y motor completamente distinto (ver
`docs/10-DOCKER-POSTGRES-PRODUCCION.md` §intro).

## Servidor

`172.16.122.171:5432` — credenciales del usuario técnico (`admin_saas`) en
`dotnet user-secrets` de `PortalSaas.Host` (`ConnectionStrings:Default`), nunca en
este repo en texto plano.

## Bases operativas (mantener esta tabla actualizada)

| Base (nombre real, minúscula) | Rol | Estado |
|---|---|---|
| `portalsaas_saas_prod` | BD SaaS productivo | Operativa |
| `portalsaas_saas_qa` | BD SaaS testing | Operativa |
| `ps_comdepor` | BD on-premise productivo (Comercial Depor) | Operativa |
| `ps_comdepor_qa` | BD on-premise productivo — testing | Operativa, **candidata a baja** (dueño del proyecto: "creo que se eliminará porque ahora no se está usando" — confirmar antes de dejar de migrarla) |

Otras bases vistas en el mismo servidor al inspeccionarlo (`ps_comdepor_rg`,
`ps_comdepor_rg_qa`, `ps_comdepor_wms`, `ps_comdepor_wms_qa`) — **no forman parte de
esta plataforma** (no se les aplicó ninguna migración de este repo), no tocarlas sin
confirmación explícita de que sí corresponden.

## Regla dura

**Toda migración nueva de `PortalSaas.Data.Migrations.PostgreSql` se aplica contra
TODAS las bases de la tabla de arriba marcadas "Operativa", antes de dar la tarea por
cerrada** — nunca alcanza con aplicarla solo contra la base de desarrollo local.
Verificar con `dotnet ef migrations list --connection "..."` (nombre real, minúscula)
contra cada una antes de aplicar, y **preguntar al dueño del proyecto explícitamente
si alguna quedó pendiente** (ej. por no tener las credenciales a mano en el momento) —
nunca asumir en silencio que "se aplica después", eso es justamente lo que deja bases
desactualizadas.

Comando de referencia (reemplazar `<db>` por el nombre real en minúscula de la tabla):

```bash
dotnet tool run dotnet-ef database update \
  --project src/PortalSaas.Data.Migrations.PostgreSql \
  --startup-project src/PortalSaas.Data.Migrations.PostgreSql \
  --connection "Host=172.16.122.171;Port=5432;Database=<db>;Username=admin_saas;Password=<real>"
```

## Historial de aplicación de `AddIntegrationDefinitionRunInterval` (2026-09-08)

Programación por intervalo de las integraciones del motor genérico —
columna `run_interval_minutes` (`int?`) en `integration_definitions`. El
`IntegrationSyncHostedService` la usa para reprogramar `NextRunAt = ahora +
run_interval_minutes` al terminar cada corrida (antes quedaba en `null` y la
integración corría una sola vez). Aplicada con éxito contra las **4 bases
operativas** (`ps_comdepor`, `portalsaas_saas_prod`, `portalsaas_saas_qa`,
`ps_comdepor_qa`) el 2026-09-08 — `20260908003604_AddIntegrationDefinitionRunInterval`,
la única `(Pending)` en cada una. Columna nullable aditiva, sin backfill ni
primer arranque requerido. La migración gemela de SQL Server quedó generada
solo por paridad — no hay BD SQL Server de plataforma activa.

## Historial de aplicación de `AddGenericImportValidationRules` (2026-09-07)

Motor de reglas de validación pre-carga de Importación Genérica —
tabla `generic_import_validation_rule_assignments` (1:N con
`generic_import_configs`, `DeleteBehavior.Cascade`), ver
`docs/superpowers/plans/2026-09-07-reglas-validacion-importacion-generica.md`.
Aplicada con éxito contra las **4 bases operativas** (`portalsaas_saas_qa`,
`portalsaas_saas_prod`, `ps_comdepor`, `ps_comdepor_qa`) el 2026-09-07 —
`20260907163712_AddGenericImportValidationRules`, la única `(Pending)` en cada
una (todas ya tenían `AddCompanyTraceabilityUdfName` y el resto al día). Sin
backfill ni primer arranque requerido (tabla nueva, arranca vacía). La
migración gemela de SQL Server quedó generada solo por paridad — no hay BD
SQL Server de plataforma activa (confirmado con el dueño del proyecto).

## Historial de aplicación de `AddCompanyExternalConnections` (2026-08-28)

Catálogo de conexiones externas por compañía (`company_external_connections` +
`company_module_connections`), ver
`docs/superpowers/specs/2026-08-27-catalogo-conexiones-externas-por-compania-design.md`.
Aplicada con éxito contra las **4 bases operativas** (`portalsaas_saas_qa`,
`portalsaas_saas_prod`, `ps_comdepor`, `ps_comdepor_qa`) el 2026-08-28 —
`20260827220011_AddCompanyExternalConnections`, la única `(Pending)` en cada una
(todas ya tenían el resto al día). Pendiente: primer arranque del Host contra cada
base para que corra `LegacyExternalConnectionBackfill` (idempotente, envuelto en
try/catch) — revisar el log por líneas `WARN` que empiecen con `Backfill:`.

## Historial de aplicación de `AddOrganizationMenuOverrides` (2026-08-25)

Primera vez que se ejecuta este proceso contra las 4 bases — sirvió para descubrir
que no estaban documentadas y que el nombre real es minúscula (el pedido original
las nombraba `PS_comdepor`, que no existe como tal en el servidor). Aplicado con
éxito contra las 4: `portalsaas_saas_qa` y `portalsaas_saas_prod` tenían 4
migraciones pendientes (`AddIntegrationRunLogDetalleConsulta`,
`AddIntegrationDefinitionLastSuccessfulSyncAt`, `AddSqlConnectorType`,
`AddOrganizationMenuOverrides` — las 3 primeras de otro trabajo en curso, no de esta
entrega); `ps_comdepor` solo tenía pendiente la última (ya tenía el resto al día);
`ps_comdepor_qa` tenía las mismas 4 que las bases SaaS.
