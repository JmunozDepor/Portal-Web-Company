# Checklist de migración — motor SQL Server contra `sqlsap.cdepor.cl`

Complementa `docs/05-RUNBOOK-PRODUCCION.md` (pasos 1-3 y 6, alta de base/usuario/secretos
y alta de la organización, siguen vigentes tal cual, no se repiten acá). Este documento
existe porque `docs/05-RUNBOOK-PRODUCCION.md` quedó escrito contra un estado más viejo del
esquema (**"con una sola migración (`InitialCreate`) hoy"**, 11 tablas) — hoy son 4
migraciones del lado SQL Server / 9 del lado Postgres y 24 tablas de negocio (25 con
`__EFMigrationsHistory`). **Nadie ha ejecutado este checklist todavía contra la base real
de producción** (mismo estado que `05`) — es la guía para cuando se decida hacerlo, no un
registro de que ya se hizo.

## 1. Migraciones que se van a aplicar (estado real, 26 jul 2026)

El motor SQL Server tiene **menos archivos de migración que Postgres porque su
`InitialCreate` está consolidado** (ver `CLAUDE.md`, "InitialCreate consolidado" — las 5
migraciones intermedias de Postgres nunca llegaron a aplicarse contra una base real, así
que del lado SQL Server se regeneraron limpias en una sola). El **esquema final es el
mismo en los dos motores** — no faltan tablas del lado SQL Server, solo está empaquetado
distinto:

| # | Migración (SQL Server) | Contenido |
|---|---|---|
| 1 | `InitialCreate` (20260724221555) | 15 tablas núcleo + comercial: `organizations`, `plans`, `platform_admins`, `platform_modules`, `email_settings`, `instances`, `on_premise_licenses`, `users`, `subscriptions`, `organization_modules`, `plan_modules`, `companies`, `password_reset_tokens`, `user_preferences`, `usage_metrics`. Incluye ya el fix de cascada `Company.Organization` (ver §2). |
| 2 | `AddCoreMenuAndPermissions` (20260724231727) | 9 tablas heredadas de PORTALWEB: `actions`, `audit_logs`, `menu_groups`, `menus`, `profiles`, `user_menu_groups`, `menu_group_items`, `profile_actions`, `user_menu_profiles`. Incluye ya el fix de cascada `Menu.ParentMenu` autorreferencial y `UserMenuGroup`/`UserMenuProfile.Company` (ver §2). |
| 3 | `SeedFixedActions` (20260724231937) | Solo datos (`HasData`) — siembra el catálogo fijo de `actions` (Ver/Crear/Editar/Eliminar/Aprobar/Exportar). Sin `CREATE TABLE`. |
| 4 | `AddPlanToOnPremiseLicense` (20260725001410) | Agrega `plan_id` (FK a `plans`) a `on_premise_licenses`, con backfill (ver §3). |

**Total: 24 tablas de negocio.** `SELECT name FROM sys.tables` después de aplicar debe
devolver 25 filas (las 24 + `__EFMigrationsHistory`).

Si entre esta fecha y el despliegue real se agregan migraciones nuevas (ej. al portar
algo de `docs/08-BRECHA-FUNCIONAL-VS-PORTALSAP-V2.md`), regenerar esta tabla antes de
desplegar — no asumir que sigue siendo válida.

## 2. Incompatibilidades Postgres/SQL Server ya encontradas y corregidas en el modelo

Todas confirmadas aplicando contra un SQL Server 2022 Express real en desarrollo — **ya
están corregidas en el código actual**, esta sección es un registro de qué buscar si
aparece un error parecido al agregar una entidad/relación nueva, no una tarea pendiente:

