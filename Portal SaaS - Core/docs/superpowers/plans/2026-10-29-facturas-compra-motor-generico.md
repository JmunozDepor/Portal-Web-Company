# Facturas de Proveedor y Facturas Reserva de Proveedor en el motor genérico de Compras: plan de implementación

> **Para agentes:** sub-skill obligatoria: superpowers:subagent-driven-development (recomendada) o superpowers:executing-plans para ejecutar el plan tarea por tarea. Los pasos usan casillas (`- [ ]`) para el seguimiento.

**Objetivo:** agregar dos tipos de documento a Compras: Factura de Proveedor y Factura Reserva de Proveedor. Ambos van sobre `OPCH`, con documento tributario y "Copiar de Pedido", y heredan todo del formulario base.

**Arquitectura:** el catálogo estático `PurchaseDocumentTypeCatalog` declara cada particularidad: filtro de listado, campos fijos de creación, tipo de línea por defecto, documento tributario y desde qué tipo se copia. El servicio de Core (`IPurchaseDocumentService`) la aplica y la expone con `Describe(type)`. Las bases `DetailGeneric*`/`IndexGeneric*` del plugin la leen de ahí; los subtipos solo se identifican. Las lecturas de Pedidos abiertos viven en Core para que la Etapa 2 las reutilice. El panel "Copiar de" es un JS genérico del Host.

**Tecnología:** .NET 8 Razor Pages, plugin Modulo.Compras, `IHanaService` (SQL en dialecto HANA), Service Layer vía `ISapSession`, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-29-facturas-compra-motor-generico-design.md`

**Rama y espacio de trabajo:**
- Worktree `.claude/worktrees/facturas-compra`, rama `feat/facturas-compra` creada desde `feat/borradores` (HEAD 8c7422a).
- El spec y este plan van en el primer commit.

## Restricciones globales

- **Todo nace del documento padre.** Ningún subtipo sobrescribe `OnGetAsync`/`OnPostAsync`. Cada particularidad sale del catálogo, vía `IPurchaseDocumentService.Describe`.
- **Mismo layout que los formularios base:**
  - el mismo chrome `DocumentForm`, editor de líneas, Resumen total, borradores, "Validar" y "Personalizar";
  - la pestaña Finanzas es la de Ventas (`AccountingView` + `AccountingTitle = "Finanzas"`).
- **Carpetas de páginas:**
  - `Pages/PurchaseInvoices/` y `Pages/PurchaseReserveInvoices/`;
  - **nunca** `Pages/ReserveInvoices/`, que ya existe en Modulo.Ventas: Razor pisa una con la otra en silencio y da 404.
- **Rutas:** `/compras/facturas-proveedor` y `/compras/facturas-reserva`.
- **Menú:**
  - `facturas` con `bi bi-receipt`;
  - `facturasreserva` con `bi bi-receipt-cutoff`.
- URLs internas con `~/` o `Request.PathBase`; nunca "/compras/..." literal en vistas.
- Todo responsive a 375 px:
  - tablas en `table-responsive-lg`;
  - el panel es el drawer estándar `.drawer-overlay/.drawer-panel`.
- Servicios nuevos que use la base se resuelven con `HttpContext.RequestServices`, como `Drafts`/`FormLayouts`, para no tocar el constructor de cada subtipo.
- SQL en dialecto HANA, con los valores del usuario siempre como parámetros.
- Sin migraciones.
- Textos de pantalla en español, con el mismo tono que los mensajes ya existentes en el archivo que se toca.
- Comentarios con la densidad y el estilo del archivo que se toca.
- Pruebas:
  - `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests"`;
  - compilar `plugins/Modulo.Compras` y `plugins/Modulo.ImportacionGenerica`.

## Foco de revisión

Lo que el spec implica y las pruebas no cubren. Ordenado de más a menos probable que le falle a quien lo use:

1. **Línea copiada con precio distinto de la lista o del precio especial del proveedor.** No debe bloquearse: el precio viene del Pedido.
   - La validación de precio (`AddLineRestrictionErrorsAsync`) excluye las líneas con `BaseEntry`.
   - Se fija en el Task 3 con `PurchaseDocumentRules.LinesSubjectToPriceCheck`.
2. **Cambiar de proveedor después de copiar.** Las líneas copiadas desaparecen del formulario (JS). Si llegaran igual, el servidor las rechaza (`ValidateCopiedLines`, prueba en el Task 3).
3. **Reabrir un borrador o volver de un error de validación con líneas copiadas.** La fila renderizada en el servidor conserva los ocultos `BaseType/BaseEntry/BaseLine/BaseDocNum`, el artículo de solo lectura y el distintivo "Pedido N.°".
4. **Pedido en USD copiado en un formulario en CLP.** El formulario pasa a USD: el `select` de moneda y la etiqueta del Resumen total cambian, y cada línea lleva su moneda.
5. **El mismo Pedido copiado dos veces.**
   - El JS salta las líneas ya presentes.
   - El servidor suma las cantidades por línea base y bloquea si superan lo pendiente (prueba en el Task 3).

## Mapa de archivos

