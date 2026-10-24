# Guías manuales Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Emitir guías de despacho (DTE 52) que no nacen de un documento SAP desde Despachos. Se arman como
borrador, viajan en un manifiesto o solas, y se emiten por la pasarela DTE de la plataforma.

**Architecture:**
- `transport_guides` pasa a ser el índice de todas las guías (`source` = manifest | manual).
- El contenido del borrador se guarda como `DteDocument` en `document_json`.
- Un constructor puro (`ManualGuideBuilder`) arma el documento a emitir: IVA según el motivo, observaciones como
  línea y transporte según el modo.
- `ManualGuideService` maneja el borrador y la emisión sola. `ManifestService` y `TransportGuideService` las
  incorporan al manifiesto y las emiten al despachar junto con las guías Res. 154.

**Tech Stack:** .NET 8 Razor Pages (plugin), EF Core 8 con dos motores (Postgres y SQL Server), xUnit con
InMemory, ClosedXML, MigraDoc o BarcodeRenderer.

**Spec:** `Portal SaaS - Plugins/Modulo.Despachos/docs/superpowers/specs/2026-10-24-modulo-despachos-guias-manuales-design.md`

## Global Constraints

- **Rutas:** módulo en `Portal SaaS - Plugins/Modulo.Despachos/src/Modulo.Despachos`, tests en
  `.../src/Modulo.Despachos.Tests`. Comando de tests (desde `Portal SaaS - Plugins/Modulo.Despachos`):
  `dotnet test src/Modulo.Despachos.Tests/Modulo.Despachos.Tests.csproj --filter <filtro>`.
- **Código del borrador:** `GM-` + correlativo de 6 dígitos por compañía (`GM-000123`). Mismo patrón que
  `DispatchGroupCode`.
- **IVA:** lo define el motivo (`transfer_motives.vat_affected`). Afecto: IVA 19% sobre el neto. No afecto: las
  líneas van exentas (`DteLine.Exempt = true`, MntExe) y el total es igual al neto.
- **Observaciones:** hasta 500 caracteres. Se envían como última línea de detalle: sin código, `Name` =
  "OBSERVACIONES", `Description` = el texto (DscItem, máximo 1000), cantidad 1, precio 0, monto 0. Esa línea
  cuenta para el máximo de líneas.
- **Máximo de líneas:** `DespachosSetting.GuideMaxLines` (1..60), incluida la línea de observaciones.
- **Transporte Res. 154:** con `TipoDespacho` 2 o 3 son obligatorios patente, RUT y nombre del chofer, y RUT del
  transportista (RUT válido). Con `TipoDespacho` 1 el transporte es opcional.
- **Reglas del repo:**
  - página hermana (copiar estructura y clases de AgruparTraslados y Configuración/MotivosRechazo);
  - URLs internas con `~/`;
  - responsive a 375 px (`table-responsive-lg`, `flex-wrap`);
  - todo menú con icono `bi-*`;
  - campana vía `TryEnqueueAsync`;
  - `InvariantCulture` en inputs numéricos con value interpolado.
- **Migraciones:** en ambos proyectos (`Modulo.Despachos.Migrations.Postgres` y `.SqlServer`). Se aplican a
  `ps_comdepor_dsp_qa` y `ps_comdepor_dsp` con el script `efdb.py` del scratchpad y se verifican (regla
  "migraciones siempre en todas las bases").
- Ninguna credencial en código, logs ni chat.

## Review Focus

1. Manifiesto anulado o documento quitado con una guía manual en emisión "pending" en la pasarela: debe
   abandonar esa emisión y volver a borrador con una llave nueva, sin reutilizar la abandonada (test en Task 6).
2. Manifiesto que solo lleva guías manuales con `GuidesEnabled = false`: igual debe emitirlas al despachar
   (nunca despachar una guía manual sin folio) (test en Task 7).
3. Doble clic en "Emitir" de una guía sola: la misma llave de emisión evita el doble folio; el segundo clic
   devuelve el estado actual (test en Task 5).
4. Importación pegada con coma decimal es-CL ("1,5") y con separador de miles ("1.500"): cantidades correctas
   (test en Task 3).
5. RUT digitado con puntos ("76.030.680-0") en el buscador SAP: debe encontrar el socio
   (`LicTradNum` = "76030680-0") (test en Task 4).

---

### Task 1: Modelo de datos y migración

**Files:**
- Modify: `Models/TransportGuide.cs`, `Models/ManifestConstants.cs` (statuses), `Models/Constantes.cs`
  (DocumentKinds), `Models/ManifestDocument.cs`, `Data/DespachosDbContext.cs`
- Create: `Models/TransferMotive.cs`, `Dominio/ManualGuideCode.cs`
- Create migration `GuiasManuales` in both migration projects
- Test: `Modulo.Despachos.Tests/Dominio/ManualGuideCodeTests.cs`,
  `Modulo.Despachos.Tests/Servicios/ManualGuideModelTests.cs`

