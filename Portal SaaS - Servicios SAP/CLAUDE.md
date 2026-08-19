# CLAUDE.md — Servicios SAP

Contexto persistente para Claude Code en este repositorio. Reglas duras para
**TransferenciaAutomatica** y cualquier servicio nuevo de esta misma familia (el próximo
planificado: un actualizador de tipo de cambio/monedas del Banco Central de Chile).

## Qué es esto

Familia de Windows Services standalone que hablan directo con el SAP Business One de un
cliente (HANA o SQL Server), pensados para correr sin depender de un portal web. Nace del
Windows Service legado de transferencia automática de stock entre bodegas
(`C:\PROYECTOS\Proyectos HCO\repos\transferenciaAutomatica\servicio`), portado acá
**sin cambiar su lógica de negocio**, con dos objetivos nuevos: soportar más de una
compañía SAP (multi-tenant opcional, no obligatorio) y más de un motor de base de datos
(HANA/SQL Server del lado del cliente SAP).

Es un proyecto **hermano, no parte** de `Proyecto Saas Portal`
(`C:\PROYECTOS\Proyecto Portal Web-Company\Proyecto Saas Portal`) — comparte convenciones
de seguridad y el modelo de conexión SAP con él, pero no depende de su base de datos ni de
su código. Ver `docs/00-VINCULO-CON-PORTAL-SAAS.md` para el detalle completo de esa
relación y bajo qué condición se integraría formalmente.

## Estructura

```
Servicios SAP/
  src/
    Servicios.Common/            contratos + implementaciones compartidas por TODOS los servicios
    TransferenciaAutomatica/     primer servicio (Worker Service)
    TipoCambioBancoCentral/      futuro, mismo patrón (no creado todavía)
  tests/
    Servicios.Common.Tests/
  docs/
    00-VINCULO-CON-PORTAL-SAAS.md
```

Cada servicio nuevo es su propia carpeta bajo `src/`, con su propio `.csproj` tipo Worker
Service y su propio `appsettings.json` — nunca comparten proceso ni configuración entre
sí, solo el código de `Servicios.Common`.

Cada carpeta de `src/` tiene su propio `CLAUDE.md` con el detalle específico de ese
proyecto (estructura interna, estado de portado, decisiones que divergen del patrón común)
— este archivo raíz solo tiene las reglas que aplican a toda la familia. Léelo primero,
pero para trabajar dentro de un servicio puntual el `CLAUDE.md` de su propia carpeta tiene
el contexto más relevante.

## Reglas que no se negocian

- **Al portar un servicio legado, la lógica de negocio SAP-específica (stored procedures,
  UDFs, el algoritmo que decide qué hacer con los datos) se copia tal cual, nunca se
  reescribe.** Para `TransferenciaAutomatica`: mismas 4 queries HANA del servicio legado
  (`Queries.cs`), mismo stored procedure `SP_DEP_ORDER_ABS` para resolver bodegas por
  prioridad, mismo mecanismo de marcar el UDF `U_NX_Auto_ABS = 'N'` al terminar de
  recorrer las bodegas configuradas. Lo único que se corrige al portar son bugs de
  plomería (SQL injection por concatenación, catch vacíos que tapan errores, TLS
- **Los nombres de los objetos SAP que la lógica de negocio consulta (vista/tabla de
  cabecera, stored procedure de asignación de bodegas, UDF de finalización) son dato de
  configuración por compañía, no literales en código** — `CompanyConnectionConfig.
  HeaderQuerySource`/`WarehouseAssignmentProcedure`/`CompletionUdfFieldName`
  (`Servicios.Common/Contratos/CompanyConnectionConfig.cs`), poblados desde
  `SociedadSetting` en `appsettings.json`. Esto es distinto de "no reescribir el
  algoritmo" (regla de arriba): el algoritmo del SP no cambia, pero cada cliente puede
  tener su propio nombre de vista/SP/UDF en su propia instancia SAP, así que el código no
  puede asumirlos fijos. El SQL en sí, cuando se porte, sigue las reglas de siempre
  (parametrizado con `HanaParameter`/`SqlParameter`, nunca concatenado).
- **El intervalo del ciclo principal de cada worker es configuración, no una constante en
  código** — `WorkerOptions.CicloIntervaloSegundos` (`Servicios.Common/Configuracion/
  WorkerOptions.cs`), sección `Worker` de `appsettings.json`, default 300 (5 minutos,
  igual que el servicio legado). Cualquier worker nuevo de esta familia registra y usa
  este mismo `WorkerOptions` en vez de hardcodear su propio `TimeSpan`.
  deshabilitado, credenciales en texto plano) — nunca el algoritmo de negocio en sí. Si
  hace falta cambiar el algoritmo, es una tarea separada y explícita, no un efecto
  colateral de portar.
