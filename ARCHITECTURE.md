# Proyecto Saas Portal — Documento de Arquitectura

> Estado: **fundacional**. Este proyecto es la vía de evolución hacia un producto vendible
> (SaaS + on-premise) de lo aprendido en `PortalSAP_v2`. No reemplaza a `PortalSAP_v2` —
> ese repo sigue sirviendo a Comercial Depor sin interrupciones. Este es un proyecto
> **paralelo**, no un fork ni una migración en caliente.
>
> (Nace de un primer borrador llamado "Proyecto Mejora PortalSAP", consolidado acá con
> el nombre definitivo — ver `docs/00-HISTORIAL-DECISIONES.md` para la línea de tiempo
> completa de decisiones previas a este repo.)

## -1. Qué hay en esta carpeta

```
Proyecto Saas Portal/
├── CLAUDE.md                      # reglas duras + resumen operativo (este proyecto)
├── ARCHITECTURE.md                # este archivo — visión y razonamiento completo
├── docs/
│   ├── 00-HISTORIAL-DECISIONES.md # memoria completa del análisis previo (Proyecto Menoja)
│   ├── 02-ARQUITECTURA-BASE-DE-DATOS.md
│   └── 03-MODELO-CORE-COMERCIAL.md
└── referencia-original/            # COPIA de solo lectura de los repos originales,
    ├── PortalSAP_v2/                # para portar/consultar sin tocar el repo real
    └── WMS_Suite/                  # (que sigue operando para Comercial Depor)
```

**`referencia-original/` es de solo lectura y de referencia** — nunca se edita ahí ni
se apunta código nuevo a esas rutas. Es una copia del disco tomada el 24 jul 2026 para
poder revisar/portar código sin depender de tener `C:\PROYECTOS\PortalSAP_v2` y
`C:\PROYECTOS\WMS_Suite` abiertos en paralelo, y sin riesgo de tocar por accidente el
sistema que hoy está en producción. Si se necesita el estado más reciente de esos
repos, hay que volver a copiar — esto no es un symlink ni se sincroniza solo.

## 0. Por qué existe este proyecto y no se hace todo dentro de `PortalSAP_v2`

El análisis de arquitectura de julio 2026 (ver memoria "Proyecto Menoja") concluyó:
la arquitectura núcleo de `PortalSAP_v2` (aislamiento real de plugins vía
`AssemblyLoadContext`, modelo multi-empresa, motores de documento genéricos, motor de
aprobación configurable, conector dual HANA/SQL Server) es sólida y **se debe reutilizar**,
no reconstruir. Pero cuatro cosas faltan por completo y no conviene construirlas encima
de un sistema que ya está en producción para un cliente real:

1. **Capa comercial** (planes, licencias, medición de uso) — no existe en `PortalSAP_v2`.
2. **Separación núcleo/vertical** — `GestionDistribucionGastos` y `SellOut` son desarrollo
   a medida de Comercial Depor (confirmado explícitamente por el dueño del proyecto),
   no son parte de lo que se vende a un cliente nuevo.
3. **Base de datos propia de la plataforma** — hoy vive dentro del HANA de un cliente
   (`PORTALWEB`), lo correcto para "instalado en 1 cliente", equivocado para SaaS.
4. **Higiene de seguridad y disciplina de tests** desde el día 1, no como deuda a pagar
   después (a diferencia de `PortalSAP_v2`, que hoy tiene 2 archivos de test reales para
   ~400 nodos de código en `Core`).

Construir esto en un proyecto paralelo permite: (a) no arriesgar el sistema que hoy
factura/opera para Comercial Depor, (b) diseñar la capa comercial y el modelo de datos
correctos desde el inicio en vez de parchar un esquema pensado para 1 cliente, y
(c) decidir con calma qué del código de `PortalSAP_v2` se porta tal cual, qué se
generaliza, y qué se descarta.

## 1. Qué es esto

La evolución de `PortalSAP_v2` hacia un producto que se pueda vender a **cualquier
empresa que use SAP Business One** (HANA o SQL Server), en dos modalidades de entrega:

- **SaaS**: hosteado centralmente, multi-organización real, con plan/licencia/medición
  de uso y facturación.
- **On-premise**: instalado en la infraestructura del cliente, con licencia por clave de
  activación y expiración, sin depender de conectividad hacia un hosting central.

Ambas modalidades comparten el mismo código base — la diferencia es dónde vive la base
de datos propia de la plataforma y cómo se valida la licencia, no una reescritura.

## 2. Qué se reutiliza de `PortalSAP_v2` y cómo

**Se porta/replica, nunca se referencia entre proyectos** — los dos repos son
independientes en su ciclo de vida (regla dura, ver `CLAUDE.md` §Reglas).

| Pieza de `PortalSAP_v2` | Decisión |
|---|---|
| Aislamiento de plugins (`AssemblyLoadContext`, `IModuloPortal`) | Se porta tal cual — es la pieza más valiosa y ya está verificada contra el código real. |
| `GenericoVenta`/`GenericoCompra`/`GenericoInventario` | Se portan tal cual — son producto, no desarrollo a medida. |
| Motor de aprobación generalizado | Se porta tal cual. |
| `HanaService`/`TraductorSqlHanaASqlServer`/`SqlServerService` (conexión al SAP del cliente) | Se porta tal cual — sigue siendo la única forma correcta de hablar con el SAP de cada organización, sin importar el motor propio de la plataforma. |
| Esquema `PORTALWEB` (usuarios, menú, permisos, empresa/instancia) | Se **reproduce el modelo** (misma disciplina de claves/FK/`EMPRESA_CODIGO`), portado de dialecto HANA a PostgreSQL, y extendido con la capa comercial (ver §4). No es un copy-paste de SQL. |
| `GestionDistribucionGastos`, `SellOut` | **No se portan.** Son desarrollo a medida de Comercial Depor, confirmado explícitamente fuera del alcance de este proyecto. |
| Higiene de tests/CI | No se hereda el gap — este proyecto exige tests desde el primer módulo comercial (ver `CLAUDE.md`). |