| Archivo | Cambio |
|---|---|
| `src/PortalSaas.Abstractions/Modelos/PurchaseDocumentType.cs` | +2 valores |
| `src/PortalSaas.Abstractions/Modelos/PurchaseDocumentTypeInfo.cs` | nuevo |
| `src/PortalSaas.Abstractions/Modelos/PurchaseDocumentDto.cs` | documento tributario en cabecera, filtro y fila de listado |
| `src/PortalSaas.Abstractions/Modelos/PurchaseCopyFromDtos.cs` | nuevo: Pedido abierto y su línea pendiente |
| `src/PortalSaas.Abstractions/Modelos/DocumentIndicatorDto.cs` | nuevo |
| `src/PortalSaas.Abstractions/Modelos/BusinessPartnerDefaultsDto.cs` | +`FederalTaxId` |
| `src/PortalSaas.Abstractions/Contratos/IPurchaseDocumentService.cs` | +`Describe`, +3 lecturas |
| `src/PortalSaas.Abstractions/Contratos/IDocumentIndicatorCatalogService.cs` | nuevo |
| `src/PortalSaas.Core/Compras/PurchaseDocumentTypeCatalog.cs` | Entry extendido, 2 entradas |
| `src/PortalSaas.Core/Compras/PurchaseDocumentService.cs` | filtros extra, campos de creación, folio, lecturas |
| `src/PortalSaas.Core/Compras/SapPurchaseDocumentModels.cs` | +4 propiedades de cabecera |
| `src/PortalSaas.Core/Catalogos/DocumentIndicatorCatalogService.cs` | nuevo (OIDC) |
| `src/PortalSaas.Core/Catalogos/BusinessPartnerDefaultsService.cs` | +`LicTradNum` |
| `src/PortalSaas.Host/Program.cs` | registro del catálogo de indicadores |
| `src/PortalSaas.Host/wwwroot/js/document-lines-editor.js` | devuelve una API `{ appendRow, recalculate }` |
| `src/PortalSaas.Host/wwwroot/js/document-partner-defaults.js` | opción `taxIdInputId` |
| `src/PortalSaas.Host/wwwroot/js/document-copy-from.js` | nuevo |
| `plugins/Modulo.Compras/Pages/PurchaseDocumentRules.cs` | nuevo: reglas puras probables |
| `plugins/Modulo.Compras/Pages/DetailGenericPurchaseDocumentModelBase.cs` | particularidades por catálogo, handlers de copia |
| `plugins/Modulo.Compras/Pages/IndexGenericPurchaseDocumentModelBase.cs` | columna y filtro Folio |
| `plugins/Modulo.Compras/Pages/Shared/_TabFinanzasCompras.cshtml` | nuevo |
| `plugins/Modulo.Compras/Pages/Shared/_TabContentCompras.cshtml` | ocultos de copia, botón y drawer |
| `plugins/Modulo.Compras/Pages/Shared/_TabGeneralCompras.cshtml` | `taxIdInputId` |
| `plugins/Modulo.Compras/Pages/PurchaseInvoices/*`, `PurchaseReserveInvoices/*` | nuevos (4 archivos cada uno) |
| `plugins/Modulo.Compras/ModuloCompras.cs` | 2 menús |
| `plugins/Modulo.ImportacionGenerica/Pages/ImportDisplayNames.cs` | 2 nombres |
| `tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj` | +ProjectReference a Modulo.Compras |
| `tests/PortalSaas.Core.Tests/Compras/*.cs` | nuevos |
| `tests/PortalSaas.Core.Tests/Integraciones/SapDocumentConnectorTests.cs` | el doble de `IPurchaseDocumentService`, si lo hay, implementa los miembros nuevos con `throw` |

---

### Task 1: Tipos, catálogo y cuerpo de creación (Core)

**Archivos:**
- Modificar: `PurchaseDocumentType.cs`, `PurchaseDocumentDto.cs`, `IPurchaseDocumentService.cs`, `PurchaseDocumentTypeCatalog.cs`, `PurchaseDocumentService.cs`, `SapPurchaseDocumentModels.cs`.
- Crear: `PurchaseDocumentTypeInfo.cs`.
- Prueba: `tests/PortalSaas.Core.Tests/Compras/PurchaseDocumentServiceTests.cs`.

**Interfaces:**
- Produce:
  - `enum PurchaseDocumentType { PurchaseQuotation, PurchaseOrder, PurchaseInvoice, PurchaseReserveInvoice }`
  - `public sealed record PurchaseDocumentTypeInfo(DocumentLineType DefaultLineType, bool HasTaxDocument, PurchaseDocumentType? CopyFromType)`
  - `IPurchaseDocumentService.Describe(PurchaseDocumentType type) → PurchaseDocumentTypeInfo` (sin implementación por defecto)
  - `PurchaseDocumentDto` suma al final `string? TaxIndicator = null, string? FolioPrefix = null, int? FolioNumber = null, string? FederalTaxId = null`
  - `PurchaseDocumentFilter` suma `string? Folio = null`
  - `PurchaseDocumentSummaryDto` suma `string? FolioPrefix`, `int? FolioNumber`
  - `PurchaseDocumentTypeCatalog.ExtraFilter(string Column, string Value, bool IsEqual)`
  - `PurchaseDocumentTypeCatalog.Entry`: a los parámetros actuales se agregan, opcionales y con nombre:
    - `IReadOnlyList<ExtraFilter>? ExtraFilters = null`
    - `IReadOnlyDictionary<string, object>? CreateFields = null`
    - `DocumentLineType DefaultLineType = DocumentLineType.Item`
    - `bool HasTaxDocument = false`
    - `PurchaseDocumentType? CopyFrom = null`
  - `SapPurchaseDocumentHeader` suma `FolioPrefixString` (string?), `FolioNumber` (int?), `Indicator` (string?), `FederalTaxID` (string?)

