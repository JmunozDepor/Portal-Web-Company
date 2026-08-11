# Estado actual — piloto Comercial Depor + entorno de desarrollo

Foto del estado real al 2026-08-02. Complementa `docs/10-RUNBOOK-IIS-PILOTO.md` (cómo se
publicó) — esto documenta **qué hay corriendo ahora mismo, dónde, y con qué credenciales**,
para que cualquier otro servicio/desarrollador (humano o agente) que necesite integrar o
probar contra esto sepa exactamente qué usar sin tener que reconstruirlo de memoria.

## 1. Topología: dos roles, mismo binario (`PortalSaas.Host`)

| Rol | Qué hace | Dónde corre hoy |
|---|---|---|
| **Central** | Emite/valida licencias on-premise (`/api/licensing/*`), panel admin de plataforma (Plans/Organizations/Licenses) | IIS real: `https://depor-sl.sapenlanube.com:9003/portalsaas-central` · Local: `http://localhost:6001` |
| **OnPremise (Comercial Depor)** | El portal que usan los usuarios del piloto — Ventas/Compras/Inventario contra el HANA real de Comercial Depor | IIS real: `https://depor-sl.sapenlanube.com:9003/portalsaas-comercialdepor` · Local: `http://localhost:6002` |

Ver `src/PortalSaas.Host/Program.cs` (`Licensing:Role`) y
`src/PortalSaas.Core/Comercial/Licenciamiento/` para el mecanismo de licenciamiento
completo (firma ECDSA, heartbeat, detección de fingerprint duplicado).

## 2. Bases de datos (SQL Server real, no local)

Ambas viven en **`sqlsap.cdepor.cl,11433`** — no hay bases locales, todo el desarrollo
(local y piloto) apunta al mismo servidor SQL Server real:

- `PS_CENTRAL` (renombrada 2026-08-08, antes `portalsaas_central`) — base propia de la
  plataforma para el rol Central.
- `PS_COMDEPOR` (renombrada 2026-08-08, antes `portalsaas_comercialdepor`) — base propia
  de la plataforma para el rol OnPremise de Comercial Depor. Contiene la Organization
  `comercial-depor`, los usuarios tenant, y las `instances`/`companies` con la conexión
  SAP (ver §4).
- `PS_COMDEPOR_RG` (renombrada 2026-08-08 a `PS_COMDEPOR_PLUGINS`, y 2026-08-09 a este
  nombre final -- el sufijo `_PLUGINS` era engañoso porque no es una base compartida;
  antes `portalsaas_rendiciones_comercialdepor`) — base propia y EXCLUSIVA de
  `Modulo.Rendiciones` (`RG` = Rendición de Gastos), resuelta vía
  `IExternalDatabaseConnectionService` (ver `CLAUDE.md`, regla dura de scoping por
  Company). `Modulo.GestionDistribucionGastos` NO vive acá -- su base (`CLDEPORFIN`) es
  externa preexistente, fuera de esta convención de nombres. Si en el futuro aparece un
  segundo plugin externo con base propia, recién ahí evaluar si comparte esta base (con
  su propio schema + login de mínimo privilegio) o si va a la suya — hoy, con un solo
  plugin real, es 1:1.

Usuario SQL: `saas_temp` (credenciales en poder de quien esté ejecutando pruebas —
no se repiten acá).

**Importante para cualquiera que corra migraciones o pruebe algo nuevo**: local y
piloto comparten la MISMA base — un cambio de datos local (insert/update/delete) es
visible de inmediato en el piloto real, y viceversa. Ver §6 sobre el riesgo real de
licencia que esto ya causó una vez.

## 3. Credenciales de prueba vigentes

| Rol | Login | Usuario | Contraseña |
|---|---|---|---|
| Platform admin Central | `/portalsaas-central/Admin/Login` (IIS) o `:6001/Admin/Login` (local) | `admin-central@test.local` | `Test-Central-2026!` |
| Platform admin OnPremise | `/portalsaas-comercialdepor/Admin/Login` (IIS) o `:6002/Admin/Login` (local) | `admin-comercialdepor@test.local` | `Test-ComercialDepor-2026!` |
| Usuario tenant (piloto) | `/portalsaas-comercialdepor/Account/Login` (IIS) o `:6002/Account/Login` (local) — Organización: `depor` (slug acortado 2026-08-02, era `comercial-depor`) | `piloto` | `Piloto-Depor-2026!` |

## 4. Conexión SAP (HANA) — Instance + 2 Companies

Una sola `Instance` (`HW-DEPOR-HDB:30015`, motor HANA, usuario técnico
`DBUSERDEPOR3`) sirve a **dos** `Company` distintas dentro de la organización
`comercial-depor` — mismo Service Layer (`https://depor-sl.sapenlanube.com:50000/b1s/v1`,
usuario `jmunoz`) para ambas, solo cambia la base SAP:

| Company (código) | Base SAP | Uso |
|---|---|---|
| `Depor` | `CLPRDDEPOR` | Producción real del cliente — **cuidado con lo que se crea/modifica acá** |
| `DeporTest` | `CLTSTDEPOR_OMS` | Testing — usar esta para probar módulos (Ventas/Compras/Inventario) sin tocar datos reales |