- **`ICompanyProvider` (`Servicios.Common/Contratos/ICompanyProvider.cs`) es el único
  punto de entrada de configuración de compañías.** Ningún servicio nuevo lee compañías
  directo de `appsettings.json` a mano — siempre a través de esta interfaz, hoy resuelta
  por `AppSettingsCompanyProvider` (lista fija en configuración). Esto es lo que permite
  que el mismo código sirva para 1 compañía (modo standalone) o N (modo multi-empresa) sin
  bifurcación, y es el punto que se reemplaza el día que este proyecto se integre a
  Proyecto Saas Portal (ver `docs/00-VINCULO-CON-PORTAL-SAAS.md`).
- **`ISecretoCifradoService`/AES-256-GCM es copia deliberada** del contrato e
  implementación de PortalSAP_v2/Proyecto Saas Portal — no una referencia de proyecto
  cruzada entre soluciones. Si el original cambia allá, acá no se actualiza solo: portar
  el cambio es una decisión explícita, documentada en el commit que lo haga.
- **Log local en archivo plano, con retención configurable por servicio**
  (`Logging:RetentionDays` en `appsettings.json`, resuelto vía `LogSinkOptions`). La
  purga corre **una vez al día**, nunca en cada ciclo corto del worker de negocio (que en
  `TransferenciaAutomatica` corre cada 5 minutos) — son dos `BackgroundService` separados
  a propósito, ver `RetentionPurgeHostedService`. El contrato `ILogSink` ya deja lugar
  para un sink de base de datos (`Logging:LocalSink = SqlServer/PostgreSql`) sin tocar
  consumidores, pero **no implementar esas variantes hasta que un servicio real lo
  necesite** — hoy lanzan `NotImplementedException` explícito en `LogSinkFactory`.
- **Nunca credenciales en texto plano** en `appsettings.json` — `dotnet user-secrets` en
  desarrollo, vault en producción. Mismo estándar que PortalSAP_v2/Proyecto Saas Portal,
  sin excepciones.
- **TLS obligatorio siempre**, sin excepción temporal — el servicio legado deshabilitaba
  la validación de certificado de forma global y permanente; no se repite acá bajo ningún
  concepto, ni siquiera "por ahora".
- **Cada servicio nuevo de esta carpeta declara explícitamente si algún día será núcleo de
  Proyecto Saas Portal (vendible a cualquier organización) o extensión puntual de un
  cliente** — mismo criterio que `PlatformModule.IsCore`/`ExclusiveOrganizationId` en ese
  proyecto, aunque acá no existe esa tabla todavía (la declaración es documental, en el
  `CLAUDE.md`/README de cada servicio, hasta que exista la integración real).
- **`Servicios.Common` no referencia ningún driver de base de datos concreto** (ni
  `Sap.Data.Hana` ni `Microsoft.Data.SqlClient`) — esas dependencias van en el proyecto de
  cada servicio (`TransferenciaAutomatica`, etc.), `Servicios.Common` solo define
  contratos y lo que es genuinamente compartible sin acoplarse a un motor.
