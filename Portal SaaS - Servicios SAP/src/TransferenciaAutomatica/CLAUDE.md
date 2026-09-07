# CLAUDE.md — TransferenciaAutomatica

Contexto persistente para este proyecto. Las reglas duras de la familia completa (portar sin
reescribir el algoritmo, nombres SAP como configuración, `WorkerOptions` para el intervalo,
`ICompanyProvider` como único punto de entrada de compañías, credenciales cifradas, TLS
obligatorio, `PlatformTarget=x64`) están en el `CLAUDE.md` raíz de `Servicios SAP` — léelo
primero.

## Qué es esto

Primer servicio de la familia. Windows Service (`NX_DEP_TransferenciaAutomatica`) que
transfiere stock automáticamente entre bodegas en SAP Business One, resolviendo por cada
documento pendiente qué bodega origen usar según un orden de prioridad. Portado del servicio
legado `C:\PROYECTOS\Proyectos HCO\repos\transferenciaAutomatica\servicio` (.NET Core 3.1,
hardcodeado a una sola compañía HANA) — ver `docs/00-VINCULO-CON-PORTAL-SAAS.md` en la raíz
para el detalle completo del origen y los bugs de plomería que se corrigieron al portar
(SQL injection en el log, catch vacíos, riesgo de duplicación en reinicios, TLS deshabilitado,
credenciales en texto plano).

## Estructura

```
TransferenciaAutomatica/
  Program.cs                    DI + AddWindowsService("NX_DEP_TransferenciaAutomatica")
  Worker.cs                     loop multi-compañía con aislamiento de errores por compañía
  RetentionPurgeHostedService.cs purga diaria de logs, BackgroundService separado del Worker
  appsettings.json / appsettings.Development.json
  public/
    NX_DEP_TransferenciaAutomatica-Install.ps1
    NX_DEP_TransferenciaAutomatica-Uninstall.ps1
    NX_DEP_TransferenciaAutomatica-SetClaveMaestra.ps1
```

## Estado actual

`Worker.EjecutarTransferenciasDeCompaniaAsync` tiene la lógica de negocio real portada del
legado (`Sap/HanaQueries.cs`, `Sap/HanaRepository.cs`, `Sap/DocumentTypeMapping.cs`,
`ServiceLayer/ServiceLayerClient.cs`), mismo algoritmo que `StrockTrans.cs`:

1. Query de cabecera contra `compania.HeaderQuerySource` (legado: vista calculada
   `_SYS_BIC."sap.clprddepor/NX_AUTO_ABS"`, hoy dato de configuración por compañía).
2. Por cada documento, `for i = 1..compania.WarehousePriorityCount`: llamar
   `{compania.WarehouseAssignmentProcedure}` (legado: `SP_DEP_ORDER_ABS`) — mismo algoritmo,
   solo el nombre es configurable.
3. Postear `StockTransfers` a Service Layer por cada bodega que aporte cantidad.
4. `UPDATE` de `compania.CompletionUdfFieldName = 'N'` (legado: `U_NX_Auto_ABS`) al terminar.

Bugs de plomería corregidos al portar (además de los ya documentados en
`docs/00-VINCULO-CON-PORTAL-SAAS.md`): las llamadas a `SP_DEP_ORDER_ABS` y el `UPDATE` de
cierre van con `HanaParameter` (nunca concatenadas), el login a Service Layer usa la
validación de certificado default de .NET en vez del `ServicePointManager.
ServerCertificateValidationCallback = true` global del legado, y los `catch` vacíos que
tragaban errores de red/credenciales en `SBOClases` se eliminaron (las excepciones suben al
try/catch por compañía de `Worker.cs`).

Solo `EngineType = Hana` está portado — una compañía `SqlServer` tira `NotSupportedException`
explícito en vez de fingir soporte.

**Driver HANA**: se vendorizó `lib/Sap.Data.Hana.Net.v8.0.dll` (driver .NET 8 actual del
HANA Client, no existe como paquete NuGet público) porque el `Sap.Data.Hana.Core.v2.1.dll`
que usaba el legado quedó incompatible tras la actualización del HANA Client del cliente —
esa incompatibilidad fue el motivo original de este reemplazo de servicio. El servidor
donde corra el `.exe` publicado necesita igual el HANA Client instalado (el `.dll`
vendorizado es el binario administrado, no trae las librerías nativas).

## Diferencia clave respecto al legado

El servicio original (`StrockTrans.cs`) envolvía **todo** el `foreach` de compañías en un
único try/catch — una excepción en una compañía abortaba el resto del batch silenciosamente.
Acá cada compañía tiene su propio try/catch (ver comentario en `Worker.cs`): una falla nunca
afecta a las demás.

**Rechazo de negocio de SAP en el POST a Service Layer**: el legado (`StrockTrans.cs`)
recibía el error por `ref er` sin lanzar, lo escribía en el log y **siempre** marcaba
`U_NX_Auto_ABS = 'N'` — un documento que SAP rechazaba (ej. `-10 Quantity falls into
negative inventory`) quedaba cerrado sin transferirse. Acá `ServiceLayerClient` lanza
`ServiceLayerPostException` (tipo propio, distinto de los errores de plomería/login), que
`Worker.cs` captura por prioridad: registra el error, sigue con las demás prioridades y
documentos, y al cierre decide:
  - hubo al menos un POST exitoso, o SAP no rechazó nada → marca `'N'` (además evita
    re-postear en el próximo ciclo las prioridades que sí entraron);
  - SAP rechazó y nada entró → marca `'N'` **solo si el documento ya está en un picking**
    (`IWarehouseTransferRepository.DocumentoEstaEnPicking`, consulta `PKL1`/`OPKL` con
    `PickStatus <> 'C'`); si no, lo deja pendiente para reintentar el próximo ciclo.

Además `ServiceLayerError.Code` pasó de `int` a `string` con converter flexible (Service
Layer manda `error.code` como número desde la DI API y como string `"-10"` desde HANA/SQL) y
`error.message` acepta tanto `{lang,value}` como string plano — antes cualquiera de las dos
formas string reventaba la deserialización con `JsonException` y tapaba el error real.

*A confirmar contra el SAP del cliente*: el filtro `PKL1."PickStatus" <> 'C'` asume el
picking nativo de B1; si Comercial Depor usa otro estado/campo para "en picking", ajustar
`HanaQueries.DocumentoEnPicking` / `SqlServerQueries.DocumentoEnPicking`.

## Comandos

```
dotnet run --project src/TransferenciaAutomatica/Servicios.TransferenciaAutomatica.csproj
dotnet publish src/TransferenciaAutomatica/Servicios.TransferenciaAutomatica.csproj -c Release -r win-x64
```