- [ ] **Paso 1: Escribir las pruebas que fallan**

Dobles en el mismo archivo:
- `FakeHana : IHanaService`: guarda `LastSql` y `LastParameters`; `QueryAsync<T>` devuelve las filas precargadas para `typeof(T)` o una lista vacía, y para `int` devuelve `[0]`.
- `FakeSession : ISapSession`: `PostAsync<T>` guarda `(resource, body)` y devuelve `new SapPurchaseDocumentHeader { DocEntry = 99 }` como `T`; `GetAsync<T>` devuelve el header precargado.
- `FakeProvider : ISapConnectionProvider`.
- `AllowAll : IOrganizationDocumentPermissionService`, que devuelve `defaultValue`.
- `DefaultUdf : ISapTraceabilityFieldResolver`, que devuelve `SapTraceabilityFieldResolver.DefaultUserFieldName`.

Helper `JsonDocument Body(object body)`: serializa con `DefaultIgnoreCondition = WhenWritingNull`.

Documento mínimo: proveedor `"PN77398220-1"`, una línea de Servicio con cuenta `"61-17-001-04"`, centro de costo `"ADM"` y cantidad 1.

```csharp
[Fact] Catalogo_Facturas_ComparteOpchConObjectCode18()
// Las dos entradas: Table "OPCH", Resource "PurchaseInvoices", ObjectCode 18, SupportsCancel true, DefaultCanCreate true.

[Fact] Describe_FacturaProveedor_ServicioConDocumentoTributarioYCopiaDePedido()
// == new PurchaseDocumentTypeInfo(DocumentLineType.Service, true, PurchaseDocumentType.PurchaseOrder)

[Fact] Describe_FacturaReserva_ArticuloConDocumentoTributarioYCopiaDePedido()
// == new(DocumentLineType.Item, true, PurchaseDocumentType.PurchaseOrder)

[Fact] Describe_Pedido_SinParticularidades()
// == new(DocumentLineType.Item, false, null)

[Fact] Create_FacturaReserva_EnviaReserveInvoiceTYesSinIsIns()
// recurso "PurchaseInvoices"; body["ReserveInvoice"] == "tYES"; sin propiedad "isIns".

[Fact] Create_FacturaProveedor_NoEnviaMarcaDeReserva()
// recurso "PurchaseInvoices"; sin "ReserveInvoice" ni "isIns".

[Fact] Create_DocumentoTributario_EnviaFolioIndicadorYRut()
// dto con TaxIndicator "33", FolioPrefix "43", FolioNumber 1157501, FederalTaxId "77398220-1":
// FolioPrefixString "43", FolioNumber 1157501, Indicator "33", FederalTaxID "77398220-1".

[Fact] Create_Pedido_SinDocumentoTributario_NoEnviaEsasPropiedades()
// recurso "PurchaseOrders"; sin FolioPrefixString/FolioNumber/Indicator/FederalTaxID/ReserveInvoice.

[Fact] Create_LineaCopiada_EnviaVinculoAlPedido()
// línea con BaseType 22, BaseEntry 10, BaseLine 0: DocumentLines[0] trae esos tres valores.

[Fact] List_FacturaReserva_FiltraIsInsY()
// LastSql contiene `o."isIns" = :extraFilter0`; LastParameters["extraFilter0"] == "Y".

[Fact] List_FacturaProveedor_FiltraIsInsN()
// el mismo filtro, con valor "N".

[Fact] List_Pedido_SinFiltroIsIns()
// LastSql no contiene "isIns".

[Fact] List_FiltroFolio_BuscaPorNumeroDeFolio()
// filtro Folio "1157": LastSql contiene `TO_VARCHAR(o."FolioNum") LIKE :folio`; parámetro "%1157%".
// El SELECT trae "FolioPref" AS "FolioPrefix" y "FolioNum" AS "FolioNumber".

[Fact] Get_LeeDocumentoTributario()
// header precargado con FolioPrefixString "43", FolioNumber 1157501, Indicator "33", FederalTaxID "77398220-1":
// el DTO trae los cuatro valores.
```

- [ ] **Paso 2: Correr las pruebas y verificar que fallan**

Correr: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter FullyQualifiedName~Compras.PurchaseDocumentServiceTests`

Esperado: FALLA de compilación (`PurchaseInvoice`, `Describe`, `TaxIndicator` no existen).

- [ ] **Paso 3: Implementar**

Entradas del catálogo:

```csharp
[PurchaseDocumentType.PurchaseInvoice] = new("OPCH", "PurchaseInvoices", true, ObjectCode: 18,
    ExtraFilters: [new("isIns", "N", true)], DefaultLineType: DocumentLineType.Service,
    HasTaxDocument: true, CopyFrom: PurchaseDocumentType.PurchaseOrder),
[PurchaseDocumentType.PurchaseReserveInvoice] = new("OPCH", "PurchaseInvoices", true, ObjectCode: 18,
    ExtraFilters: [new("isIns", "Y", true)], CreateFields: new Dictionary<string, object> { ["ReserveInvoice"] = "tYES" },
    HasTaxDocument: true, CopyFrom: PurchaseDocumentType.PurchaseOrder),
