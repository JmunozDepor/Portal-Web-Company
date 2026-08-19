# Vínculo entre Servicios SAP y Proyecto Saas Portal

Registro de decisión — 2026-07-29.

## Origen

`Servicios SAP` nace del análisis del Windows Service legado de transferencia automática
de stock entre bodegas (`C:\PROYECTOS\Proyectos HCO\repos\transferenciaAutomatica\servicio`),
un servicio .NET Core 3.1 standalone, hardcodeado a una sola compañía SAP (Comercial
Depor, sobre HANA), con varios bugs reales de plomería (SQL injection en el log por
concatenación sin escapar, catch vacíos que tapaban el procesamiento completo de un
batch, riesgo de duplicación de transferencias en reinicios por falta de atomicidad entre
la transferencia y el marcado del UDF, TLS deshabilitado globalmente, credenciales en
texto plano en `appsettings.json`).

Se decidió portarlo **sin cambiar su lógica de negocio** (mismas queries, mismo stored
procedure `SP_DEP_ORDER_ABS`, mismo mecanismo de UDF), pero como el primer miembro de una
familia de servicios SAP standalone — el segundo planificado es un actualizador de tipo de
cambio/monedas del Banco Central de Chile, que comparte con este los mismos problemas de
fondo (conexión a un SAP concreto, credenciales que cifrar, log local con retención) sin
compartir nada de su lógica de negocio específica.

## Por qué es un proyecto separado y no un módulo de Proyecto Saas Portal

`Proyecto Saas Portal` es la vía de evolución de `PortalSAP_v2` hacia un producto SaaS/
on-premise vendible — un portal web con plugins en `AssemblyLoadContext` aislado,
organizado por `organizations`/`companies`/`instances`. `Servicios SAP` no es un portal
web: son procesos Windows Service standalone que corren solos, sin UI, sin sesión HTTP, y
que hoy no necesitan (ni deben esperar) que exista una organización dada de alta en la
plataforma para poder correr — de hecho, el caso de uso real inmediato es correr contra
UNA sola compañía, exactamente como corría el servicio legado.

Meterlo dentro de `Proyecto Saas Portal` hubiera significado acoplar su arranque a la
disponibilidad de esa base de datos (`ICurrentCompanyAccessor`, `HanaService`, todo el
stack de Core de ese proyecto asume una compañía fijada en una sesión HTTP — ver el
hallazgo real: no existe ningún `BackgroundService` multi-organización en ese repo hoy,
porque nunca lo necesitó). Separarlo evita ese acoplamiento prematuro y dos consecuencias
concretas: (a) un cliente que solo necesita el servicio de transferencias no tiene que
desplegar ni operar el portal web completo, y (b) un cambio en el modelo comercial de
Proyecto Saas Portal (planes, licencias, medición de uso) no puede romper, por accidente,
un proceso que corre desatendido en el servidor de un cliente.

## Qué se comparte, y cómo

No hay dependencia de proyecto ni de solución entre los dos repos. Lo que se comparte es
**patrón**, copiado deliberadamente:

- **Cifrado de secretos** (`ISecretoCifradoService`, AES-256-GCM, clave maestra de 32
  bytes fuera de la base) — copia literal del contrato e implementación de
  `PortalSAP.Core.Seguridad.SecretoCifradoService`. Si ese servicio cambia en
  PortalSAP_v2/Proyecto Saas Portal, este repo no se entera solo; portar el cambio es una
  decisión explícita y documentada.
- **Shape del connection string hacia el SAP del cliente** (HANA/SQL Server) — mismos
  nombres de parámetro y mismo criterio de motor dual que `SapConnectionStringFactory` de
  Proyecto Saas Portal, para que una compañía configurada hoy a mano en el
  `appsettings.json` de `TransferenciaAutomatica` sea estructuralmente idéntica a una fila
  futura de `companies`/`instances` en la base de esa plataforma.
- **Convención de nombres y reglas de seguridad** (nunca credenciales en texto plano, TLS
  siempre, `PlatformTarget=x64` donde haga falta el driver nativo de HANA).

## Condición de integración futura

`Servicios SAP` se integraría formalmente a Proyecto Saas Portal el día que
`ICompanyProvider` (`Servicios.Common/Contratos/ICompanyProvider.cs`) gane una segunda
implementación (`PlatformOrganizationCompanyProvider`, todavía no escrita) que lea
`organizations`/`companies`/`instances` de la base de esa plataforma en vez de
`appsettings.json`. Ese cambio no debería tocar nada del resto del código de cada
servicio — es exactamente el motivo por el que `ICompanyProvider` existe como interfaz
desde el día uno. Hasta que eso ocurra, cada servicio de esta carpeta corre de forma
completamente independiente, contra la configuración que tenga en su propio
`appsettings.json`.

Ver la entrada correspondiente en `Proyecto Saas Portal/CLAUDE.md` (sección "Decisiones
ya tomadas") para el registro simétrico desde el otro lado.
