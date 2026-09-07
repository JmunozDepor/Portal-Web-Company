# Motor de reglas de validación pre-carga (Importación Genérica)

Fecha: 2026-09-07
Estado: aprobado, pendiente de implementación
Módulo: `plugins/Modulo.ImportacionGenerica` + `PortalSaas.Core.ImportacionGenerica`

## Problema

El wizard de importación masiva (`/importacion-generica/importar`) hoy valida cada fila
contra un set fijo de reglas hardcodeadas en `GenericImportService.ProcessRow` (socio
existe, artículo existe, paridad de SKU configurada, almacén/cuenta/centro de costos
existen, cantidad positiva, descuento en rango) más `BuiltInRules`
(`IGenericImportValidationRule`: `PositiveQuantityRule`/`ValidDiscountPercentRule`).
Cualquier fila inválida bloquea la creación de **todo el documento** al que pertenece
(`GenericImportDocumentDto.CanCreate`).

El dueño del proyecto necesita, antes de cargar pedidos masivos a SAP (caso real: Venta,
archivos con muchas líneas para varias sucursales), poder simular la carga y detectar
problemas de negocio que hoy no se validan en absoluto:

1. Precio del cliente distinto al de la lista de precio (fija o la asignada al cliente).
2. Stock insuficiente -- la demanda total del archivo (sumada entre TODOS los documentos
   que se están importando juntos) por artículo+bodega, contra el disponible real.
3. Cliente o artículo inactivos en SAP.
4. Sucursal del pedido no pertenece al cliente.

Estas validaciones **no deben ser fijas en código ni iguales para todas las
compañías/formatos** -- lo que se considera bloqueante para un cliente puede ser solo
una alerta para otro, y no todos los Formatos de importación necesitan las mismas
reglas activas. La solución: un catálogo de tipos de regla reutilizables, activables por
Formato de importación (`GenericImportConfig`), cada uno con severidad configurable
(Bloqueante/Alerta) y sus propios parámetros.

**Relación con el spec `2026-09-06-reporte-resultado-importacion-generica-design.md`**:
son features distintas y complementarias, no se pisan. Ese reporte es **posterior** a
"Confirmar" (cruza DocNum SAP real). Este motor corre **antes** de confirmar, dentro de
la misma simulación que ya hace "Procesar (vista previa)" (`ProcessFileAsync`) --
nunca toca SAP para escribir.

## Alcance

- Aplica a los 3 módulos (Venta/Compra/Inventario) -- cada tipo de regla declara a
  cuáles aplica (ver catálogo abajo); una regla que no aplica al módulo del Formato
  simplemente no se puede activar ahí (validado en la UI de configuración).
- Reemplaza `IGenericImportValidationRule`/`BuiltInRules` -- esas dos reglas
  (`PositiveQuantityRule`/`ValidDiscountPercentRule`) pasan a ser dos tipos más del
  catálogo nuevo, con severidad fija en `Block` (integridad estructural mínima, siempre
  activas, no aparecen en la UI de configuración por Formato).
- Las advertencias (`Severity = Warning`) **nunca** bloquean `IsValid`/`CanCreate` --
  se acumulan en una lista nueva `Warnings`, separada de `Errors`.
- Fuera de alcance en esta entrega: el tipo de regla `PriceVsDiscountMatrix` (matriz de
  descuento por Marca/Línea, caso Depor) -- queda documentado como extensión futura del
  catálogo, no se construye ahora (ver "Decisiones descartadas").

## Decisiones descartadas (y por qué)

- **SQL libre configurable por admin**: descartado por riesgo de inyección y por no
  poder revisar/testear una consulta arbitraria antes de que corra en producción. En su
  lugar, cada tipo de regla es una clase de código (revisada, testeada), con solo sus
  PARÁMETROS configurables desde la UI -- nunca la lógica SQL en sí.