```

En el servicio:
- **`ListAsync`:** pasa `entry.ExtraFilters ?? []` a `BuildWhereClause`, con el mismo bucle de `SalesDocumentService` (`o."{Column}" =|<> :extraFilter{i}`). Agrega el filtro `Folio` y las dos columnas al SELECT. `isIns` es el filtro de listado y nunca se envía en el POST.
- **`BuildCreateRequestAsync`:**
  - mapea los cuatro campos tributarios a la cabecera, con `null` si vienen vacíos;
  - si hay `CreateFields` o campos adicionales, aplana con `SapAdditionalFieldsHelper.Flatten` y escribe cada `CreateFields[k]`.
- **`GetAsync`:** mapea los cuatro campos de vuelta.
- **Comentarios:** actualizar los que hoy dicen "2 tipos", "sin Copy-From" o "Ninguno de los 2 tipos comparte tabla". Documentar en `ExtraFilters` frente a `CreateFields` por qué difiere de Ventas: el listado filtra por la columna de la base, y la creación marca la propiedad de Service Layer.
- **Doble de `IPurchaseDocumentService`:** si existe en `SapDocumentConnectorTests`, implementa `Describe` con `throw`.

- [ ] **Paso 4: Correr las pruebas y verificar que pasan**

Correr el filtro del Paso 2 y luego la suite completa de Core.Tests.

Esperado: PASA; el resto de la suite, sin regresiones.

- [ ] **Paso 5: Commit**

```bash
git add -A "Portal SaaS - Core/src" "Portal SaaS - Core/tests" "Portal SaaS - Core/docs/superpowers"
git commit -m "feat(compras): facturas de proveedor y de reserva en el catálogo genérico (OPCH)"
```

---

### Task 2: Lecturas para "Copiar de Pedido", duplicados, RUT e indicadores (Core)

**Archivos:**
- Crear: `PurchaseCopyFromDtos.cs`, `DocumentIndicatorDto.cs`, `IDocumentIndicatorCatalogService.cs`, `DocumentIndicatorCatalogService.cs`.
- Modificar: `IPurchaseDocumentService.cs`, `PurchaseDocumentService.cs`, `BusinessPartnerDefaultsDto.cs`, `BusinessPartnerDefaultsService.cs`, `Program.cs` (`AddScoped`, junto a `IPaymentTermsCatalogService`).
- Prueba: `tests/PortalSaas.Core.Tests/Compras/PurchaseCopyFromQueriesTests.cs`.

**Interfaces:**
- Consume: `FakeHana` del Task 1. Si las dos clases de prueba lo usan, se mueve a `tests/PortalSaas.Core.Tests/Compras/ComprasFakes.cs`.
- Produce: DTOs no posicionales, con `{ get; init; }`, porque HanaService los instancia por reflexión.
  - `PurchaseOpenOrderDto`:
    - campos `DocEntry, DocNum, DocDate (DateTime), SupplierReferenceNumber?, DocCurrency?, DocTotal, DocType ("I"|"S"), OpenLineCount`;
    - propiedad calculada `DocumentLineType LineType`.
  - `PurchaseOpenOrderLineDto`:
    - campos `DocEntry, DocNum, LineNum, CardCode, DocType, DocCurrency?, BuyerCode?, ItemCode?, Description?, OpenQuantity, UnitPrice?, Currency?, DiscountPercent, WarehouseCode?, AccountCode?, CostCenterCode?, CostCenterCode2?, CostCenterCode3?`;
    - propiedad calculada `LineType`.
  - `IPurchaseDocumentService`:
    - `Task<IReadOnlyList<PurchaseOpenOrderDto>> ListOpenOrdersAsync(string supplierCardCode, CancellationToken ct = default)`
    - `Task<IReadOnlyList<PurchaseOpenOrderLineDto>> GetOpenOrderLinesAsync(IReadOnlyCollection<int> docEntries, CancellationToken ct = default)`
    - `Task<int?> FindTaxFolioAsync(string supplierCardCode, string folioPrefix, int folioNumber, CancellationToken ct = default)`: devuelve el DocNum de la factura no cancelada que ya usa ese folio.
  - `DocumentIndicatorDto { Code, Name }` y `IDocumentIndicatorCatalogService.ListAsync(CancellationToken ct = default)`.
  - `BusinessPartnerDefaultsDto.FederalTaxId` (de `OCRD."LicTradNum"`).

- [ ] **Paso 1: Escribir las pruebas que fallan**

```csharp
[Fact] ListOpenOrders_FiltraProveedorAbiertosYLineasPendientes()
// LastParameters["cardCode"] == "PE71506673"; LastSql contiene `FROM "OPOR"`, `"POR1"`,
// `o."DocStatus" = 'O'`, `o."CANCELED" = 'N'`, `l."LineStatus" = 'O'`, `l."OpenQty" > 0`.

[Fact] GetOpenOrderLines_SinPedidos_NoConsulta()
// docEntries vacío → resultado vacío y LastSql == null.

[Fact] GetOpenOrderLines_PideCadaPedidoComoParametro()
// [10, 11] → LastSql contiene `IN (:docEntry0, :docEntry1)`; parámetros 10 y 11.
// El precio sale de `l."PriceBefDi" AS "UnitPrice"` (precio ANTES del descuento, igual que UnitPrice de Service Layer).
// La cantidad sale de `l."OpenQty" AS "OpenQuantity"`.

[Fact] GetOpenOrderLines_CalculaTipoDeLinea()
// fila precargada DocType "S" → LineType Service; "I" → Item.

