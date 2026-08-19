# CLAUDE.md — TipoCambioBancoCentral

Contexto persistente para este proyecto. Las reglas duras de la familia completa (portar sin
reescribir el algoritmo, `WorkerOptions` para el intervalo, credenciales cifradas, TLS
obligatorio, `PlatformTarget=x64`) están en el `CLAUDE.md` raíz de `Servicios SAP` — léelo
primero.

## Qué es esto

Segundo servicio de la familia. Windows Service (`NX_DEP_TipoCambioBancoCentral`) que
sincroniza el Tipo de Cambio USD (tabla `ORTT` de SAP) contra el histórico oficial de la API
del Banco Central de Chile. Portado del servicio legado `G:\Unidades compartidas\
Proyectos_Depor\SAP Desarrollos\SAP Services\Servicio NX_DEP_TipoCambio - Actualizado`
(Windows Service .NET Framework 4.7.2, `System.ServiceProcess.ServiceBase` + `B1SLayer`,
hardcodeado a una sola compañía, con `Timer.FromHours` y credenciales en texto plano en
`App.config`).

## Estructura

```
TipoCambioBancoCentral/
  Program.cs                    DI + AddWindowsService("NX_DEP_TipoCambioBancoCentral")
  Worker.cs                     loop multi-compañía + orquestador de las 3 reglas de negocio
  RetentionPurgeHostedService.cs purga diaria de logs (mismo patrón que TransferenciaAutomatica)
  BancoCentral/
    BancoCentralClient.cs        consulta HTTP a la API del BC (serie F073.TCO.PRE.Z.D)
    BancoCentralResponse.cs      shape JSON de la respuesta (copia tal cual del legado)
  Sap/
    SapCurrencyRateClient.cs     consulta/upsert de ORTT vía B1SLayer (SBOBobService_Get/SetCurrencyRate)
  Configuracion/
    TipoCambioCompanySetting.cs  shape de appsettings.json de UNA compañía (solo Service Layer)
    AppSettingsTipoCambioCompanyProvider.cs implementación de hoy (lista fija)
    BancoCentralOptions.cs       credenciales GLOBALES del servicio (no por compañía)
  Contratos/
    ITipoCambioCompanyProvider.cs / TipoCambioCompanyConfig.cs  ver sección de abajo
  appsettings.json / appsettings.Development.json
  public/
    Install-NX_DEP_TipoCambioBancoCentral.ps1
    Uninstall-NX_DEP_TipoCambioBancoCentral.ps1
```

## El algoritmo de negocio (portado tal cual, no reescribir)

`Worker.EjecutarProcesoDeCompaniaAsync` corre, por cada compañía activa, en este orden:

1. **Salvavidas e-commerce** (cada ciclo): si SAP tiene el TC de hoy en 0 o null, carga
   temporalmente el TC del día hábil anterior (viernes si hoy es lunes).
2. **Pre-carga nocturna** (solo después de las 22:00): si el TC de mañana en SAP es distinto
   al de hoy, lo pre-carga con el valor de hoy — asegura la transición de medianoche antes de
   que exista el oficial del día siguiente.
3. **Regla retroactiva**: pide un rango de 10 días al Banco Central, y por cada día sin TC
   oficial (fin de semana/feriado) que haya quedado con un valor "placeholder", lo corrige
   retroactivamente con el TC oficial posterior en cuanto este aparece.

Ninguna de las tres reglas cambia al portar — si hace falta modificar el algoritmo, es una
tarea separada y explícita, no un efecto colateral de tocar otra cosa acá.

## Diferencias de plomería respecto al legado (esto sí cambió, a propósito)

- **Multi-compañía con aislamiento de errores**: el legado era single-tenant (un `SLConnection`
  fijo por instancia del servicio). Acá cada compañía activa corre en su propio try/catch —
  mismo criterio que `TransferenciaAutomatica.Worker`, una falla no aborta el resto del batch.
- **Credenciales cifradas**: el legado tenía `sap_pass`/`bco_pass` en texto plano en
  `App.config`. Acá se descifran recién al momento de usarlas vía `ISecretoCifradoService` —
  nunca en `appsettings.json`, siempre `dotnet user-secrets`/vault.
- **Intervalo configurable**: `Timer.FromHours(int.Parse(...))` hardcodeado → `WorkerOptions.
  CicloIntervaloSegundos` (default 3600 = 1 hora, igual que el legado).
- **Logging**: `Logger.Log` a un archivo propio (`NX_DEP_TipoCambio_LOG.txt`, catch vacío si
  fallaba) → `ILogSink`/`ILogger` compartidos de `Servicios.Common`, con retención configurable.

## Por qué NO reutiliza `ICompanyProvider`/`CompanyConnectionConfig` de Servicios.Common

Ese contrato (usado por `TransferenciaAutomatica`) exige campos obligatorios de conexión
HANA/SQL Server (`Host`/`Port`/`Schema`/`DbSecretCifrado`) y de negocio de bodegas
(`HeaderQuerySource`/`WarehouseAssignmentProcedure`/`CompletionUdfFieldName`) que este
servicio no usa — el legado (y este puerto) nunca conectan a la base, solo a Service Layer.
Forzar ese shape habría significado rellenar campos de DB con datos falsos solo para
satisfacerlo. Por eso `Contratos/ITipoCambioCompanyProvider.cs` y `TipoCambioCompanyConfig.cs`
son un contrato propio de este proyecto, con el mismo espíritu ("nunca leer compañías directo
de appsettings.json a mano") pero un shape que sí encaja. Si en el futuro un tercer servicio
tiene necesidades de conexión igual de heterogéneas, ahí vale la pena evaluar un contrato base
más genérico en `Servicios.Common` — no antes.

Las credenciales del Banco Central (`BancoCentralOptions`) son **globales al servicio, no por
compañía** — es una única fuente externa compartida por todas las compañías que se sincronizan,
a diferencia de Service Layer que sí es por compañía.

## Estado actual — pendiente

- **Secretos reales sin cargar**: `appsettings.json`/`appsettings.Development.json` tienen
  placeholders `REEMPLAZAR_*` para `BancoCentral:SecretoCifrado` y
  `Sociedades[].ServiceLayerSecreto`. Cifrarlos con `ISecretoCifradoService.Cifrar` y cargarlos
  vía `dotnet user-secrets` (dev) o vault (producción) antes de correr contra un SAP real.
- **Sin tests**: no hay `tests/Servicios.TipoCambioBancoCentral.Tests/` todavía. Candidatos
  obvios: `BancoCentralClient.TryParseValorOficial` (parsing de "NAN"/decimal con distintas
  culturas) y la regla retroactiva de `Worker` (acumulación/limpieza de `diasSinTipoCambio`).

## Comandos

```
dotnet run --project src/TipoCambioBancoCentral/Servicios.TipoCambioBancoCentral.csproj
dotnet publish src/TipoCambioBancoCentral/Servicios.TipoCambioBancoCentral.csproj -c Release -r win-x64
```
