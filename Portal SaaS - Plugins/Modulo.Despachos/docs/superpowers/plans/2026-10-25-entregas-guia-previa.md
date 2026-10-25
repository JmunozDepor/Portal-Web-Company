# Entregas con guía previa Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agrupar por OC las Entregas SAP (ODLN) de un cliente que recibe solo con guía y, al despachar el manifiesto,
emitir una guía 52 valorizada por OC (detalle por producto, referencia 801, transporte del manifiesto). El folio de
la guía se escribe en la Entrega.

**Architecture:**
- La tabla `despachos_dispatch_groups` gana `kind` (`transfer` | `delivery`) y los datos de la entrega. Las
  agrupaciones de entregas comparten correlativo, estados, hoja PDF y escaneo con las AT-; su código es `EG-`.
- Un servicio nuevo `DeliveryGroupService` arma, edita y anula las agrupaciones EG-.
- `ManifestService` las agrega como documentos `delivery`. `GuidePlanner` y `TransportGuideService` las planifican,
  emiten y escriben el folio en SAP (Service Layer `DeliveryNotes`).

**Tech Stack:** .NET 8 Razor Pages (plugin), EF Core 8 con dos motores (Postgres y SQL Server), xUnit InMemory,
HANA vía consultas editables (`sap_document_queries`), Service Layer.

**Spec:** `Portal SaaS - Plugins/Modulo.Despachos/docs/superpowers/specs/2026-10-25-modulo-despachos-entregas-guia-previa-design.md`

## Global Constraints

- **Rutas:** módulo en `Portal SaaS - Plugins/Modulo.Despachos/src/Modulo.Despachos`, tests en
  `.../src/Modulo.Despachos.Tests`. Comando de tests (desde `Portal SaaS - Plugins/Modulo.Despachos`):
  `dotnet test src/Modulo.Despachos.Tests/Modulo.Despachos.Tests.csproj --filter <filtro>`.
- **Código visible:** `EG-` + correlativo de 6 dígitos (`EG-000123`). El correlativo es el mismo `number` de las
  AT- (índice único `company_id, number` ya existente), así que AT- y EG- nunca repiten número.
- **Defaults al agrupar:** indicador de traslado **2**, tipo de despacho **2**. Rangos válidos: indicador 1–9, tipo 1–3.
- **Una guía = una OC.** OC = `ORDR.NumAtCard` del pedido base (`DLN1.BaseType = 17`). El `NumAtCard` de la Entrega
  es el LPN.
- **No agrupables:** Entrega sin OC, con más de una OC (`po_count > 1`) o con OC no numérica (el folio de referencia
  de la plataforma es `long?`). Se muestran con el motivo y sin checkbox.
- **Guía de entrega siempre valorizada**, sin importar `DespachosSetting.GuideValued`. Se emite aunque
  `GuidesEnabled = false`.
- **Referencia:** `DteReference(1, "801", <OC>, <fecha del pedido>, null, "Orden de compra")`, una por guía. Si se
  divide en varias guías, cada una lleva la misma referencia.
- **Folio en SAP:** `DeliveryNotes(DocEntry)` con `FolioNumber` = folio y `FolioPrefixString` =
  `DespachosSetting.TransferFolioPrefix` (si está vacío, solo `FolioNumber`). Los campos de usuario de
  transferencias no se escriben en la Entrega.
- **Menú:** `entregas-guia-previa`, padre `grupo-operacion`, nombre "Entregas con guía previa", icono
  `bi-box-arrow-right`, ruta `/despachos/entregas-guia-previa`, orden 2 (Manifiestos pasa a 3, Guías 4, Retorno 5,
  Reporte de documentos 6). Acciones View, Create (agrupar), Edit (agregar, quitar, condiciones), Delete (anular).
- **Mensajes fijos:**
  - LPN de Entrega no agrupada: `"La Entrega {DocNum} (LPN {lpn}) no está en una agrupación con guía previa; se despacha con su factura."`;
  - LPN de Entrega agrupada: `"El LPN {lpn} pertenece a la agrupación {EG-…}; escanee la hoja."`.
- **Reglas del repo:**
  - página hermana de AgruparTraslados (copiar estructura y clases);
  - URLs internas con `~/`;
  - responsive a 375 px;
  - icono en todo menú;
  - campana vía `TryEnqueueAsync`;
  - `InvariantCulture` en inputs con value interpolado;
  - comentarios de los seeds SQL sin `:`.