**Interfaces:**
- Produces:
  - `TransportGuide` gains:
    - `string Source` (default `TransportGuideSources.Manifest`), `int? Number`, `DateOnly? IssueDate`;
    - `int? DispatchType`, `long? OriginId`, `string? ReceiverCardCode`, `string? TransportMode`;
    - `decimal? NetAmount`, `decimal? VatAmount`, `decimal? TotalAmount`;
    - `string? Notes`, `string? AnnulReason`, `Guid? CreatedBy`, `Guid? IssuedBy`;
    - `ManifestId` becomes `Guid?` and `Sequence` becomes `int?`.
  - `TransportGuideSources { Manifest = "manifest", Manual = "manual" }`.
  - `ManualTransportModes { Manifest = "manifest", Master = "master", Manual = "manual" }`.
  - `TransportGuideStatuses` adds `Draft = "draft"`, `InManifest = "in_manifest"`, `Annulled = "annulled"`, plus
    `Label(string)`:

    | Constante | Label |
    |---|---|
    | Draft | Borrador |
    | InManifest | En manifiesto |
    | Planned | Planificada |
    | Pending | Pendiente |
    | Issued | Emitida |
    | Failed | Rechazada |
    | Abandoned | Abandonada |
    | Annulled | Anulada |

  - `DocumentKinds.ManualGuide = "manual_guide"` (NOT in `All`). `Label` → "Guía manual".
  - `ManifestDocument.TransportGuideId` (`Guid?`).
  - `TransferMotive : AuditedEntity { int Code; string Name; bool VatAffected; bool IsActive }`.
  - `DespachosDbContext.TransferMotives`.
  - `ManualGuideCode.Format(int) -> string`, `ManualGuideCode.TryParse(string?, out int) -> bool`, regex
    `^GM-(\d{1,9})$` IgnoreCase after Trim.
- Mapping:
  - `transport_guides` new snake_case columns (`source` varchar(20) not null default 'manifest', `number`,
    `issue_date`, `dispatch_type`, `origin_id`, `receiver_card_code` varchar(30), `transport_mode` varchar(20),
    `net_amount`/`vat_amount`/`total_amount` numeric(18,2), `notes` varchar(500), `annul_reason` varchar(250),
    `created_by`, `issued_by`).
  - Unique `(manifest_id, sequence)` gets `.HasFilter("manifest_id IS NOT NULL")`. New unique
    `uq_transport_guides_company_id_number` `(company_id, number)` with `.HasFilter("number IS NOT NULL")`.
  - FK `fk_transport_guides_manifests` stays (nullable).
  - `manifest_documents.transport_guide_id` with index `ix_manifest_documents_transport_guide_id`, FK
    `fk_manifest_documents_transport_guides` (Restrict).
  - `transfer_motives` (id, company_id, code, name varchar(80), vat_affected, is_active + audit), unique
    `uq_transfer_motives_company_id_code`.

- [ ] **Step 1: Write failing tests**
  - `ManualGuideCodeTests`:
    - `Format(123) == "GM-000123"`;
    - `TryParse(" gm-000123 ", out n)` → true, n = 123;
    - `TryParse("AT-000123")` → false;
    - `TryParse("GM-")` → false.
  - `ManualGuideModelTests.Guia_SinManifiesto_SeGuarda`: InMemory `TestDb.Create()`; add a TransportGuide with
    `Source = manual`, `ManifestId = null`, `Sequence = null`, `Number = 1`, `Status = draft`; SaveChanges; reload →
    `Source == "manual"`, `ManifestId == null`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~ManualGuideCode|FullyQualifiedName~ManualGuideModel"` →
  compile FAIL (types missing).
- [ ] **Step 3: Implement** the model, constants, mapping and `ManualGuideCode`. Make the existing code compile
  with nullable `ManifestId` and `Sequence`:
  - `TransportGuideService` uses `g.Sequence!.Value` / `?? 0` where it formats or compares;
  - `Persist` sets `Source = TransportGuideSources.Manifest`.
- [ ] **Step 4: Run the full module suite** → all PASS (was 501 + new).
- [ ] **Step 5: Generate migrations.** Create `GuiasManuales` in both projects with the same dotnet-ef command
  used for `PreGuias` (see `scratchpad/desp-ef.sh`). Review that the Postgres migration alters `manifest_id` and
  `sequence` to nullable and creates the filtered indexes. The SqlServer migration must use the same filter text.
- [ ] **Step 6: Commit** `feat(despachos): modelo de guías manuales (transport_guides como índice de todas las guías, motivos de traslado)`.

### Task 2: Constructor puro del documento (`ManualGuideBuilder`)

**Files:**
- Create: `Dominio/Guias/ManualGuideBuilder.cs`
- Test: `Modulo.Despachos.Tests/Dominio/Guias/ManualGuideBuilderTests.cs`

