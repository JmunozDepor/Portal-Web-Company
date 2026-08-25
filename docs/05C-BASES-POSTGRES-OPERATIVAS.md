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