- **`PlatformTarget=x64` en todo proyecto que pueda cargar el driver nativo de HANA** —
  mismo motivo que PortalSAP_v2/Proyecto Saas Portal, no cambiar a Any CPU.
- **Nombre del Windows Service: siempre `NX_DEP_<NombreDelServicio>`** (ej.
  `NX_DEP_TransferenciaAutomatica`), fijado en `AddWindowsService(options =>
  options.ServiceName = "...")` de cada `Program.cs`. Los scripts de alta/baja del
  servicio (`NX_DEP_<NombreDelServicio>-Install.ps1`/`NX_DEP_<NombreDelServicio>-Uninstall.ps1`)
  viven en la carpeta `public/` de cada servicio (ej.
  `src/TransferenciaAutomatica/public/`) — es la carpeta que se copia junto al publish al
  servidor del cliente, separada del código fuente. El nombre pasado a `sc.exe`/
  `New-Service` en el script debe coincidir EXACTO con el `ServiceName` del `Program.cs`
  correspondiente.

## Comandos

```
dotnet build ServiciosSAP.sln
dotnet run --project src/TransferenciaAutomatica/Servicios.TransferenciaAutomatica.csproj
dotnet test tests/Servicios.Common.Tests/Servicios.Common.Tests.csproj
```

## Estado actual

`TransferenciaAutomatica` tiene el chasis (loop multi-compañía con aislamiento de errores
por compañía, log local con retención, resolución de secretos cifrados) y la lógica de
negocio real portada del legado (`Worker.EjecutarTransferenciasDeCompaniaAsync`, solo
`EngineType = Hana` por ahora) — ver el `CLAUDE.md` de `TransferenciaAutomatica` para el
detalle de qué se portó igual y qué bugs de plomería se corrigieron.

`TipoCambioBancoCentral` tiene la lógica de negocio completa portada desde el servicio
legado (`G:\Unidades compartidas\Proyectos_Depor\SAP Desarrollos\SAP Services\Servicio
NX_DEP_TipoCambio - Actualizado`, Windows Service .NET Framework 4.7.2 con B1SLayer): las
tres reglas del algoritmo (salvavidas e-commerce si SAP queda en 0, pre-carga nocturna
después de las 22:00, regla retroactiva de feriados/fines de semana contra el histórico del
Banco Central) están intactas en `Worker.EjecutarProcesoDeCompaniaAsync`. Adaptado a
multi-compañía con aislamiento de errores (mismo criterio que `TransferenciaAutomatica`),
credenciales descifradas vía `ISecretoCifradoService` (el legado las tenía en texto plano
en `App.config`) y `WorkerOptions.CicloIntervaloSegundos` en vez del `Timer.FromHours`
hardcodeado.

**Diverge deliberadamente de `ICompanyProvider`/`CompanyConnectionConfig`**: este servicio
define su propio `ITipoCambioCompanyProvider`/`TipoCambioCompanyConfig`
(`src/TipoCambioBancoCentral/Contratos/`) en vez de reutilizar los de `Servicios.Common`,
porque `CompanyConnectionConfig` exige campos de conexión HANA/SQL Server y de negocio de
bodegas (`HeaderQuerySource`/`WarehouseAssignmentProcedure`/`CompletionUdfFieldName`)
específicos de `TransferenciaAutomatica` que este servicio no usa — el legado nunca conectó
a la base, solo a Service Layer. Si en el futuro aparece un tercer servicio con necesidades
de conexión igual de heterogéneas, vale la pena evaluar extraer un contrato común más
genérico recién ahí, no antes.

Falta explícitamente: cifrar y cargar los secretos reales (`dotnet user-secrets`) para
`BancoCentral:SecretoCifrado` y `Sociedades[].ServiceLayerSecreto`, y pruebas — hoy no hay
`tests/` para este servicio.