**Interfaces:**
- Consumes: DTE records from `PortalSaas.Abstractions.Modelos.Dte` and `RutChileno`.
- Produces:
  ```csharp
  public sealed record ManualGuideLineInput(string? ItemCode, string Description, decimal Quantity, string? Unit, decimal UnitPrice);
  public sealed record ManualGuideReferenceInput(string DocumentType, long Folio, DateOnly Date, string? Reason);
  public sealed record ManualGuideTransportInput(string? Plate, string? TrailerPlate, string? DriverTaxId, string? DriverName,
      string? CarrierTaxId, string? CarrierName);
  public sealed record ManualGuideInput(
      DateOnly IssueDate, int TransferIndicator, int DispatchType, long OriginId,
      string ReceiverTaxId, string ReceiverName, string? ReceiverActivity, string? ReceiverCardCode,
      string ReceiverAddress, string ReceiverCommune, string? ReceiverCity, string? ReceiverContact, string? ReceiverEmail,
      string DestinationAddress, string DestinationCommune, string? DestinationCity,
      IReadOnlyList<ManualGuideLineInput> Lines, IReadOnlyList<ManualGuideReferenceInput> References,
      string? Notes, string TransportMode, ManualGuideTransportInput? Transport);
  public sealed record ManualGuideOrigin(string Address, string Commune, string? City);
  public static class ManualGuideBuilder
  {
      public const int MaxNotes = 500;
      public const string NotesLineName = "OBSERVACIONES";
      // Errores de formulario (vacío = válido). maxLines = GuideMaxLines; requireTransport = el modo no es "manifest".
      public static IReadOnlyList<string> Validate(ManualGuideInput input, bool vatAffected, int maxLines);
      // Documento guardado del borrador (sin línea de observaciones; transporte digitado/maestro o null si modo manifest).
      public static DteDocument BuildDraft(ManualGuideInput input, ManualGuideOrigin origin, bool vatAffected);
      // Documento a emitir: agrega la línea de observaciones y, si transport != null, reemplaza el bloque Transporte
      // (conservando dirección/comuna/ciudad de destino) y la fecha de emisión.
      public static DteDocument ForEmission(DteDocument draft, string? notes, DteTransport? transport, DateOnly issueDate);
      public static ManualGuideInput ToInput(DteDocument draft, TransportGuide row); // para reabrir el formulario
      public static (decimal Net, decimal Vat, decimal Total) Totals(IEnumerable<DteLine> lines);
  }
  ```
  - Línea DTE: `Name` = descripción; `ItemCode` = código (null si vacío); `Amount = Math.Round(Quantity * UnitPrice, 0, AwayFromZero)`;
    `Exempt = !vatAffected`.
  - `Totals`: neto = Σ afectas, exento = Σ exentas, IVA = round(neto × 0,19), total = neto + exento + IVA.
  - `DteDocument.Totals` = null (la plataforma calcula con las mismas reglas).

- [ ] **Step 1: Write failing tests** (`ManualGuideBuilderTests`):
  - `Afecto_Mueble_TotalConIva`: 1 × 500.000, afecto → `Totals` = (500000, 95000, 595000).
  - `NoAfecto_Cajas_LineasExentas_TotalIgualNeto`: 30 × 608, no afecto → todas las líneas `Exempt`, total 18240,
    IVA 0.
  - `Validate_RutInvalido` ("12.345.678-0" → error contains "RUT del receptor").
  - `Validate_SinLineas` → "Agregue al menos una línea".
  - `Validate_CantidadCero` → "línea 1".
  - `Validate_ExcedeMaximo`: 27 líneas + notas con max 27 → "máximo de 27 líneas (incluida la de observaciones)".
  - `Validate_Despacho2_SinPatente_Error`: modo manual, dispatchType 2, transport null → error contains "patente".
  - `Validate_Despacho1_SinTransporte_Ok`.
  - `Validate_ModoManifiesto_NoExigeTransporte`.
  - `Validate_NotasMasDe500` → error.
  - `ForEmission_AgregaLineaObservaciones_AlFinal`: última línea `Name == "OBSERVACIONES"`, `Description` = notes,
    `Amount == 0`, `LineNumber == n + 1`.
  - `ForEmission_ConTransporte_UsaPatenteDelManifiesto`.
  - `BuildDraft_ToInput_IdaYVuelta`: los campos del input se conservan.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~ManualGuideBuilder` → FAIL (compile).
- [ ] **Step 3: Implement** per the Interfaces. Error texts are in Spanish and name the field. The receiver giro
  (`ReceiverActivity`) is required (the SII format requires GiroRecep for DTE 52).
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(despachos): armado del documento de la guía manual (IVA por motivo, observaciones, transporte Res. 154)`.

### Task 3: Importación masiva del detalle (`ManualGuideLineImporter`)

**Files:**
- Create: `Dominio/Guias/ManualGuideLineImporter.cs`
- Test: `Modulo.Despachos.Tests/Dominio/Guias/ManualGuideLineImporterTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record ImportedLinesResult(IReadOnlyList<ManualGuideLineInput> Lines, IReadOnlyList<string> Errors);
  public static class ManualGuideLineImporter
  {
      // Columnas en orden: Código, Descripción, Cantidad, Unidad, Precio unitario. Separador: tabulador o ';'.
      public static ImportedLinesResult FromText(string text);
      public static ImportedLinesResult FromXlsx(Stream xlsx); // primera hoja, mismas columnas desde A
      public static byte[] TemplateXlsx(); // encabezados: Código | Descripción | Cantidad | Unidad | Precio unitario
  }
  ```
- Reglas:
  - Fila 1 = encabezado si su Cantidad no es numérica (se omite).
  - Las filas vacías se omiten.
  - Descripción es obligatoria. Cantidad debe ser > 0. Precio vacío = 0 y debe ser ≥ 0.
