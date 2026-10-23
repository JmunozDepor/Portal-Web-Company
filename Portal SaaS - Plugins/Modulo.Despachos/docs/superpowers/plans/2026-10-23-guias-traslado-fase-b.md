# Guías de traslado interno desde el manifiesto (Fase B) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que el manifiesto acepte transferencias de stock a sucursales (por LPN o en bloque), emita al despachar una guía de traslado interno (52, IndTraslado 5) por bodega sucursal de destino y deje el folio en la transferencia de SAP.

**Architecture:** Las transferencias entran como `manifest_documents` con `document_kind = "transfer"`, buscadas con consultas SAP configurables por compañía (mismo mecanismo que las facturas). El `GuidePlanner` de la Fase A agrupa las transferencias por bodega destino, con receptor = la propia compañía y sin valorizar. La emisión usa la misma pasarela DTE. Después de emitir, un escritor SAP del plugin hace PATCH a `StockTransfers` vía `ISapConnectionProvider` (ya existe en Abstractions). Una falla de escritura deja el documento "SAP pendiente" con reintento, sin afectar la guía.

**Tech Stack:** .NET 8 Razor Pages (plugin Modulo.Despachos, solo referencia PortalSaas.Abstractions), EF Core 8.0.8 dual (Postgres + SQL Server), SAP HANA (IHanaService), SAP Service Layer (ISapConnectionProvider/ISapSession), xUnit.

**Spec:** `Portal SaaS - Plugins/Modulo.Despachos/docs/superpowers/specs/2026-10-23-modulo-despachos-fase-b-traslados-design.md`

## Global Constraints

- El plugin solo referencia `PortalSaas.Abstractions`; la escritura en SAP usa `ISapConnectionProvider.GetConnectionAsync()` + `ISapSession.PatchAsync("StockTransfers", docEntry /*int, nunca string*/, body)`.
- Diseño y layout como los módulos hermanos: copiar estructura y clases de una página hermana (mantenedores: `Configuracion/Feriados`; ajustes: `Configuracion/Origenes`). Sin estilos propios.
- URLs internas siempre con `~/` o tag helpers (test guardián `UrlsRelativasAlPathBaseTests`); vistas responsivas: tablas en `table-responsive-lg`, botones `btn btn-*`, `flex-wrap` (test `VistasResponsivasTests`).
- Avisos por campana con `INotificationService.TryEnqueueAsync` y URL `/despachos/...`.
- Todo menú nuevo con icono `bi-*` propio.
- Números en `<input type=number>` con `InvariantCulture` (el Host fija es-CL).
- Cada cambio de modelo: migración en Postgres **y** SQL Server (`Modulo.Despachos.Migrations.Postgres` / `.SqlServer`), aplicada y verificada en `ps_comdepor_dsp_qa` y `ps_comdepor_dsp`.
- Valores fijados por el spec: LPN = `OWTR."U_NX_LPN"`, 1 transferencia = 1 LPN = 1 bulto; prefijo `FolioPref` por defecto **`52`**; IndTraslado interno por defecto **5**, TipoDespacho interno por defecto **3**; control de emitidas por `DocEntry` + objeto 67 en el portal (no en SAP).
- Mensajes al usuario en español, mismo tono que los existentes.
- Comando de pruebas del módulo: `dotnet test "Portal SaaS - Plugins/Modulo.Despachos/src/Modulo.Despachos.Tests"`; de plataforma: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests"`.

**Desviación del spec (ruling del plan):** §3.13 pide un contrato nuevo de plataforma `ISapDocumentFieldWriter`. `ISapConnectionProvider`/`ISapSession.PatchAsync` ya están en Abstractions y los usan Compras/Ventas/Inventario; el escritor queda dentro del plugin (`ISapTransferWriter`). Costo si es errado: mover una clase a Core más adelante.

## Review Focus