- **Matriz de descuento por Marca/Línea (caso Depor) como tipo de regla ahora**: es una
  lógica genuinamente específica de una compañía (lee una tabla de usuario de SAP con
  nombre/columnas propios). Se deja fuera de esta entrega a propósito (YAGNI -- sin un
  segundo caso real que confirme la forma general, construirla ahora sería
  sobre-diseñar) -- el catálogo está armado para agregarla como un tipo de regla más
  (`PriceVsDiscountMatrix`) sin tocar el motor ni las reglas existentes, el día que haga
  falta.

## Modelo de datos

Nueva tabla `generic_import_validation_rule_assignments` (Postgres + SQL Server, migración
`AddGenericImportValidationRules`):

```csharp
public sealed class GenericImportValidationRuleAssignment
{
    public int Id { get; set; }

    public int GenericImportConfigId { get; set; }
    public GenericImportConfig GenericImportConfig { get; set; } = null!;

    /// <summary>Nombre del enum GenericImportValidationRuleType (string, igual criterio que Module/DocumentType en GenericImportConfig -- PortalSaas.Data nunca referencia PortalSaas.Abstractions).</summary>
    public string RuleType { get; set; } = null!;

    /// <summary>"Block" | "Warning". Ignorado en el motor para los tipos con severidad fija (ver catálogo) -- ahí siempre se trata como Block sin importar lo que tenga guardado.</summary>
    public string Severity { get; set; } = "Warning";

    public bool IsActive { get; set; } = true;

    /// <summary>JSON con los parámetros propios del tipo de regla (ej. {"priceListNum":2,"tolerancePercent":1.5}). Null si el tipo no tiene parámetros.</summary>
    public string? ParametersJson { get; set; }
}
```

FK `GenericImportConfigId` -> `generic_import_configs.id`, `DeleteBehavior.Cascade`
(borrar un Formato borra sus reglas -- a diferencia de `Company`/`Instance`, acá no hay
razón para bloquear el borrado por dependientes, la regla no tiene valor sin su
Formato). Constraint única `(GenericImportConfigId, RuleType)` -- un tipo de regla se
activa una sola vez por Formato (evita duplicados accidentales).

`GenericImportConfigDto` gana `IReadOnlyList<GenericImportValidationRuleAssignmentDto> ValidationRules`.
`GenericImportValidationRuleAssignmentDto` (record): `Id, RuleType (enum), Severity (enum), IsActive, IReadOnlyDictionary<string, object?> Parameters`.

`IGenericImportConfigService` gana:
```csharp
Task SaveValidationRulesAsync(int configId, IReadOnlyList<GenericImportValidationRuleAssignmentDto> rules, CancellationToken ct = default);
```
Reemplaza TODAS las reglas del Formato de una vez (mismo criterio que
`UpdateAsync(fields)` con los campos núcleo -- el caller siempre manda el set completo
vigente, no hay altas/bajas incrementales del lado del servicio). Valida ahí mismo (no
en la UI únicamente, defensa en profundidad): un `RuleType` de precio (`PriceVsFixedList`/
`PriceVsCustomerList`) no puede repetirse ni convivir el uno con el otro para el mismo
Formato -- `InvalidOperationException` si se intenta.

## Catálogo de tipos de regla

`enum GenericImportValidationRuleType` (`PortalSaas.Abstractions.Modelos`):