- Números: usar `es-CL` cuando el texto tiene coma; si solo tiene puntos y un punto seguido de exactamente 3
  dígitos, es separador de miles; si no, punto decimal.
- Errores: `"Fila {n}: {motivo}"`, con n = número de fila del origen (1-based).

- [ ] **Step 1: Write failing tests**:
  - `Texto_Tabulado_ConEncabezado`: `"Código\tDescripción\tCantidad\tUnidad\tPrecio\nITEM-0\tCAJAS 550x500x345\t30\tUN\t608"` →
    1 línea (ItemCode "ITEM-0", Quantity 30, UnitPrice 608).
  - `Texto_PuntoYComa_ComaDecimal`: `";Pallet;1,5;;1.500"` → Quantity 1.5, UnitPrice 1500.
  - `Texto_SinDescripcion_ErrorConFila`: errors contains "Fila 2".
  - `Texto_CantidadNegativa_Error`.
  - `Xlsx_DesdePlantilla`: write two rows via ClosedXML in a MemoryStream → 2 lines.
  - `Plantilla_TieneEncabezados`: open `TemplateXlsx()` → A1 == "Código", E1 == "Precio unitario".
- [ ] **Step 2: Run** `--filter FullyQualifiedName~ManualGuideLineImporter` → FAIL.
- [ ] **Step 3: Implement** with ClosedXML (already referenced).
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(despachos): importación masiva del detalle de la guía manual (pegar desde Excel o planilla)`.

### Task 4: Consulta SAP de socio de negocio por RUT

**Files:**
- Modify: `Models/Constantes.cs` (`SapQueryKinds.BusinessPartnerByTaxId = "business_partner_by_tax_id"`, en `All`,
  Label "Socio de negocio por RUT"), `Servicios/SapQuerySeeds.cs`, `Servicios/ISapDocumentLookupService.cs`,
  `Servicios/SapDocumentLookupService.cs`, `Modulo.Despachos.csproj` (EmbeddedResource),
  `Pages/Despachos/Configuracion/ConsultasSap/*` (muestra = RUT), `Tests/Fakes/FakeSapDocumentLookupService.cs`
- Create: `Sql/Seeds/hana_business_partner_by_tax_id.sql`
- Test: `Modulo.Despachos.Tests/Servicios/SapBusinessPartnerTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record SapPartnerAddress(string? Name, string? Address, string? Commune, string? City);
  public sealed record SapBusinessPartner(string CardCode, string Name, string? Activity, bool IsActive, IReadOnlyList<SapPartnerAddress> Addresses);
  Task<SapBusinessPartner?> FindBusinessPartnerAsync(string taxId, CancellationToken ct = default); // en ISapDocumentLookupService
  ```
- Contrato de columnas (una fila por dirección de despacho, cabecera repetida): `card_code`, `card_name`,
  `activity`, `is_active`, `address_name`, `address`, `commune`, `city`.
- Parámetro: `:tax_id`, que recibe `RutChileno.Normalize(taxId)` ("76030680-0").
- Lanza `InvalidOperationException("El RUT … no es válido.")` si el RUT no es válido.
- Si varias filas traen distinto `card_code`, prefiere el activo; si quedan varios, toma el primero ordenado por
  `card_code`.
- Seed HANA:
  ```sql
  SELECT T0."CardCode" AS "card_code", T0."CardName" AS "card_name", T0."Notes" AS "activity",
         CASE WHEN T0."validFor" = 'N' OR T0."frozenFor" = 'Y' THEN 0 ELSE 1 END AS "is_active",
         T1."Address" AS "address_name", T1."Street" AS "address", T1."County" AS "commune", T1."City" AS "city"
  FROM OCRD T0 LEFT JOIN CRD1 T1 ON T1."CardCode" = T0."CardCode" AND T1."AdresType" = 'S'
  WHERE T0."CardType" = 'C' AND REPLACE(T0."LicTradNum", '.', '') = :tax_id
  ORDER BY T0."CardCode", T1."Address"
  ```
  (sin `:parámetros` en comentarios).
- `ProbeAsync(kind = BusinessPartnerByTaxId, sampleText = RUT)` valida que solo use `:tax_id` y devuelve las
  filas: el campo nuevo `SapQueryProbeResult.Partner` es `SapBusinessPartner?`.

- [ ] **Step 1: Write failing tests** (`SapBusinessPartnerTests`, real service + `FakeHanaService` como en `SapTransferQueriesTests`):
  - `RutConPuntos_SeNormaliza`: `FindBusinessPartnerAsync("76.030.680-0")` → `LastParameters` has `tax_id = "76030680-0"`.
  - `DosDirecciones_UnSocio`: 2 rows same card_code → `Addresses.Count == 2`.
  - `SinFilas_Null`.
  - `RutInvalido_Lanza`.
  - `Seed_CumpleContrato`: the seed passes `CheckStatic` (probe with fake rows returns Valid).
  - `SapQueryKinds.All` now has 9 kinds; update any test that counts kinds.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~SapBusinessPartner|FullyQualifiedName~SapTransferQueries|FullyQualifiedName~ConsultasSap"` → FAIL.
- [ ] **Step 3: Implement.** On the Consultas SAP page the sample for this kind is a text input labeled "RUT de
  prueba", like the LPN sample.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(despachos): consulta SAP de socio de negocio por RUT para el receptor de la guía manual`.

### Task 5: `ManualGuideService` (borrador, motivos, emisión sola, lista de guías)

**Files:**
- Create: `Servicios/IManualGuideService.cs`, `Servicios/ManualGuideService.cs`
- Modify: `ModuloDespachos.cs` (DI), `Tests/Servicios/ManifestTestEnv.cs` (exponer `ManualGuides`)
- Test: `Modulo.Despachos.Tests/Servicios/ManualGuideServiceTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces:
  ```csharp
  public sealed record GuideFilter(string? Source, string? Status, DateOnly? From, DateOnly? To, string? Receiver);
  public sealed record GuideListItem(Guid Id, string Source, string Code /* "GM-000123" o "Manifiesto N° 7 / guía 2" */,
      Guid? ManifestId, int? ManifestNumber, long? Folio, DateOnly Date, string ReceiverName, string ReceiverTaxId,
      string DestinationCommune, int TransferIndicator, decimal? TotalAmount, string Status);
  public interface IManualGuideService
  {
      Task<IReadOnlyList<TransferMotive>> ListMotivesAsync(bool includeInactive = false); // siembra 5,6 activos y 2,4,7 inactivos la primera vez
      Task SaveMotiveAsync(TransferMotive motive);
      Task<Guid> CreateAsync(ManualGuideInput input);
      Task UpdateAsync(Guid id, ManualGuideInput input);          // solo Draft o InManifest (en InManifest el modo de transporte queda "manifest")
      Task<TransportGuide?> GetAsync(Guid id);
      Task<TransportGuide?> GetByNumberAsync(int number);
      Task<ManualGuideInput> GetInputAsync(Guid id);
      Task<IReadOnlyList<GuideListItem>> ListAsync(GuideFilter filter);   // manifest + manual
      Task AnnulAsync(Guid id, string reason);                    // solo Draft; motivo obligatorio
      Task<byte[]> PreviewPdfAsync(Guid id);
      Task<TransportGuide> EmitAsync(Guid id);                    // solo guía sola (Draft/Failed/Pending con manifest null)
      Task<byte[]> GetPdfAsync(Guid id);                          // Issued
      Task<SapBusinessPartner?> FindReceiverAsync(string taxId);
  }
  ```
  La importación (Task 3) es pura: la página la llama directamente.
- Reglas:
  - **Número:** `Create` asigna `max(number) + 1` por compañía, con reintento ante colisión del único (como
    `ManifestService.CreateDraftAsync`, 3 intentos). También asigna `EmissionKey = Guid.NewGuid()`,
    `Source = manual`, `Status = draft` y `CreatedBy`.
  - **Al guardar:** valida con `ManualGuideBuilder.Validate` (motivo activo y existente; origen existente con
    dirección y comuna; `maxLines` = setting). Arma `BuildDraft` y llena las columnas resumen (receptor, destino,
    `LineCount`, `TotalQuantity`, totales, `DispatchType`, `TransferIndicator`, `OriginId`, `TransportMode`,
    `Notes`, `IssueDate`).
  - **Modo "master":** el formulario manda `Transport` ya resuelto desde los maestros (la página lo arma desde
    vehículo, chofer y transportista).
  - **`EmitAsync`:**
    - una guía en un manifiesto lanza "Esta guía viaja en el manifiesto N° X: se emite al despacharlo.";
    - si la pasarela ya la tiene (`_dte.GetAsync`) y no está Failed sin folio, aplica el estado (idempotente);
    - si no, `ForEmission(draft, notes, transport: null, issueDate: max(IssueDate, hoy Chile))` y
      `_dte.EmitAsync(new DteEmissionRequest(key, "despachos", $"manual:{code}", doc))`;
    - Issued → `IssuedAt`, `IssuedBy`, `document_json` = "" (el contenido queda en la pasarela);
    - Failed → `LastError` y campana "Guía manual GM-… rechazada" con url
      `/despachos/guias-manuales/detalle?id={id}` (vía `TryEnqueueAsync`, try/log).
  - **`ListAsync`:**
    - Date = `IssueDate`, o `IssuedAt` (local) para las Res. 154; el filtro Receiver busca en nombre o RUT;
    - orden por fecha desc y luego Number/Sequence;
    - las Res. 154 muestran el número de su manifiesto.
- [ ] **Step 1: Write failing tests** (`ManualGuideServiceTests`, usando `ManifestTestEnv.Build()` + `env.ManualGuides`):
  - `Motivos_SemillaInicial`: 5 activos (5 no afecto, 6 afecto); 2, 4, 7 inactivos.
  - `Crear_AsignaGm1_YCalculaTotales`: crea con 1 × 500000 motivo 6 → `Number == 1`, `TotalAmount == 595000`,
    `Status == draft`.
  - `Crear_Segunda_Gm2`.
  - `Crear_Invalida_NoGuarda`: RUT inválido → throws `InvalidOperationException`, 0 rows.
  - `Emitir_Sola_FolioYPdf`: emite → `Status == issued`, `Folio == 500`, `env.Dte.Calls.Count == 1`, la última
    línea del documento enviado es "OBSERVACIONES" cuando hay notas.
  - `Emitir_DosVeces_UnSoloFolio`: segunda llamada → `env.Dte.Calls` sin una emisión nueva (o la misma llave),
    `Folio` igual.
  - `Emitir_Rechazada_LuegoCorregidaYReemitida`: `RejectNext` → `failed`; `UpdateAsync` corrige; `EmitAsync` →
    `issued` (misma llave: la pasarela acepta otro contenido si falló sin folio).
  - `Anular_SoloBorrador`: anular una emitida lanza; anular un borrador sin motivo lanza.
  - `Lista_IncluyeManifiestoYManual`: una guía Res. 154 emitida (`GuideReadyDraftAsync` + dispatch con guías) más
    una manual → 2 items, filtro `Source = manual` → 1.
  - `Receptor_DelegaEnSap`: fake devuelve un socio → `FindReceiverAsync` lo retorna.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~ManualGuideService` → FAIL.