- **Migración** `EntregasGuiaPrevia` en ambos proyectos de migraciones, generada con el mismo comando de
  `scratchpad/desp-ef.sh`. Se aplica en `ps_comdepor_dsp_qa` y `ps_comdepor_dsp` con `efdb.py` y se verifica.
- Ninguna credencial en código, logs ni chat.

## Review Focus

1. **DocEntry repetido entre OWTR y ODLN** (transferencia 501 y Entrega 501): la ocupación de transferencias no
   debe contar agrupaciones de entregas, y viceversa. Lo mismo en el manifiesto (`FindActiveElsewhereAsync`,
   `ActiveGroupNumberOfAsync`). Test en Task 3 y Task 4.
2. **Manifiesto solo con Entregas y `GuidesEnabled = false`:** igual emite la guía valorizada al despachar. Nunca
   despacha sin guía. Test en Task 5.
3. **Entrega facturada o cerrada entre agrupar y escanear la hoja:** no entra, se informa y el resto de la hoja
   entra. Test en Task 4.
4. **OC con más líneas que el máximo de la guía:** se divide en varias guías y cada una lleva la referencia 801.
   Test en Task 5.
5. **LPN suelto:**
   - de una Entrega no agrupada → mensaje fijo de rechazo;
   - sin la consulta `delivery_by_lpn` configurada → se mantiene el "No se encontró el LPN" de siempre.

   Test en Task 4.

**Brecha conocida (fuera de este plan):** el anti-objetivo "Entrega en una guía y además con su factura en otro
manifiesto" no se controla. La factura no expone sus Entregas base en el contrato actual. Queda en el ledger como
ruling.

---

### Task 1: Modelo, código EG- y migración

**Files:**
- Modify: `Models/DispatchGroup.cs`, `Models/ManifestConstants.cs`, `Models/Constantes.cs`,
  `Dominio/DispatchGroupCode.cs`, `Data/DespachosDbContext.cs`
- Create: migraciones `EntregasGuiaPrevia` en `Modulo.Despachos.Migrations.Postgres` y `.SqlServer`
- Test: `Modulo.Despachos.Tests/Dominio/DispatchGroupCodeTests.cs`,
  `Modulo.Despachos.Tests/Servicios/DeliveryGroupModelTests.cs`

**Interfaces:**
- Produces:
  - `DispatchGroupKinds { Transfer = "transfer", Delivery = "delivery" }` en `ManifestConstants.cs`.
  - `DispatchGroup`:
    - `Kind` (string, default Transfer, max 10);
    - `CustomerTaxId` (string?, 20);
    - `CustomerPo` (long?);
    - `CustomerPoDate` (DateOnly?);
    - `TransferIndicator` (int?);
    - `DispatchType` (int?).

    Para entregas, `WarehouseCode` = CardCode y `BranchName` = nombre del cliente: son el "destino" de la
    agrupación y reutilizan lista y filtros. `WarehouseCode` sube a max 50 (CardCode).
  - `DocumentKinds.Delivery = "delivery"`, que no está en `All`. Label "Entrega", PluralLabel "entregas".
  - `DispatchGroupCode`:
    - `Format(int number, string kind = DispatchGroupKinds.Transfer)` → "AT-…" o "EG-…";
    - `TryParse(string? raw, out int number, out string kind)` acepta AT- y EG-;
    - el `TryParse(raw, out number)` existente sigue aceptando solo AT-.

- [ ] **Step 1: Write the failing tests**
  - `DispatchGroupCodeTests.Format_Entrega` → `Format(123, "delivery") == "EG-000123"`.
  - `DispatchGroupCodeTests.TryParse_ConTipo` (Theory):
    - `("EG-000123", 123, "delivery")`;
    - `("at-7", 7, "transfer")`;
    - `TryParse("EG-", out _, out _)` → false.
  - `DeliveryGroupModelTests.AgrupacionEntrega_SeGuarda`: InMemory. Guarda un `DispatchGroup` con
    `Kind = delivery`, `WarehouseCode = "C76030680"`, `CustomerPo = 4500123456`, `TransferIndicator = 2` y
    `DispatchType = 2`. Al recargar los valores son iguales.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~DispatchGroupCode|FullyQualifiedName~DeliveryGroupModel"` →
  compile FAIL.