[Fact] FindTaxFolio_BuscaFacturasNoCanceladasDelProveedor()
// LastSql contiene `FROM "OPCH"`, `o."CANCELED" = 'N'`, `o."FolioPref" = :folioPrefix`, `o."FolioNum" = :folioNumber`.
// Fila precargada con DocNum 10021509 → devuelve 10021509; sin filas → null.

[Fact] IndicatorCatalog_LeeOidc()
// LastSql contiene `FROM "OIDC"`; devuelve las filas precargadas.

[Fact] PartnerDefaults_TraeRutDelProveedor()
// LastSql contiene `"LicTradNum" AS "FederalTaxId"`.
```

- [ ] **Paso 2: Correr las pruebas y verificar que fallan**

Correr: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter FullyQualifiedName~Compras`

Esperado: FALLA de compilación.

- [ ] **Paso 3: Implementar**

`ListOpenOrdersAsync`:
- agrupa por cabecera;
- `DocTotal` toma `DocTotalFC` si es > 0, y si no, `DocTotal`;
- ordena por `o."DocDate" DESC, o."DocNum" DESC`.

`GetOpenOrderLinesAsync`:
- une `POR1` con `OPOR`, con los mismos filtros de abierto/pendiente;
- mapea `"Dscription"→Description`, `"WhsCode"`, `"AcctCode"`, `"OcrCode"`, `"OcrCode2"`, `"OcrCode3"`, `"DiscPrcnt"`, `"Currency"`, `o."DocCur"`, `o."SlpCode"`;
- ordena por `o."DocNum", l."VisOrder"`.

`FindTaxFolioAsync` usa `FirstOrDefault`, sin `LIMIT`.

Implementar las tres lecturas en el doble de `SapDocumentConnectorTests` con `throw`, si existe.

- [ ] **Paso 4: Correr las pruebas y verificar que pasan**

Correr el filtro del Paso 2 y luego la suite completa.

Esperado: PASA.

- [ ] **Paso 5: Commit**

`feat(compras): lecturas de pedidos abiertos, folio duplicado, RUT e indicadores para facturas`

---

### Task 3: Formulario base: tipo de línea, pestaña Finanzas y validaciones (Modulo.Compras)

**Archivos:**
- Crear:
  - `plugins/Modulo.Compras/Pages/PurchaseDocumentRules.cs`
  - `plugins/Modulo.Compras/Pages/Shared/_TabFinanzasCompras.cshtml`
- Modificar:
  - `DetailGenericPurchaseDocumentModelBase.cs`
  - `_TabGeneralCompras.cshtml`
  - `document-partner-defaults.js`
  - `PortalSaas.Core.Tests.csproj` (ProjectReference a `..\..\plugins\Modulo.Compras\Modulo.Compras.csproj`)
- Prueba: `tests/PortalSaas.Core.Tests/Compras/PurchaseDocumentRulesTests.cs`

**Interfaces:**
- Consume: `Describe`, `FindTaxFolioAsync`, `GetOpenOrderLinesAsync`, `IDocumentIndicatorCatalogService` y `BusinessPartnerDefaultsDto.FederalTaxId` (Tasks 1 y 2).
- Produce, todo `public static class PurchaseDocumentRules`:
  - `record RuleError(string Field, string Message)` y `record LineRuleError(int LineIndex, string Message)`
  - `record CopiedLineCheck(int LineIndex, int BaseEntry, int BaseLine, string? ItemCode, decimal Quantity)`
  - `IReadOnlyList<RuleError> ValidateTaxDocument(string? indicator, string? folioPrefix, int? folioNumber)`: `Field` es `nameof(InputModel.TaxIndicator)`, `FolioPrefix` o `FolioNumber`.
  - `IReadOnlyList<LineRuleError> ValidateCopiedLines(string supplierCardCode, DocumentLineType documentLineType, IReadOnlyList<CopiedLineCheck> lines, IReadOnlyList<PurchaseOpenOrderLineDto> openLines)`
  - `IReadOnlyList<int> LinesSubjectToPriceCheck(IReadOnlyList<int?> baseEntries)`: índices de las líneas **sin** Pedido base.
  - `string BasedOnComment(IReadOnlyList<int> orderDocNums)`: devuelve `"Basado en Pedidos 10005322."` o `"Basado en Pedidos 10005322, 10005323."`.
  - `string? CheckCopySelection(string supplierCardCode, IReadOnlyList<PurchaseOpenOrderLineDto> lines)`: null si es válida; si no, el mensaje.
- `InputModel` suma:
  - `TaxIndicator` (Display "Indicador")
  - `FolioPrefix` (Display "Prefijo de folio", `[StringLength(4)]`)
  - `FolioNumber` (int?, Display "N.° de folio")
  - `FederalTaxId` (Display "RUT")
- `LineInput` suma `int? BaseType, BaseEntry, BaseLine, BaseDocNum`.
- La base expone `bool HasTaxDocument`, `bool CanCopyFromOrders` (documento nuevo y `CopyFromType == PurchaseOrder`) y `List<SelectListItem> Indicators`.

- [ ] **Paso 1: Escribir las pruebas que fallan**