- [ ] **Step 3: Implement** and register `services.AddScoped<Servicios.IManualGuideService, Servicios.ManualGuideService>()`.
- [ ] **Step 4: Run** → PASS; full suite PASS.
- [ ] **Step 5: Commit** `feat(despachos): servicio de guías manuales (borrador, motivos, emisión sola, lista de guías)`.

### Task 6: La guía manual dentro del manifiesto (agregar, quitar, anular)

**Files:**
- Modify: `Servicios/ManifestService.cs`, `Servicios/IManifestService.cs`, `Models/ManifestConstants.cs`
  (`AddDocumentOutcomes` sin cambios: se reutiliza `Added`)
- Test: `Modulo.Despachos.Tests/Servicios/ManifestManualGuideTests.cs`

**Interfaces:**
- Consumes: `ManualGuideCode`, `TransportGuide` (Task 1), `IManualGuideService` no (se usa `_db` directo).
- Produces:
  - `AddDocumentAsync` reconoce `GM-` antes de AT (`ManualGuideCode.TryParse`) → `AddManualGuideAsync(m, number)`.
  - `Task<AddDocumentResult> AddManualGuideByIdAsync(Guid manifestId, Guid guideId)` (selector de la página).
- Reglas de agregar:
  - No existe → NotFound "No existe la guía manual GM-…".
  - Anulada → Canceled.
  - Emitida o con emisión en curso (Pending, Issued) → NotFound "La guía GM-… ya fue emitida: no se agrega a un
    manifiesto."
  - En otro manifiesto no anulado → InOtherManifest.
  - Ya en este → DuplicateInThis.