1. **Múltiples rutas de cascada hacia el mismo destino** (error SQL Server 1785, *"may
   cause cycles or multiple cascade paths"*) — Postgres lo permite en silencio, SQL
   Server lo rechaza al crear la tabla. Encontrado 3 veces:
   - `Company` → `Organization` (directa) y `Company` → `Instance` → `Organization`
     (indirecta) — `Company.Organization` pasó a `DeleteBehavior.Restrict`.
   - `UserMenuGroup.Company`/`UserMenuProfile.Company` — alcanzables también vía `User`
     — mismo fix, `DeleteBehavior.Restrict`.
   - `Menu.ParentMenu` (autorreferencial) — SQL Server rechaza cascada
     autorreferencial directamente, sin necesidad de una segunda ruta.
   **Qué revisar en una entidad nueva**: cualquier FK que se pueda alcanzar por más de
   un camino de cascada, o cualquier FK autorreferencial con `DeleteBehavior.Cascade`
   (el default de EF Core) — cambiarlo a `Restrict` preventivamente en vez de esperar
   el error 1785 al migrar.
2. **`NOT NULL` + `FOREIGN KEY` sobre una tabla con filas existentes** —
   `AddPlanToOnPremiseLicense` agrega `plan_id` NOT NULL a `on_premise_licenses`; sin
   backfill previo (`UPDATE ... SET plan_id = (plan más antiguo existente)` **antes**
   de crear el `NOT NULL`/FK en la misma migración), falla contra una base con datos
   en los dos motores por igual (no es un problema Postgres-vs-SQL-Server, es un
   problema de "generar la migración no prueba nada, aplicarla contra datos reales
   sí"). No aplica al primer despliegue contra `sqlsap.cdepor.cl` (base nueva, sin
   filas) pero sí aplicará a cualquier columna `NOT NULL` nueva que se agregue
   **después** de que la organización ya tenga datos reales — repetir el patrón
   backfill-antes-de-constraint en ese caso.
3. **`DateTimeOffset` con offset no-UTC contra Postgres** (`timestamptz`) — bug real
   encontrado en `Subscriptions/Edit.cshtml.cs` (`DateTimeOffset.Parse` tomaba el
   offset de la zona horaria del servidor, Chile `-04:00`; Npgsql solo acepta escribir
   con offset `0`). **No aplica a SQL Server** (`datetimeoffset` sí acepta cualquier
   offset tal cual), se documenta acá solo para no repetir el error si en algún punto
   se comparte código de UI entre los dos motores sin pensar cuál está activo — el
   fix real (parsear como `DateOnly` y construir el `DateTimeOffset` con
   `TimeSpan.Zero` explícito) ya es portable a los dos motores, así que no hace falta
   una rama de código por proveedor.

## 3. Caveats generales a vigilar en migraciones futuras (no encontrados todavía, prevención)

- **`decimal` sin precisión explícita**: si una entidad nueva agrega una columna
  `decimal` (ej. un monto) sin `HasPrecision`/`[Column(TypeName = "decimal(18,2)")]`,
  el provider de SQL Server puede emitir una advertencia de build
  (`"Decimal or double... will be truncated"`) al generar la migración aunque el
  default (`decimal(18,2)`) sea razonable para montos — no ignorar esa advertencia sin
  confirmar que 2 decimales alcanzan para el campo en cuestión (no alcanza, por
  ejemplo, para tipos de cambio con más precisión). Postgres no emite esta advertencia
  (su `numeric` sin precisión no trunca), así que este chequeo hay que hacerlo mirando
  el output de `dotnet-ef migrations add` contra el proyecto **SQL Server**
  específicamente, no alcanza con mirar el de Postgres.
- **Longitud de `string`**: sin `HasMaxLength`, Postgres genera `text` (sin límite) y
  SQL Server genera `nvarchar(max)` — comportamiento equivalente en la práctica, no
  requiere atención, pero si a futuro se agrega un `HasMaxLength` a una columna
  existente con datos más largos que el nuevo límite, probarlo contra los dos motores
  (SQL Server trunca/rechaza distinto que Postgres ante un valor que ya no entra).
- **Nombres de columna/tabla**: la convención `snake_case` en minúsculas sin comillas
  (`docs/01-CONVENCION-NOMBRES-BD.md`) ya evita el problema clásico de Postgres
  (pliega identificadores sin comillas a minúsculas) vs. SQL Server (case-insensitive
  por collation default) — mientras se seleccione toda entidad/columna nueva en
  minúsculas explícitas (ya lo hace `PortalSaasDbContext` vía la convención de
  nombres), no hace falta pensar en esto de nuevo.
- **Nada de `HasDefaultValueSql`/funciones específicas de proveedor**: ya es regla
  dura del proyecto (`CLAUDE.md`) — `Guid`/`DateTimeOffset` se generan en C# como
  inicializador de propiedad, nunca `gen_random_uuid()`/`NEWID()` ni
  `UseIdentityAlwaysColumn`. Confirmar que ninguna migración nueva generada por
  `dotnet-ef` haya agregado algo así por error antes de aplicarla (revisar el diff del
  archivo de migración generado, no solo confiar en que compiló).

## 4. Pasos de aplicación contra `sqlsap.cdepor.cl`

Con la base/usuario/secretos ya creados (`docs/05-RUNBOOK-PRODUCCION.md` §1-3):

```powershell
dotnet tool restore

dotnet tool run dotnet-ef database update `
  --project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj `
  --startup-project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj `
  --context PortalSaasDbContext `
  --connection "Server=sqlsap.cdepor.cl;Database=portalsaas_comercial_depor_prod;User Id=portalsaas_app;Password=<la contraseña real>;TrustServerCertificate=True"
```

Aplica las 4 migraciones de la tabla del §1 en orden, en una sola pasada — no hay que
invocarlas una por una.

## 5. Verificación post-aplicación

```sql
USE portalsaas_comercial_depor_prod;

SELECT name FROM sys.tables ORDER BY name;
-- 24 filas de negocio (ver la lista completa en §1) + __EFMigrationsHistory = 25 total.

SELECT * FROM __EFMigrationsHistory ORDER BY MigrationId;
-- Debe listar las 4 migraciones del §1, en ese orden.

SELECT code, name FROM actions ORDER BY code;
-- Debe listar las 6 acciones fijas (VIEW/CREATE/EDIT/DELETE/APPROVE/EXPORT) --
-- confirma que SeedFixedActions corrió de verdad, no solo que la tabla existe.

SELECT COUNT(*) FROM on_premise_licenses WHERE plan_id IS NULL;
-- Debe dar 0 -- confirma que la columna quedó NOT NULL de verdad (0 filas en una base
-- nueva hace este chequeo trivial, pero repetirlo igual: una migración "aplicada sin
-- error" no es lo mismo que "el constraint quedó bien", ver la lección de §2.2).
```

## 6. Rollback

Igual que `docs/05-RUNBOOK-PRODUCCION.md` — `dotnet-ef database update <Migración>`
apuntando a la migración anterior, con la misma `--connection`. Con 4 migraciones hoy,
un rollback completo es `dotnet-ef database update 0` — **no ejecutar sobre una base
con datos reales sin respaldo previo** (mismo criterio que el runbook original).