- [ ] **Step 3: Implement** constantes, propiedades, mapeo y `DispatchGroupCode`.
  - Columnas `kind` (default `'transfer'`, NOT NULL), `customer_tax_id`, `customer_po`, `customer_po_date`,
    `transfer_indicator`, `dispatch_type`.
  - `HasDefaultValue("transfer")` para que las filas existentes queden como traslados.
- [ ] **Step 4: Run full module suite** → PASS.
- [ ] **Step 5: Generate migrations** `EntregasGuiaPrevia` (ambos motores). Revisar:
  - que `kind` traiga el default `'transfer'`;
  - que `warehouse_code` pase a 50.
- [ ] **Step 6: Commit** `feat(despachos): modelo de agrupaciones de entregas (kind, OC, condiciones de la guía) y código EG-`.

### Task 2: Consultas SAP de Entregas

**Files:**
- Create:
  - `Sql/Seeds/hana_delivery_pending.sql`, `hana_delivery_by_lpn.sql`, `hana_delivery_lines.sql`
    (`EmbeddedResource` como los demás);
  - `Modulo.Despachos.Tests/Servicios/SapDeliveryQueriesTests.cs`.
- Modify:
  - `Models/Constantes.cs` (`SapQueryKinds`);
  - `Servicios/SapQuerySeeds.cs`;
  - `Servicios/ISapDocumentLookupService.cs`, `Servicios/SapDocumentLookupService.cs`;
  - `Modulo.Despachos.Tests/Fakes/FakeSapDocumentLookupService.cs`;
  - `Pages/Despachos/Configuracion/ConsultasSap/*`: etiqueta de muestra y tabla de prueba para Entregas, copiando
    el bloque de transferencias.