1. **El mismo LPN o transferencia dos veces:** reescaneo en el mismo manifiesto → aviso "ya está en este manifiesto"; en otro manifiesto no anulado (borrador o despachado) → rechazo con el N° de ese manifiesto. Test en Task 4.
2. **Falla al escribir en SAP después de emitir:** el manifiesto queda `dispatched`, la guía `issued`, el documento `sap_writeback_status = pending` con el error, aviso por campana; "Reintentar escritura SAP" no vuelve a emitir guía. Test en Task 7.
3. **Manifiesto mixto** (facturas + transferencias + guías del add-on por folio): guías separadas, las de factura con IndTraslado de facturas y valorizadas si corresponde, las de transferencia con IndTraslado 5 y en cero; las del add-on no generan guía; un manifiesto solo con guías del add-on despacha sin modo guías. Tests en Task 6.
4. **Bodega no sucursal o sucursal inactiva:** por LPN se rechaza con el nombre de la bodega; en bloque solo se ofrecen sucursales activas. Test en Task 4.
5. **Nombres de campos de usuario mal configurados** (`U_X; DROP`, vacío, sin `U_`): se rechazan al guardar; prefijo vacío + sin campos = no se escribe nada (estado `null`, no `pending`). Tests en Tasks 5 y 7.

---

### Task 1: Plataforma — razón social y giro del emisor en la configuración DTE

**Files:**
- Modify: `Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/Dte/DteEmissionModels.cs` (record `DteConfigurationStatus`)
- Modify: `Portal SaaS - Core/src/PortalSaas.Core/DocumentosElectronicos/DteEmissionService.cs:38-46`
- Test: `Portal SaaS - Core/tests/PortalSaas.Core.Tests/DocumentosElectronicos/DteEmissionServiceTests.cs`

**Interfaces:**
- Produces: `DteConfigurationStatus(..., string? Error, string? IssuerName = null, string? IssuerActivity = null)`. Los dos parámetros nuevos van al final y con default para no romper llamadores.

- [ ] **Step 1: Write the failing test** `GetConfiguration_Configurada_TraeRazonSocialYGiroDelEmisor`: con la compañía configurada en `DteTestSupport`, `Assert.Equal(issuer.Name, status.IssuerName)` y `Assert.Equal(issuer.Activity, status.IssuerActivity)`.
- [ ] **Step 2: Run** `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter GetConfiguration_Configurada_TraeRazonSocialYGiroDelEmisor` → FAIL (no compila).
- [ ] **Step 3: Implement**: agregar los parámetros y llenarlos desde `r.Issuer.Name` / `r.Issuer.Activity` en `GetConfigurationAsync`.
- [ ] **Step 4: Run** la suite de Core completa → PASS.
- [ ] **Step 5: Commit** `feat(dte): razón social y giro del emisor en DteConfigurationStatus`.

---

### Task 2: Modelo de datos de Despachos + bodegas sucursal (servicio)

**Files:**
- Modify: `src/Modulo.Despachos/Models/Constantes.cs` (`DocumentKinds.Transfer = "transfer"`, label "Transferencia", plural "transferencias"; **no** se agrega a `DocumentKinds.All`, que sigue siendo la lista buscable por folio). Nueva clase `SapWritebackStatuses { Ok = "ok", Pending = "pending" }`.
- Modify: `src/Modulo.Despachos/Models/ManifestDocument.cs`: `string? Lpn`, `string? SapWritebackStatus`, `string? SapWritebackError`.
- Modify: `src/Modulo.Despachos/Models/DespachosSetting.cs`: `string? LpnPattern`, `string? TransferFolioPrefix = "52"`, `string? TransferManifestField`, `string? TransferDispatchDateField`, `int InternalTransferIndicator = 5`, `int? InternalDispatchType = 3`.
- Create: `src/Modulo.Despachos/Models/BranchWarehouse.cs` (`: AuditedEntity`): `WarehouseCode`, `Name`, `Address?`, `CommuneName?`, `IsActive = true`.
- Modify: `src/Modulo.Despachos/Data/DespachosDbContext.cs`.
- Modify: `src/Modulo.Despachos/Servicios/IMasterDataService.cs`, `MasterDataService.cs`.
- Create: migración `FaseBTraslados` en ambos proyectos de migraciones.
- Test: `src/Modulo.Despachos.Tests/Servicios/BranchWarehouseTests.cs`

**Interfaces:**
- Produces (IMasterDataService):
  - `Task<IReadOnlyList<BranchWarehouse>> ListBranchWarehousesAsync(bool includeInactive = false)`
  - `Task<long> SaveBranchWarehouseAsync(BranchWarehouse e)`
  - `Task<DeleteResult> DeleteBranchWarehouseAsync(long id)`
  - `Task<BranchWarehouse?> FindBranchWarehouseAsync(string warehouseCode)` (activa o no; el llamador decide)
