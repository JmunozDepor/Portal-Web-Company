# Reporte Excel de resultado de la importación genérica

Fecha: 2026-09-06
Estado: aprobado, pendiente de implementación
Módulo: `plugins/Modulo.ImportacionGenerica` + `PortalSaas.Core.ImportacionGenerica`

## Problema

El wizard de importación masiva (`/importacion-generica/importar`) permite procesar un
archivo Excel (vista previa) y luego "Confirmar creación en SAP". Al terminar, el
resultado solo se ve en pantalla como una tabla HTML por grupo (Grupo / Resultado /
N° documento / Mensaje), sin forma de exportarlo. El usuario necesita un **reporte
descargable en Excel** que cruce el detalle de cada línea del archivo con el **DocNum
SAP generado** por cada documento, para archivarlo como constancia de la corrida.

Ya existe un botón "Descargar archivo con errores" (`OnPostDownloadWithErrorsAsync` +
`IGenericImportService.GenerateFileWithErrorsAsync`) que regenera el archivo original
más una columna "Errores" — pensado para corregir y re-subir, NO como reporte de
resultado. El reporte nuevo es una salida distinta: post-creación, con DocNum y estado.

## Alcance

- **Filas incluidas**: todos los grupos de la corrida — documentos creados OK (con
  DocNum), grupos con errores de validación (nunca se intentaron crear) y grupos
  rechazados por SAP al crear.
- **Una fila por línea del archivo** (`GenericImportRowDto`), ordenadas por
  `RowNumber`. `DocNum` y `Estado` se repiten en cada línea del mismo documento.
- **Columnas de línea adaptativas por módulo**, mismo criterio que la plantilla y el
  archivo con errores:
  - Inventario: `Almacén Origen`, `Almacén Destino`.
  - Venta / Compra: `Bodega`, `Cuenta Mayor`, `Centro de Costos`.
- **Columnas comunes**: `Fila`, `Artículo`, `Descripción`, `Cantidad`, `Precio`,
  `DocNum`, `Estado`, `Errores / Mensaje`.
- Hoja única plana llamada `Reporte`.
- Filas de grupos NO creados resaltadas en rojo claro
  (`XLColor.FromArgb(255, 214, 214)`, mismo valor que `GenerateFileWithErrorsAsync`).

Fuera de alcance: envío por correo, historial persistente de corridas, reporte en PDF,
resumen por documento en hoja aparte.

## Estado por grupo

Se resuelve cruzando `GenericImportRowDto.GroupingKey` contra la lista
`GenericImportProgressDto.Results` (`GenericImportDocumentResultDto` con
`GroupingKey` / `Success` / `DocNum` / `Message`):

| Situación | `Estado` | `DocNum` | `Errores / Mensaje` |
|---|---|---|---|
| GroupingKey en Results, `Success = true` | `Creado` | `result.DocNum` | vacío |
| GroupingKey en Results, `Success = false` | `Rechazado por SAP` | vacío | `result.Message` (a nivel documento, repetido en cada fila del grupo) |
| GroupingKey NO está en Results | `No creado (errores)` | vacío | `string.Join("; ", row.Errors)` (por fila) |

Si `Results` viene null/vacío (no se pasó `jobId`, o el store no tiene el trabajo),
todos los grupos caen en `No creado (errores)` — el reporte igual se genera.

## Flujo de datos

"Confirmar" es un `fetch` que devuelve JSON y el archivo no queda en sesión de
servidor. El reporte reusa el patrón ya establecido para "Descargar archivo con
errores": un `<form method="post">` real (descarga nativa del navegador, no `fetch`)
con el archivo en un hidden base64.

1. **Vista** (`Index.cshtml`): dentro del bloque
   `@if (Model.PreviewResult.Documents.Any(d => d.CanCreate))`, se agrega un
   `<form id="formReporte" method="post">` oculto con `@Html.AntiForgeryToken()` y los
   mismos hidden que `formConfirmar` (`Input.Module`, `Input.SalesDocumentType`,
   `Input.PurchaseDocumentType`, `Input.InventoryDocumentType`, `Input.LineType`,
   `Input.BusinessPartnerCardCode`, `Input.Base64File`) **más** un
   `<input type="hidden" name="jobId" id="reporteJobId" />`. Un botón
   `<button type="submit" form="formReporte" id="btnDescargarReporte">` que arranca
   **oculto** (`style="display:none"`).
