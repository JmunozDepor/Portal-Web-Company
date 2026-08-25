# 10 — PostgreSQL de plataforma (QA + productivo) en servidor dedicado

Cubre solo la base de datos propia de la plataforma (Postgres) — completamente
separada del SQL Server de SAP (`sqlsap.cdepor.cl`, `PS_CENTRAL`/`PS_COMDEPOR`/etc.)
-- esa base es de otro proyecto (`PortalSAP_v2`) y nunca se toca desde acá. **La app
(`PortalSaas.Host`) sigue corriendo en IIS**, decisión tomada porque `PortalSaas.Core`
depende del cliente nativo de SAP HANA (`Sap.Data.Hana.Net.v8.0.dll`, Windows/x64-only,
ver `CLAUDE.md` "Cambio de plataforma de build a x64") y no puede correr dentro de un
contenedor Linux estándar.

## Estado real (actualizado 20 ago 2026) — reemplaza el enfoque Docker original

**Decisión revisada**: en vez de 2 contenedores Docker (`postgres-test`/`postgres-prod`,
enfoque original de este documento, ver más abajo "Enfoque original (Docker,
histórico)"), se optó por **una única instancia PostgreSQL nativa en Windows** (no
contenedor), con **2 bases de datos separadas dentro del mismo servidor** — más simple
de administrar (un solo servicio Windows, un solo puerto, un solo firewall que abrir).

- **Servidor**: `172.16.122.171:5432` (red interna corporativa, detrás de VPN Fortinet
  SSL — no accesible desde internet). PostgreSQL 18.6, instalación nativa de Windows
  (no Docker).
- **Bases** (convención `docs/01-CONVENCION-NOMBRES-BD.md` §13, `environment` ∈
  {dev, qa, prod} — nunca "test"):
  - `portalsaas_saas_prod` — la base real, consumida por `PortalSaas.Host` en IIS
    productivo.
  - `portalsaas_saas_qa` — ambiente de prueba, mismo esquema, datos independientes.
- **Usuario de aplicación**: `admin_saas` (superusuario del servidor — evaluar crear un
  rol de aplicación con permisos acotados por base antes de exponer esto a un
  ambiente productivo real, no se hizo en esta entrega).
- **Migraciones**: aplicadas directo con `dotnet ef database update` contra cada base
  (ver "Aplicar migraciones nuevas" más abajo) — sin dump/`docker-entrypoint-initdb.d`,
  las dos bases se crearon vacías y se migraron desde cero.
- **Ambiente local de desarrollo**: apunta a `portalsaas_saas_qa` vía
  `dotnet user-secrets` (nunca en `appsettings.Development.json`, que sigue apuntando
  al Docker de desarrollo local — ver `docker-compose.yml` en la raíz del repo, sin
  relación con esto):
  ```powershell
  dotnet user-secrets set "ConnectionStrings:Default" `
    "Host=172.16.122.171;Port=5432;Database=portalsaas_saas_qa;Username=admin_saas;Password=<clave-real>" `
    --project src/PortalSaas.Host
  ```

### Diagnóstico real de conectividad (20 ago 2026, dejado documentado por si se repite)

La conexión inicial falló con `Connection refused` — diagnóstico completo con
`Test-NetConnection`/`tracert`/sniffer del FortiGate (`diagnose sniffer packet`)
confirmó que **la ruta de red y las políticas del FortiGate estaban bien** (política
`SSL-VPN-TO-172` con Servicio ALL/Acción ACEPTAR, el `RST` se veía llegar directo desde
el destino, no generado por el firewall). La causa real fue mucho más simple: **la IP
del servidor que se estaba usando para probar era incorrecta** (`172.16.122.186` en vez
de la real, `172.16.122.171`) — un error humano, no un problema de infraestructura. Una
vez corregida la IP, la conexión funcionó al primer intento. Si esto se repite,
**confirmar la IP real del servidor antes que nada**, no asumir un problema de red.

### Configurar `PortalSaas.Host` en IIS (productivo/QA)

En el `appsettings.Production.json` (nunca versionado) de cada instalación IIS:

```json
{
  "Database": { "Provider": "postgresql" },
  "ConnectionStrings": {
    "Default": "Host=172.16.122.171;Port=5432;Database=portalsaas_saas_prod;Username=admin_saas;Password=<clave-real>"
  }
}
```

QA usa la misma cadena con `Database=portalsaas_saas_qa`. Confirmar que el firewall del
servidor (Windows Firewall + cualquier firewall perimetral en el camino) permite el
puerto 5432 **solo** desde las IPs que realmente lo necesitan (servidores IIS + accesos
de desarrollo autorizados), nunca expuesto abierto a internet.

### Aplicar migraciones nuevas

Después de generar una migración nueva (`docs/CLAUDE.md` tiene el comando de
`migrations add`), aplicarla contra cada base real:

```bash
ConnectionStrings__Default="Host=172.16.122.171;Port=5432;Database=portalsaas_saas_qa;Username=admin_saas;Password=<clave-real>" \
dotnet tool run dotnet-ef database update \
  --project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj \
  --startup-project src/PortalSaas.Data.Migrations.PostgreSql/PortalSaas.Data.Migrations.PostgreSql.csproj \
  --context PortalSaasDbContext
```

Repetir con `Database=portalsaas_saas_prod` para productivo — **siempre probar primero
contra `qa`**, nunca aplicar una migración nueva contra `prod` sin haberla validado ahí
primero.

## Enfoque original (Docker, histórico — ya no vigente, no usado)

Lo que sigue describe el plan original de 2 contenedores Docker independientes
(`postgres-test`/`postgres-prod`) en un servidor cloud. Se documenta por trazabilidad,
pero **no es el estado real** (ver sección de arriba) — `docker-compose.prod.yml`/
`.env.prod.example`/`scripts/dump-postgres.ps1` siguen en el repo sin usarse, no se
borraron por si se retoma un enfoque containerizado más adelante, pero no reflejan la
infraestructura real hoy.

### Archivos

- `docker-compose.prod.yml` — 2 servicios Postgres 16 (`postgres-test`/
  `postgres-prod`), cada uno con su propio volumen, puerto y credenciales.
  Healthcheck en los dos. Lee credenciales de `.env.prod` (nunca versionado).
- `.env.prod.example` — plantilla de variables para ambas bases, copiar a
  `.env.prod` y completar con contraseñas DISTINTAS para test y prod.
- `db/init/` — carpeta montada en `/docker-entrypoint-initdb.d` de **los dos**
  contenedores. Postgres ejecuta cualquier `.sql`/`.sh` que encuentre ahí **solo la
  primera vez** que el volumen de datos está vacío (comportamiento estándar de la
  imagen oficial `postgres`) -- los dos arrancan con el mismo dump el día 1 y
  después divergen de forma independiente (cada uno con su propio volumen). Ni la
  carpeta ni su contenido se versionan (ver `.gitignore`) — contienen datos reales
  de clientes.
- `scripts/dump-postgres.ps1` — genera `db/init/01-dump.sql` desde el contenedor de
  desarrollo local (`docker-compose.yml`).

### Puertos

| Contenedor       | Base                    | Puerto default |
|------------------|-------------------------|----------------|
| `postgres-test`  | `TEST_POSTGRES_DB`      | `5433`         |
| `postgres-prod`  | `PROD_POSTGRES_DB`      | `5432`         |

Ajustables vía `.env.prod` (`TEST_POSTGRES_PORT`/`PROD_POSTGRES_PORT`) si el
servidor ya usa esos puertos para otra cosa.