- Columnas: tabla `despachos_branch_warehouses` (`warehouse_code` varchar(20) requerido, `name` varchar(100) requerido, `address` varchar(70), `commune_name` varchar(60), `is_active`), único `uq_despachos_branch_warehouses_company_id_warehouse_code`. `manifest_documents`: `lpn` varchar(30), `sap_writeback_status` varchar(10), `sap_writeback_error` varchar(500), índice `ix_manifest_documents_company_id_document_kind_sap_doc_entry`. `despachos_settings`: `lpn_pattern` varchar(100), `transfer_folio_prefix` varchar(10) default `'52'`, `transfer_manifest_field` varchar(50), `transfer_dispatch_date_field` varchar(50), `internal_transfer_indicator` int default 5, `internal_dispatch_type` int (nullable) default 3.

- [ ] **Step 1: Write the failing tests**:
  - `BranchWarehouse_CrearEditarListarYEliminar`: crea "TDA01"; la edita cambiando el nombre; `ListBranchWarehousesAsync()` devuelve 1 fila; `DeleteBranchWarehouseAsync` → `Deleted`.
  - `BranchWarehouse_CodigoDuplicado_Rechaza`: "tda01 " después de "TDA01" lanza `InvalidOperationException` con "Ya existe". Código en mayúsculas y sin espacios en los bordes.
  - `BranchWarehouse_Inactiva_NoSeListaPorDefecto`.
  - `BranchWarehouse_UsadaEnManifiesto_NoSeElimina`: con un `ManifestDocument` de kind transfer y `CustomerCode = "TDA01"` → `Deleted == false` con "desactívela".
- [ ] **Step 2: Run** `dotnet test ... --filter BranchWarehouseTests` → FAIL (no compila).
- [ ] **Step 3: Implement** entidades, configuración EF y métodos con los helpers existentes (`SaveAsync`, `DeleteAsync`, `Scoped`).
- [ ] **Step 4: Generate migrations** desde `Portal SaaS - Core`: `dotnet tool run dotnet-ef migrations add FaseBTraslados --project <proj> --startup-project <proj> -o Migrations` para Postgres y SqlServer. Revisar que el `Up` solo agregue lo listado.
- [ ] **Step 5: Run** la suite del módulo → PASS.
- [ ] **Step 6: Commit** `feat(despachos): modelo de transferencias y bodegas sucursal (migración FaseBTraslados)`.

---

### Task 3: Consultas SAP de transferencias

**Files:**
- Modify: `src/Modulo.Despachos/Models/Constantes.cs`: `SapQueryKinds` agrega `TransferByLpn = "transfer_by_lpn"`, `TransferPending = "transfer_pending"`, `TransferLines = "transfer_lines"`, `Warehouses = "warehouses"`; `All` las incluye; labels "Transferencia por LPN", "Transferencias pendientes", "Líneas de transferencia", "Bodegas".
- Create seeds: `src/Modulo.Despachos/Sql/Seeds/hana_transfer_by_lpn.sql`, `hana_transfer_pending.sql`, `hana_transfer_lines.sql`, `hana_warehouses.sql` (recursos embebidos como los existentes); registrar en `SapQuerySeeds.Get`.
- Modify: `src/Modulo.Despachos/Servicios/ISapDocumentLookupService.cs`, `SapDocumentLookupService.cs`.
- Modify: `src/Modulo.Despachos/Pages/Despachos/Configuracion/ConsultasSap/Index.cshtml(.cs)`.
- Test: `src/Modulo.Despachos.Tests/Servicios/SapTransferQueriesTests.cs`; ajustar `SapDocumentLookupServiceTests.cs` y `Pages/ConsultasSapPageTests.cs` al parámetro de prueba de tipo texto.