2. **JS** (bloque de `btnConfirmar`): la variable `jobId` que ya se genera en el
   `click` se guarda en un scope accesible. Cuando el trabajo termina — tanto por la
   respuesta final del `fetch('?handler=Confirm')` como por
   `consultarProgreso` con `progreso.finished` — se hace:
   `document.getElementById('reporteJobId').value = jobId;` y
   `document.getElementById('btnDescargarReporte').style.display = '';`
   El botón se muestra siempre que la corrida haya terminado (haya creados o no),
   porque el reporte cubre todos los grupos.
3. **Handler** `OnPostDownloadReportAsync(string jobId, CancellationToken ct)` en
   `Index.cshtml.cs`:
   - Si `string.IsNullOrEmpty(Input.Base64File)` →
     `ModelState.AddModelError(string.Empty, "Volvé a procesar el archivo antes de descargar el reporte.")`,
     `LoadCreatableDocumentTypesAsync` + `ResolveBusinessPartnerFromFileAsync`,
     `return Page()` (mismo patrón que `OnPostDownloadWithErrorsAsync`).
   - `var parameters = BuildParameters();`
   - `var bytes = Convert.FromBase64String(Input.Base64File);` → `MemoryStream` →
     `preview = await _importService.ProcessFileAsync(parameters, stream, ct);`
   - `var results = _progress.Get(jobId)?.Results;` (puede ser null).
   - `var fileBytes = await _importService.GenerateResultReportAsync(parameters, preview.Documents, results, ct);`
   - `return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Reporte_{Input.Module}_{parameters.DocumentType}.xlsx");`

Caveat aceptado (ya documentado para el reproceso de "Confirmar"): si los catálogos
SAP cambiaron entre confirmar y descargar, el reproceso puede recalcular validez de
filas distinto. Es determinístico respecto al archivo + configuración vigentes.

## Componente nuevo: `GenerateResultReportAsync`

Firma en `IGenericImportService`:

```csharp
/// <summary>
/// Genera el .xlsx de "Descargar reporte" -- una fila por línea del archivo, de TODOS
/// los grupos, con el DocNum SAP y el estado (Creado / Rechazado por SAP / No creado
/// (errores)) resueltos cruzando GroupingKey contra `results`. Columnas de línea
/// adaptativas por módulo (mismo criterio que GenerateTemplateAsync/
/// GenerateFileWithErrorsAsync). `results` null/vacío => todo cae en "No creado".
/// </summary>
Task<byte[]> GenerateResultReportAsync(GenericImportParametersDto parameters,
    IReadOnlyList<GenericImportDocumentDto> documents,
    IReadOnlyList<GenericImportDocumentResultDto>? results,
    CancellationToken ct = default);
```

Implementación en `GenericImportService` (ClosedXML, `XLWorkbook`, hoja `Reporte`):

- Encabezados, en orden:
  `Fila | Artículo | Descripción | Cantidad | Precio | <columnas de módulo> | DocNum | Estado | Errores / Mensaje`
  - `<columnas de módulo>`: `Almacén Origen | Almacén Destino` si
    `parameters.Module == GenericImportModule.Inventory`; si no,
    `Bodega | Cuenta Mayor | Centro de Costos`.
- `var byKey = results?.ToDictionary(r => r.GroupingKey) ?? new();`
- `var allRows = documents.SelectMany(d => d.Rows).OrderBy(r => r.RowNumber).ToList();`
  (mismo patrón que `GenerateFileWithErrorsAsync`).