**Aviso real para cualquiera que pruebe**: como Company vive en la base compartida
(§2), la opción "Comercial Depor (Testing)" aparece tanto en local como en el
servidor IIS real — cualquier usuario del piloto la puede seleccionar también. Si eso
deja de ser aceptable, hay que restringirla o sacarla antes de un uso más amplio.

## 5. Claves y secretos vigentes (piloto — rotar antes de producción real)

Generadas y usadas en este piloto, documentadas acá porque ya están desplegadas —
**no son aptas para producción real** tal cual (aparecieron en texto plano durante
esta sesión de trabajo, ver nota de seguridad en `docs/10-RUNBOOK-IIS-PILOTO.md`):

- `Licensing:ActivationKey` vigente: `sAs4ImoPXnINrtE2kjRAhcHDs/Rqhjhl` (rotada una vez
  ya, la de la prueba de escritorio original quedó invalidada).
- `Security:MasterSecretKey` — una por instalación (Central ≠ OnPremise), cifra
  `TechnicalSecretKey`/`IntegrationSecretKey` de `instances`/`companies`. Sin la clave
  correcta, esos campos no se pueden descifrar — no se puede "adivinar" ni reusar la
  de otro rol.
- Par de claves ECDSA P-256 de firma de licencia (`Licensing:SigningPrivateKey` solo
  en Central, `Licensing:CentralPublicKey` en ambos).

Quien necesite estos valores exactos para levantar una instancia nueva, pedirlos —
no se repiten en texto plano en más de un lugar a propósito.

## 6. Incidentes reales de esta sesión (para no repetirlos)