```csharp
[Fact] TaxDocument_Vacio_EsValido()                       // (null, null, null) → []
[Fact] TaxDocument_SoloFolio_ExigePrefijoEIndicador()     // (null, null, 1157501) → errores en FolioPrefix y TaxIndicator
[Fact] TaxDocument_SoloPrefijo_ExigeNumero()              // ("33", "43", null) → error en FolioNumber
[Fact] TaxDocument_Completo_EsValido()                    // ("33", "43", 1157501) → []

[Fact] CopiedLines_LineaYaNoAbierta_Error()               // check sin openLine que coincida → "ya no está abierta"
[Fact] CopiedLines_PedidoDeOtroProveedor_Error()          // openLine.CardCode "OTRO" → error de esa línea
[Fact] CopiedLines_TipoDeLineaDistinto_Error()            // openLine DocType "S" y documento Item → error
[Fact] CopiedLines_ArticuloDistinto_Error()               // ItemCode "A" contra openLine "B" → error
[Fact] CopiedLines_SuperaPendiente_Error()                // Quantity 13 contra OpenQuantity 12 → mensaje con "12"
[Fact] CopiedLines_MismaLineaBaseDosVeces_SumaCantidades() // 7 + 6 sobre la misma base, pendiente 12 → error en la segunda
[Fact] CopiedLines_ParcialDentroDelPendiente_Valida()     // 5 de 12 → []

[Fact] PriceCheck_ExcluyeLineasCopiadas()                 // [null, 10, null] → [0, 2]
[Fact] BasedOnComment_UnoYVarios()                        // [10005322] y [10005322, 10005323]
[Fact] CopySelection_MonedasDistintas_Error()             // líneas USD y CLP → mensaje no nulo
[Fact] CopySelection_TiposDistintos_Error()               // DocType "I" y "S" → mensaje no nulo
[Fact] CopySelection_OtroProveedor_Error()
[Fact] CopySelection_Valida_Null()
```

- [ ] **Paso 2: Correr las pruebas y verificar que fallan**

Correr: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter FullyQualifiedName~PurchaseDocumentRulesTests`

Esperado: FALLA de compilación.

- [ ] **Paso 3: Implementar `PurchaseDocumentRules`**

Todos los métodos son puros. La tolerancia de cantidad es `0.0001m`.

- [ ] **Paso 4: Correr las pruebas y verificar que pasan**

Correr el filtro del Paso 2. Esperado: PASA.

- [ ] **Paso 5: Cablear la base**

`DetailGenericPurchaseDocumentModelBase`, sin métodos `virtual` nuevos:
- **Documento nuevo:** `LineType = Describe(Type).DefaultLineType`.
- **Documento existente:** mapea los cuatro campos tributarios desde el DTO.
- **`LoadCatalogsAsync`:** carga `Indicators` (`"{Code} — {Name}"`) solo si `HasTaxDocument`. `IDocumentIndicatorCatalogService` se resuelve por `RequestServices`.
- **`BuildDocumentViewModel`:** si `HasTaxDocument`, fija `AccountingView = "~/Pages/Shared/_TabFinanzasCompras.cshtml"` y `AccountingTitle = "Finanzas"`; si no, quedan en null, como hoy.
- **`OnPostAsync`, después de las validaciones de líneas existentes:**
  1. `ValidateTaxDocument` → `ModelState` con la clave `Input.{Field}`.
  2. Con prefijo y número presentes, `FindTaxFolioAsync`: si hay coincidencia, error en `Input.FolioNumber`: "Ya existe en SAP la factura N.° {docNum} de este proveedor con el folio {prefijo}-{número}.".
  3. Las líneas con `BaseEntry` pasan por `GetOpenOrderLinesAsync` (BaseEntry distintos) y `ValidateCopiedLines`, con error en `Input.Lines[i].Quantity`.
- **`AddLineRestrictionErrorsAsync`:** valida solo los índices de `LinesSubjectToPriceCheck`, y las claves de error conservan el índice original.
- **Mapeo al DTO:** los cuatro campos tributarios y `BaseType/BaseEntry/BaseLine`, este último solo si `BaseEntry` tiene valor.
- **`OnGetPartnerDefaultsAsync`:** devuelve también `FederalTaxId`.
- **`_TabFinanzasCompras.cshtml`:** copia la estructura de `_TabFinanzasVentas.cshtml`. Un `doc-tab-seccion` con `form-row`: Indicador (`select`, opción vacía "Sin indicador"), Prefijo, N.° de folio y RUT. Todos `disabled="@readOnly"`, con `asp-validation-for`, sin `data-form-field` (no se pueden ocultar).
- **`_TabGeneralCompras.cshtml`:** pasa `taxIdInputId: 'Input_FederalTaxId'` a `wirePartnerDefaults`.
- **`document-partner-defaults.js`:** si existe `options.taxIdInputId` y llega `defaults.federalTaxId`, escribe el valor. Sobrescribe, porque el RUT es del proveedor.

- [ ] **Paso 6: Compilar y correr la suite**

Correr:
- `dotnet build "Portal SaaS - Core/plugins/Modulo.Compras/Modulo.Compras.csproj"`
- `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests"`

Esperado: compila sin errores y la suite pasa, incluidas `VistasResponsivasTests` y `UrlsRelativasAlPathBaseTests`.

- [ ] **Paso 7: Commit**

`feat(compras): documento tributario, tipo de línea por defecto y validaciones de factura en el formulario base`

---

### Task 4: Copiar de Pedido (Host JS + base de Compras)

**Archivos:**
- Crear: `src/PortalSaas.Host/wwwroot/js/document-copy-from.js`.
- Modificar: `document-lines-editor.js`, `_TabContentCompras.cshtml`, `DetailGenericPurchaseDocumentModelBase.cs`.
- Prueba: la suite existente (sin JS testeable) y la verificación manual del Task 6.