- Si se agrega:
  - crea `ManifestDocument` con `DocumentKind = manual_guide`, `SiiDocumentType = 52`, `Folio = 0`,
    `SapDocEntry/SapDocNum = 0`, `TransportGuideId`, `DocumentDate = IssueDate`, `TotalAmount`,
    `CustomerCode = ReceiverCardCode ?? ""`, `CustomerName`, `CustomerTaxId`, `DestinationAddress`,
    `DestinationCommuneName` y `DestinationCommuneId` (vía `_master.FindCommuneByNameAsync`),
    `Units = TotalQuantity`, `Packages = 1` y el `CaptureMethod` del escaneo;
  - deja la guía en `Status = in_manifest` y `TransportMode = manifest`;
  - registra el evento `DocumentAdded` con `{ kind, code }`.
- Volver a borrador (helper privado `ReleaseManualGuidesAsync(IEnumerable<ManifestDocument>)`, llamado desde
  `RemoveDocumentAsync`, `RemoveAllDocumentsAsync` y `AnnulAsync` cuando el manifiesto estaba en Draft):
  - `Status = draft`;
  - `TransportMode = master` si `DispatchType` es 1; si no, `manual` con el transporte vacío (el usuario lo
    completa antes de emitir sola).
- Duplicado en el despacho: `FindActiveElsewhereAsync` para `manual_guide` compara por `TransportGuideId`, no por
  folio (folio 0 en todas).

