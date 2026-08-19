# CLAUDE.md — TransferenciaAutomatica_v2

Contexto persistente para este proyecto. Las reglas duras de la familia completa (`CLAUDE.md`
raíz de `Servicios SAP`) siguen aplicando: credenciales cifradas, TLS obligatorio,
`PlatformTarget=x64`, `NX_DEP_<NombreDelServicio>` como nombre de servicio Windows.

## Qué es esto

Rediseño de `TransferenciaAutomatica` (v1), como proyecto **nuevo y separado** — no
reemplaza a v1, corren en paralelo. v1 sigue en producción para `comercialdepor` (HANA) sin
tocarse; v2 es donde se prueba el algoritmo nuevo, pensado primero para un cliente SQL
Server.

## Por qué existe un v2 en vez de modificar v1

v1 porta tal cual el algoritmo del legado: un stored procedure (`SP_DEP_ORDER_ABS`) con SQL
dinámico que resuelve la cascada de bodegas en la base, atado a una tabla de prioridad con
columnas fijas (`U_WhsCode1/2/3`, máximo 3 niveles), sin chequeo de picking y sin orden de
prioridad de proceso. Se definieron 4 requisitos nuevos que ese diseño no soporta bien:

1. Completar con stock solo si la bodega destino no cubre la necesidad y hay de dónde sacar.
2. Si el documento ya fue enviado a picking, no se le debe seguir asignando stock.
3. Los pedidos más nuevos se procesan primero.
4. Debe poder sacar de N bodegas en cascada (configurable, máximo 5).

Para cubrir esto de forma dinámica (no hardcodeada) y portable entre HANA y SQL Server sin
reescribir un SP por motor, el algoritmo de asignación se movió de SQL a C#
(`Domain/AllocationEngine.cs`) — la base solo hace lecturas simples.

## Diseño

- **`Domain/AllocationEngine.cs`**: la cascada de asignación (tomar de la bodega de mayor
  prioridad primero, cortar apenas se cubre la necesidad), función pura sin I/O — testeada
  en `tests/Servicios.TransferenciaAutomatica_v2.Tests/AllocationEngineTests.cs` sin
  necesitar HANA ni SQL Server.
- **`Db/IStockRepository.cs`** + `HanaStockRepository`/`SqlServerStockRepository` +
  `StockRepositoryFactory`: lecturas por motor (documentos pendientes, líneas del
  documento, prioridad de bodegas, disponible por bodega, picking pendiente, marcar
  completado). Ninguna tiene lógica de negocio — eso vive en `Worker.cs` +
  `AllocationEngine`.
- **`Worker.cs`**: por cada compañía, por ciclo, mantiene un "ledger" en memoria
  `(WhsCode, ItemCode) -> cantidad ya comprometida en ESTE ciclo` — evita que dos
  documentos distintos procesados en el mismo ciclo se lleven el mismo stock de una bodega
  origen escasa (algo que el SP original de v1, al llamarse independiente por documento, no
  podía prevenir).
- **`ServiceLayer/`**: copia literal de v1 (`ServiceLayerClient.cs`/`ServiceLayerModels.cs`)
  — probado en producción (TLS correcto, `Content-Type` sin charset, tolerancia de nombre
  de certificado configurable). No se promovió a `Servicios.Common` para no tocar nada de
  v1 mientras v2 está en prueba; si v2 se valida, consolidar ahí es la próxima limpieza
  natural.

## Convención de configuración nueva (`CompanyConnectionConfig`)

Dos campos que v1 no usa (opcionales en el modelo compartido, no rompen v1):

- **`HeaderQuerySource`**: a diferencia de v1, acá la compañía debe escribir en su propio
  texto SQL el filtro de "sin picking pendiente" **y** el `ORDER BY` de prioridad de
  proceso (nuevos primero) — no es código nuevo, es una convención de cómo se arma esa
  query. El código solo envuelve esto con `SELECT DocEntry, DocNum, ObjType, CardCode FROM
  {headerQuerySource}` (ver `SqlServerQueries.Cabecera`/`HanaQueries.Cabecera`), así que
  `HeaderQuerySource` debe ser una derived table completa con alias. En SQL Server, un
  `ORDER BY` dentro de una derived table exige `TOP`/`OFFSET` (si no, tira el error 1033
  "The ORDER BY clause is invalid in views...") — por eso el `TOP 100 PERCENT`. Ejemplo (ver
  `appsettings.Development.json` para el JSON completo):

  ```sql
  (SELECT TOP 100 PERCENT h.DocEntry, h.DocNum, h.ObjType, h.CardCode FROM (
    SELECT DocEntry, DocNum, '17' AS ObjType, CardCode, DocDate FROM ORDR WHERE U_DEP_Auto_ABS = 'Y'
    UNION ALL
    SELECT DocEntry, DocNum, '13', CardCode, DocDate FROM OINV WHERE U_DEP_Auto_ABS = 'Y' AND isIns = 'Y'
    UNION ALL
    SELECT DocEntry, DocNum, '1250000001', CardCode, DocDate FROM OWTQ WHERE U_DEP_Auto_ABS = 'Y'
  ) h ORDER BY h.DocDate DESC, h.DocEntry DESC) fuente
  ```