**Interfaces:**
- Produces:
  - `public sealed record SapTransferInfo(int DocEntry, int DocNum, DateOnly DocumentDate, string WarehouseCode, string WarehouseName, string? DestinationAddress, string? DestinationCommune, decimal Units, bool IsCanceled, string? Lpn);`
  - `public sealed record SapWarehouse(string Code, string Name, string? Address, string? Commune);`
  - `Task<SapTransferInfo?> FindTransferByLpnAsync(string lpn, CancellationToken ct = default)` (parámetro `:lpn`; con más de una fila lanza "El LPN {lpn} está en más de una transferencia").
  - `Task<IReadOnlyList<SapTransferInfo>> ListPendingTransfersAsync(string warehouseCode, DateOnly fromDate, CancellationToken ct = default)` (`:warehouse`, `:from_date`).
  - `GetLinesAsync(SapQueryKinds.TransferLines, docEntry)` con el mismo contrato de columnas que las líneas de factura (`LineColumns`; el seed devuelve `NULL AS "customer_activity"`).
  - `Task<IReadOnlyList<SapWarehouse>> ListSapWarehousesAsync(CancellationToken ct = default)` (sin parámetros).
  - `ProbeAsync(string kind, string sql, string sample)` y `SaveQueryAsync(string kind, string sql, bool isActive, string sample)`: **la muestra pasa de `long` a `string`**. Facturas/guías: folio numérico; líneas: DocEntry numérico; LPN: texto; pendientes: código de bodega (fecha desde = hoy − 30 días); bodegas: se ignora.
- Contrato de columnas de transferencia (`TransferColumns`): `doc_entry, doc_num, document_date, warehouse_code, warehouse_name, destination_address, destination_commune, units, is_canceled, lpn`. Bodegas: `warehouse_code, warehouse_name, address, commune`.
- Seeds (HANA, `[validar]` en comentario): base `OWTR T0` + `OWHS W ON W."WhsCode" = T0."ToWhsCode"`; `units` = `SUM(WTR1."Quantity")`; `lpn` = `T0."U_NX_LPN"`; dirección `W."Street"`, comuna `W."County"`. En pendientes: `T0."ToWhsCode" = :warehouse AND T0."DocDate" >= :from_date AND T0."CANCELED" = 'N'`. **Sin** tablas del add-on (`@INX_ODGC`/`@INX_DGC1`): el control de emitidas es del portal (Task 4). En líneas (`WTR1 L`): mismo armado que `hana_invoice_lines.sql` (FrgnName, peso, `model_code`/`model_name`), sin `line_amount` y con `W."City"` como `destination_city`.

- [ ] **Step 1: Write the failing tests** (con el fake de HANA existente):
  - `FindTransferByLpn_MapeaContrato_YEnlazaLpn`: una fila → `SapTransferInfo` con `WarehouseCode`, `Units` y `Lpn`; el parámetro enlazado es `lpn = "CTBO123"`.
  - `FindTransferByLpn_SinFila_Null` y `FindTransferByLpn_DosFilas_Lanza`.
  - `ListPendingTransfers_EnlazaBodegaYFecha`.
  - `GetLines_Transferencia_UsaConsultaDeLineasDeTransferencia`.
  - `Seeds_SinFilaEnHana_SiembraLasCuatroConsultasDeTransferencias`: `ListQueriesAsync()` devuelve 7 consultas; la de LPN contiene `"U_NX_LPN" = :lpn`.
  - `Probe_TransferenciaPorLpn_ConMuestraTexto`.
- [ ] **Step 2: Run** `--filter SapTransferQueriesTests` → FAIL.
- [ ] **Step 3: Implement** el servicio, los seeds y `CheckStatic` por tipo (parámetros permitidos por tipo). En la página ConsultasSap, cambiar `SampleFolio` (long) por `Sample` (string) con la ayuda por tipo; el layout no cambia.
- [ ] **Step 4: Run** la suite del módulo → PASS. Las pruebas existentes que esperaban 3 consultas sembradas ahora esperan 7: actualizarlas.
- [ ] **Step 5: Commit** `feat(despachos): consultas SAP de transferencias (LPN, pendientes, líneas, bodegas)`.

---

### Task 4: Agregar transferencias al manifiesto (por LPN y en bloque)

**Files:**
- Modify: `src/Modulo.Despachos/Servicios/IManifestService.cs`, `ManifestService.cs`.
- Modify: `src/Modulo.Despachos/Pages/Despachos/Manifiestos/Detalle.cshtml(.cs)`.
- Test: `src/Modulo.Despachos.Tests/Servicios/ManifestTransfersTests.cs`; `Fakes/FakeSapDocumentLookupService.cs` (el de `ManifestTestEnv.Sap`) agrega `List<SapTransferInfo> Transfers` y `Dictionary<string, List<SapTransferInfo>> PendingTransfers`; las líneas de transferencia van en el `Lines` existente por DocEntry.