| Tipo | Módulos | Severidad | Parámetros | Descripción |
|---|---|---|---|---|
| `PositiveQuantity` | Todos | Fija: Block | — | Cantidad > 0. Reemplaza `PositiveQuantityRule`, siempre activa, no configurable. |
| `ValidDiscountPercent` | Venta/Compra | Fija: Block | — | Descuento entre 0 y 100. Reemplaza `ValidDiscountPercentRule`, siempre activa. |
| `ItemCodeExists` | Todos | Fija: Block | — | El código de artículo resuelve a un `ItemCode` real en SAP. Sin esto no hay línea que postear -- no tiene sentido como Alerta (confirmado con el dueño del proyecto). |
| `SkuCrossReferenceExists` | Todos (solo si `SkuIsCustomerOwn`) | Fija: Block | — | El SKU del cliente tiene paridad configurada. Mismo motivo que `ItemCodeExists` -- sin paridad no hay `ItemCode` que resolver. |
| `CustomerActiveInSap` | Venta/Compra | Configurable | — | `OCRD.validFor = 'Y'` para el `CardCode` de la fila. |
| `ItemActiveInSap` | Todos | Configurable | — | `OITM.validFor = 'Y'` para el `ItemCode` resuelto. |
| `PriceVsFixedList` | Venta/Compra, línea Artículo | Configurable | `priceListNum: int`, `tolerancePercent: decimal = 0` | `UnitPrice` importado vs. precio del artículo en la lista `priceListNum` (`ITM1`), fuera de tolerancia = advertencia/error. |
| `PriceVsCustomerList` | Venta/Compra, línea Artículo | Configurable | `tolerancePercent: decimal = 0` | Igual que arriba, pero la lista de referencia es la asignada al `CardCode` (`OCRD.ListNum`, vía `IBusinessPartnerDefaultsService`) -- mutuamente excluyente con `PriceVsFixedList` para el mismo Formato. |
| `StockAvailable` | Venta/Compra (`Warehouse`), Inventario (`SourceWarehouse`) | Configurable | `tolerancePercent: decimal = 0` (tolerancia sobre el déficit, no sobre el precio) | Demanda agregada de TODO el archivo por `(ItemCode, Warehouse)` vs. disponible real (`OnHand - IsCommited`). Única regla que corre sobre el LOTE completo, no fila por fila -- ver "Motor". |
| `CustomerBranchValid` | Venta/Compra | Configurable | — | El `Branch` de la fila corresponde a una dirección de despacho (`CRD1`, `AddressType = 'S'`) del `CardCode` de esa fila. |

