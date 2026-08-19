# CLAUDE.md — Servicios.Common

Contexto persistente para este proyecto. Las reglas duras de la familia completa (nunca
credenciales en texto plano, TLS obligatorio, `PlatformTarget=x64`, purga de logs una vez
al día, etc.) están en el `CLAUDE.md` raíz de `Servicios SAP` — no se repiten acá, léelo
primero si vas a tocar algo de este proyecto.

## Qué es esto

Librería de contratos e implementaciones compartidas por **todos** los servicios de la
familia (`TransferenciaAutomatica`, `TipoCambioBancoCentral`, y los que vengan). No
referencia ningún driver de base de datos concreto (ni `Sap.Data.Hana` ni
`Microsoft.Data.SqlClient`) — eso vive en el proyecto de cada servicio. `PlatformTarget=x64`
está fijado acá igual (ver comentario en el `.csproj`) porque cualquier consumidor que cargue
el driver nativo de HANA lo necesita, y así la plataforma nunca queda en manos de la
resolución por defecto del SDK.

## Estructura

```
Servicios.Common/
  Contratos/
    ICompanyProvider.cs          contrato de compañías para TransferenciaAutomatica
    CompanyConnectionConfig.cs   shape de conexión HANA/SQL Server + objetos de bodega
    ISecretoCifradoService.cs    contrato de cifrado (copia de PortalSAP_v2)
    ILogSink.cs                  contrato de log local (LogEntry, NivelLog)
  Configuracion/
    SociedadSetting.cs           shape de appsettings.json de UNA compañía
    AppSettingsCompanyProvider.cs implementación de ICompanyProvider hoy (lista fija)
    WorkerOptions.cs             intervalo del ciclo principal del worker
  Logging/
    FileLogSink.cs                ILogSink real: un archivo JSON-lines por día
    LogRetentionPurger.cs         purga (invocada por un BackgroundService propio de c/servicio)
    LogSinkFactory.cs             resuelve ILogSink según LogSinkOptions.LocalSink
    LogSinkOptions.cs             bindea la sección "Logging" de appsettings.json
  Seguridad/
    SecretoCifradoService.cs      AES-256-GCM, copia deliberada del original en Proyecto Saas Portal
```

## Puntos que no son obvios leyendo el código

- **`ICompanyProvider`/`CompanyConnectionConfig` están modelados para TransferenciaAutomatica**,
  no para "cualquier servicio de la familia". Incluyen campos obligatorios de conexión HANA/
  SQL Server (`Host`/`Port`/`Schema`/`DbSecretCifrado`) y de negocio de bodegas
  (`HeaderQuerySource`/`WarehouseAssignmentProcedure`/`CompletionUdfFieldName`) que solo tienen
  sentido para ese servicio. `TipoCambioBancoCentral` **no** los reutiliza — define su propio
  `ITipoCambioCompanyProvider`/`TipoCambioCompanyConfig` local (ver
  `src/TipoCambioBancoCentral/Contratos/`) precisamente porque forzar este shape le habría
  exigido rellenar campos de DB que nunca usa. Si aparece un tercer servicio con necesidades de
  conexión distintas otra vez, ahí sí vale la pena evaluar extraer un contrato base más genérico
  — no antes (YAGNI deliberado).
- **`ILogSink` ya tiene el método `PurgeOlderThanAsync`** pensado para que la purga diaria sea
  responsabilidad del sink, no del worker de negocio — cada servicio nuevo registra su propio
  `LogRetentionPurger` como `BackgroundService` separado (ver `RetentionPurgeHostedService` en
  cada servicio), nunca dentro del loop corto del worker principal.
- **`LogSinkFactory` lanza `NotImplementedException` explícito** para `SqlServer`/`PostgreSql` —
  no implementar esas variantes hasta que un servicio real lo necesite (regla dura del
  `CLAUDE.md` raíz, repetida acá porque es el archivo donde se tocaría).
- **`SecretoCifradoService` es copia byte a byte del algoritmo** de
  `PortalSAP.Core.Seguridad.SecretoCifradoService` (PortalSAP_v2/Proyecto Saas Portal) — nunca
  actualizar acá "porque cambió allá" sin que sea una decisión explícita y documentada en el
  commit (ver `docs/00-VINCULO-CON-PORTAL-SAAS.md`).

## Tests

`tests/Servicios.Common.Tests/` cubre `AppSettingsCompanyProvider`, `FileLogSink`,
`SecretoCifradoService` y `WorkerOptions`. Cualquier tipo nuevo agregado acá que tenga lógica
no trivial (parsing, cifrado, cálculo de fechas de retención) debería sumar su propio archivo
de test ahí, siguiendo el mismo patrón de nombres.

## Comandos

```
dotnet build src/Servicios.Common/Servicios.Common.csproj
dotnet test tests/Servicios.Common.Tests/Servicios.Common.Tests.csproj
```