**Interfaces:**
- Produces:
  - `SapQueryKinds.DeliveryPending = "delivery_pending"`, `DeliveryByLpn = "delivery_by_lpn"`,
    `DeliveryLines = "delivery_lines"`.
    - Se agregan a `All`.
    - Labels: "Entregas pendientes del cliente", "Entrega por LPN", "Líneas de Entrega".
  - `public sealed record SapDeliveryInfo(int DocEntry, int DocNum, DateOnly DocumentDate, string CustomerCode, string CustomerName, string? CustomerTaxId, string? DestinationAddress, string? DestinationCommune, string? Lpn, string? CustomerPo, DateOnly? OrderDate, int PoCount, decimal Units, decimal NetAmount, bool IsOpen);`
  - `SapDocumentLookupService.DeliveryColumns`:
    `doc_entry, doc_num, document_date, customer_code, customer_name, customer_tax_id, destination_address, destination_commune, lpn, customer_po, order_date, po_count, units, net_amount, is_open`.
  - `ISapDocumentLookupService`:
    - `Task<IReadOnlyList<SapDeliveryInfo>> ListPendingDeliveriesAsync(string customerCode, DateOnly fromDate, CancellationToken ct = default)`
      (`:customer`, `:from_date`; distinct por DocEntry);
    - `Task<SapDeliveryInfo?> FindDeliveryByLpnAsync(string lpn, CancellationToken ct = default)` (`:lpn`; varias
      Entregas con el mismo LPN → prefiere la única abierta; si no, lanza "El LPN {lpn} está en más de una Entrega
      en SAP; revise el LPN.");
    - `GetLinesAsync` acepta además `SapQueryKinds.DeliveryLines`, con el mismo contrato `LineColumns`;
    - `SapQueryProbeResult` gana `IReadOnlyList<SapDeliveryInfo>? Deliveries = null`.
  - **Fake:**
    - `List<SapDeliveryInfo> Deliveries`;
    - `ListPendingDeliveriesAsync` filtra por `CustomerCode`, `DocumentDate >= fromDate` e `IsOpen`;
    - `FindDeliveryByLpnAsync` busca por LPN sin importar mayúsculas;
    - `bool DeliveryByLpnMissing`: cuando es true, `FindDeliveryByLpnAsync` lanza "Configure la consulta SAP «Entrega
      por LPN»…".
- **Seeds HANA:**
  - Cabecera de Entregas sobre ODLN, DLN12 y OCRD:
    - lpn = `T0."NumAtCard"`;
    - customer_po y order_date = `MAX(O."NumAtCard")` y `MAX(O."DocDate")` de las líneas DLN1 con `BaseType = 17`
      unidas a ORDR;
    - po_count = `COUNT(DISTINCT COALESCE(O."NumAtCard", ''))` sobre todas las líneas (LEFT JOIN a ORDR);
    - net_amount = `T0."DocTotal" - T0."VatSum"`;
    - is_open = `DocStatus 'O'` y `CANCELED 'N'`.
  - Filtros por consulta:
    - pending: `CardCode = :customer`, `DocDate >= :from_date`, abiertas;
    - by_lpn: `NumAtCard = :lpn`, sin filtro de estado.
  - lines: copia de `hana_invoice_lines.sql` sobre DLN1, ODLN y DLN12.

- [ ] **Step 1: Write the failing tests** (estilo `SapTransferQueriesTests` / `SapBusinessPartnerTests`, con el
  fake de HANA que ya usan):
  - `Pendientes_MapeaContrato`: una fila HANA con todas las columnas → `SapDeliveryInfo` con
    `CustomerPo == "4500123456"`, `PoCount == 1`, `IsOpen == true`.
  - `PorLpn_DosEntregas_PrefiereLaAbierta`.
  - `PorLpn_DosAbiertas_Lanza`.
  - `Lineas_DeliveryLines_UsaContratoDeLineas`.
  - `Seeds_EntregasSinParametrosEnComentarios`: los tres seeds, sin `:` en las líneas `--`, y con `:customer`,
    `:lpn` y `:doc_entry` respectivamente.
  - `Probar_Pendientes_DevuelveEntregas`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~SapDeliveryQueries` → FAIL.
- [ ] **Step 3: Implement.** Parámetros permitidos:
  - pending: `("customer", ["customer","from_date"])`;
  - by_lpn: `("lpn", ["lpn"])`;
  - lines: `LinesParameter`.

  Muestra de "Probar":
  - pending: `sampleText` = código de cliente, últimos 30 días;
  - by_lpn: `sampleText` = LPN.
- [ ] **Step 4: Run full module suite** → PASS. Ajustar los tests que cuentan `SapQueryKinds.All` (9 → 12) y el
  conteo de seeds en BD.
- [ ] **Step 5: Commit** `feat(despachos): consultas SAP de Entregas (pendientes por cliente, por LPN y líneas) con OC del pedido`.

### Task 3: `DeliveryGroupService` — agrupar, editar, anular

**Files:**
- Create:
  - `Servicios/IDeliveryGroupService.cs`, `Servicios/DeliveryGroupService.cs`;
  - `Modulo.Despachos.Tests/Servicios/DeliveryGroupServiceTests.cs`.
- Modify:
  - `Servicios/DispatchGroupService.cs`: la ocupación y `ListAsync` filtran por `Kind`;
  - `Servicios/IDispatchGroupService.cs`: `DispatchGroupFilter` gana `string Kind = DispatchGroupKinds.Transfer`;
  - `Servicios/DispatchGroupPdfService.cs`: código por kind; en la hoja EG-, cliente, RUT y OC en lugar de la
    sucursal;
  - `ModuloDespachos.cs` (DI);
  - `ManifestTestEnv.cs` (agregar `Deliveries`).

**Interfaces:**
- Consumes: Task 1 (`DispatchGroupKinds`, propiedades nuevas, `DispatchGroupCode.Format(n, kind)`), Task 2
  (`SapDeliveryInfo`, `ListPendingDeliveriesAsync`, `FindDeliveryByLpnAsync`, `FindBusinessPartnerAsync`).
- Produces:
  ```csharp
  public sealed record BlockedDelivery(SapDeliveryInfo Delivery, string Reason);
  public sealed record PoCandidates(long CustomerPo, DateOnly? OrderDate, IReadOnlyList<SapDeliveryInfo> Deliveries);
  public sealed record DeliveryCandidates(string CustomerCode, string CustomerName, string? CustomerTaxId,
      IReadOnlyList<PoCandidates> Orders, IReadOnlyList<BlockedDelivery> Blocked);
  public sealed record DeliveryGroupInput(string Customer, DateOnly FromDate, DateOnly DispatchDate,
      int TransferIndicator, int DispatchType, IReadOnlyCollection<int> DocEntries);
  public interface IDeliveryGroupService
  {
      Task<DeliveryCandidates> ListCandidatesAsync(string customer, DateOnly fromDate);
      Task<IReadOnlyList<DispatchGroup>> GenerateAsync(DeliveryGroupInput input);   // una agrupación por OC
      Task<string> AddLpnAsync(long groupId, string lpn);
      Task RemoveDeliveryAsync(long groupId, long lineId);
      Task SetTermsAsync(long groupId, int transferIndicator, int dispatchType);
      Task AnnulAsync(long groupId, string reason);
  }
  ```
  - `GetAsync` y `ListAsync` siguen en `IDispatchGroupService`; la lista de entregas usa
    `Kind = delivery`.
  - Las líneas reutilizan `DispatchGroupTransfer`.
- **Reglas:**
  - `customer`: si es un RUT válido → `FindBusinessPartnerAsync` → CardCode (no existe → "No se encontró el
    cliente {rut} en SAP."); si no, se usa como CardCode.
  - Motivos de bloqueo, en este orden:
    - sin OC: `"La Entrega no tiene orden de compra (pedido sin N° de referencia)."`;
    - `PoCount > 1`: `"La Entrega tiene líneas de más de una OC; sepárela en SAP."`;
    - OC no numérica: `"La OC «{po}» no es numérica."`.
  - Ocupadas se excluyen. Una Entrega está ocupada cuando:
    - está en una agrupación `delivery` en `Holding`; o
    - está en un documento de manifiesto `delivery` no anulado y no "no entregado".
  - `GenerateAsync`:
    - valida indicador 1–9 ("Indicador de traslado inválido.") y tipo 1–3;
    - la selección vacía o sin Entregas libres lanza "No hay Entregas libres para agrupar con la selección indicada.";
    - `Sequence` se cuenta por (CardCode, fecha de despacho) entre las de kind delivery;
    - copia `CustomerTaxId`, `CustomerPo` y `CustomerPoDate` (fecha del pedido).
  - `AddLpnAsync` exige:
    - agrupación armada;
    - Entrega abierta;
    - mismo cliente ("El LPN {lpn} es de otro cliente.");
    - misma OC ("El LPN {lpn} es de la OC {x}, no de la OC {y} de esta agrupación.");
    - que no esté bloqueada ni ocupada.
  - `SetTermsAsync` solo funciona con la agrupación armada.
  - El orden de los estados reutiliza el `RequireArmedAsync` de traslados, con mensajes que usan el código EG-.

- [ ] **Step 1: Write the failing tests** (`DeliveryGroupServiceTests`). Datos:
  - cliente `C001` ("Falabella", `76.123.456-0`);
  - Entregas 701 y 702 (OC 4500000001), 703 (OC 4500000002), 704 (po_count 2) y 705 (OC "ABC").
  - Tests:
    - `Candidatos_AgrupaPorOc_YBloqueaLasNoAgrupables` → Orders `[4500000001: 701,702]`,
      `[4500000002: 703]`; Blocked 704 y 705 con sus motivos.
    - `Candidatos_PorRut_ResuelveCardCode`.
    - `Generar_UnaAgrupacionPorOc_ConCondiciones` → 2 grupos `delivery` con `TransferIndicator 2` y
      `DispatchType 2`. Sus códigos empiezan con "EG-".
    - `Generar_IndicadorFueraDeRango_Lanza` (0 y 10).
    - `Candidatos_ExcluyeOcupadas` (701 en una agrupación).
    - `OcupacionDeTraslados_NoCuentaEntregasConMismoDocEntry`: transferencia 701 a una sucursal y Entrega 701
      agrupada → la transferencia sigue candidata en `IDispatchGroupService.ListCandidatesAsync`. Es el Review
      Focus 1.
    - `AgregarLpn_OtraOc_Lanza`, `AgregarLpn_MismaOc_Agrega`.
    - `CambiarCondiciones_EnManifiesto_Lanza`.
    - `Anular_LiberaEntregas`.
    - `ListaTraslados_NoMuestraEntregas`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~DeliveryGroupService` → FAIL.
- [ ] **Step 3: Implement** el servicio y los filtros por kind en `DispatchGroupService`. En el PDF, el título es
  "Entregas con guía previa" y el detalle va por LPN, Entrega, fecha y unidades.
- [ ] **Step 4: Run full module suite** → PASS (incluye `DispatchGroupPdfServiceTests`; agregar
  `Hoja_Entrega_MuestraOcYCodigoEG`).
- [ ] **Step 5: Commit** `feat(despachos): agrupar Entregas por OC con indicador y tipo de despacho (EG-)`.

### Task 4: Entregas en el manifiesto

**Files:**
- Modify: `Servicios/ManifestService.cs`
- Test: `Modulo.Despachos.Tests/Servicios/ManifestDeliveryGroupTests.cs`

**Interfaces:**
- Consumes: Task 1–3.
- Produces:
  - `AddDocumentOutcomes`: se reutilizan `GroupAdded`, `InDispatchGroup` y `NotFound`.
  - Agrega un nuevo outcome `NotInDeliveryGroup = "not_in_delivery_group"`.
  - Documento de manifiesto de entrega:
    - `DocumentKind = delivery`, `SiiDocumentType = 0`, `Folio = DocNum`, `SapDocEntry`, `Lpn`;
    - cliente: `CustomerCode`, `CustomerName`, `CustomerTaxId`;
    - destino: `DestinationAddress` y `DestinationCommuneName` de la Entrega (comuna resuelta como en facturas, con
      el mismo aviso);
    - `CustomerPo = OC`, `TotalAmount = NetAmount`, `Units`, `Packages = 1`, `DispatchGroupId`.
- **Reglas:**
  - El escaneo de la hoja usa `DispatchGroupCode.TryParse(raw, out n, out kind)` y busca por número y kind.
  - Hoja EG-:
    - revalida con `ListPendingDeliveriesAsync(group.WarehouseCode, min DocumentDate)`;
    - una línea que ya no está (facturada o cerrada) agrega la nota "La Entrega {DocNum} ya no está abierta en SAP." y
      el resto entra;
    - el mensaje final es `"{EG-…}: {n} entregas agregadas."`.
  - LPN escaneado:
    - si no es transferencia, se busca como Entrega;
    - Entrega en una agrupación `delivery` vigente → `InDispatchGroup` con el mensaje fijo;
    - Entrega no agrupada → `NotInDeliveryGroup` con el mensaje fijo;
    - `FindDeliveryByLpnAsync` lanza `InvalidOperationException` → "No se encontró el LPN {lpn} en SAP." (la
      compañía no usa Entregas).
  - Control "en otro manifiesto" por DocEntry también para `delivery`: `FindActiveElsewhereAsync`, que usa
    `byEntry` para Transfer o Delivery.
  - `ActiveGroupNumberOfAsync` filtra por kind.
  - Quitar la hoja, quitar todos y anular el manifiesto sincronizan las agrupaciones EG- como las AT-, porque el
    mecanismo existente es por `DispatchGroupId`.

- [ ] **Step 1: Write the failing tests**:
  - `HojaEG_AgregaSusEntregas_ConClienteYOc`.
  - `HojaEG_EntregaFacturadaEntretanto_SeInformaYElRestoEntra` (Review Focus 3).
  - `LpnEntregaAgrupada_PideLaHoja`.
  - `LpnEntregaSuelta_SeRechaza`, que exige el mensaje fijo.
  - `LpnSinConsultaDeEntregas_NoEncontrado` (Review Focus 5).
  - `EntregaEnOtroManifiesto_SeRechaza`.
  - `TransferenciaYEntregaConMismoDocEntry_NoChocan` (Review Focus 1).
  - `QuitarHojaEG_AgrupacionVuelveAArmada`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~ManifestDeliveryGroup` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run full module suite** → PASS.
- [ ] **Step 5: Commit** `feat(despachos): hojas EG- y LPN de Entregas en el manifiesto`.

### Task 5: Guía valorizada por OC al despachar y folio en la Entrega

**Files:**
- Modify:
  - `Dominio/Guias/GuidePlanner.cs`;
  - `Servicios/TransportGuideService.cs`;
  - `Servicios/ManifestService.cs` (`withGuides`);
  - `Servicios/ISapTransferWriter.cs`, `Servicios/SapTransferWriter.cs`;
  - `Modulo.Despachos.Tests/Fakes/FakeSapTransferWriter.cs`;
  - `Pages/Despachos/Configuracion/Origenes/Index.cshtml(.cs)`.
- Test:
  - `Modulo.Despachos.Tests/Dominio/Guias/GuidePlannerDeliveryTests.cs`;
  - `Modulo.Despachos.Tests/Servicios/ManifestDeliveryEmissionTests.cs`.

**Interfaces:**
- Consumes: Task 1–4.
- Produces:
  - `public sealed record DeliveryGuideTerms(string Code, int TransferIndicator, int? DispatchType, long PurchaseOrder, DateOnly OrderDate);`
  - `GuidePlanInput` gana el último parámetro opcional
    `IReadOnlyDictionary<long, DeliveryGuideTerms>? DeliveryTerms = null`, indexado por `DispatchGroupId`.
  - `ISapTransferWriter` gana:
    - `Task WriteDeliveryAsync(int docEntry, IReadOnlyDictionary<string, object?> fields, CancellationToken ct = default)`;
    - `Task<IReadOnlyDictionary<string, object?>> ReadDeliveryAsync(int docEntry, IReadOnlyList<string> fields, CancellationToken ct = default)`.

    El recurso es `DeliveryNotes`. El fake agrega `DeliveryWrites` y `DeliveryCurrent`.
  - `TransportGuideService`:
    - `public static IReadOnlyDictionary<string, object?> DeliveryWritebackFields(DespachosSetting s, long folio)`;
    - `Task<string> TestSapDeliveryWriteAsync(int docEntry)`.
- **Planner:**
  - Una guía por `DispatchGroupId` de entrega, con la llave `D|{groupId}`.
  - Sin `DeliveryTerms` para ese grupo → error "Entrega {Folio}: falta su agrupación con guía previa.".
  - Receptor: `doc.CustomerTaxId` (RUT inválido → error), `doc.CustomerName` y la actividad de las líneas.
  - Destino: el del documento.
  - `DteIdDoc(52, IssueDate, terms.TransferIndicator, terms.DispatchType)`.
  - Siempre valorizada: líneas sin `Amount` → error "…no trae el monto (columna line_amount)…".
  - Referencia 801 fija.
  - `Split` usa `valued: true` para estos grupos.
- **TransportGuideService:**
  - `PlanAsync` pide `DeliveryLines` para las Entregas y arma `DeliveryTerms` desde `DispatchGroups`.
  - `WantsRes154` es true si hay documentos `delivery`, aunque `GuidesEnabled` sea false. En ese caso usa
    `settings ?? new DespachosSetting()`.
  - `WriteBackAsync` escribe en Entregas con `WriteDeliveryAsync(DeliveryWritebackFields)`. Las fallas siguen el
    mismo camino "SAP pendiente" y aviso de campana, con el texto "en la Entrega".
- **ManifestService:** `withGuides` es true si hay `delivery`, sin depender de `IsEnabledAsync`.
- **Orígenes:** "Probar escritura" con un selector Transferencia | Entrega que llama a `TestSapDeliveryWriteAsync`.

- [ ] **Step 1: Write the failing tests**:
  - `GuidePlannerDeliveryTests`:
    - `UnaGuiaPorAgrupacion_Valorizada_ConReferencia801`: dos Entregas de un grupo → 1 guía con:
      - `IdDoc.TransferIndicator 2`, `DispatchType 2`;
      - receptor = RUT del cliente;
      - `References == [("801", 4500000001, fecha pedido)]`;
      - líneas con `UnitPrice != null`.
    - `ValorizaAunqueGuideValuedFalse`.
    - `ExcedeMaxLineas_DivideYRepiteReferencia` (MaxLines 2, 3 SKU → 2 guías, cada una con su 801) (Review Focus 4).
    - `SinMonto_Error`.
    - `DosAgrupacionesMismoCliente_DosGuias`.
  - `ManifestDeliveryEmissionTests`:
    - `Despachar_SoloEntregas_GuiasApagadas_EmiteGuia` (Review Focus 2);
    - `Emitida_EscribeFolioEnLaEntrega` → `DeliveryWrites` con `FolioNumber` = folio y `FolioPrefixString = "52"`,
      y ninguna escritura en `Writes` (transferencias);
    - `EscrituraEntregaFalla_SapPendienteYAviso`;
    - `Despachado_AgrupacionPasaADespachada`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~GuidePlannerDelivery|FullyQualifiedName~ManifestDeliveryEmission"` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run full module suite** → PASS.
- [ ] **Step 5: Commit** `feat(despachos): guía valorizada por OC con referencia 801 al despachar Entregas y folio en la Entrega de SAP`.

### Task 6: Pantallas "Entregas con guía previa"

**Files:**
- Create: `Pages/Despachos/EntregasGuiaPrevia/Index.cshtml(.cs)`, `Generar.cshtml(.cs)`, `Detalle.cshtml(.cs)`
  (hermanas de AgruparTraslados).
- Modify:
  - `ModuloDespachos.cs` (menú);
  - `Pages/DespachosUi.cs`: etiqueta "Entrega" y código EG- en la tabla de documentos del manifiesto (verificar
    dónde se arma el código AT-).
- Test: `Modulo.Despachos.Tests/Pages/EntregasGuiaPreviaPermisosTests.cs`

**Interfaces:**
- Consumes: `IDeliveryGroupService`, `IDispatchGroupService.GetAsync/ListAsync(Kind = delivery)`,
  `IDispatchGroupPdfService`, `TransferIndicators.Label`, `DispatchTypes.Label`.
- **Index** (VIEW):
  - filtros: fecha de despacho, cliente (código) y estado;
  - columnas: Código EG-, Cliente, OC, Fecha despacho, Entregas, Unidades, Indicador (label), Estado, Manifiesto;
  - acciones: botón "Agrupar" (CREATE) e imprimir seleccionadas.
- **Generar** (CREATE), `/despachos/entregas-guia-previa/generar`:
  - filtros: cliente (RUT o código), Entregas desde y fecha de despacho;
  - condiciones: un select de indicador (1–9 con `TransferIndicators.Label`, default 2) y uno de tipo de despacho
    (1–3 con `DispatchTypes.Label`, default 2);
  - un bloque por OC con checkbox de grupo y por Entrega (nada marcado; "Seleccionar todos"); columnas LPN, Entrega, Fecha, Unidades y Neto; usa
    `despachos-agrupar.js` y sus `data-*`;
  - las bloqueadas van en una tabla aparte con su motivo y sin checkbox.
- **Detalle**:
  - cabecera: cliente, RUT, OC, fecha del pedido, indicador y tipo;
  - formulario de condiciones (EDIT, armada);
  - agregar LPN y quitar (EDIT), anular (DELETE), PDF (VIEW).

- [ ] **Step 1: Write the failing tests** (copiar `AgruparTrasladosPermisosTests`):
  - `Menu_EntregasGuiaPrevia_ConIconoYRuta` → `("grupo-operacion", "bi-box-arrow-right", "/despachos/entregas-guia-previa")`;
  - `Index_SinView_Forbid`;
  - `Generar_SinCreate_Forbid`;
  - `Generar_DefaultsIndicador2Tipo2`;
  - `Detalle_Condiciones_SinEdit_Forbid`;
  - `Detalle_Anular_SinDelete_Forbid`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~EntregasGuiaPreviaPermisos` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run module suite + Core guardians**
  (`dotnet test "Portal SaaS - Core/src/PortalSaas.Core.Tests" --filter "FullyQualifiedName~UrlsRelativas|FullyQualifiedName~VistasResponsivas"`) → PASS.
- [ ] **Step 5: Commit** `feat(despachos): pantallas Entregas con guía previa (agrupar por OC, detalle y hoja EG-)`.

### Task 7: Migración en las bases y cierre

- [ ] **Step 1:** `python -I efdb.py despachos update ps_comdepor_dsp_qa` y luego `ps_comdepor_dsp`. Verificar con
  `pgq.py` que existen `despachos_dispatch_groups.kind` (default `transfer`) y `customer_po`, y que las filas AT-
  existentes quedaron con kind `transfer`.
- [ ] **Step 2:** Correr el full module suite (PASS) y compilar con `build-all.ps1`.
- [ ] **Step 3:**
  - commit;
  - fast-forward de `feat/borradores` (`git -C <main> merge --ff-only -q feat/guias-res154`);
  - actualizar la memoria `modulo-despachos-estado.md`.
