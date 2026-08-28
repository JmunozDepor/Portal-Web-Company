# Runbook — despliegue de una instalación on-premise (SQL Server)

Guía para desplegar la base propia de la plataforma contra un SQL Server que el
cliente ya tiene (ej. Comercial Depor en `sqlsap.cdepor.cl`) — motor `sqlserver`, ver
`docs/02-ARQUITECTURA-BASE-DE-DATOS.md` §1. **Nadie ha ejecutado este runbook
todavía** — es la guía para cuando se decida hacerlo, no un registro de que ya se hizo.

No incluye credenciales reales ni las inventa — cada paso indica dónde va el valor
real, nunca lo escribe.

## 1. Antes de empezar

- Nombre de base según `docs/01-CONVENCION-NOMBRES-BD.md` §13: `portalsaas_<client_slug>_<environment>`.
  Para Comercial Depor producción: **`portalsaas_comercial_depor_prod`**.
- Confirmar con el DBA/administrador del servidor que el nombre no choca con nada
  existente (`CLDEPORFIN`/`CLSELLOUT`/`CLINV` ya están ahí).
- Definir el usuario SQL Server dedicado para esta base (no reusar `sa_loc` u otro
  usuario compartido de otro sistema) — principio de menor privilegio: acceso solo a
  `portalsaas_comercial_depor_prod`, no a las demás bases del servidor.

## 2. Crear la base y el usuario (ejecutar el DBA, en el servidor real)

```sql
CREATE DATABASE portalsaas_comercial_depor_prod;
GO

CREATE LOGIN portalsaas_app WITH PASSWORD = '<contraseña real, generada ahí mismo>';
GO

USE portalsaas_comercial_depor_prod;
CREATE USER portalsaas_app FOR LOGIN portalsaas_app;
ALTER ROLE db_owner ADD MEMBER portalsaas_app;
GO
```

`db_owner` es lo que necesita EF Core para aplicar migraciones (`CREATE TABLE`, etc.)
— si se quiere separar "usuario que migra" de "usuario que la aplicación usa en
runtime" (más granular, más operación), evaluarlo aparte; no es parte de este runbook
mínimo.

## 3. Configurar el secreto (nunca en un archivo versionado)

En el servidor donde corra el Host (todavía no existe, ver `CLAUDE.md` "Todavía no
existe" — este paso aplica el día que exista):

```
dotnet user-secrets set "ConnectionStrings:Default" "Server=sqlsap.cdepor.cl;Database=portalsaas_comercial_depor_prod;User Id=portalsaas_app;Password=<la contraseña real>;TrustServerCertificate=True" --project <PortalSaas.Host>
```

O, si el despliegue es un servicio real (IIS/systemd/contenedor), variable de entorno
en vez de user-secrets:
```
PORTALSAAS_ConnectionStrings__Default=Server=sqlsap.cdepor.cl;Database=portalsaas_comercial_depor_prod;User Id=portalsaas_app;Password=<la contraseña real>;TrustServerCertificate=True
```

También hace falta la clave maestra de cifrado (`Security:MasterSecretKey`, ver
`SecretoCifradoService`) — generar una nueva de 32 bytes, nunca reusar la de
desarrollo:
```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))
```
Guardarla con el mismo mecanismo (`user-secrets`/variable de entorno), nunca en un
archivo del repo.

## 4. Aplicar las migraciones

Desde una máquina con acceso de red al servidor (no necesariamente el servidor mismo):

```
dotnet tool restore
dotnet tool run dotnet-ef database update \
  --project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj \
  --startup-project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj \
  --context PortalSaasDbContext \
  --connection "Server=sqlsap.cdepor.cl;Database=portalsaas_comercial_depor_prod;User Id=portalsaas_app;Password=<la contraseña real>;TrustServerCertificate=True"
```

El flag `--connection` sobrescribe, solo para este comando, lo que devuelve
`DesignTimeDbContextFactory` — no hace falta editar ningún `appsettings.Development.json`
para esto (esos son solo para desarrollo local, ver `CLAUDE.md`).

## 5. Verificación post-despliegue

```sql
USE portalsaas_comercial_depor_prod;
SELECT name FROM sys.tables ORDER BY name;
-- Deben aparecer las 11 tablas: companies, instances, on_premise_licenses,
-- organization_modules, organizations, plan_modules, plans, platform_modules,
-- subscriptions, usage_metrics, users.

SELECT * FROM __EFMigrationsHistory;
-- Debe listar "InitialCreate" (y cualquier migración posterior).
```

## 6. Dar de alta la organización y la licencia on-premise

Una vez la base existe, insertar (a mano por ahora — no hay UI de administración
todavía, ver `ARCHITECTURE.md` §6 paso 5):

```sql
INSERT INTO organizations (id, legal_name, tax_id, country, mode, status, created_at)
VALUES (NEWID(), 'Comercial Depor', '<RUT real>', 'CL', 'on_premise', 'active', SYSDATETIMEOFFSET());
```

Luego `on_premise_licenses` con `activation_key`/`expires_at` reales (ver
`docs/03-MODELO-CORE-COMERCIAL.md` §5 para el modelo completo) y, finalmente,
`instances`/`companies` apuntando al HANA/SQL Server real de SAP B1 de Comercial Depor
(eso sigue siendo el motor de conexión SAP, sin relación con esta base — ver
`ARCHITECTURE.md` §4).

## Rollback

```
dotnet tool run dotnet-ef database update <MigraciónAnterior> \
  --project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj \
  --startup-project src/PortalSaas.Data.Migrations.SqlServer/PortalSaas.Data.Migrations.SqlServer.csproj \
  --context PortalSaasDbContext \
  --connection "<la misma connection string>"
```
Con una sola migración (`InitialCreate`) hoy, "rollback" equivale a
`dotnet-ef database update 0` (deshace todo) — no ejecutar esto sobre una base con
datos reales sin respaldo primero.