**Interfaces:**
- Consume:
  - `ListOpenOrdersAsync`, `GetOpenOrderLinesAsync` (Task 2)
  - `CheckCopySelection`, `BasedOnComment` (Task 3)
  - `LineInput.Base*` (Task 3)
- Produce:
  - `OnGetOpenOrdersAsync(string cardCode)` → JSON con la lista de `PurchaseOpenOrderDto`.
  - `OnGetOpenOrderLinesAsync(string cardCode, int[] docEntries)` → JSON `{ error, lineType, docCurrency, buyerCode, comment, lines[] }`.
  - Los dos exigen el permiso Crear del menú y `CanCopyFromOrders`; si no, `Forbid()`.
  - `wireDocumentLinesEditor(options)` ahora **devuelve** `{ appendRow(): HTMLTableRowElement, recalculate(): void }`.
  - `window.wireDocumentCopyFrom(options)`, con las opciones:
    - `buttonId, drawerId, partnerInputSelector, linesBodyId, editor`
    - `lineTypeSelectId, itemLineTypeValue, serviceLineTypeValue`
    - `currencySelectId, buyerSelectId, commentsId, statusId`
    - `fillRow(row, line)`

- [ ] **Paso 1: Handlers en la base**

`OnGetOpenOrderLinesAsync`:
- lee las líneas;
- si `CheckCopySelection` devuelve un mensaje, responde `{ error }`;
- si no, responde:
  - `lineType` ("Item"/"Service");
  - `docCurrency`;
  - `buyerCode` (`null` si es -1 o 0);
  - `comment` = `BasedOnComment(DocNums distintos)`;
  - `lines`: cada una con sus campos y `baseType: 22`, `baseEntry`, `baseLine`, `baseDocNum`.

- [ ] **Paso 2: API de filas en `document-lines-editor.js`**

`appendRow()`:
- crea la fila con `crearFilaDesdePlantilla()`, la agrega al cuerpo y recalcula;
- si la única fila existente está vacía (sin artículo ni descripción), la reemplaza.

El editor devuelve `{ appendRow, recalculate: recalcularTotales }`. Ventas e Inventario ignoran el valor devuelto, así que no cambian.

- [ ] **Paso 3: `document-copy-from.js`**

1. El botón se habilita solo con un proveedor elegido. Al abrir, llama a `?handler=OpenOrders&cardCode=` y pinta una tabla con checkbox: N.° SAP, fecha, N.° ref., moneda, total y líneas pendientes.
2. "Copiar" llama a `?handler=OpenOrderLines&cardCode=…&docEntries=…`. Si hay `error`, lo muestra en el drawer.
3. **Tipo de línea:** si el formulario ya tiene líneas con contenido de otro tipo, avisa en `statusId` y no copia.
4. **Cabecera:**
   - fija el `select` de tipo de línea y dispara `change`;
   - fija la moneda y dispara `change` (actualiza la etiqueta del Resumen total);
   - fija el comprador solo si está vacío;
   - agrega `comment` a Comentarios si aún no lo contiene.
5. **Filas:** por cada línea que no esté ya presente (`baseEntry` + `baseLine`), `editor.appendRow()` y luego `fillRow(row, line)`.
6. **Al cambiar de proveedor**, quita las filas con `BaseEntry` y lo informa en `statusId`.

- [ ] **Paso 4: Vista `_TabContentCompras.cshtml`**

En **cada fila renderizada en el servidor y en el `<template>`**:
- ocultos `Input.Lines[i].BaseType/BaseEntry/BaseLine/BaseDocNum`;
- si `BaseEntry` tiene valor, el input de artículo queda `readonly` y bajo él aparece `<span class="badge bg-secondary">Pedido N.° {BaseDocNum}</span>`.

Si `Model.CanCopyFromOrders`:
- botón `+ Copiar de Pedido` (`btn btn-sm btn-outline-secondary`), primero en `admin-row-actions`;
- drawer estándar `.drawer-overlay` + `.drawer-panel.drawer-panel--wide` con encabezado "Copiar de Pedido", cuerpo con `table-responsive-lg` y `table-ps`, y pie con Cancelar (`btn btn-secondary`) y Copiar (`btn btn-primary`), copiado del drawer de `Modulo.Administracion/Pages/GruposMenu/Index.cshtml`;
- `<script src="~/js/document-copy-from.js">` y `wireDocumentCopyFrom({...})`, con `editor` = el valor que devuelve `wireDocumentLinesEditor`;
- `fillRow` escribe en los `name$=".ItemCode"`, `.Description`, `.Quantity`, `.UnitPrice`, `.Currency`, `.DiscountPercent`, `.WarehouseCode`, `.AccountCode`, `.CostCenterCode`, `.CostCenterCode2`, `.CostCenterCode3` y en los cuatro ocultos `Base*`, y marca el artículo readonly más el distintivo.

- [ ] **Paso 5: Compilar y correr la suite**

Igual que en el Task 3, Paso 6. Esperado: compila y pasa.

- [ ] **Paso 6: Commit**

`feat(compras): copiar de Pedido en las facturas de proveedor`

---

### Task 5: Páginas, menú, listado e Importación Genérica

**Archivos:**
- Crear:
  - `plugins/Modulo.Compras/Pages/PurchaseInvoices/{Index,Detail}.cshtml(.cs)`
  - `plugins/Modulo.Compras/Pages/PurchaseReserveInvoices/{Index,Detail}.cshtml(.cs)`