- **`WarehousePriorityTable`**: nombre de la tabla normalizada que reemplaza a las columnas
  fijas `U_WhsCode1/2/3`. Se implementa como tabla de usuario (UDT) estándar de SAP B1 —
  nombre con `@` y columnas con prefijo `U_` (igual convención que cualquier UDF), creada
  por el Basis/funcional del cliente con las herramientas normales de B1 (Tools > Customization
  Tools > User-Defined Tables), no por DDL a mano. Columnas esperadas (`HanaQueries.
  PrioridadBodegas`/`SqlServerQueries.PrioridadBodegas` las leen con estos nombres exactos,
  literales en código porque son la convención UDF de B1, no config por compañía):

  | Columna UDT          | Significado                          |
  |-----------------------|---------------------------------------|
  | `U_WhsCodeDestino`    | Bodega que necesita cubrir stock      |
  | `U_Prioridad`         | Orden de la cascada (1 = primera)     |
  | `U_WhsCodeOrigen`     | Bodega candidata para esa prioridad   |

  En `appsettings.json`, `WarehousePriorityTable` va con el nombre completo entre corchetes
  (T-SQL) o comillas (HANA), ej. SQL Server: `"[@DEP_ORDEN_ASIG_STK]"`.

  `HanaStockRepository`/`SqlServerStockRepository` tiran `InvalidOperationException` si una
  bodega destino tiene más de 5 prioridades configuradas — tope de negocio, no técnico.

- **`PickingPendingQuery`**: template de la reconsulta puntual de picking (antes de postear
  cada transferencia), con el placeholder `{TablaDetalle}` (reemplazado en código por
  `RDR1`/`INV1`/`WTQ1` según `ObjType`) y el parámetro `docEntry` en la sintaxis del motor
  de esa compañía. Asume por convención el campo estándar de B1 `PickIdNo`:

  ```
  SELECT COUNT(*) FROM {TablaDetalle} WHERE DocEntry = @docEntry AND PickIdNo IS NOT NULL
  ```

  Si un cliente marca "enviado a picking" de otra forma (otro campo, otra tabla), esto
  cambia en su `appsettings.json`, no en código.

- **`WarehouseAssignmentProcedure`** (heredado del modelo compartido con v1) no se usa acá
  — el algoritmo de cascada está en `AllocationEngine`, no en un SP.

- **`ConfiaCertificadoBaseDatos`** (solo SQL Server): pone `TrustServerCertificate=true` en
  la cadena de conexión para compañías cuyo SQL Server tiene un certificado no confiable
  (típico en ambientes de test con certificado autofirmado) — el tráfico sigue yendo
  cifrado si `DatabaseEncryptada=true`, solo se salta la validación de la cadena. Default
  `false`, excepción acotada y documentada por compañía, mismo criterio que
  `ToleraNombreCertificadoServiceLayer`.

## Estado actual

Chasis completo y algoritmo nuevo implementado, soporta HANA y SQL Server. **No hay
ninguna compañía real dada de alta** (`Sociedades: []` en `appsettings.json`) — falta la
tabla `WarehousePriorityTable` creada en la base del cliente de prueba y sus datos de
conexión reales para la primera prueba de punta a punta contra SQL Server.

## Comandos

```
dotnet build ServiciosSAP.slnx
dotnet run --project src/TransferenciaAutomatica_v2/Servicios.TransferenciaAutomatica_v2.csproj
dotnet test tests/Servicios.TransferenciaAutomatica_v2.Tests/Servicios.TransferenciaAutomatica_v2.Tests.csproj
dotnet publish src/TransferenciaAutomatica_v2/Servicios.TransferenciaAutomatica_v2.csproj -c Release
```