**Interfaces:**
- Consumes: Task 2 (`FindBranchWarehouseAsync`, campos nuevos), Task 3 (`FindTransferByLpnAsync`, `ListPendingTransfersAsync`).
- Produces:
  - `AddDocumentAsync` reconoce un LPN: si `RawScan` no es un timbre (`TedParser.TryParse` falla) y no contiene `<`, el texto (trim, mayúsculas) es un LPN si calza con `settings.LpnPattern` (`Regex`, `IgnoreCase`). Sin patrón configurado, todo texto no-timbre se trata como LPN. Si hay patrón y no calza → `InvalidScan` con el mensaje actual de timbre ilegible.
  - `public sealed record AddPendingTransfersResult(int Added, IReadOnlyList<string> Skipped);`
  - `Task<AddPendingTransfersResult> AddPendingTransfersAsync(Guid manifestId, string warehouseCode, DateOnly fromDate)`.
  - El documento transferencia: `DocumentKind = Transfer`, `SiiDocumentType = 0`, `Folio = DocNum`, `SapDocEntry`, `SapDocNum`, `Lpn`, `CustomerCode = WarehouseCode`, `CustomerName = BranchWarehouse.Name`, `CustomerTaxId = settings.IssuerTaxId`, dirección/comuna = las de `BranchWarehouse` si tienen dato, si no las de SAP; comuna resuelta con `FindCommuneByNameAsync` (equivalencias incluidas), `Units`, `Packages = 1`, `TotalAmount = 0`, `CaptureMethod = Pdf417` si vino del escáner o `Manual` en bloque.
- Reglas, en este orden y con estos mensajes:
  - "No se encontró el LPN {lpn} en SAP."
  - "La transferencia {DocNum} está anulada en SAP."
  - "La transferencia {DocNum} va a la bodega {code} ({name}), que no es sucursal." (no existe o está inactiva)
  - duplicado en este manifiesto → `DuplicateInThis`
  - en otro manifiesto no anulado → `InOtherManifest` con su número (control por `DocumentKind = transfer` + `SapDocEntry`).
- Transición: las transferencias guiadas por el add-on antes de la puesta en marcha no se detectan (no se consulta su tabla). La fecha "desde" de las pendientes se elige a partir de la puesta en marcha.
- En bloque: aplica las mismas reglas por fila y salta las que no pasan, con el motivo en `Skipped` (`"{DocNum}: <motivo>"`). Exige manifiesto en borrador y bodega sucursal activa.
- UI (pestaña Documentos, solo si `canScan` y hay bodegas sucursal activas): un formulario compacto bajo el escáner con `select` de bodegas sucursal, `input type=date` (default hoy − 30 días, `yyyy-MM-dd`) y el botón "Agregar pendientes", handler `OnPostAgregarPendientesAsync(Guid id, string warehouseCode, DateOnly fromDate)`. El resultado va a `SuccessMessage` ("Se agregaron N transferencia(s).") y los saltados a `ErrorMessage` como lista. La tabla de documentos muestra el LPN bajo el folio en las filas de transferencia.

- [ ] **Step 1: Write the failing tests** (uno por regla y uno por camino feliz):
  - `Lpn_Escaneado_AgregaTransferenciaConDatosDeLaSucursal`
  - `Lpn_NoExiste_NotFound`
  - `Lpn_Anulada_Canceled`
  - `Lpn_BodegaNoSucursal_Rechaza` (también inactiva)
  - `Lpn_DosVecesEnElMismo_Duplicado`
  - `Lpn_EnOtroManifiestoDespachado_InOtherManifest`
  - `Lpn_PatronConfigurado_TextoQueNoCalza_InvalidScan`
  - `Pendientes_AgregaLasQuePasan_YReportaLasSaltadas` (3 pendientes: 1 ok, 1 ya en otro manifiesto, 1 anulada → `Added == 1`, 2 `Skipped`)
  - `Pendientes_BodegaNoSucursal_Lanza`