- **Nunca crear una segunda `on_premise_licenses` para la misma organización sin
  pensar en el orden.** `CheckLicenseAsync` siempre usa la licencia con `issued_at`
  más reciente de la organización, sin importar qué `ActivationKey` use cada
  instalación física. Insertar una licencia de prueba nueva (aunque sea para "no
  interferir") rompió momentáneamente el acceso del piloto real porque pasó a ser la
  "más reciente" con `signed_status_token` en `NULL`. Se corrigió borrándola. Antes de
  tocar `on_premise_licenses`, confirmar cuál es la fila que el piloto real está
  usando.
- **Bypass de seguridad encontrado y revertido** (2026-08-02): `Login.cshtml.cs` tenía
  `if (!access.IsAllowed && false) // TEMP DEBUG bypass` que anulaba el gate de
  licencia por completo. Origen no confirmado (no fue un cambio consciente de esta
  sesión). Ya revertido en el código; **confirmar que el binario desplegado en IIS
  también tenga el revert** (ver `docs/10-RUNBOOK-IIS-PILOTO.md`, sección de redeploy
  de seguridad) antes de dar por cerrado el tema.
- **Instancias locales corriendo desde la misma DB que el piloto real deben usar un
  rol sin heartbeat activo.** Levantar el rol OnPremise localmente con
  `Licensing:Role=OnPremise` real (heartbeat propio) genera un fingerprint local que
  compite con el del servidor IIS real y dispara un conflicto de licencia. Para
  desarrollo local: NO setear `Licensing:Role` en la instancia local de Comercial
  Depor — así solo lee el token ya firmado por el heartbeat real de IIS, sin pelear
  por el fingerprint. El rol Central sí puede correr localmente sin problema (nadie le
  pega salvo que se lo pida a propósito).
- **IIS hostea cada app como subaplicación de un sitio compartido (`:9003`), no como
  sitio propio.** Cualquier código que arme una URL con un literal absoluto
  (`"/Home/Index"`, `$"{RouteBase}/..."`) en vez de `Url.Content("~/...")` o
  `Request.PathBase + ...` pierde el prefijo de la subaplicación y cae en 404. Ya
  corregido en `Account/Login`, `Account/SelectCompany`, `Admin/Login`, y los 3
  motores genéricos (`Index`/`DetailGenericXDocumentModelBase.cs` de
  Ventas/Compras/Inventario) — cualquier código NUEVO que arme URLs a mano tiene que
  seguir el mismo patrón.
- **Los Application Pools de IIS necesitan `Load User Profile = True`.** Sin esto,
  `ECDsa.Create()`/`ImportPkcs8PrivateKey` (firma de licencias) falla con
  `CryptographicException: The system cannot find the file specified` — Windows CNG
  no tiene dónde alojar el key container sin perfil de usuario cargado. Ver
  `docs/10-RUNBOOK-IIS-PILOTO.md` para el comando exacto.

## 7. Cómo levantar el entorno local de desarrollo (resumen — ver conversación/runbooks para el detalle)

```powershell
# Central (opcional para dev local -- solo hace falta si vas a tocar algo del panel admin)
$env:Database__Provider = "sqlserver"
$env:ConnectionStrings__Default = "Server=sqlsap.cdepor.cl,11433;Database=PS_CENTRAL;User Id=saas_temp;Password=<pedir>;TrustServerCertificate=True"
$env:Security__MasterSecretKey = "<pedir>"
$env:Licensing__Role = "Central"
$env:Licensing__SigningPrivateKey = "<pedir>"
$env:Licensing__CentralPublicKey = "<pedir>"
dotnet run --project src\PortalSaas.Host --urls "http://localhost:6001"

# Comercial Depor (SIN Licensing:Role -- ver incidente en §6)
$env:ConnectionStrings__Default = "Server=sqlsap.cdepor.cl,11433;Database=PS_COMDEPOR;User Id=saas_temp;Password=<pedir>;TrustServerCertificate=True"
$env:Security__MasterSecretKey = "<pedir, distinta de la de Central>"
$env:Licensing__CentralPublicKey = "<misma que arriba>"
dotnet run --project src\PortalSaas.Host --urls "http://localhost:6002"
```

Plugins (`artifacts/plugins/`) se cargan automáticamente al arrancar — recompilar el
plugin correspondiente (`dotnet build plugins/Modulo.X -c Release`) alcanza para que
la próxima corrida del Host lo tome (target `PublicarComoPlugin` copia solo).

## 8. Qué falta / pendiente conocido

- Confirmar redeploy en IIS del fix de seguridad (§6).
- Decidir si `DeporTest`/`CLTSTDEPOR_OMS` debe quedar visible para usuarios reales del
  piloto o restringirse.
- Migrar `PS_CENTRAL` a Azure (SQL o Postgres, a definir) — mencionado como plan futuro,
  no iniciado.
- Rotar claves/secretos de §5 antes de cualquier uso más allá de este piloto.
- Bug real corregido 2026-08-08: `CompanySwitcher` del topbar revertía en silencio a la
  compañía anterior sin explicar por qué (causa real: `CompanySessionActivator.
  TryActivateAsync` devuelve `null` cuando el usuario no tiene acceso a esa compañía
  puntual, y el handler no avisaba) -- ahora `_Layout.cshtml` muestra un banner de error.
  El subtítulo "Portal SaaS / X" del sidebar también mostraba `OrganizationSlug` (fijo)
  en vez de la Company activa -- corregido para usar el claim `CompanyCode`, que sí se
  reemite en cada switch.
- Bug real corregido 2026-08-08: el selector de Compañía del topbar (`CompanySwitcher`)
  nunca marcaba la opción activa -- `SelectListItem.Value` se armaba con `c.Id.ToString()`
  DENTRO de la consulta LINQ, que EF Core traduce a `CONVERT(varchar(36), ...)` en SQL
  Server (mayúsculas), contra el claim `CompanyId` armado en C# (minúsculas) -- la
  comparación ordinal nunca coincidía. Corregido a `StringComparison.OrdinalIgnoreCase`
  en `Default.cshtml`.
- Bug real corregido 2026-08-08: `dotnet ef database update` fallaba siempre con "Named
  Pipes Provider, error 40" sin importar la connection string pasada por variable de
  entorno -- `DesignTimeDbContextFactory` (SqlServer y PostgreSql) leía con
  `prefix: "PORTALSAAS_"`, inconsistente con el nombre estándar (`ConnectionStrings__Default`,
  sin prefijo) que usa el resto del proyecto -- nunca encontraba la variable y caía al
  fallback hardcodeado `Server=localhost` (inexistente en cualquier equipo real). Sacado
  el prefijo en los dos factories.
- Bug real corregido 2026-08-08: Data Protection sin persistir a disco -- cada reinicio
  del proceso invalidaba cookies/tokens antiforgery de pestañas abiertas de antes,
  síntoma real "HTTP ERROR 400" en `POST /Admin/Logout` (y cualquier otro POST) desde una
  pestaña vieja. Agregado `AddDataProtection().PersistKeysToFileSystem(...)` en
  `Program.cs` -- el llavero ahora sobrevive reinicios/recycles de App Pool.
- Bug real corregido 2026-08-08: clave privada PEM de Google Workspace (Admin >
  Organización > Correo) rechazada con "No supported key formats were found" al pegar el
  valor `private_key` crudo del .json de la cuenta de servicio (trae `\n` LITERAL, no
  saltos de línea reales). Normalizado tanto al guardar (`EmailSettings/Index.cshtml.cs`)
  como al firmar el JWT (`GoogleServiceAccountJwtBuilder`, defensa para configs ya
  guardadas antes del fix).
- Feature agregada 2026-08-09: `Tenant:DefaultOrganizationSlug` (appsettings/env var) --
  precarga y bloquea el campo "Organización" en `Login.cshtml` para un perfil OnPremise
  de una sola organización real, mismo patrón ya usado por `Tenant:DefaultCompanyCode`
  un paso más adelante (`SelectCompany.cshtml`). En local (`build-all.ps1`) ya está
  seteado (`Tenant__DefaultOrganizationSlug = "Depor"`, el slug real -- ver §3, se acortó
  de `comercial-depor` a `depor` el 2026-08-02) -- **falta agregar la
  misma variable al `web.config` del sitio IIS real** (`ConnectionStrings` vive ahí
  mismo, ver §7 de este doc) para que el piloto real también la tenga.
