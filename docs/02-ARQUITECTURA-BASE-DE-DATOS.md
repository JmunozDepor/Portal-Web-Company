# Arquitectura de base de datos — plataforma

Cubre solo la **base propia de la plataforma** (equivalente a `PORTALWEB`). La conexión
hacia el SAP de cada organización cliente (HANA o SQL Server) es un tema aparte, sin
cambios respecto a `PortalSAP_v2` — ver `ARCHITECTURE.md` §4.

## 1. Decisión: motor dual — PostgreSQL por defecto, SQL Server para on-premise existente

Decisión revisada (24 jul 2026, confirmada con el usuario) — reemplaza la versión
original de este documento, que fijaba PostgreSQL como único motor. Razón del cambio:
la solución necesita ser **mixta** desde el día 1 — un programa funcional para
Comercial Depor (que ya tiene SQL Server corriendo en `sqlsap.cdepor.cl`, sin
justificar instalar un motor nuevo ahí) y, en paralelo, el modelo pensado para vender.
Ver `docs/03-MODELO-CORE-COMERCIAL.md` — el mismo modelo EF Core Code-First (`Entities/`
+ `PortalSaasDbContext`, en `src/PortalSaas.Data`, deliberadamente sin nada específico
de un proveedor) se despliega sobre uno u otro motor según el contexto:

| Motor | Cuándo se usa |
|---|---|
| **PostgreSQL** (default) | SaaS hosteado en la nube, e instalaciones on-premise nuevas sin base de datos previa. |
| **SQL Server** | Instalaciones on-premise que **ya tienen SQL Server** corriendo (ej. Comercial Depor) — cero infraestructura nueva que instalar/mantener. |

Esto NO es lo mismo que el conector hacia el SAP del cliente (`HanaService`/
`TraductorSqlHanaASqlServer`, portado sin cambios) — son dos motores dual completamente
independientes, uno para "la base propia de la plataforma" y otro para "leer/escribir
el SAP de cada cliente". Ver §7 para el detalle técnico de cómo se implementó.

**Por qué no hace falta un traductor de SQL para esto** (a diferencia de
`TraductorSqlHanaASqlServer` en `PortalSAP_v2`): ese traductor existe porque el código
de `PortalSAP_v2` escribe SQL en texto plano a mano contra el SAP del cliente — no usa
un ORM ahí. La base propia de la plataforma, en cambio, usa **EF Core Code-First**: el
modelo en C# es la única fuente de verdad, y cada proveedor de EF Core (Npgsql/
SqlServer) ya sabe generar el DDL/DML nativo correcto a partir de ese mismo modelo — es
un traductor mejor y más confiable que cualquier traductor de texto que se pudiera
escribir a mano, y no requiere mantenimiento manual: se cambia el modelo una vez, se
regeneran las dos migraciones.

## 2. Hosting del lado PostgreSQL — plan por etapa, no una elección única para siempre

Aplica a la vía SaaS (no a la instalación on-premise de Comercial Depor, que usa SQL
Server existente — ver §1). PostgreSQL es portable entre proveedores gestionados
(protocolo estándar, sin lock-in fuerte si se evitan extensiones propietarias):

| Etapa | Opción recomendada | Por qué |
|---|---|---|
| **MVP / validación con 1-3 organizaciones piloto** | **Neon** o **Supabase** (Postgres serverless gestionado) | Costo casi cero para bajo tráfico, autoescala, branching de base de datos útil para ambientes de prueba, sin operar infraestructura. |
| **Crecimiento (10+ organizaciones reales, SLA comercial)** | **Azure Database for PostgreSQL — Flexible Server** (tier Burstable o General Purpose) | Mejor para cumplimiento/soporte enterprise, integración nativa con el resto del stack .NET/Azure si el hosting de la app también es Azure, backups/HA gestionados con SLA real. |
| **Cliente con requisito de residencia de datos en su propio país/nube** | Postgres dedicado en la región/nube que exija ese cliente | La arquitectura de organización/tenant (ver `03-MODELO-CORE-COMERCIAL.md`) debe permitir, a futuro, que una organización puntual apunte a una instancia Postgres distinta — no construir esto de entrada, pero no cerrar la puerta (ver §4). |

**No autohospedar Postgres en una VM propia** salvo que el volumen ya justifique el
ahorro — la operación (backups, parches, alta disponibilidad) tiene un costo real de
tiempo de ingeniería que un servicio gestionado ya resuelve por un costo bajo en las
etapas tempranas.

## 3. Pooling de conexiones — lección aprendida de `PortalSAP_v2`