- [ ] **Step 2: Run** `--filter ManifestTransfersTests` → FAIL.
- [ ] **Step 3: Implement** en `ManifestService` (un método privado `AddTransferAsync(Manifest m, SapTransferInfo t, string captureMethod, List<string> warnings)` compartido por ambos caminos) y la UI.
- [ ] **Step 4: Run** la suite del módulo + `--filter "UrlsRelativasAlPathBase|VistasResponsivas"` en Core → PASS.
- [ ] **Step 5: Commit** `feat(despachos): transferencias en el manifiesto por LPN y pendientes por sucursal`.

---

### Task 5: Ajustes de traslados y página Bodegas sucursal

**Files:**
- Modify: `src/Modulo.Despachos/Servicios/IMasterDataService.cs`, `MasterDataService.cs`.
- Modify: `src/Modulo.Despachos/Pages/Despachos/Configuracion/Origenes/Index.cshtml(.cs)`.
- Create: `src/Modulo.Despachos/Pages/Despachos/Configuracion/BodegasSucursal/Index.cshtml(.cs)` (copia de la estructura de `Feriados`).
- Modify: `src/Modulo.Despachos/ModuloDespachos.cs`: `Config("bodegas-sucursal", "Bodegas sucursal", "bi-shop", 13)`.
- Test: `src/Modulo.Despachos.Tests/Pages/OrigenesEmisorPageTests.cs`, `src/Modulo.Despachos.Tests/Pages/BodegasSucursalPageTests.cs`.

**Interfaces:**
- Produces: `Task SaveTransferSettingsAsync(string? lpnPattern, string? folioPrefix, string? manifestField, string? dispatchDateField, int internalTransferIndicator, int? internalDispatchType)`.
- Validaciones: el patrón compila como `Regex`; los campos, si vienen, calzan con `^U_[A-Za-z0-9_]{1,50}$`, si no: "El campo de usuario '{x}' no es válido (debe empezar con U_)."; prefijo hasta 10 caracteres; indicador 1..9; tipo de despacho 1..3 o null.
- Orígenes: nueva tarjeta "Guías de traslado a sucursales" (mismo `filter-card` que la de guías) con los 6 campos y "Guardar ajustes de traslados" (`OnPostSaveTransfersAsync`). "Probar escritura" va en la Task 7.
- Bodegas sucursal: grilla (código, nombre, dirección, comuna, activa) + drawer. El campo código usa `datalist` con las bodegas de SAP (`ListSapWarehousesAsync`; si falla, la página carga igual sin sugerencias y muestra el error en `ErrorMessage`). Al guardar sin dirección o comuna, se completan con las de SAP para ese código.

- [ ] **Step 1: Write the failing tests**:
  - `SaveTransfers_ConEdit_Guarda` (patrón `^(CTBO|ICBO)\d+$`, prefijo "52", campos `U_NX_Manifiesto` / `U_NX_FechaDesp`, 5, 3)
  - `SaveTransfers_CampoInvalido_Rechaza` (`"U_X; DROP"`, `"Manifiesto"`)
  - `SaveTransfers_PatronInvalido_Rechaza` (`"(("`)
  - `SaveTransfers_SinPermisoEdit_Forbid`
  - `BodegasSucursal_Guardar_SinDireccion_CompletaDesdeSap`
  - `BodegasSucursal_SinPermisoView_Forbid`
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** el servicio, las páginas y el menú.
- [ ] **Step 4: Run** la suite del módulo + los guardianes de Core → PASS.
- [ ] **Step 5: Commit** `feat(despachos): ajustes de traslados y mantenedor de bodegas sucursal`.

---

### Task 6: Planificador y emisión — guías de traslado interno

**Files:**
- Modify: `src/Modulo.Despachos/Dominio/Guias/GuidePlanner.cs`.
- Modify: `src/Modulo.Despachos/Servicios/TransportGuideService.cs` (`PlanAsync`, `Persist`).
- Modify: `src/Modulo.Despachos/Servicios/ManifestService.cs` (`DispatchAsync`, línea ~324).
- Test: `src/Modulo.Despachos.Tests/Dominio/Guias/GuidePlannerTests.cs`, `src/Modulo.Despachos.Tests/Servicios/TransportGuideServiceTests.cs`.