- [ ] **Step 1: Write failing tests** (`ManifestManualGuideTests`):
  - `Escanear_Gm_AgregaComoDocumento`: crea guía (Task 5) y escanea `"GM-000001"` → `Added`; el documento tiene
    kind `manual_guide` y `Units` = cantidad; la guía queda en `in_manifest`.
  - `Escanear_Gm_EnOtroManifiesto_Rechaza` → `InOtherManifest`.
  - `Escanear_Gm_Emitida_Rechaza`.
  - `Quitar_VuelveABorrador`.
  - `QuitarTodos_VuelveABorrador`.
  - `AnularManifiestoBorrador_VuelveABorrador`.
  - `Selector_PorId_Agrega`.
  - `DosGuiasManuales_NoSeConsideranDuplicadas` (dos GM distintas, folio 0 ambas → las dos `Added`).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~ManifestManualGuide` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS; full suite PASS.
- [ ] **Step 5: Commit** `feat(despachos): la guía manual viaja en el manifiesto (escaneo GM-, selector, quitar y anular la devuelven a borrador)`.

### Task 7: Emisión de las guías manuales al despachar

**Files:**
- Modify: `Dominio/Guias/GuidePlanner.cs` (skip `ManualGuide` como `TransferGuide`),
  `Servicios/TransportGuideService.cs`, `Servicios/ManifestService.cs` (`withGuides`)
- Test: `Modulo.Despachos.Tests/Servicios/ManifestManualGuideEmissionTests.cs`,
  `Modulo.Despachos.Tests/Dominio/Guias/GuidePlannerTests.cs` (1 caso)

**Interfaces:**
- Consumes: `ManualGuideBuilder.ForEmission` (Task 2), the manifest snapshot fields (`Plate`, `TrailerPlate`,
  `CarrierTaxId`, `DriverTaxId`, `DriverName`, `OriginAddress`, `OriginCommune`, `EstimatedArrivalDate`).
- Reglas:
  - `DispatchAsync`: `withGuides = _guides != null && (m.Documents.Any(d => d.DocumentKind == ManualGuide) || (await _guides.IsEnabledAsync() && m.Documents.Any(invoice|transfer)))`.
  - `PreviewAsync` / `EmitAsync` de `TransportGuideService`:
    - Planifican Res. 154 solo si `GuidesEnabled` y hay facturas o transferencias. Si no, plan vacío sin error
      "no tiene documentos" ni `RequireSettingsAsync`.
    - Validan cada guía manual del manifiesto: `ForEmission(draft, notes, transportFromSnapshot, issueDate = fecha
      de salida)` y luego `_dte.ValidateAsync`; los errores salen como `"Guía manual GM-…: {msg}"`.
      - `transportFromSnapshot` = `new DteTransport(m.Plate, m.TrailerPlate, m.CarrierTaxId, driver, destAddr, destCommune, destCity, null, issueDate, departureTime, m.EstimatedArrivalDate)`.
      - En borrador usa los maestros vigentes (como `ApplyCurrentMastersAsync`).
    - En la emisión, cada guía manual con `Status` in_manifest, failed o pending:
      - se reconcilia con `_dte.GetAsync` como las Res. 154;
      - si no está bloqueada en la pasarela, se emite con su `EmissionKey` y `SourceRef = $"manifest:{m.Number}/manual:{code}"`;
      - al quedar Issued, la fila `ManifestDocument` toma `Folio = folio` y la guía `IssuedAt`, `IssuedBy` y
        `document_json = ""`.
    - `AllIssued` = (todas las Res. 154 + todas las manuales) emitidas y al menos una guía en total.
    - `GetPdfAsync(manifestId, null)` incluye las manuales emitidas (orden: Res. 154 por secuencia, luego GM por
      número).
    - `AbandonPendingAsync`: para las manuales no emitidas, abandona la emisión si la pasarela la tiene, asigna
      `EmissionKey = Guid.NewGuid()`, `Status = draft` y quita el vínculo (se borra la fila del manifiesto en
      `ManifestService.AnnulAsync` vía `ReleaseManualGuidesAsync`; el documento del manifiesto se conserva como
      historial, con la guía liberada).
- [ ] **Step 1: Write failing tests**:
  - `Planner_IgnoraGuiaManual` (GuidePlannerTests): un documento `manual_guide` no genera guía ni error.
  - `Despachar_SoloGuiaManual_SinGuidesEnabled_EmiteYDespacha`: `Status == dispatched`, documento con folio 500,
    `Dte.Calls[0].Document.Transport.Plate == "AB-1234"`.
  - `Despachar_FacturaYGuiaManual_DosEmisiones`: `EnableGuidesAsync` + factura + GM → 2 llamadas, `AllIssued`.
  - `Despachar_ProveedorCaido_QuedaGuiasPendientes_YReintentoEmite`: `Down = true` → `pending_guides`;
    `Down = false` + `RetryGuidesAsync` → `dispatched`.
  - `AnularConGuiasPendientes_GuiaManualVuelveABorradorConLlaveNueva`: `Down = true`, despachar, anular → guía
    `draft`, `EmissionKey` distinta de la original, `Dte.Abandoned` contiene la original.
  - `Despachar_GuiaManualInvalida_NoCambiaNada`: guía con despacho 2 y el manifiesto sin fecha de llegada → error
    de la Res. 154 y manifiesto sigue en `draft`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~ManifestManualGuideEmission|FullyQualifiedName~GuidePlanner"` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS; full suite PASS.
- [ ] **Step 5: Commit** `feat(despachos): las guías manuales del manifiesto se emiten al despachar con su transporte`.

### Task 8: Pantallas — lista de guías, guía manual (formulario, detalle, importación) y motivos