El análisis de `HanaService`/`SapConnectionProvider` de `PortalSAP_v2` encontró que hoy
no hay pooling ni aislamiento de "vecino ruidoso" entre empresas (conexión nueva por
request). Para la base propia de la plataforma, con Postgres serverless, esto se
resuelve distinto pero hay que decidirlo a propósito:

- Postgres serverless (Neon/Supabase) tiene **límite de conexiones concurrentes bajo**
  por defecto — un pool de conexiones .NET (Npgsql ya poolea internamente) mal
  configurado puede agotarlo rápido con pocas instancias del Host corriendo.
- **Usar el pooler que trae el proveedor** (PgBouncer integrado en Supabase/Neon, modo
  `transaction`) para la cadena de conexión de la aplicación, no la conexión directa —
  esto es una configuración de infraestructura, no de código, pero debe quedar
  documentada en el `appsettings`/user-secrets del Host cuando se implemente.

## 4. Estrategia de aislamiento multi-tenant

**Default: schema compartido + `organization_id` obligatorio** en toda tabla que
dependa de contexto de cliente — mismo criterio que ya probó `PortalSAP_v2` con
`EMPRESA_CODIGO`, ahora un nivel más arriba (ver `03-MODELO-CORE-COMERCIAL.md`). Es la
opción de menor costo operativo y la correcta para la mayoría de los clientes. Aplica
igual sin importar el motor activo (§1).

**No construir aislamiento físico (schema-per-tenant o base-per-tenant) por defecto.**
Es una complejidad cara que hoy no tiene un caso real que la justifique (mismo criterio
"YAGNI" que ya aplicó el equipo en `WMS_Suite` para los adaptadores formales de WMS).

## 5. Convención de nombres — decisión consciente, distinta a HANA

Ver `docs/01-CONVENCION-NOMBRES-BD.md` — inglés, plural, `snake_case`. Se aplica
**igual en los dos motores**: `EFCore.NamingConventions` es agnóstico de proveedor, no
hay que mantener dos convenciones distintas para Postgres y SQL Server.

## 6. Migraciones — un proyecto de migraciones por motor

EF Core Code-First, no scripts `.sql` numerados a mano como en `PortalSAP_v2/db/hana/`.
El esquema de referencia para el **modelo** (no para el SQL literal) es
`PortalSAP_v2/db/hana/002_crear_tablas.sql` — la disciplina de claves/FK/unicidad de
esas 23 migraciones ya es correcta y se reproduce, extendida con la capa comercial
nueva.

Como Postgres y SQL Server generan SQL incompatible entre sí, las migraciones no
pueden convivir en el mismo ensamblado — estructura real del repo:

```
src/
├── PortalSaas.Data/                        # Entities/ + PortalSaasDbContext, SIN
│                                              nada específico de un proveedor
├── PortalSaas.Data.Migrations.PostgreSql/  # migraciones + DesignTimeDbContextFactory
│                                              (UseNpgsql), referencia PortalSaas.Data
└── PortalSaas.Data.Migrations.SqlServer/   # migraciones + DesignTimeDbContextFactory
                                               (UseSqlServer), referencia PortalSaas.Data
```

El modelo se cambia **una sola vez** en `PortalSaas.Data`; después se regenera la
migración en cada proyecto de motor (ver `CLAUDE.md` para el comando exacto). El Host
futuro (paso 3 de `ARCHITECTURE.md` §6) decide en runtime, por configuración
(`Database:Provider` = `postgresql` | `sqlserver`), cuál de los dos usar — típicamente
fijo por instalación, no por tenant individual (a diferencia de `InstanceEngineType`,
que sí varía por compañía SAP dentro de una misma organización).

## 7. Detalle técnico del motor dual

- **Sin valores por defecto generados en la base** (`gen_random_uuid()`/`NEWID()`,
  `now()`/`GETUTCDATE()` difieren por motor y no son portables) — los `Guid` y
  `DateTimeOffset` con default se generan en C#, como inicializador de propiedad en la
  entidad (`= Guid.NewGuid()`, `= DateTimeOffset.UtcNow`). Efecto idéntico, portable
  sin condicionales.
- **Todo lo demás** (`ToTable`, `HasCheckConstraint`, `HasMaxLength`, `HasPrecision`,
  `HasIndex`) es parte de la API relacional de EF Core, no específico de un proveedor
  — el código de `PortalSaasDbContext.OnModelCreating` es el mismo sin importar el
  motor activo.
- **Identity/auto-incremento** en las PK `bigint`: se deja el default de cada
  proveedor (no se fuerza `GENERATED ALWAYS AS IDENTITY` de Postgres) — ambos motores
  auto-incrementan igual de bien sin configuración adicional.