**Interfaces:**
- Consumes: Task 1 (`IssuerName`, `IssuerActivity`), Task 2, Task 3 (`GetLinesAsync(TransferLines, ...)`).
- Produces: `GuidePlanInput` agrega al final `DteReceiver? OwnCompany = null, int InternalTransferIndicator = 5, int? InternalDispatchType = 3`.
- Reglas del planificador:
  - `transfer_guide` (guía del add-on): se omite sin error; ya tiene su guía.
  - `transfer`: se agrupa con la llave `"T|" + CustomerCode + "|" + dirección normalizada + "|" + DestinationCommuneId`, sin mezclarse con facturas.
  - Guía de transferencias: `DteIdDoc(52, fecha, InternalTransferIndicator, InternalDispatchType)`; receptor = `OwnCompany` con la dirección, comuna y ciudad de la sucursal; sin referencias (`Array.Empty`); nunca valorizada (precio null, monto 0), aunque `Valued` esté activo; mismas reglas de líneas (modelo/SKU, máximo, código).
  - `OwnCompany` null con transferencias → error "Configure los documentos electrónicos de la compañía (razón social y giro del emisor) para emitir guías de traslado."
- `TransportGuideService.PlanAsync`: líneas de transferencia con `SapQueryKinds.TransferLines`; `OwnCompany = new DteReceiver(config.IssuerTaxId, config.IssuerName, config.IssuerActivity, null, null, null)`; indicadores internos desde los ajustes. `Persist` guarda `TransferIndicator` desde `plan.Document.IdDoc.TransferIndicator`, no desde los ajustes.
- `ManifestService.DispatchAsync`: el modo guías aplica solo si `IsEnabledAsync()` **y** el manifiesto tiene al menos un documento `invoice` o `transfer`.

- [ ] **Step 1: Write the failing tests**:
  - `Plan_Transferencias_GuiaPorSucursal_IndTraslado5_ReceptorPropio_SinReferencias_EnCero` (2 transferencias a TDA01 y 1 a TDA02 → 2 guías; la de TDA01 lleva 2 documentos; `Receiver.TaxId == "76030680-0"`; `References` vacío; montos 0 con `Valued: true`)
  - `Plan_Mixto_FacturasYTransferencias_GuiasSeparadas`
  - `Plan_GuiaDelAddon_SeOmite`
  - `Plan_TransferenciaSinEmpresaPropia_Error`
  - En `TransportGuideServiceTests`: `Dispatch_SoloGuiasDelAddon_DespachaSinModoGuias` y `Emit_Transferencia_PersisteIndicador5`.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** la suite del módulo → PASS.
- [ ] **Step 5: Commit** `feat(despachos): guías de traslado interno por sucursal (IndTraslado 5)`.

---

### Task 7: Escritura en SAP después de emitir, reintento y "Probar escritura"

**Files:**
- Create: `src/Modulo.Despachos/Servicios/ISapTransferWriter.cs`, `SapTransferWriter.cs` (registrar en `ModuloDespachos.RegisterServices`).
- Modify: `src/Modulo.Despachos/Servicios/TransportGuideService.cs` (después de `Apply(g, r)` con `Issued`), `ITransportGuideService.cs`.
- Modify: `src/Modulo.Despachos/Servicios/ManifestService.cs` (`RetrySapWritebackAsync` delega).
- Modify: `Pages/Despachos/Manifiestos/Detalle.cshtml(.cs)`, `Pages/Despachos/Configuracion/Origenes/Index.cshtml(.cs)`.
- Create: `src/Modulo.Despachos.Tests/Fakes/FakeSapTransferWriter.cs`; ajustar `ManifestTestEnv` para inyectarlo.
- Test: `src/Modulo.Despachos.Tests/Servicios/SapWritebackTests.cs`.