## 3. Arquitectura general (sin cambios respecto a `PortalSAP_v2`)

Modular monolith con plugins reales cargados en runtime — mismo patrón, mismas reglas
de dependencia (`Abstractions` → todos, `Core` → solo `Host`, ningún plugin referencia
`Host`/`Core`/otro plugin). Ver `ARCHITECTURE.md` de `PortalSAP_v2` para el detalle
completo de esa decisión — no se repite acá.

Lo que cambia es **qué hay debajo del Core**: una capa comercial nueva que gobierna
quién puede usar qué, con qué límites, y bajo qué licencia.

## 4. Dos bases de datos, dos decisiones distintas

1. **Datos de SAP de cada organización cliente** (HANA o SQL Server) — no es una
   elección propia, la define lo que el cliente tenga instalado. Sin cambios respecto a
   `PortalSAP_v2`.
2. **Base propia de la plataforma** (equivalente a `PORTALWEB`, ahora con capa
   comercial) — **decisión: PostgreSQL gestionado**, no HANA. Ver
   `docs/02-ARQUITECTURA-BASE-DE-DATOS.md` para el detalle completo (hosting, pooling,
   estrategia de aislamiento multi-tenant, convención de nombres).

## 5. Capa comercial — resumen

Ver `docs/03-MODELO-CORE-COMERCIAL.md` para el modelo completo. Idea central: hoy
`EMPRESA` (una compañía/schema SAP) es la unidad más alta del modelo de
`PortalSAP_v2`. Para SaaS real hace falta un nivel **por encima**: `organizations` — el
cliente que paga, que puede tener una o varias `companies` (igual que Comercial Depor
hoy tiene DEPOR + DEPORQA). Todo lo comercial (plan, suscripción, licencia, límites,
medición de uso) cuelga de `organizations`, no de `companies` — los límites de contrato
son por cliente, no por compañía SAP individual.

## 6. Próximos pasos sugeridos

1. ✅ Levantar un Postgres gestionado de desarrollo — `docker-compose.yml` (local) +
   `docs/02-...md` para opciones de hosting gestionado por etapa.
2. ✅ Portar el esquema `PORTALWEB` + la capa comercial nueva (`docs/03-...md`) con
   EF Core Code-First — **motor dual** (Postgres/SQL Server, no solo Postgres, ver §7
   de `docs/02-...md`), con `IContractLimitService` implementado y testeado.
   Extendido además con autenticación/recuperación/preferencias (correo obligatorio,
   `IAuthenticationService`, `IPasswordResetService`, `IUserPreferenceService` — ver
   `docs/06-AUTENTICACION-Y-PREFERENCIAS.md`, más envío de correo dual Google Workspace/Microsoft 365). **40 tests reales**,
   `tests/PortalSaas.Core.Tests`.
3. ✅ (parcial) Portar de `PortalSAP_v2` lo que no depende de HANA/SAP ni de tablas
   núcleo todavía inexistentes: `IModuloPortal`/`MenuItemDefinition` (contrato de
   plugin), `PluginLoadContext`/`PluginManager` (cargador de plugins, con el bug de
   orden de versión ya corregido), `SecretoCifradoService` (AES-256-GCM),
   `PasswordHasher` (PBKDF2-SHA256).
4. ✅ `PortalSaas.Host` — login (resuelve organización por `Slug`, nuevo campo),
   logout, recuperación de contraseña (junta `IPasswordResetService` +
   `IEmailSenderService`, el flujo real de punta a punta), preferencias personales.
   Probado con `dotnet run` + `curl`: rutas/redirects/autorización funcionan
   (`/` → login, `/Home/Index` rechaza sin sesión). El login POST real y la
   recuperación con correo real siguen sin probarse contra una base real (falta
   Postgres/SQL Server local del lado del usuario). Evaluado y descartado por ahora:
   perfiles multi-organización (1 usuario = 1 organización se mantiene, ver
   `docs/03-...md` §1).
5. **Pendiente**: portar `HanaService`/`SapConnectionProvider`/traductor, tablas
   núcleo heredadas (`menus`/`profiles`/`actions`/`menu_groups`/etc.),
   `CurrentUserContext`/`CurrentEmpresaAccessor`, y sincronización de menú en
   `PluginManager` (hoy deliberadamente sin ella, ver el propio archivo).
6. Portar los 3 motores genéricos y el motor de aprobación — deliberadamente pospuesto
   hasta tener un caso real que los necesite; son SAP-específicos y no tienen
   consumidor en este proyecto todavía.
7. Construir `Modulo.Administracion` con la capa comercial nueva (alta de organización,
   plan, licencia) como el primer módulo de negocio real de este proyecto — sin esto no
   hay forma de dar de alta un cliente nuevo.
8. Validar todo con una organización de prueba distinta de Comercial Depor antes de
   considerar esto "listo para vender".