- Por cada `row`:
  - `Fila` = `row.RowNumber`
  - `Artículo` = `row.ItemCode ?? row.RawValues.GetValueOrDefault(GenericImportLogicalField.ItemCode)`
  - `Descripción` = `row.Description ?? row.ItemName ?? row.RawValues.GetValueOrDefault(GenericImportLogicalField.Description)`
  - `Cantidad` = `row.Quantity`; `Precio` = `row.UnitPrice`
  - Inventario: `Almacén Origen` = `row.SourceWarehouse`; `Almacén Destino` = `row.DestinationWarehouse`
  - Venta/Compra: `Bodega` = `row.Warehouse`; `Cuenta Mayor` = `row.Account`; `Centro de Costos` = `row.CostCenter`
  - `DocNum` / `Estado` / `Errores / Mensaje` según la tabla de "Estado por grupo",
    mirando `byKey.TryGetValue(row.GroupingKey, out var res)`.
  - Si el grupo no está `Creado` → resaltar el rango de la fila en rojo claro.
- `ws.Columns().AdjustToContents();` → `SaveAs(MemoryStream)` → `ToArray()`.

No usa la configuración de columnas del Excel original (`config.Fields`/`ExcelColumn`)
como hace `GenerateFileWithErrorsAsync` — el reporte tiene layout propio fijo, no
reproduce el archivo del usuario. No necesita `_userFieldsCatalog`.

## Tests

Archivo `tests/PortalSaas.Core.Tests/ImportacionGenerica/GenericImportServiceReportTests.cs`
(o casos nuevos en el archivo existente si ya hay uno para este servicio). Se
construyen `GenericImportDocumentDto` a mano (sin tocar SAP ni Excel de entrada), se
llama `GenerateResultReportAsync`, se abre el `byte[]` resultante con
`new XLWorkbook(stream)` y se afirman celdas concretas:

1. **Grupo creado OK**: `results` con `Success = true, DocNum = 12345`. El reporte
   tiene la celda `DocNum` = `12345`, `Estado` = `Creado`, `Errores / Mensaje` vacío,
   sin relleno rojo.
2. **Grupo con error de validación**: `GroupingKey` NO está en `results`, la fila
   tiene `Errors = ["Artículo X no existe"]`. `DocNum` vacío, `Estado` =
   `No creado (errores)`, `Errores / Mensaje` = `Artículo X no existe`, fila con
   relleno rojo.
3. **Grupo rechazado por SAP**: `results` con `Success = false, DocNum = 0,
   Message = "Quantity falls into negative inventory"`. `Estado` =
   `Rechazado por SAP`, `Errores / Mensaje` = ese mensaje repetido en cada fila del
   grupo, `DocNum` vacío, fila con relleno rojo.
4. **`results` null**: todos los grupos caen en `No creado (errores)`, el archivo se
   genera sin excepción.
5. **Columnas por módulo**: con `Module = Inventory` el encabezado tiene
   `Almacén Origen`/`Almacén Destino`; con `Module = Sales` tiene
   `Bodega`/`Cuenta Mayor`/`Centro de Costos`.

## Archivos tocados

- `src/PortalSaas.Abstractions/Contratos/IGenericImportService.cs` — firma nueva.
- `src/PortalSaas.Core/ImportacionGenerica/GenericImportService.cs` — impl.
- `plugins/Modulo.ImportacionGenerica/Pages/Importar/Index.cshtml.cs` — handler
  `OnPostDownloadReportAsync`.
- `plugins/Modulo.ImportacionGenerica/Pages/Importar/Index.cshtml` — `formReporte`
  oculto, botón, JS.
- `tests/PortalSaas.Core.Tests/ImportacionGenerica/GenericImportServiceReportTests.cs`
  — nuevo.

## Verificación final

- `dotnet build PortalSaas.sln` en 0/0.
- `dotnet test tests/PortalSaas.Core.Tests` en verde.
- Detener procesos `PortalSaas.Host` para que el plugin se republique a
  `artifacts/plugins/`, relanzar, y probar E2E: procesar un archivo con al menos un
  grupo válido y uno con error, confirmar, y descargar el reporte — verificar DocNum,
  estados y columnas por módulo contra un ambiente SAP real.
- `graphify update .`