**Interfaces:**
- Produces:
  - `public interface ISapTransferWriter { Task WriteAsync(int docEntry, IReadOnlyDictionary<string, object?> fields, CancellationToken ct = default); Task<IReadOnlyDictionary<string, object?>> ReadAsync(int docEntry, IReadOnlyList<string> fields, CancellationToken ct = default); }`
  - `SapTransferWriter`:
    - `WriteAsync` hace `session.PatchAsync("StockTransfers", docEntry, fields)` con `docEntry` como `int`;
    - `ReadAsync` hace `session.GetAsync<Dictionary<string, object?>>($"StockTransfers({docEntry})")` y devuelve solo los campos pedidos.
  - Campos a escribir (`static IReadOnlyDictionary<string, object?> WritebackFields(DespachosSetting s, long folio, int manifestNumber, DateOnly dispatchDate)` en `TransportGuideService`):
    - con prefijo: `FolioNumber = folio`, `FolioPrefixString = prefix`;
    - campo de manifiesto: su nombre → `manifestNumber`;
    - campo de fecha: su nombre → `dispatchDate.ToString("yyyy-MM-dd")`.

    Vacío = no se escribe ese dato. Diccionario vacío = no se llama a SAP y el estado queda `null`.
  - `Task<int> RetrySapWritebackAsync(Guid manifestId)` en `ITransportGuideService`: reintenta los documentos `pending` de guías emitidas y devuelve cuántos siguen pendientes.
  - `Task<string> TestSapWriteAsync(int docEntry)` en `ITransportGuideService`: lee los campos configurados y los reescribe con su valor actual. Devuelve "SAP aceptó la escritura en la transferencia {docEntry}." o lanza `InvalidOperationException` con el mensaje de SAP.
- Comportamiento al emitir una guía de traslado:
  - Por cada documento `transfer` de la guía, `WriteAsync`. Éxito → `SapWritebackStatus = ok`, error en null.
  - Excepción → `pending`, `SapWritebackError` (truncado a 500), evento `sap_writeback_failed` y campana: "Manifiesto N° {n}: no se pudo escribir el folio {folio} en la transferencia {DocNum} de SAP. Reintente desde la pestaña Guías.", URL `/despachos/manifiestos/{id}?tab=guias`.
  - La guía queda `issued` y el despacho sigue.
- UI:
  - Pestaña Guías: si hay documentos `pending`, alerta `alert-warning` con la lista y el botón "Reintentar escritura SAP" (`OnPostReintentarSapAsync`, permiso Edit).
  - Documentos: badge "SAP pendiente" en la fila.
  - Orígenes, tarjeta de traslados: `input` DocEntry + botón "Probar escritura" (`OnPostTestSapWriteAsync`, permiso Edit).

- [ ] **Step 1: Write the failing tests**:
  - `Emitir_Traslado_EscribeFolioPrefijoYCampos` (el fake registra `docEntry` y campos: `FolioNumber = 107`, `FolioPrefixString = "52"`, `U_NX_Manifiesto = 4`)
  - `Emitir_Traslado_FallaSap_GuiaEmitida_DocumentoPendiente_YAviso` (manifiesto `dispatched`, guía `issued`, notificación encolada)
  - `ReintentarSap_EscribeLosPendientes_SinReemitir` (0 llamadas nuevas a `EmitAsync` de la pasarela)
  - `Emitir_Traslado_SinPrefijoNiCampos_NoLlamaSap_EstadoNulo`
  - `Emitir_Factura_NoEscribeEnSap`
  - `ProbarEscritura_ReescribeValoresActuales` y `ProbarEscritura_SapRechaza_MensajeClaro`
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** la suite del módulo + los guardianes de Core → PASS.
- [ ] **Step 5: Commit** `feat(despachos): folio de la guía en la transferencia SAP con reintento y "Probar escritura"`.

---

### Task 8: Cierre — suites, migraciones y verificación

- [ ] **Step 1: Run** las suites completas: Core, Despachos y los guardianes. Todas PASS.
- [ ] **Step 2: Build** del proyecto de migraciones Postgres y aplicar `FaseBTraslados` en `ps_comdepor_dsp_qa` y `ps_comdepor_dsp` (regla dura del usuario: QA y producción). Verificar en `information_schema` la tabla `despachos_branch_warehouses` y las columnas nuevas.
- [ ] **Step 3: Commit** si quedó algo, fast-forward de `feat/borradores` y actualizar la memoria del módulo (estado Fase B).
- [ ] **Step 4: Handoff E2E** (lo hace el usuario en QA con caoba):
  1. cargar bodegas sucursal;
  2. configurar el patrón LPN y "Probar escritura" con una transferencia cerrada;
  3. armar un manifiesto con 2 LPN a 2 sucursales y "Agregar pendientes";
  4. despachar;
  5. verificar `FolioNum`/`FolioPref` en `OWTR`.