- Modificar: `ModuloCompras.cs`, `IndexGenericPurchaseDocumentModelBase.cs`, `ImportDisplayNames.cs`.
- Prueba: `tests/PortalSaas.Core.Tests/Compras/ModuloComprasMenuTests.cs`.

**Interfaces:**
- Consume: `Describe(...).HasTaxDocument` y `PurchaseDocumentFilter.Folio`, `PurchaseDocumentSummaryDto.FolioPrefix/FolioNumber` (Task 1).

- [ ] **Paso 1: Escribir la prueba que falla**

```csharp
[Fact] Menu_FacturasConIconoYRuta()
// GetMenu() contiene Code "facturas" (PageRoute "/compras/facturas-proveedor", Icon "bi bi-receipt")
// y "facturasreserva" ("/compras/facturas-reserva", "bi bi-receipt-cutoff"); todo ítem tiene Icon no vacío.
```

- [ ] **Paso 2: Correr y verificar que falla**

Correr con `--filter FullyQualifiedName~ModuloComprasMenuTests`. Esperado: FALLA (los códigos no existen).

- [ ] **Paso 3: Implementar**

- **Subclases:** copias de las de Pedidos, de unas 20 líneas, con estos valores:
  - `Type`: `PurchaseInvoice` / `PurchaseReserveInvoice`
  - `MenuCode`: `"Compras.facturas"` / `"Compras.facturasreserva"`
  - `DocumentName`: `"Factura de Proveedor"` / `"Factura Reserva de Proveedor"`
  - `DocumentNamePlural`: `"Facturas de Proveedor"` / `"Facturas Reserva de Proveedor"`
  - `RouteBase` y `@page`: las rutas de las Restricciones globales
- **Menú:** `Order` 3 y 4.
- **Listado:** si `HasTaxDocument`, agrega la columna "Folio" después de "N° ref. proveedor" (`"{prefijo}-{número}"` o "-") y el filtro "Folio" (texto), y pasa `Folio` al `PurchaseDocumentFilter`.
- **`ImportDisplayNames`:** `("Factura de proveedor", "Purchase invoice")` y `("Factura de reserva de proveedor", "Purchase reserve invoice")`.

- [ ] **Paso 4: Correr la prueba y la suite, y compilar todo**

Correr:
- la suite de Core.Tests;
- `dotnet build` de Modulo.Compras y Modulo.ImportacionGenerica;
- `build-all.ps1` desde la raíz.

Esperado: todo en verde y sin advertencias nuevas de colisión de rutas en el log del PluginManager al arrancar el Host.

- [ ] **Paso 5: Commit**

`feat(compras): páginas y menú de facturas de proveedor y de reserva`

---

### Task 6: Documentación y verificación contra SAP QA

**Archivos:**
- Modificar: `Portal SaaS - Core/CLAUDE.md` y `Portal SaaS - Core/docs/08-BRECHA-FUNCIONAL-VS-PORTALSAP-V2.md`.

- [ ] **Paso 1: Documentación**

- **CLAUDE.md:** Compras pasa a 4 tipos; Compras tiene pestaña Finanzas en los tipos con documento tributario; "Copiar de" existe en Compras.
- **docs/08:** Compras con facturas; brecha "Copiar de" en Ventas (Factura de reserva desde Orden de venta) e Inventario (Traslado desde Solicitud).

- [ ] **Paso 2: Verificación con el usuario contra SAP QA**

Requiere el Host local contra la compañía QA. **Se pide al usuario antes de levantarlo.** Todo con "Validar", que simula y no crea nada.
1. **Factura de Proveedor** de servicio con documento tributario (Indicador, 43, folio de prueba, RUT). Esperado: "Validación OK".
2. **Factura Reserva de Proveedor**, copiando de un Pedido abierto real, con cantidad parcial. Esperado: "Validación OK".
3. **Mismo folio que una factura existente.** Esperado: error de duplicado.
4. **Factura de reserva de Ventas** (`/ventas/facturas-reserva`). Si SAP rechaza `isIns`, se ejecuta el Task 7.

Si 1 o 2 fallan por el nombre de una propiedad, se corrige el catálogo o el modelo SAP con una prueba que falle primero, y se deja constancia en el ledger.

- [ ] **Paso 3: Commit**

`docs(compras): facturas de proveedor en CLAUDE.md y brechas`

---

### Task 7 (solo si el Task 6, punto 4, muestra el rechazo): Factura de reserva de Ventas

**Archivos:**
- Modificar: `SalesDocumentTypeCatalog.cs`, `SalesDocumentService.cs`.
- Prueba: `tests/PortalSaas.Core.Tests/Ventas/SalesDocumentServiceCreateTests.cs`.

- [ ] **Paso 1: Escribir la prueba que falla**

```csharp
[Fact] Create_FacturaReservaVentas_EnviaReserveInvoiceTYesSinIsIns()
```

Usa los mismos dobles del Task 1.

- [ ] **Paso 2: Verificar que falla**

Esperado: el cuerpo trae `"isIns": "Y"`.

- [ ] **Paso 3: Implementar**

Agregar `CreateFields` al `Entry` de Ventas, con el mismo patrón de Compras. `ReserveInvoice` envía `ReserveInvoice = tYES`, y `BuildRequestBody` deja de copiar `isIns` al POST para esa entrada.

- [ ] **Paso 4: Verificar**

Corre la suite, y "Validar" en QA responde "Validación OK".

- [ ] **Paso 5: Commit**

`fix(ventas): la factura de reserva se marca con ReserveInvoice, no con isIns`