Extensión futura (no en esta entrega): `PriceVsDiscountMatrix` (ver "Decisiones
descartadas").

## Motor de reglas

Nuevo namespace `PortalSaas.Core.ImportacionGenerica.Reglas` (vive dentro del mismo
proyecto -- no es un `IModuloPortal` aparte, es una separación lógica de
responsabilidad dentro de `PortalSaas.Core`, mismo criterio que `Sap/`/`Catalogos/`).

```csharp
public interface IGenericImportValidationRule
{
    GenericImportValidationRuleType RuleType { get; }
    /// <summary>Módulos a los que aplica -- el motor lo usa para no correr una regla fuera de su módulo, aunque esté mal configurada.</summary>
    IReadOnlyList<GenericImportModule> ApplicableModules { get; }
    /// <summary>true si la severidad configurada en el RuleAssignment se respeta; false = siempre Block (ItemCodeExists/SkuCrossReferenceExists/PositiveQuantity/ValidDiscountPercent).</summary>
    bool SeverityIsConfigurable { get; }
}

/// <summary>Regla por FILA -- corre una vez por cada GenericImportRowDto ya resuelto.</summary>
public interface IGenericImportRowValidationRule : IGenericImportValidationRule
{
    IEnumerable<string> Validate(GenericImportRowDto row, IReadOnlyDictionary<string, object?> parameters);
}

/// <summary>Regla de LOTE -- corre una sola vez sobre todas las filas ya resueltas del archivo completo. Hoy solo StockAvailable la implementa.</summary>
public interface IGenericImportBatchValidationRule : IGenericImportValidationRule
{
    /// <summary>Devuelve, por cada fila afectada, los mensajes que le corresponden -- una fila puede aparecer en el resultado de más de una regla de lote.</summary>
    IReadOnlyDictionary<GenericImportRowDto, IReadOnlyList<string>> Validate(
        IReadOnlyList<GenericImportRowDto> allRows, IReadOnlyDictionary<string, object?> parameters);
}
```

`IGenericImportValidationRuleEngine`:
```csharp
Task<IReadOnlyList<GenericImportRowDto>> ApplyAsync(
    IReadOnlyList<GenericImportRowDto> rows, GenericImportConfigDto? config, CancellationToken ct = default);
```

Flujo dentro de `GenericImportService.ProcessFileAsync` (después de que todas las filas
ya pasaron por `ProcessRow` con la validación estructural actual sin cambios):

1. Las 4 reglas fijas (`PositiveQuantity`/`ValidDiscountPercent`/`ItemCodeExists`/
   `SkuCrossReferenceExists`) **siguen corriendo dentro de `ProcessRow` como hoy** --
   no se mueven al motor nuevo por practicidad (ya están ahí, ya tienen acceso directo a
   las variables resueltas de la fila) pero SÍ se registran como tipos del catálogo
   (`RuleType` fijo en el `Errors` que ya generan) para que el reporte/UI las liste de
   forma consistente con el resto.
2. El motor corre sobre las filas ya resueltas: para cada `RuleAssignment` activo en
   `config.ValidationRules` (si `config` es null -- sin configuración resuelta -- el
   motor no corre nada más, mismo comportamiento que hoy), busca la implementación por
   `RuleType`, y:
   - Si es `IGenericImportRowValidationRule`: corre `Validate(row, parameters)` por cada
     fila; los mensajes van a `row.Errors` (si `Severity` efectiva es `Block`) o
     `row.Warnings` (si `Warning`).
   - Si es `IGenericImportBatchValidationRule` (`StockAvailable`): corre una vez con
     TODAS las filas, reparte los mensajes devueltos a cada fila afectada.
3. `IsValid` se recalcula al final: `Errors.Count == 0` (sin cambios de fórmula, solo
   que ahora `Errors` puede tener más mensajes que antes).

`StockAvailable` en detalle (`IGenericImportBatchValidationRule`):
```csharp
var demandaPorParClave = allRows
    .Where(r => r.ItemCode is not null && ResolveWarehouse(r, module) is not null)
    .GroupBy(r => (r.ItemCode!, Warehouse: ResolveWarehouse(r, module)!))
    .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity ?? 0));

var disponible = await _stock.GetAvailableStockAsync(demandaPorParClave.Keys.ToList(), ct);

foreach (var (clave, demanda) in demandaPorParClave)
{
    var stockDisponible = disponible.GetValueOrDefault(clave, 0);
    var tolerancia = demanda * (tolerancePercent / 100m);
    if (demanda > stockDisponible + tolerancia)
    {
        var mensaje = $"Demanda total de \"{clave.ItemCode}\" en bodega \"{clave.Warehouse}\": {demanda:N2} -- disponible: {stockDisponible:N2} (faltan {demanda - stockDisponible:N2}).";
        // agregar mensaje a cada fila cuyo (ItemCode, Warehouse) == clave
    }
}
```
`ResolveWarehouse` = `row.Warehouse` (Venta/Compra) o `row.SourceWarehouse`
(Inventario) -- mismo criterio ya usado para paridad entre motores.

## Cambios de contrato/catálogo nuevos

- **`IItemStockService`** gana:
  ```csharp
  Task<IReadOnlyDictionary<(string ItemCode, string WhsCode), decimal>> GetAvailableStockAsync(
      IReadOnlyList<(string ItemCode, string WhsCode)> pairs, CancellationToken ct = default);
  ```
  Una sola consulta `WHERE (T0."ItemCode", T0."WhsCode") IN (...)` contra `OITW` (mismo
  patrón anti-N+1 que el resto de catálogos batch del proyecto). Lista vacía de entrada
  -> diccionario vacío sin consultar.
- **`ICustomerShipToAddressService`** (nuevo, `PortalSaas.Core.Catalogos`):
  ```csharp
  Task<IReadOnlyList<string>> GetShipToAddressCodesAsync(string cardCode, CancellationToken ct = default);
  ```
  `SELECT "Address" FROM "CRD1" WHERE "CardCode" = :cardCode AND "AddressType" = 'S'`.
- **`ICustomerCatalogService`**/**`IItemCatalogService`** ganan cada uno:
  ```csharp
  Task<IReadOnlyDictionary<string, bool>> GetActiveStatusAsync(IReadOnlyList<string> codes, CancellationToken ct = default);
  ```
  (`CardCode`/`ItemCode` -> `validFor == "Y"`). Mismo patrón batch que
  `GetAvailableStockAsync` -- un solo `SELECT ... WHERE "CardCode" IN (...)` /
  `WHERE "ItemCode" IN (...)`. Método nuevo explícito en vez de reusar el `SELECT` de
  listado/búsqueda existente de cada catálogo -- ese trae columnas para UI (nombre,
  etc.), no vale la pena acoplar el chequeo de estado a esa consulta.
- **`IPriceListService`** ya existe (`GetPriceAsync`/`GetPricesAsync` batch) -- se
  reusa tal cual para `PriceVsFixedList`/`PriceVsCustomerList`, sin cambios de
  contrato.
- **`IBusinessPartnerDefaultsService`** ya existe (resuelve `ListNum` del cliente) --
  se reusa tal cual para `PriceVsCustomerList`.

## UI

### Configuración por Formato (`Pages/Configuracion/Index.cshtml`)

Nueva sección "Reglas de validación" debajo de "Campos de usuario" -- tabla fija de
las 6 reglas configurables del catálogo (las 4 estructurales fijas en Block NO
aparecen acá, corren siempre igual que hoy):

| Columna | Contenido |
|---|---|
| Regla | Nombre legible (ej. "Precio vs. lista fija") |
| Activa | checkbox |
| Severidad | `<select>` Bloqueante/Alerta |
| Parámetros | inputs específicos, mostrados solo si `Activa` (JS toggle, mismo patrón que `mostrarTipoDocumentoConfig`) |

Validación cliente + servidor (`SaveValidationRulesAsync`, ver arriba): `PriceVsFixedList`
y `PriceVsCustomerList` no pueden estar Activas las dos a la vez.

### Vista previa (`Pages/Importar/Index.cshtml`)

Cada fila de la tabla de detalle (dentro de cada `<details>` de documento) gana una
celda "Advertencias" (texto ámbar, `class="text-warning"`) cuando `row.Warnings.Count > 0`
-- separada de la columna "Errores" existente (roja). El resumen del `<summary>` de
documento agrega, junto al conteo de filas/errores que ya muestra, algo como
`· 2 advertencia(s)` cuando corresponda.

### Reporte de validación descargable

Nuevo botón "Descargar reporte de validación" (mismo patrón `<form>` oculto +
`@Html.AntiForgeryToken()` que `formReporte`, del spec de resultado post-carga, PERO
un form/handler propio -- `OnPostDownloadValidationReportAsync`, disponible apenas hay
`PreviewResult`, sin esperar a "Confirmar" -- a diferencia del reporte de resultado, que
solo tiene sentido después de correr `Confirmar`). Genera un `.xlsx` con:
- Hoja `Detalle`: una fila por línea del archivo, con columnas `Fila | Artículo | ... |
  Errores | Advertencias` (reusa el layout de `GenerateFileWithErrorsAsync` como base,
  agregando la columna `Advertencias`).
- Hoja `Stock por artículo-bodega`: una fila por cada `(ItemCode, Warehouse)` que
  disparó `StockAvailable`, con `Artículo | Bodega | Demanda | Disponible | Faltante`.

`IGenericImportService` gana:
```csharp
Task<byte[]> GenerateValidationReportAsync(GenericImportParametersDto parameters,
    IReadOnlyList<GenericImportDocumentDto> documents, CancellationToken ct = default);
```

## Tests

- `GenericImportValidationRuleEngineTests`: cada tipo de regla configurable, caso pasa /
  caso falla; `Severity = Warning` puebla `Warnings` sin tocar `IsValid`;
  `Severity = Block` sí lo hace; una regla no aplicable al módulo del Formato no corre
  aunque esté activa (defensa en profundidad).
- `StockAvailable`: demanda de 2 documentos distintos del mismo archivo se suma antes de
  comparar; dos bodegas distintas para el mismo artículo se validan independiente;
  tolerancia en 0 exige exacto, tolerancia > 0 permite un margen.
- `GenericImportConfigServiceTests` (`SaveValidationRulesAsync`): reemplaza el set
  completo; rechaza `PriceVsFixedList` + `PriceVsCustomerList` juntas; aislamiento entre
  Formatos de distinta Company.
- Catálogos nuevos (`GetAvailableStockAsync`/`GetShipToAddressCodesAsync`): sin tests de
  integración real (mismo criterio que el resto de `PortalSaas.Core.Catalogos`, se
  verifican E2E contra SAP real cuando tienen consumidor).

## Archivos tocados (estimado, a confirmar en el plan de implementación)

- `src/PortalSaas.Data/Entities/GenericImportValidationRuleAssignment.cs` -- nuevo.
- `src/PortalSaas.Data/PortalSaasDbContext.cs` -- `DbSet` + FK + constraint única.
- `src/PortalSaas.Data.Migrations.PostgreSql/` y `.SqlServer/` -- migración
  `AddGenericImportValidationRules`.
- `src/PortalSaas.Abstractions/Modelos/GenericImportValidationRuleType.cs`,
  `GenericImportValidationRuleAssignmentDto.cs` -- nuevos.
- `src/PortalSaas.Abstractions/Contratos/IGenericImportConfigService.cs` --
  `SaveValidationRulesAsync`.
- `src/PortalSaas.Abstractions/Contratos/IItemStockService.cs` --
  `GetAvailableStockAsync`.
- `src/PortalSaas.Abstractions/Contratos/ICustomerShipToAddressService.cs` -- nuevo.
- `src/PortalSaas.Core/ImportacionGenerica/Reglas/` -- namespace nuevo:
  `IGenericImportValidationRule.cs` (interfaces), una clase por tipo de regla,
  `GenericImportValidationRuleEngine.cs`.
- `src/PortalSaas.Core/ImportacionGenerica/GenericImportService.cs` -- integra el motor
  en `ProcessFileAsync`, `GenerateValidationReportAsync`.
- `src/PortalSaas.Core/Catalogos/ItemStockService.cs` -- `GetAvailableStockAsync`.
- `src/PortalSaas.Core/Catalogos/CustomerShipToAddressService.cs` -- nuevo.
- `plugins/Modulo.ImportacionGenerica/Pages/Configuracion/Index.cshtml(.cs)` -- sección
  de reglas.
- `plugins/Modulo.ImportacionGenerica/Pages/Importar/Index.cshtml(.cs)` -- columna de
  advertencias, botón de reporte de validación.
- `tests/PortalSaas.Core.Tests/ImportacionGenerica/` -- tests nuevos.

## Verificación final

- `dotnet build PortalSaas.sln` en 0/0.
- `dotnet test tests/PortalSaas.Core.Tests` en verde.
- Migraciones aplicadas contra Postgres y SQL Server de desarrollo (no solo generadas).
- E2E contra un ambiente SAP real: activar `StockAvailable` en un Formato de Venta real,
  importar un archivo cuya demanda agregada supere el disponible de una bodega real,
  confirmar que aparece como advertencia (no bloquea) y que el reporte descargable trae
  el detalle correcto. Repetir con `PriceVsCustomerList` contra un cliente con lista de
  precio real distinta al precio del archivo.
- `graphify update .`