**Files:**
- Create:
  - `Pages/Despachos/GuiasManuales/Index.cshtml(.cs)`: lista "Guías", filtros de §6.4 de la spec;
  - `Pages/Despachos/GuiasManuales/Editar.cshtml(.cs)`: crear/editar, handlers `OnGetReceptorAsync(taxId)` (JSON),
    `OnPostImportarAsync` (texto o archivo → JSON con líneas y errores), `OnGetPlantillaAsync`, `OnPostGuardarAsync`
    y `OnPostVistaPreviaAsync`;
  - `Pages/Despachos/GuiasManuales/Detalle.cshtml(.cs)`: datos, líneas, observaciones y estado. Handlers
    `OnPostEmitirAsync`, `OnGetPdfAsync` (emitida), `OnGetBorradorPdfAsync` (vista previa del proveedor) y `OnPostAnularAsync`. El código `GM-` se muestra como
    texto y como imagen `BarcodeRenderer.Code128Png` (handler `OnGetCodigoAsync`), para imprimir y escanear;
  - `Pages/Despachos/Configuracion/MotivosTraslado/Index.cshtml(.cs)`: copia de MotivosRechazo, con columnas
    Código, Nombre, Afecto a IVA y Activo;
  - `wwwroot/js/despachos-guia-manual.js`: filas dinámicas, totales en vivo, buscador de RUT, modal de
    importación y modo de transporte.
- Modify:
  - `ModuloDespachos.cs` (menú): `Leaf("guias-manuales", "grupo-operacion", "Guías", "bi-file-earmark-text", "/despachos/guias-manuales", 3)`.
    Retorno pasa a 4 y Reporte de documentos a 5.
    `Config("motivos-traslado", "Motivos de traslado", "bi-arrow-left-right", 14)`.
  - `Pages/Despachos/Manifiestos/Detalle.cshtml(.cs)`:
    - selector "Agregar guía manual" (borradores sin manifiesto, `OnPostAgregarGuiaManualAsync(id, guideId)`);
    - la fila de una guía manual muestra el código `GM-` bajo el folio (enlace al detalle) y "Guía manual" como
      tipo.
- Permisos (`MenuCode = "guias-manuales"`):

  | Acción | Permite |
  |---|---|
  | View | Index, Detalle y PDF |
  | Create | Editar (nueva) y Emitir |
  | Edit | Editar (existente) |
  | Delete | Anular |

  Motivos: menú `motivos-traslado` con el mismo patrón que MotivosRechazo.
- Formulario (orden de secciones = FACELE recortado, spec §6.1):
  - **Receptor:** RUT + botón Buscar; atajo "Sucursal propia" (select de bodegas sucursal que llena destino y
    receptor = emisor).
  - **Condiciones:** fecha, motivo, tipo de despacho y forma de pago (solo texto "Contado", no editable).
  - **Origen:** select de Orígenes.
  - **Destino.**
  - **Detalle:** tabla con agregar y quitar fila, más botón "Importar" (modal con textarea y archivo, vista previa
    de errores, Reemplazar o Agregar).
  - **Referencias:** colapsable.
  - **Observaciones:** textarea con maxlength 500.
  - **Transporte:** radio Maestros / Particular; en manifiesto se muestra fijo "Del manifiesto N° X".
- Test: `Modulo.Despachos.Tests/Pages/GuiasManualesPermisosTests.cs`
- [ ] **Step 1: Write failing tests** (`GuiasManualesPermisosTests`, patrón de `AgruparTrasladosPermisosTests`):
  - `Menu_GuiasManuales_ConIconoYRuta`;
  - `Menu_MotivosTraslado_ConIcono`;
  - `Index_SinView_Forbid`;
  - `Editar_Nueva_SinCreate_Forbid`;
  - `Editar_Guardar_CreaYRedirigeAlDetalle`;
  - `Detalle_Emitir_SinCreate_Forbid`;
  - `Detalle_Anular_SinDelete_Forbid`;
  - `Importar_DevuelveLineasYErrores` (JSON con `lines.Count` y `errors`);
  - `Receptor_Json_DevuelveSocio`;
  - `Manifiesto_AgregarGuiaManual_RequiereEditar`.
  - Los guardianes de Core `UrlsRelativasAlPathBaseTests` y `VistasResponsivasTests` deben seguir en verde.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~GuiasManualesPermisos` → FAIL.
- [ ] **Step 3: Implement** pages copying sibling layout (AgruparTraslados Index/Detalle, Manifiestos/Cabecera
  form, MotivosRechazo).
- [ ] **Step 4: Run** module suite + Core guardians
  (`dotnet test "Portal SaaS - Core/src/PortalSaas.Core.Tests" --filter "FullyQualifiedName~UrlsRelativas|FullyQualifiedName~VistasResponsivas"`) → PASS.
- [ ] **Step 5: Commit** `feat(despachos): pantallas de guías (lista, guía manual con importación, motivos de traslado) y agregar guía manual al manifiesto`.

### Task 9: Migración en las bases y cierre

- [ ] **Step 1:** `python -I efdb.py despachos update ps_comdepor_dsp_qa` y luego `ps_comdepor_dsp`. Verificar
  con `pgq.py`: existe `transfer_motives`, `transport_guides.source`, y `transport_guides.manifest_id` es nullable
  (`information_schema.columns`).
- [ ] **Step 2:** Full module suite → PASS. Build `build-all.ps1` compila.
- [ ] **Step 3:** Commit, fast-forward `feat/borradores` (`git -C <main> merge --ff-only feat/guias-res154`) y
  actualizar la memoria `modulo-despachos-estado.md`.
