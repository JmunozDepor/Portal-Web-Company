# Facturas de Proveedor — Etapa 1 (aprobación y rechazo ante el SII) + Pasarela DTE P2 (recepción) — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Objetivo:** el portal lee las facturas recibidas desde el operador DTE de la compañía (FACELE para DEPOR), las pasa por reglas configurables con excepciones por proveedor y las acepta o rechaza ante el SII. Lo hace por regla o por decisión de una jefatura, siempre dentro del plazo legal.

**Arquitectura:**
- **Plataforma (`Portal SaaS - Core`):**
  - un adaptador de recepción por proveedor (`IDteReceptionProvider`; FACELE `DTEService`);
  - la bandeja `dte_received` con su sincronizador en segundo plano;
  - el contrato `IDteInboxService` en Abstractions, con respuesta idempotente.
- **Plugin `Modulo.FacturasProveedor`** (base propia por compañía):
  - motor de reglas puro;
  - orquestador en `BackgroundService` (evaluación, vencimientos, envío y avisos);
  - páginas con la estructura de los módulos hermanos.
- **Los dos lados se comunican solo por `IDteInboxService`.**

**Tecnología:** .NET 8, Razor Pages, EF Core 8.0.8 (Postgres + SQL Server, migraciones duales), xUnit + EF InMemory, SOAP a mano sobre `HttpClient` (patrón `FaceleSoapClient`), `IHanaService` para SAP.

**Spec:** `Portal SaaS - Plugins/Modulo.FacturasProveedor/docs/superpowers/specs/2026-10-27-modulo-facturas-proveedor-aprobacion-design.md`. Boceto: `.../specs/mockups/2026-10-27-facturas-proveedor-boceto-v2.html`. Plataforma: `Portal SaaS - Core/docs/superpowers/specs/2026-10-22-pasarela-documentos-electronicos-design.md` §6.

Las rutas son relativas a la raíz del repo. Abreviaturas:
- `CORE` = `Portal SaaS - Core`;
- `FP` = `Portal SaaS - Plugins/Modulo.FacturasProveedor`;
- `P` = `FP/src/Modulo.FacturasProveedor`;
- `T` = `FP/src/Modulo.FacturasProveedor.Tests`;
- `DSP` = `Portal SaaS - Plugins/Modulo.Despachos/src/Modulo.Despachos`.

## Global Constraints

- **Plugin:** referencia **solo** `PortalSaas.Abstractions` (`CORE/docs/09-GUIA-DESARROLLO-PLUGINS.md` §1). Copia la forma de Despachos: csproj, proyectos de migraciones, `publish-dist.ps1`, `PageModelBase` y `Ui`.
- **Aislamiento por compañía:** toda tabla lleva `company_id`. Plataforma y plugin resuelven la compañía solo con `ICurrentCompanyAccessor`. Los jobs fijan la compañía con `ICurrentCompanyOverride.Set` dentro de un scope por compañía.
- **Motor dual:** dos migraciones (PostgreSql y SqlServer) con los comandos de `CORE/CLAUDE.md` ("Cómo generar/aplicar migraciones").
  - Nada de `jsonb` ni funciones de un motor: los JSON van como `string`.
  - Defaults y Guids se generan en C#.
  - Fechas en `DateTimeOffset` **UTC** (Npgsql exige `Kind=Utc`).
  - Fechas de calendario en `DateOnly`.
- **Nombres de BD:** inglés, plural, `snake_case`. Los valores de estado y acción también en inglés `snake_case`; los rótulos en español van en `FacturasProveedorUi`.
- **Efecto legal:** nunca se llama `RespondAsync` sin una regla activa o una persona que lo decida.
  - `Mode` arranca en `"simulation"`.
  - Lo recibido antes de `ProcessFromDate` (o si esa fecha es nula) **nunca** se decide.
- **Credenciales:**
  - contraseña cifrada con `ISecretoCifradoService` y nunca en logs ni excepciones;
  - en tests de integración, solo por las variables de entorno `FACELE_USER`, `FACELE_PASS`, `FACELE_USER_MAIL` y `FACELE_RUT_RECEPTOR`.
  - **Nunca se llama `aprobarDTERecibidos` en producción desde un test.**
- **UI** (spec §10):
  - estructura de páginas hermanas: `card-surface`, `filter-card`, `table-ps` dentro de `table-responsive-lg`, `badge-status`, `drawer-*` y `doc-tabs`;
  - páginas bajo `P/Pages/FacturasProveedor/`, rutas `/facturas-proveedor/...` y URLs internas con `~/`;
  - responsivas a 375 px; todo ítem de menú con icono `bi-*`;
  - `<input type="number">` con `value` interpolado usa `CultureInfo.InvariantCulture`;
  - mensajes en español; los errores para el usuario son `InvalidOperationException`.
- **Avisos:** `INotificationService.TryEnqueueAsync(userId, title, message, severity, url)`.
- **Hora:** todas las fechas de plazo se calculan en hora Chile (`ChileClock`, copia de `DSP/Dominio/ChileClock.cs`).
- **Migraciones aplicadas:**
  - plataforma en las 4 bases (`portalsaas_saas_prod`, `portalsaas_saas_qa`, `ps_comdepor`, `ps_comdepor_qa`);
  - plugin en las bases del módulo QA y PROD;
  - **pedir confirmación al usuario antes de tocar producción.**
- **Comandos de test:**
  - Core: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests"` (línea base: 528 en verde);
  - plugin: `dotnet test "FP/src/Modulo.FacturasProveedor.Tests"`.

## Review Focus

1. **Facturas que ya estaban en FACELE al activar el módulo:** quedan `out_of_scope` y nunca se rechazan, aunque no tengan OC (Task 11, `Evaluate_RecibidaAntesDeProcesarDesde_FueraDeAlcance` y `Evaluate_SinProcesarDesde_TodoFueraDeAlcance`).
2. **Alguien responde en el portal de FACELE mientras la revisión está pendiente o por enviar:** el módulo no envía y la marca `external` (Task 12, `Run_RespondidaEnOperador_NoEnvia_QuedaExterna`).
3. **RUT con puntos, sin guion o con "k" minúscula** en el DTE o en SAP: no debe producir un falso "OC de otro proveedor" (Task 6, `OcMismoProveedor_RutEnOtroFormato_Cumple`).
4. **Dos facturas parciales de la misma OC en el mismo ciclo:** la segunda descuenta el saldo de la primera (Task 11, `Evaluate_DosParcialesMismaOc_LaSegundaSuperaSaldo_SeRechaza`).
5. **Paso de Simulación a Activo:** las revisiones `simulated` no se envían retroactivamente al SII (Task 12, `Run_PasoAActivo_NoEnviaLasSimuladas`).

---

## Parte 1 — Plataforma: recepción (P2)

### Task 1: Persistencia de recepción y configuración por compañía

**Files:**
- Create: `CORE/src/PortalSaas.Data/Entities/DteReceived.cs`
- Modify: `CORE/src/PortalSaas.Data/Entities/CompanyDteSetting.cs`, `CORE/src/PortalSaas.Data/PortalSaasDbContext.cs`
- Create: migración `AddDteReception` (PostgreSql y SqlServer)
- Create: `CORE/src/PortalSaas.Core/DocumentosElectronicos/IDteReceptionProvider.cs`, `CORE/src/PortalSaas.Core/DocumentosElectronicos/DteReceptionProviderRegistry.cs`, `CORE/src/PortalSaas.Abstractions/Modelos/Dte/DteInboxModels.cs` (solo `DteReceivedReference` y `DteResponseActions`; el resto, en la Task 4)
- Modify: `CORE/src/PortalSaas.Core/DocumentosElectronicos/DteSettingsService.cs`, `CORE/src/PortalSaas.Host/Pages/Admin/Organizations/Companies/Dte/Index.cshtml(.cs)`, `CORE/src/PortalSaas.Host/Program.cs` (`AddScoped<DteReceptionProviderRegistry>()`)
- Test: `CORE/tests/PortalSaas.Core.Tests/DocumentosElectronicos/DteSettingsServiceTests.cs`

**Interfaces:**
- Produces `CompanyDteSetting`, columnas nuevas:
  - `ReceptionEnabled bool`;
  - `ReceptionBaseUrl string?(300)` (FACELE: `https://caoba.docele.cl/cl-dol`, distinta de la de emisión `.../cl-dol-auth`);
  - `ReceptionUserMail string?(150)`;
  - `ReceptionIntervalMinutes int` (30), `ReceptionInitialDays int` (60), `ReceptionWindowDays int` (15);
  - `ReceptionInitialLoadDoneAt DateTimeOffset?`, `ReceptionLastSyncAt DateTimeOffset?`, `ReceptionLastError string?(500)`.
- Produces `DteReceived` (tabla `dte_received`):
  - columnas: `Id long`, `CompanyId Guid`, `ProviderCode(20)`, `Environment(20)`, `IssuerTaxId(12)`, `IssuerName(100)`, `DteType int`, `Folio long`, `IssueDate DateOnly`, `SiiReceivedAt DateTimeOffset?`;
  - montos: `NetAmount`, `ExemptAmount`, `VatAmount`, `TotalAmount` (long), `OtherTaxesJson string?`;
  - estado del operador: `SiiStatus(40)?`, `CommercialStatus(40)?`, `ClaimStatus(40)?`, `IsAssigned`, `IsVoided`, `ProviderUrl(500)?`;
  - contenido: `ReferencesJson string`, `PurchaseOrderRefsJson string`, `Xml string?`;
  - respuesta: `ResponseStatus(20)` ("none"), `ResponseAction(30)?`, `ResponseReason(500)?`, `ResponseActor(80)?`, `ResponseKey(80)?`, `RespondedAt?`, `ResponseAttempts int`, `LastError(1000)?`;
  - Etapa 2: `ProcessingModule(50)?`, `ProcessingState(30)?`, `ProcessingRef(80)?`, `ProcessingAt?`;
  - control: `FirstSeenAt`, `UpdatedAt`;
  - índices: único `(company_id, issuer_tax_id, dte_type, folio)`, `(company_id, updated_at)` y `(company_id, sii_received_at)`.
- Produces en `DteSettingsService`:
  - `DteSettingsInput` y `DteSettingsDto` con los 6 campos editables de recepción (`Enabled`, `BaseUrl`, `UserMail`, `IntervalMinutes`, `InitialDays`, `WindowDays`) y, solo en el DTO, `ReceptionLastSyncAt` y `ReceptionLastError`;
  - `internal Task<(DteReceptionContext Ctx, IDteReceptionProvider Provider)?> ResolveReceptionAsync(Guid companyId)` (null si la recepción no está habilitada);
  - `Task TestReceptionAsync(Guid companyId)`.
  - El contrato `IDteReceptionProvider` (con sus records) y `DteReceptionProviderRegistry` se crean **en esta tarea**, con las firmas exactas de la Task 2, que solo implementa FACELE. `DteSettingsService` recibe `DteReceptionProviderRegistry` por constructor.

- [ ] **Step 1: Tests que fallan**
  - `Save_RecepcionHabilitadaSinUrlOCorreo_Falla`;
  - `Save_RecepcionProveedorSinCapacidad_Falla`, con un proveedor fake que no está en `DteReceptionProviderRegistry`;
  - `Save_IntervaloFueraDeRango_Falla`: intervalo entre 5 y 1440, días iniciales entre 30 y 365, ventana entre 3 y 60;
  - `ResolveReception_Deshabilitada_Null`;
  - `ResolveReception_UsaUrlYCorreoDeRecepcion`: `Ctx.BaseUrl` es la URL de recepción y `Ctx.ReceiverTaxId` es `IssuerTaxId`.
- [ ] **Step 2:** Correrlos. Esperado: FAIL.
- [ ] **Step 3:**
  - Entidades, configuración del DbContext (mismo estilo que `DteEmission`) y las dos migraciones.
  - El servicio valida la URL con `ValidateBaseUrl`.
  - La página /Admin agrega la sección "Recepción de documentos" con los 6 campos, el botón "Probar recepción" (`OnPostTestReceptionAsync`) y la última sincronización y error en modo lectura.
- [ ] **Step 4:** Tests en verde y build del Host.
- [ ] **Step 5:** Commit `feat(dte): bandeja dte_received y configuración de recepción por compañía`.

### Task 2: Adaptador FACELE de recepción

**Files:**
- Modify: `CORE/src/PortalSaas.Core/DocumentosElectronicos/Proveedores/Facele/FaceleSoapClient.cs`
- Create: `CORE/src/PortalSaas.Core/DocumentosElectronicos/Proveedores/Facele/FaceleReceptionProvider.cs`
- Test: `CORE/tests/PortalSaas.Core.Tests/DocumentosElectronicos/Facele/FaceleReceptionProviderTests.cs` + fixtures `consultar-recepcion-ok.resp.xml`, `consultar-referencia-801.resp.xml`, `aprobar-ok.resp.xml`, `aprobar-error.resp.xml`; casos nuevos en `FaceleCaobaIntegrationTests.cs`
- Modify: `CORE/src/PortalSaas.Host/Program.cs` (registro)

**Interfaces** (el contrato ya existe desde la Task 1; aquí se fija su forma y se implementa FACELE):
- Contrato (Core):
  - `DteReceptionContext(string BaseUrl, string Username, string Password, string ReceiverTaxId, string UserMail, string Environment)`
  - `DteReceptionCapabilities(IReadOnlyList<int> RespondableTypes, IReadOnlyList<string> SupportedActions, bool SupportsResponseNote, bool SupportsDocumentDownload)`
  - `DteReceivedRecord(string IssuerTaxId, string IssuerName, int DteType, long Folio, DateOnly IssueDate, DateTimeOffset? SiiReceivedAt, long Net, long Exempt, long Vat, long Total, string? OtherTaxesJson, string? SiiStatus, string? CommercialStatus, string? ClaimStatus, bool IsAssigned, bool IsVoided, string? Url)`
  - `DteRespondProviderResult(bool Ok, bool AlreadyResponded, string? Error)`
  - `interface IDteReceptionProvider`:
    - `string Code`, `DteReceptionCapabilities Capabilities`
    - `Task<IReadOnlyList<DteReceivedRecord>> ListReceivedAsync(DteReceptionContext ctx, DateOnly from, DateOnly to, CancellationToken ct)`, que pagina internamente
    - `Task<IReadOnlyList<DteReceivedReference>> GetReferencesAsync(DteReceptionContext ctx, string issuerTaxId, int dteType, long folio, CancellationToken ct)`
    - `Task<string?> GetXmlAsync(DteReceptionContext ctx, DteReceivedRecord record, CancellationToken ct)`
    - `Task<byte[]?> GetPdfAsync(DteReceptionContext ctx, string issuerTaxId, int dteType, long folio, string? url, CancellationToken ct)`
    - `Task<DteRespondProviderResult> RespondAsync(DteReceptionContext ctx, string issuerTaxId, int dteType, long folio, string action, CancellationToken ct)`
    - `Task SetNoteAsync(DteReceptionContext ctx, string issuerTaxId, int dteType, long folio, string note, CancellationToken ct)`
    - `Task TestAsync(DteReceptionContext ctx, CancellationToken ct)`
  - Las fallas de red lanzan `DteProviderUnavailableException`, la misma de emisión.
  - `DteReceptionProviderRegistry(IEnumerable<IDteReceptionProvider>)` con `Get(code)` y `Contains(code)`, misma forma que `DteProviderRegistry`.
- Consumes: `DteReceivedReference` y `DteResponseActions` (Abstractions, creados en la Task 1 y definidos en la Task 4).
- `FaceleSoapClient`:
  - nueva constante `ReceptionService = "DTEService"` con namespace `http://facele.cl/docele/servicios/DTE/` (elementos hijos sin calificar, igual que hoy);
  - nuevo método `Task<XElement> CallAsync(string baseUrl, string username, string password, string service, string operation, IEnumerable<XElement> body, CancellationToken ct)`, que permite elementos anidados;
  - el `CallAsync` actual delega en él, sin cambiar su firma.

- [ ] **Step 1: Tests que fallan** (`HttpMessageHandler` fake que devuelve los fixtures):
  - `ListReceived_MapeaRegistro`: emisor, tipo, folio, montos, `fechaRegistroSII` → `SiiReceivedAt`, `estadoLey19983` → `CommercialStatus`, `estadoLey20956` → `ClaimStatus` (texto crudo de FACELE). El request lleva `operacion=RECEPCION`, `rutAbonado` y `userMail`.
  - `ListReceived_Pagina_HastaQueVieneMenosQueLimit`: limit 100, con dos páginas.
  - `GetReferences_Devuelve801`: `Type == "801"`, `Folio == "4512"`.
  - `Respond_MapeaAcciones`: `accept_content` → `Acepta_Contenido_Documento`, `claim_content` → `Reclamo_Contenido_Documento`, `claim_partial_goods` → `Reclamo_Falta_Parcial_Mercaderias`, `claim_total_goods` → `Reclamo_Falta_Total_Mercaderias`, `goods_receipt` → `Otorga_Recibo_Mercaderias`. El cuerpo es `<aprobacion><rutEmisor/><tipoDTE/><folio/><accion/></aprobacion>`.
  - `Respond_EstadoDistintoDeCero_DevuelveErrorConGlosa`;
  - `Respond_GlosaYaRespondido_AlreadyResponded`: si la glosa contiene "ya" y alguno de "aprob", "reclam" o "acept" (sin distinguir mayúsculas), entonces `AlreadyResponded = true`. Se ajusta con la glosa real del Step 4.
  - `Capabilities_Facele`: `RespondableTypes = [33, 34, 43]`.
- [ ] **Step 2:** Correrlos. Esperado: FAIL.
- [ ] **Step 3:** Implementar. `SupportsResponseNote` y `SupportsDocumentDownload` quedan en **false** por ahora:
  - `SetNoteAsync` lanza `NotSupportedException`;
  - `GetXmlAsync` y `GetPdfAsync` devuelven null;
  - `TestAsync` = `consultarResumen(operacion=RECEPCION, offset=0)`.
- [ ] **Step 4: Verificación en caoba** (`FaceleCaobaIntegrationTests`; se saltan sin las variables de entorno). Cada resultado se anota en `FP/PENDIENTE.md`.
  - `Caoba_ConsultarRecepcion`: lista los últimos 30 días; se guarda una respuesta real anonimizada como fixture.
  - `Caoba_ConsultarReferencia`: sobre un recibido con OC, confirma que viene el 801 (spec §16 FACELE-1).
  - **Observaciones:** se prueba `actualizarObservacionesDTE` sobre un recibido. Si funciona, `SupportsResponseNote = true` y se implementa `SetNoteAsync`.
  - **XML y PDF:** se revisa `https://caoba.docele.cl/cl-dol/DocumentosRecibidosService?wsdl`. Si existe `Obtener(rutContribuyente, rutEmisor, tipoDTE, folioDTE, formato XML|PDF)`, se implementan `GetXmlAsync` y `GetPdfAsync` con él y `SupportsDocumentDownload = true`. Si no existe, quedan en false.
  - `aprobarDTERecibidos` solo se prueba si `FACELE_ALLOW_RESPOND=1`, sobre un documento de prueba. Se anota la glosa de "ya respondido".
- [ ] **Step 5:** Tests en verde y registro en `Program.cs`: `AddScoped<IDteReceptionProvider, FaceleReceptionProvider>()`.
- [ ] **Step 6:** Commit `feat(dte): adaptador FACELE de recepción (DTEService)`.

### Task 3: Sincronizador de recibidos

**Files:**
- Create: `CORE/src/PortalSaas.Core/DocumentosElectronicos/DteReceptionSyncService.cs`, `CORE/src/PortalSaas.Core/DocumentosElectronicos/DteReceptionHostedService.cs`
- Modify: `CORE/src/PortalSaas.Host/Program.cs`
- Test: `CORE/tests/PortalSaas.Core.Tests/DocumentosElectronicos/DteReceptionSyncServiceTests.cs`

**Interfaces:**
- Produces:
  - `DteReceptionSyncService(PortalSaasDbContext db, DteSettingsService settings, TimeProvider clock, ILogger<...>)` con `Task<DteReceptionSyncResult> SyncCompanyAsync(Guid companyId, CancellationToken ct)`;
  - `record DteReceptionSyncResult(int New, int Updated, int MarkedExternal, string? Error)`;
  - `DteReceptionHostedService`, que cada 1 minuto recorre las compañías con `ReceptionEnabled` y `ReceptionLastSyncAt + ReceptionIntervalMinutes <= ahora`, con un scope y `ICurrentCompanyOverride.Set` por compañía (igual que `DteReconciliationHostedService`).
- Reglas del sincronizador:
  - **Ventana:**
    - si `ReceptionInitialLoadDoneAt` es null: `[hoy − InitialDays, hoy]`, y al terminar se fija `ReceptionInitialLoadDoneAt`;
    - si no: `[hoy − WindowDays, hoy]`;
    - "hoy" es la fecha Chile.
  - **Upsert** por `(company, issuer_tax_id, dte_type, folio)`.
    - En los nuevos se llama `GetReferencesAsync`. Las referencias con `Type == "801"` y folio numérico dan `PurchaseOrderRefs` con source `"dte"`.
    - En los nuevos también se pide `GetXmlAsync` si la capacidad existe.
    - `UpdatedAt` cambia solo si cambió algún dato.
  - **Respuesta fuera del portal.** `ClaimStatus` final = `Acepta_Contenido_Documento`, `Reclamo_*` u `Otorga_Recibo_Mercaderias`:
    - con `ResponseStatus` en `none` o `failed` y estado final → `external`, con `ResponseAction` mapeado;
    - con `pending` y estado final igual a la `ResponseAction` pendiente → `sent`;
    - con `pending` y estado final distinto → `external`.
  - **Errores:** un error se guarda en `ReceptionLastError`, sin credenciales, y no corta el resto de las compañías.

- [ ] **Step 1: Tests que fallan** (InMemory + `IDteReceptionProvider` fake + `FakeTimeProvider`):
  - `Sync_PrimeraVez_UsaDiasIniciales_YMarcaCargaInicial`;
  - `Sync_Siguiente_UsaVentana`;
  - `Sync_Nuevo_TraeReferencias_Y801EnOc`;
  - `Sync_Existente_SinCambios_NoTocaUpdatedAt`;
  - `Sync_RespondidoEnOperador_SinRespuestaPortal_QuedaExternal`;
  - `Sync_PendienteYOperadorCoincide_QuedaSent`;
  - `Sync_ErrorProveedor_GuardaErrorSinClave`: el mensaje no contiene la contraseña.
- [ ] **Step 2:** Correrlos. Esperado: FAIL.
- [ ] **Step 3:** Implementar y registrar `DteReceptionSyncService` (scoped) y `DteReceptionHostedService`.
- [ ] **Step 4:** Tests en verde y suite de Core en verde.
- [ ] **Step 5:** Commit `feat(dte): sincronizador de documentos recibidos con detección de respuestas externas`.

### Task 4: Contrato para módulos `IDteInboxService`

**Files:**
- Create: `CORE/src/PortalSaas.Abstractions/Contratos/IDteInboxService.cs`
- Modify: `CORE/src/PortalSaas.Abstractions/Modelos/Dte/DteInboxModels.cs`
- Create: `CORE/src/PortalSaas.Core/DocumentosElectronicos/DteInboxService.cs`, `CORE/src/PortalSaas.Core/DocumentosElectronicos/DteReceivedXmlReader.cs`
- Modify: `CORE/src/PortalSaas.Host/Program.cs`
- Test: `CORE/tests/PortalSaas.Core.Tests/DocumentosElectronicos/DteInboxServiceTests.cs`, `DteReceivedXmlReaderTests.cs`

**Interfaces (Abstractions, namespace `PortalSaas.Abstractions.Modelos.Dte`):**
- `static class DteResponseActions { AcceptContent="accept_content"; ClaimContent="claim_content"; ClaimPartialGoods="claim_partial_goods"; ClaimTotalGoods="claim_total_goods"; GoodsReceipt="goods_receipt"; }`
- `static class DteResponseStatuses { None="none"; Pending="pending"; Sent="sent"; Failed="failed"; External="external"; }`
- `record DteReceivedReference(string Type, string Folio, DateOnly? Date, int? Code, string? Reason)`
- `record DtePurchaseOrderRef(int DocNum, string Source, Guid? UserId, DateTimeOffset At)`, con `Source` = `"dte"` o `"manual"`
- `record DteReceivedLine(int LineNumber, string? ItemCode, string Name, decimal Quantity, decimal? UnitPrice, long Amount)`
- `sealed record DteReceivedDto`, con propiedades `init` espejo de `DteReceived`: `Id`, emisor, tipo, folio, fechas, montos y estados del operador, `References`, `PurchaseOrderRefs`, los campos `Response*` y `Processing*`, `LastError` y `UpdatedAt`. No incluye el `Xml`.
- `record DteReceivedFilter(DateOnly? ReceivedFrom = null, DateOnly? ReceivedTo = null, string? Text = null, int Limit = 1000)`
- `record DteInboxStatus(bool ReceptionEnabled, string? ProviderCode, IReadOnlyList<int> RespondableTypes, IReadOnlyList<string> SupportedActions, bool SupportsResponseNote, bool SupportsDocumentDownload, int InitialHistoryDays, int IntervalMinutes, DateTimeOffset? LastSyncAt, string? LastSyncError)`. `InitialHistoryDays` = `ReceptionInitialDays`.
- `record DteRespondRequest(long ReceivedId, string Action, string? Reason, string Actor, string ResponseKey)`, con `Actor` = `"user:<guid>"` o `"rule:<código>"`
- `enum DteRespondOutcome { Sent, PendingConfirmation, AlreadyResponded, Failed, NotRespondable }` y `record DteRespondResult(DteRespondOutcome Outcome, string? Error)`
- `interface IDteInboxService` (`PortalSaas.Abstractions.Contratos`):
  - `GetStatusAsync`
  - `ListAsync(DteReceivedFilter)`
  - `ListChangedSinceAsync(DateTimeOffset since, int max)`, en orden ascendente por `UpdatedAt`
  - `GetAsync(long id)`
  - `GetLinesAsync(long id)`, que devuelve vacía si no hay XML
  - `GetPdfAsync(long id)`, que devuelve null si no hay capacidad
  - `RespondAsync(DteRespondRequest)`
  - `SetPurchaseOrderRefsAsync(long id, IReadOnlyList<int> manualDocNums, Guid userId)`, que reemplaza solo las de source `"manual"`
  - `MarkProcessingAsync(long id, string moduleCode, string state, string? reference)`
  - `RequestSyncAsync()`, que sincroniza ya la compañía de la sesión
  - todos con `CancellationToken ct = default`.
- **`RespondAsync`, en este orden:**
  1. Si el tipo no está en `RespondableTypes` o la acción no está en `SupportedActions` → `NotRespondable`.
  2. Si el estado es `sent` o `external` → `AlreadyResponded`, sin llamar al operador.
  3. Si es `pending` con otra clave → `AlreadyResponded`. Si es `pending` con la misma clave, se reintenta.
  4. Si no: se guarda `pending` con la clave, acción, motivo, actor y `ResponseAttempts++`, se hace `SaveChanges` y se llama al proveedor.
     - `Ok` → `sent` y `RespondedAt`; si hay motivo y `SupportsResponseNote`, se llama `SetNoteAsync` (best-effort, sin tirar).
     - `AlreadyResponded` → queda `pending` con `LastError` "ya respondida en el operador; se confirma en la próxima sincronización" → `PendingConfirmation`.
     - Error de negocio → `failed` → `Failed`.
     - `DteProviderUnavailableException` → `failed` → `Failed`.
- `DteReceivedXmlReader.ReadLines(string xml) : IReadOnlyList<DteReceivedLine>`:
  - lee `Documento/Detalle` (`NroLinDet`, `CdgItem/VlrCodigo`, `NmbItem`, `QtyItem`, `PrcItem`, `MontoItem`) sin importar el namespace;
  - acepta ISO-8859-1.

- [ ] **Step 1: Tests que fallan**
  - `Respond_Ok_QuedaSent_YNoSeLlamaDosVeces`: segunda llamada con la misma clave → `AlreadyResponded` y el fake cuenta 1 llamada.
  - `Respond_FallaRed_QuedaFailed_YReintentoMismaClaveEnvia`
  - `Respond_ExternalNoLlamaAlOperador`
  - `Respond_TipoNoRespondible_NotRespondable` (tipo 61)
  - `Respond_OperadorDiceYaRespondida_PendingConfirmation`
  - `Respond_OtraCompania_NoEncuentra`: un id de otra compañía → `InvalidOperationException`.
  - `SetPurchaseOrderRefs_ConservaLasDelDte`
  - `ListChangedSince_OrdenAscendente_YRespetaMax`
  - `ReadLines_EjemploSii`, con un fixture XML de factura 33 con 2 líneas.
- [ ] **Step 2:** Correrlos. Esperado: FAIL.
- [ ] **Step 3:** Implementar y registrar `AddScoped<IDteInboxService, DteInboxService>()`.
- [ ] **Step 4:** Tests en verde y suite de Core en verde.
- [ ] **Step 5:** Commit `feat(dte): IDteInboxService para módulos (bandeja, respuesta idempotente, OC asociadas)`.

---

## Parte 2 — Plugin `Modulo.FacturasProveedor`

### Task 5: Esqueleto del plugin

**Files:**
- Create: `P/Modulo.FacturasProveedor.csproj`, copia de `DSP/Modulo.Despachos.csproj` sin PDFsharp, ZXing, Skia ni ClosedXML.
- Create: `FP/src/Modulo.FacturasProveedor.Migrations.Postgres/` y `.SqlServer/` (copia de Despachos, con la variable de entorno `FACTURAS_PROVEEDOR_CONNECTION_STRING` y la base de desarrollo `modulo_facturas_proveedor_dev`)
- Create: `T/Modulo.FacturasProveedor.Tests.csproj` y `T/Fakes/`. Se copian de Despachos `FakeCurrentUserContext`, `FakeCurrentCompanyAccessor`, `FakeNotificationService`, `FakeHanaService`, `FixedTimeProvider` y `TestDb`, y se crea `FakeDteInboxService`.
- Create: `P/ModuloFacturasProveedor.cs`, `P/Data/FacturasProveedorDbContext.cs`, `P/_ViewStart.cshtml`, `P/Pages/_ViewImports.cshtml`, `P/Pages/FacturasProveedorPageModelBase.cs`, `P/Pages/FacturasProveedorUi.cs`, `P/Dominio/ChileClock.cs`, `P/Dominio/RutChileno.cs`, `P/wwwroot/css/facturas-proveedor.css`
- Create: `FP/publish-dist.ps1` (`$moduleName = "Modulo.FacturasProveedor"`), `FP/PENDIENTE.md`
- Test: `T/ModuloFacturasProveedorTests.cs`

**Interfaces:**
- `ModuloFacturasProveedor : IModuloPortal`:
  - `ModuleCode = "FacturasProveedor"`, `Name = "Facturas de Proveedor"`, `RequiresExternalDatabase = true`, `ServesOwnWwwRoot = true`, `Version = "1.0.0"`.
  - Menú:
    - raíz `raiz`, `bi-receipt-cutoff`, Order 180;
    - `bandeja` `bi-inbox` `/facturas-proveedor/bandeja`;
    - `mis-aprobaciones` `bi-check2-square` `/facturas-proveedor/mis-aprobaciones`;
    - grupo `grupo-config` "Configuración" `bi-gear`, con `reglas` `bi-sliders`, `proveedores` `bi-person-exclamation`, `aprobadores` `bi-people` y `consultas-sap` `bi-database-gear`, todos en `/facturas-proveedor/configuracion/<código>`.
  - `RegisterServices` copia el bloque `AddDbContext` de `ModuloDespachos`.
- `FacturasProveedorPageModelBase(ICurrentUserContext)`, igual que `DespachosPageModelBase` pero con el prefijo `"FacturasProveedor."`.
- `FacturasProveedorUi.StatusLabel(string)` / `StatusBadgeClass(string)` (clases `badge-status--*`) para los estados de la Task 8:

  | Estado | Rótulo | Badge |
  |---|---|---|
  | `new` | Nueva | neutral |
  | `warning` | Aviso | warning |
  | `in_review` | Por decidir | warning |
  | `to_send` | Por enviar | neutral |
  | `accepted` | Aceptada | success |
  | `rejected` | Rechazada | danger |
  | `simulated` | Simulada | neutral |
  | `external` | Respondida fuera del portal | neutral |
  | `expired` | Vencida (aceptada por ley) | danger |
  | `out_of_scope` | Fuera de alcance | neutral |

- `facturas-proveedor.css`: `.fpr-kpis`, `.fpr-kpi`, `.fpr-kpi__label`, `.fpr-kpi__value`, iguales a `.dsp-kpi*` y solo con tokens `var(--...)`.
- `FakeDteInboxService : IDteInboxService`:
  - estado en memoria: `Items` (`List<DteReceivedDto>`) y `Status`;
  - registros: `Responses` (`List<DteRespondRequest>`), `ManualRefs`;
  - `NextRespondOutcome` configurable.

- [ ] **Step 1: Test que falla** `T/ModuloFacturasProveedorTests.cs`:
  - `ModuleCode_EsFacturasProveedor`;
  - `Menu_TodoItemTieneIconoYCodigoUnico`: todo `Icon` empieza con `bi-`, los códigos son únicos y toda ruta empieza con `/facturas-proveedor/`.
- [ ] **Step 2:** Correrlo. Esperado: FAIL (no compila).
- [ ] **Step 3:** Crear los proyectos y archivos listados.
- [ ] **Step 4:** Correr el test. Esperado: PASS. `.\FP\publish-dist.ps1` deja `FP/dist/Modulo.FacturasProveedor/1.0.0/Modulo.FacturasProveedor.dll`.
- [ ] **Step 5:** Commit `feat(facturas-proveedor): esqueleto del plugin`.

### Task 6: Catálogo de reglas y motor de decisión (dominio puro)

**Files:**
- Create: `P/Dominio/Reglas/ISupplierInvoiceRule.cs` (contrato y tipos), `P/Dominio/Reglas/InvoiceRuleEngine.cs`, `P/Dominio/Reglas/RuleSettingsResolver.cs`
- Create: `P/Dominio/Reglas/Catalogo/` con una clase por regla
- Test: `T/Dominio/Reglas/` (una clase de test por regla, más `InvoiceRuleEngineTests.cs` y `RuleSettingsResolverTests.cs`)

**Interfaces:**
- Constantes:
  - `static class RuleActions { Reject="reject"; WarnThenReject="warn_then_reject"; ManualReview="manual_review"; Accept="accept"; }`
  - `static class SupplierConditions { FixedMonthly="fixed_monthly"; AlwaysManual="always_manual"; Custom="custom"; }`
- `enum RuleScope { OcSuppliers, MarkedSuppliers, AllSuppliers, Pending }` y `enum RuleOutcomeKind { Pass, NotApplicable, Objection }`.
- Datos de definición y configuración:
  - `record RuleParameterDefinition(string Key, string Label, decimal DefaultValue, decimal Min, decimal Max, string Unit)`
  - `record EffectiveRuleSettings(bool IsActive, string Action, int? ActionDays, IReadOnlyDictionary<string, decimal> Parameters)`
  - `record RuleOutcome(RuleOutcomeKind Kind, string? Action, string? Message, string? DataJson)`, con `static Pass`, `static NotApplicable` y `static Objection(action, message, data?)`
- Datos de la factura y su contexto:
  - `record InvoiceFacts(long ReceivedId, string IssuerTaxId, int DteType, long Folio, DateOnly IssueDate, DateOnly SiiReceivedOn, long TotalAmount, IReadOnlyList<int> PurchaseOrderDocNums)`
  - `record PurchaseOrderFacts(int DocNum, int DocEntry, string Series, string CardCode, string? SupplierTaxId, DateOnly DocDate, decimal TotalLocal, string Status, decimal AlreadyInvoicedTotal)`, con `Status` = `"open"`, `"closed"` o `"canceled"`
  - `record SupplierProfileFacts(string Condition, IReadOnlyDictionary<string, RuleOverrideFacts> Overrides, long? ApproverGroupId)`
  - `record RuleOverrideFacts(string Mode, string? Action, int? ActionDays, IReadOnlyDictionary<string, decimal>? Parameters)`, con `Mode` = `"not_applicable"` o `"custom"`
  - `record SupplierFacts(bool ExistsInSap, bool IsActiveInSap, string? CardCode, SupplierProfileFacts? Profile)`
  - `record InvoiceEvaluationContext(InvoiceFacts Invoice, SupplierFacts Supplier, IReadOnlyList<PurchaseOrderFacts> PurchaseOrders, IReadOnlyList<int> UnresolvedDocNums, IReadOnlyList<int> AmbiguousDocNums)`
- `interface ISupplierInvoiceRule`:
  - definición: `Code`, `Name`, `Description`, `RuleScope Scope`, `IReadOnlyList<RuleParameterDefinition> Parameters`, `IReadOnlyList<string> AllowedActions`;
  - valores por defecto: `DefaultAction`, `int? DefaultActionDays`, `bool DefaultActive`, `bool CanBeDisabled`, `int DefaultOrder`;
  - `RuleOutcome Evaluate(InvoiceEvaluationContext ctx, EffectiveRuleSettings settings)`.
- Configuración y resultado del motor:
  - `record CompanyRuleConfiguration(IReadOnlyDictionary<string, EffectiveRuleSettings> ByRule, IReadOnlyDictionary<string, int> OrderByRule, string FinalActionWhenNoObjection)`
  - `record RuleResult(string RuleCode, RuleOutcomeKind Kind, string? Action, string? Message, string? DataJson)`
  - `record InvoiceDecision(string Action, string? RecommendedAction, int? WarnDays, string? WinningRuleCode, string? Message, IReadOnlyList<RuleResult> Results)`
- `RuleSettingsResolver.Resolve(CompanyRuleConfiguration company, SupplierProfileFacts? profile) : CompanyRuleConfiguration`:
  - `fixed_monthly` → las reglas con `Scope == OcSuppliers` quedan inactivas;
  - un override `not_applicable` → regla inactiva;
  - un override `custom` → reemplaza acción, días o parámetros (se fusionan por clave);
  - `CanBeDisabled == false` se respeta siempre.
- `InvoiceRuleEngine(IEnumerable<ISupplierInvoiceRule> rules)`:
  - `IReadOnlyList<ISupplierInvoiceRule> Catalog`, ordenado por `DefaultOrder`;
  - `InvoiceDecision Decide(InvoiceEvaluationContext ctx, CompanyRuleConfiguration config)`:
    1. omite las reglas con `Scope == Pending` y las inactivas;
    2. gana la objeción más severa (`reject` > `warn_then_reject` > `manual_review`); en empate gana el menor orden;
    3. sin objeciones → `config.FinalActionWhenNoObjection` con `WinningRuleCode = null`;
    4. con perfil `always_manual` → `Action = manual_review` y `RecommendedAction` = lo calculado en 2 o 3.
- Catálogo (código, clase, valores por defecto):

  | Código | Clase | Por defecto |
  |---|---|---|
  | `oc-referenciada` | `OcReferenciadaRule` | orden 1, `warn_then_reject` 3 días, activa |
  | `oc-mismo-proveedor` | `OcMismoProveedorRule` | orden 2, `reject`, activa |
  | `oc-antiguedad` | `OcAntiguedadRule` | orden 3, parámetro `max_days` 30 (1–365), `reject`, activa |
  | `oc-saldo` | `OcSaldoRule` | orden 4, parámetros `tolerance_pct` 0 (0–20) y `tolerance_amount` 0 (0–10 000 000), `reject`, activa |
  | `oc-abierta` | `OcAbiertaRule` | orden 5, `manual_review`, **inactiva** |
  | `proveedor-en-sap` | `ProveedorEnSapRule` | orden 6, Scope `AllSuppliers`, `manual_review`, activa |
  | `revision-manual-proveedor` | `RevisionManualProveedorRule` | orden 7, Scope `MarkedSuppliers`, `manual_review`, `CanBeDisabled = false` |
  | `plazo-por-vencer` | `PlazoPorVencerRule` | orden 8, Scope `Pending`, parámetro `days_before` 0 (0–5), acciones `accept`/`reject`, por defecto `reject`; `Evaluate` devuelve `NotApplicable` (la usa la Task 7) |

- Semántica:
  - **1:** sin OC resuelta → objeción con la acción configurada y el mensaje "La factura no referencia una OC existente en SAP". Si hay algún DocNum ambiguo → objeción `manual_review` forzada, con el mensaje "La OC {n} existe en más de una serie".
  - **2:** compara `RutChileno` normalizado del emisor contra `SupplierTaxId`; si falta el RUT en la OC, compara con el `CardCode` del proveedor resuelto.
  - **3:** `(IssueDate − DocDate).Days > max_days`.
  - **4:** `saldo = Σ(TotalLocal − AlreadyInvoicedTotal)` y `permitido = saldo + max(saldo × pct / 100, amount)`; objeción si `TotalAmount > permitido`. `DataJson` lleva `{ocTotal, alreadyInvoiced, balance, invoiceTotal}`.
  - **5:** alguna OC con `Status != "open"`.
  - **6:** `!ExistsInSap || !IsActiveInSap`.
  - **7:** perfil con `fixed_monthly` o `always_manual`.

- [ ] **Step 1: Tests que fallan** (uno o más por regla, más el motor):
  - `OcReferenciada_SinOc_ObjecionConDias`
  - `OcReferenciada_Ambigua_ForzaRevision`
  - `OcMismoProveedor_RutEnOtroFormato_Cumple`: `"76.412.553-k"` contra `"76412553-K"`
  - `OcMismoProveedor_OtroRut_Rechaza`
  - `OcAntiguedad_31Dias_Rechaza_30Cumple`
  - `OcSaldo_ParcialDentroDelSaldo_Cumple`: OC 3 200 000, ya facturado 1 000 000, factura 2 200 000
  - `OcSaldo_SuperaSaldo_Rechaza`: misma OC, factura 2 380 000
  - `OcSaldo_Tolerancia1Pct_Cumple`
  - `ProveedorEnSap_NoExiste_Revision`
  - `Engine_RechazoGanaAAvisoYRevision`
  - `Engine_SinObjeciones_UsaAccionFinal`
  - `Engine_SiempreManual_RecomiendaYMandaARevision`
  - `Resolver_GastoFijo_DesactivaReglasDeOc`
  - `Resolver_OverrideCustom_CambiaParametro`: `max_days` 60
  - `Resolver_NoPermiteDesactivarRegla7`
- [ ] **Step 2:** Correrlos. Esperado: FAIL.
- [ ] **Step 3:** Implementar. Las reglas se registran como `AddSingleton<ISupplierInvoiceRule, …>` y `AddSingleton<InvoiceRuleEngine>` en `RegisterServices`.
- [ ] **Step 4:** Tests en verde.
- [ ] **Step 5:** Commit `feat(facturas-proveedor): catálogo de reglas y motor de decisión`.

### Task 7: Plazos y saldo de OC (dominio puro)

**Files:**
- Create: `P/Dominio/ReviewDeadlines.cs`, `P/Dominio/OcBalanceCalculator.cs`, `P/Dominio/DueActionPlanner.cs`
- Test: `T/Dominio/ReviewDeadlinesTests.cs`, `OcBalanceCalculatorTests.cs`, `DueActionPlannerTests.cs`

**Interfaces:**
- `ReviewDeadlines`:
  - `static DateOnly LegalDeadline(DateOnly siiReceivedOn, int legalDays)` = `siiReceivedOn + legalDays` (último día hábil para responder);
  - `static DateOnly WarnDeadline(DateOnly siiReceivedOn, int warnDays, DateOnly legalDeadline)` = `min(siiReceivedOn + warnDays, legalDeadline − 1)`;
  - `static bool IsDue(DateOnly dueDate, TimeOnly cutoff, DateTime nowChile)` = el día ya pasó, o es hoy y ya pasó la hora de corte;
  - `static bool ShouldEscalate(DateOnly legalDeadline, int escalateDaysBefore, DateTime nowChile)` = `nowChile.Date >= legalDeadline − escalateDaysBefore`.
- `record InvoicedAgainstOc(string SupplierTaxId, long Folio, decimal Total)` y `OcBalanceCalculator.AlreadyInvoiced(IReadOnlyList<InvoicedAgainstOc> portal, IReadOnlyList<InvoicedAgainstOc> sap) : decimal`: suma con deduplicación por `(RutChileno normalizado, folio)`; si está en ambas listas, manda la del portal.
- `DueActionPlanner.Plan(DueSnapshot s, DueSettings settings, DateTime nowChile) : DueAction`:
  - `record DueSnapshot(string Status, DateOnly LegalDeadlineOn, DateOnly? WarnDeadlineOn, bool Escalated)`
  - `record DueSettings(TimeOnly Cutoff, int EscalateDaysBefore, bool DeadlineRuleActive, string DeadlineRuleAction, int DeadlineDaysBefore)`
  - `enum DueActionKind { None, RejectWarnExpired, ApplyDeadlineRule, Escalate, MarkExpired }` y `record DueAction(DueActionKind Kind, string? Action)`
  - prioridad:
    1. `MarkExpired`: estado en `new`, `warning`, `in_review` o `to_send` y `nowChile.Date > LegalDeadlineOn`;
    2. `RejectWarnExpired`: `warning` e `IsDue(WarnDeadlineOn, cutoff)`;
    3. `ApplyDeadlineRule`: `warning` o `in_review`, regla activa e `IsDue(LegalDeadlineOn − DeadlineDaysBefore, cutoff)`;
    4. `Escalate`: `warning` o `in_review`, sin escalar y `ShouldEscalate`;
    5. `None`.

- [ ] **Step 1: Tests que fallan**
  - `LegalDeadline_Recibida20Oct_Plazo8_28Oct`
  - `WarnDeadline_NuncaLlegaAlUltimoDia`: aviso 10 días con plazo 8 → `legal − 1`
  - `IsDue_MismoDiaAntesDelCorte_False_DespuesTrue`, con corte 10:00
  - `AlreadyInvoiced_DeduplicaPortalYSap`
  - `Plan_UltimoDiaDespuesDelCorte_AplicaReglaDePlazo`
  - `Plan_PasadoElPlazo_MarcaVencida`
  - `Plan_AvisoVencido_Rechaza`
  - `Plan_Escalamiento_UnaSolaVez`
- [ ] **Step 2:** Correrlos. Esperado: FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Tests en verde.
- [ ] **Step 5:** Commit `feat(facturas-proveedor): cálculo de plazos, saldo de OC y acciones por vencimiento`.

### Task 8: Modelo de datos, migración Inicial y configuración de reglas

**Files:**
- Create: `P/Models/` (`FpSettings`, `FpRuleSetting`, `FpConfigEvent`, `FpSupplierProfile`, `FpSupplierRuleOverride`, `FpApproverGroup`, `FpApproverGroupMember`, `FpInvoiceReview`, `FpReviewRuleResult`, `FpReviewEvent`, `FpSapQuery`), `P/Models/ReviewStatuses.cs`
- Modify: `P/Data/FacturasProveedorDbContext.cs`; migración `Inicial` (Postgres y SqlServer)
- Create: `P/Servicios/IModuleSettingsService.cs` + `ModuleSettingsService.cs`
- Test: `T/Servicios/ModuleSettingsServiceTests.cs`

**Interfaces:**
- **Tablas** (todas con `company_id` salvo las hijas; columnas según spec §11):
  - `fp_settings`:
    - `LegalDeadlineDays` 8, `EscalateDaysBefore` 2, `CutoffTime TimeOnly` 10:00, `FinalActionWhenNoObjection` `"accept"`;
    - `ProcessFromDate DateOnly?`, `Mode` `"simulation"`, `DailyEmailDigest` false, `FinanceGroupId long?`;
    - `InboxCursor DateTimeOffset?`, `LastDigestOn DateOnly?`, `UpdatedAt`, `UpdatedBy`.
  - `fp_rule_settings`: único `(company_id, rule_code)`.
  - `fp_supplier_profiles`: único `(company_id, tax_id)`.
  - `fp_supplier_rule_overrides`: único `(profile_id, rule_code)`.
  - `fp_approver_group_members`: PK `(group_id, user_id)`.
  - `fp_invoice_reviews`:
    - columnas:
      - identidad: `Id`, `CompanyId`, `ReceivedId long`, `IssuerTaxId`, `IssuerName`, `DteType`, `Folio`, `TotalAmount long`;
      - fechas: `SiiReceivedOn DateOnly`, `LegalDeadlineOn DateOnly`, `WarnDeadlineOn DateOnly?`;
      - decisión: `Status`, `ProposedAction?`, `RecommendedAction?`, `DecidedAction?`, `DecisionSource?`, `DecidedBy Guid?`, `DecidedAt?`, `Reason?(500)`, `ClaimType?(30)`, `AssignedGroupId long?`, `IsSimulated bool`, `DecisionNumber int`;
      - control: `EvaluationCount int`, `LastEvaluatedAt?`, `EscalatedAt?`, `Attempts int`, `LastError?(1000)`, `ConcurrencyStamp Guid`, `CreatedAt`, `UpdatedAt`;
    - único `(company_id, received_id)`;
    - `ConcurrencyStamp` como `IsConcurrencyToken`, regenerado en C# en cada cambio;
    - índice `(company_id, status)`.
  - `fp_review_rule_results`: `Id`, `ReviewId`, `RuleCode`, `Kind`, `Action?`, `Message?`, `DataJson?`. Solo guarda la última evaluación.
  - `fp_review_events`: `Id`, `ReviewId`, `OccurredAt`, `EventType(40)`, `UserId Guid?`, `Detail?(1000)`.
  - `fp_sap_queries`: único `(company_id, query_kind)`.
- **Estados** — `ReviewStatuses` (constantes): `New="new"`, `Warning="warning"`, `InReview="in_review"`, `ToSend="to_send"`, `Accepted="accepted"`, `Rejected="rejected"`, `Simulated="simulated"`, `External="external"`, `Expired="expired"`, `OutOfScope="out_of_scope"`.
  - `IsFinal(status)`: `accepted`, `rejected`, `external`, `expired` y `out_of_scope` son finales.
- **Servicio `ModuleSettingsService`** (`FacturasProveedorDbContext`, `InvoiceRuleEngine`, `ICurrentCompanyAccessor`, `ICurrentUserContext`, `TimeProvider`):
  - `Task<ModuleSettingsDto> GetAsync(CancellationToken ct)`: crea en forma perezosa la fila de `fp_settings` y una `fp_rule_settings` por regla del catálogo con sus valores por defecto. Así, una regla nueva del catálogo aparece sola.
  - `Task SaveGeneralAsync(GeneralSettingsInput input, CancellationToken ct)`. Valida:
    - plazo 1–30;
    - escalamiento entre 0 y plazo − 1;
    - `Mode = "active"` exige `FinanceGroupId` y `ProcessFromDate`;
    - los días de aviso de toda regla `warn_then_reject` deben ser menores que el plazo.
  - `Task SaveRuleAsync(RuleSettingsInput input, CancellationToken ct)`: acción dentro de `AllowedActions`, parámetros dentro de `Min`/`Max`, y `CanBeDisabled = false` no se desactiva.
  - Cada `Save*` escribe un `FpConfigEvent` (`EntityType` `"settings"` o `"rule"`, `BeforeJson`, `AfterJson`, `UserId`).
  - `Task<CompanyRuleConfiguration> GetEngineConfigurationAsync(CancellationToken ct)`.
- **DTO e inputs:**
  - `ModuleSettingsDto(GeneralSettingsDto General, IReadOnlyList<RuleSettingsDto> Rules)`;
  - `RuleSettingsDto` incluye la definición (nombre, descripción, scope, parámetros, acciones permitidas) más lo configurado;
  - `GeneralSettingsInput` y `RuleSettingsInput(string RuleCode, bool IsActive, string Action, int? ActionDays, IReadOnlyDictionary<string, decimal> Parameters)`.

- [ ] **Step 1: Tests que fallan**
  - `Get_PrimeraVez_CreaDefaultsDelCatalogo`: 8 reglas, `oc-abierta` inactiva, `Mode == "simulation"`
  - `SaveRule_AccionNoPermitida_Falla`
  - `SaveRule_ParametroFueraDeRango_Falla`
  - `SaveRule_DesactivarRegla7_Falla`
  - `SaveGeneral_AvisoMayorOIgualQuePlazo_Falla`
  - `SaveGeneral_ActivoSinFinanzasOSinFecha_Falla`
  - `Save_RegistraEventoDeAuditoria`
  - `GetEngineConfiguration_ReflejaLoGuardado`
- [ ] **Step 2:** Correrlos. Esperado: FAIL.
- [ ] **Step 3:** Implementar entidades, DbContext, las dos migraciones `Inicial` y el servicio, registrado como scoped.
- [ ] **Step 4:** Tests en verde y build de las migraciones.
- [ ] **Step 5:** Commit `feat(facturas-proveedor): modelo de datos y configuración de reglas con auditoría`.

### Task 9: Consultas SAP configurables

**Files:**
- Create: `P/Servicios/ISapLookupService.cs` + `SapLookupService.cs`
- Create: `P/Sql/Seeds/hana_purchase_order_by_docnum.sql`, `hana_invoiced_by_purchase_order.sql`, `hana_supplier_by_tax_id.sql`, `hana_supplier_search.sql`, como recursos embebidos
- Test: `T/Servicios/SapLookupServiceTests.cs`

**Interfaces:**
- `static class SapQueryKinds`:

  | Constante | Valor | Parámetro | Columnas |
  |---|---|---|---|
  | `PurchaseOrderByDocNum` | `"purchase_order_by_docnum"` | `:doc_num` | `doc_entry, doc_num, series, card_code, supplier_tax_id, doc_date, total_local, currency, status` |
  | `InvoicedByPurchaseOrder` | `"invoiced_by_purchase_order"` | `:doc_entry` | `supplier_tax_id, folio, total_amount_local, invoice_doc_entry` |
  | `SupplierByTaxId` | `"supplier_by_tax_id"` | `:tax_id` | `card_code, card_name, tax_id, is_active` |
  | `SupplierSearch` | `"supplier_search"` | `:text` | `card_code, card_name, tax_id, is_active` (máximo 20 filas) |

- Tipos:
  - `record SapPurchaseOrder(int DocEntry, int DocNum, string Series, string CardCode, string? SupplierTaxId, DateOnly DocDate, decimal TotalLocal, string Currency, string Status)`
  - `record SapSupplier(string CardCode, string CardName, string TaxId, bool IsActive)`
  - `record QueryValidationResult(bool IsValid, IReadOnlyList<string> MissingColumns, string? Error)`
- `ISapLookupService`:
  - `Task<IReadOnlyList<SapPurchaseOrder>> FindPurchaseOrdersAsync(int docNum, CancellationToken ct)`, que puede devolver más de una (DocNum ambiguo);
  - `Task<IReadOnlyList<InvoicedAgainstOc>> FindInvoicedAsync(int docEntry, CancellationToken ct)`, vacía si la consulta está inactiva;
  - `Task<SapSupplier?> FindSupplierByTaxIdAsync(string taxId, CancellationToken ct)`, con el RUT normalizado antes de consultar;
  - `Task<IReadOnlyList<SapSupplier>> SearchSuppliersAsync(string text, CancellationToken ct)`;
  - `Task<IReadOnlyList<SapQueryDto>> ListQueriesAsync(CancellationToken ct)`, que carga los seeds la primera vez;
  - `Task SaveQueryAsync(string kind, string sql, bool isActive, CancellationToken ct)`;
  - `Task<QueryValidationResult> TestQueryAsync(string kind, string sql, string sampleValue, CancellationToken ct)`.
- Validaciones, iguales a `DSP/Servicios/SapDocumentLookupService.cs`:
  - una sola sentencia `SELECT` o `WITH` y solo el parámetro de su tipo;
  - claves case-insensitive;
  - `status` en minúsculas.
- Seeds HANA:
  - **OC:** `OPOR T0`, `NNM1` (`"SeriesName"`) y `OCRD` (`"LicTradNum"`). `total_local` = `T0."DocTotal"`. `status` = `CASE WHEN T0."CANCELED"='Y' THEN 'canceled' WHEN T0."DocStatus"='C' THEN 'closed' ELSE 'open' END`.
  - **Facturado por OC:** `OPCH H` JOIN `PCH1 L` con `L."BaseType"=22 AND L."BaseEntry"=:doc_entry` y `H."CANCELED"='N'`. `total_amount_local` = `SUM(L."GTotal")`, agrupado por `H."DocEntry"`, `H."LicTradNum"` y `H."FolioNum"`.
  - **Proveedor:** `OCRD` con `"CardType"='S'` y RUT comparado sin puntos. `is_active` = `CASE WHEN "frozenFor"='Y' THEN 0 ELSE 1 END`.

- [ ] **Step 1: Tests que fallan** (`FakeHanaService`):
  - `FindPurchaseOrders_PasaDocNum_YMapea`
  - `FindPurchaseOrders_DosSeries_DevuelveAmbas`
  - `FindInvoiced_ConsultaInactiva_Vacio`
  - `FindSupplierByTaxId_NormalizaRut`: `"76.412.553-k"` → parámetro `"76412553-K"`
  - `Test_ColumnasFaltantes`
  - `Save_RechazaNoSelect`
  - `List_PrimeraVez_CargaSeeds`
- [ ] **Step 2:** Correrlos. Esperado: FAIL.
- [ ] **Step 3:** Implementar y registrar como scoped.
- [ ] **Step 4:** Tests en verde.
- [ ] **Step 5:** Commit `feat(facturas-proveedor): consultas SAP configurables (OC, facturado por OC, proveedor)`.

### Task 10: Proveedores con excepciones y aprobadores (servicios)

**Files:**
- Create: `P/Servicios/ISupplierProfileService.cs` + `SupplierProfileService.cs`, `P/Servicios/IApproverGroupService.cs` + `ApproverGroupService.cs`
- Test: `T/Servicios/SupplierProfileServiceTests.cs`, `ApproverGroupServiceTests.cs`

**Interfaces:**
- `SupplierProfileService` (DbContext, `ISapLookupService`, `ICurrentCompanyAccessor`, `ICurrentUserContext`, `TimeProvider`):
  - `ListAsync`, `GetAsync(long id)`, `DeleteAsync(long id)`;
  - `SaveAsync(SupplierProfileInput input)`:
    - `SupplierProfileInput(long? Id, string TaxId, string Condition, long? ApproverGroupId, long? BackupGroupId, DateOnly ValidFrom, DateOnly? ValidTo, string? Note, bool IsActive, IReadOnlyList<RuleOverrideInput> Overrides)`;
    - valida el RUT con `FindSupplierByTaxIdAsync`, que debe existir, y guarda una copia de `CardCode` y `SupplierName`;
    - `ValidTo >= ValidFrom`;
    - `fixed_monthly` y `always_manual` exigen `ApproverGroupId`;
    - los overrides validan contra el catálogo (acción y rangos);
    - escribe `FpConfigEvent` con `EntityType` `"supplier"`.
  - `Task<SupplierProfileFacts?> GetActiveFactsAsync(string taxId, DateOnly onDate, CancellationToken ct)`, que respeta la vigencia y `IsActive`.
- `ApproverGroupService`:
  - `ListAsync`, `SaveAsync(ApproverGroupInput(long? Id, string Name, bool IsFinance, bool IsActive, IReadOnlyList<Guid> MemberUserIds))`, `DeleteAsync` (bloqueado si un perfil o `fp_settings.FinanceGroupId` lo usa);
  - `Task<IReadOnlySet<long>> GetGroupIdsForUserAsync(Guid userId, CancellationToken ct)`;
  - `Task<IReadOnlyList<Guid>> GetMemberIdsAsync(long groupId, CancellationToken ct)`;
  - solo un grupo puede tener `IsFinance = true`; al marcar uno, se fija `fp_settings.FinanceGroupId`.
  - Los integrantes se eligen de `ITenantUserAdminService.ListAsync()`.

- [ ] **Step 1: Tests que fallan**
  - `Save_RutInexistenteEnSap_Falla`
  - `Save_GastoFijoSinAprobador_Falla`
  - `Save_GuardaCardCodeYNombreDeSap`
  - `GetActiveFacts_FueraDeVigencia_Null`
  - `Save_OverrideFueraDeRango_Falla`
  - `Groups_SoloUnFinanzas`
  - `DeleteGroup_EnUso_Falla`
  - `GetGroupIdsForUser`
- [ ] **Step 2:** Correrlos. Esperado: FAIL.
- [ ] **Step 3:** Implementar y registrar.
- [ ] **Step 4:** Tests en verde.
- [ ] **Step 5:** Commit `feat(facturas-proveedor): proveedores con excepciones y grupos de aprobadores`.

### Task 11: Orquestador — evaluación de facturas

**Files:**
- Create: `P/Servicios/IInvoiceReviewService.cs` + `InvoiceReviewService.cs`, `P/Servicios/ReviewContextBuilder.cs`
- Test: `T/Servicios/InvoiceReviewServiceTests.cs`

**Interfaces:**
- Consumes: `IDteInboxService` (Task 4), `InvoiceRuleEngine` y `RuleSettingsResolver` (Task 6), `ReviewDeadlines` y `OcBalanceCalculator` (Task 7), `IModuleSettingsService` (Task 8), `ISapLookupService` (Task 9), `ISupplierProfileService` (Task 10), `IApproverGroupService` (Task 10), `INotificationService`.
- `IInvoiceReviewService`:
  - `Task<ReviewCycleResult> EvaluatePendingAsync(CancellationToken ct)` y `Task EvaluateOneAsync(long reviewId, CancellationToken ct)`;
  - `record ReviewCycleResult(int Created, int Evaluated, int Changed)`.
- `EvaluatePendingAsync`, en orden:
  1. **Traer cambios:** `inbox.ListChangedSinceAsync(settings.InboxCursor ?? MinValue, 500)`, en bucle hasta vaciar; al final se actualiza `InboxCursor`.
  2. **Crear o actualizar revisiones** de los tipos en `RespondableTypes` (los demás se ignoran):
     - sin `ProcessFromDate`, o con `SiiReceivedOn` anterior a esa fecha → `out_of_scope`;
     - `ResponseStatus` `external` en el inbox y revisión no final → `external`. Conserva `IsSimulated` y `DecidedAction` para comparar. Solo en modo `active` se avisa a Finanzas ("Factura respondida fuera del portal").
     - `ResponseStatus` `sent` y revisión `to_send` → `accepted` o `rejected`, según `DecidedAction`.
  3. **Evaluar** las revisiones en `new`, `warning` e `in_review` sin decisión humana, ordenadas por `SiiReceivedOn` y luego `ReceivedId`.
     - `ReviewContextBuilder` arma el `InvoiceEvaluationContext` con caché por ciclo para las OC y los proveedores. Un `DocNum` con más de una OC va a `AmbiguousDocNums`.
     - `AlreadyInvoicedTotal` por OC = `OcBalanceCalculator.AlreadyInvoiced(portal, sap)`.
     - La lista *portal* son los documentos del inbox de los últimos `max(60, max_days + LegalDeadlineDays)` días que referencian la OC y cuentan como aceptados:
       - `ResponseStatus` `sent` o `external` con `ResponseAction` `accept_content`;
       - o `ClaimStatus` `Acepta_Contenido_Documento` u `Otorga_Recibo_Mercaderias`;
       - o plazo vencido sin reclamo;
       - o revisión `to_send` con `accept`;
       - o aceptada antes en este mismo ciclo.
     - La lista *sap* sale de `FindInvoicedAsync(docEntry)`.
  4. **Aplicar** `InvoiceDecision`:
     - `accept` o `reject`:
       - en `simulation` → `simulated`;
       - en `active` → `to_send`;
       - con `DecidedAction`, `DecisionSource = "rule:<código>"` (o `"rule:final"` sin objeciones), `Reason` = mensaje y `DecisionNumber++`.
     - `warn_then_reject` → `warning`, con `WarnDeadlineOn = WarnDeadline(...)` solo si estaba vacío. Al entrar por primera vez se avisa por campana al grupo de Finanzas ("Factura {folio} de {proveedor} sin OC: se rechaza el {fecha} si no se asocia").
     - `manual_review` → `in_review`, `AssignedGroupId` = grupo del perfil o, si no hay, `FinanceGroupId`. La primera vez se avisa por campana a los integrantes ("Factura {tipo} {folio} de {proveedor} requiere tu revisión", con URL `~/facturas-proveedor/bandeja/{id}`).
     - Se reemplazan los `FpReviewRuleResult`. Se escribe un `FpReviewEvent` (`"evaluated"`, con el detalle de la acción) solo si cambió la acción resultante.
- `LegalDeadlineOn` y `SiiReceivedOn` se fijan al crear la revisión (fecha Chile de `SiiReceivedAt`).

- [ ] **Step 1: Tests que fallan** (`FakeDteInboxService`, `FakeHanaService` con seeds, InMemory):
  - `Evaluate_RecibidaAntesDeProcesarDesde_FueraDeAlcance`
  - `Evaluate_SinProcesarDesde_TodoFueraDeAlcance`
  - `Evaluate_CalzaConOc_Simulacion_QuedaSimulada_SinResponder`: `Responses` vacío
  - `Evaluate_CalzaConOc_Activo_QuedaPorEnviar`
  - `Evaluate_SinOc_QuedaEnAvisoConFecha_YAvisaFinanzas`
  - `Evaluate_ExternaEnActivo_AvisaFinanzas_EnSimulacionNo`
  - `Evaluate_GastoFijo_VaARevision_YAvisaAlGrupo`
  - `Evaluate_DosParcialesMismaOc_LaSegundaSuperaSaldo_SeRechaza`: OC 3 200 000, facturas de 2 000 000 y 1 500 000 recibidas el mismo día
  - `Evaluate_AceptadaEnFaceleCuentaParaElSaldo`
  - `Evaluate_ExternaEnInbox_QuedaExterna`
  - `Evaluate_OcAsociadaDespues_SaleDeAviso`
  - `Evaluate_TipoNoRespondible_NoCreaRevision` (61)
- [ ] **Step 2:** Correrlos. Esperado: FAIL.
- [ ] **Step 3:** Implementar y registrar.
- [ ] **Step 4:** Tests en verde.
- [ ] **Step 5:** Commit `feat(facturas-proveedor): evaluación de facturas recibidas con reglas, saldo de OC y simulación`.

### Task 12: Vencimientos, envío al operador, avisos y job

**Files:**
- Create: `P/Servicios/IDueActionsService.cs` + `DueActionsService.cs`, `P/Servicios/FacturasProveedorBackgroundService.cs`
- Modify: `P/ModuloFacturasProveedor.cs` (`AddHostedService<FacturasProveedorBackgroundService>()`)
- Test: `T/Servicios/DueActionsServiceTests.cs`

**Interfaces:**
- `IDueActionsService.RunAsync(CancellationToken ct) : Task<DueRunResult>`, con `record DueRunResult(int Sent, int Failed, int Expired, int Escalated)`.
- **Vencimientos** (`DueActionPlanner`, Task 7) en las revisiones no finales:
  - `RejectWarnExpired` → reject con `DecisionSource = "rule:oc-referenciada"`.
  - `ApplyDeadlineRule` → acción de `plazo-por-vencer`, resuelta con el override del proveedor si existe, con `"rule:plazo-por-vencer"`.
  - En los dos casos anteriores, la revisión pasa a `simulated` en modo simulación y a `to_send` en activo.
  - `Escalate` → campana a los integrantes de Finanzas y del grupo asignado, y se fija `EscalatedAt`.
  - `MarkExpired` → `expired`, con evento y campana a Finanzas.
- **Envío** de las revisiones `to_send`:
  - llamada: `inbox.RespondAsync(new(ReceivedId, action, Reason, actor, $"fp:{review.Id}:{review.DecisionNumber}"))`;
  - `action` = `accept_content` para aceptar; para rechazar, `review.ClaimType ?? claim_content`;
  - `actor` = `DecisionSource` o `"user:<DecidedBy>"`.
  - Resultados:
    - `Sent` → `accepted` o `rejected`;
    - `PendingConfirmation` → sigue `to_send` (lo resuelve la Task 11 paso 2);
    - `AlreadyResponded` → `external`;
    - `NotRespondable` → `out_of_scope`;
    - `Failed` → `Attempts++` y `LastError`. Se avisa a Finanzas cuando `Attempts` llega a 3 o cuando `LegalDeadlineOn` es hoy (una sola vez por revisión, con el evento `"send_failed_notified"`).
- **Nunca se envía** una revisión en `simulated`, aunque el modo ahora sea `active`.
- **`FacturasProveedorBackgroundService`**, cada 5 minutos:
  - recorre `IExternalDatabaseConnectionService.ListActiveCompanyIdsAsync("FacturasProveedor")`;
  - por compañía: crea un scope, llama `ICurrentCompanyOverride.Set`, luego `EvaluatePendingAsync` y después `RunAsync`;
  - si `DailyEmailDigest` está activo y `LastDigestOn` es anterior a hoy y ya son las 08:00 Chile, envía el resumen diario: `IEmailSenderService` a cada integrante con pendientes, usando la dirección de `IUserContactLookupService` (patrón `RendicionesReminderBackgroundService`);
  - una excepción en una compañía se loguea y no corta las demás. El recorrido vive en `internal static Task RunForCompaniesAsync(IReadOnlyList<Guid> companyIds, Func<Guid, Task> work, ILogger logger)`, para poder probarlo.

- [ ] **Step 1: Tests que fallan**
  - `Run_PorEnviar_EnviaConClaveEstable_YQuedaAceptada`
  - `Run_RespondidaEnOperador_NoEnvia_QuedaExterna`: el inbox devuelve `AlreadyResponded`
  - `Run_FallaTresVeces_AvisaFinanzasUnaVez`
  - `Run_FallaElUltimoDia_AvisaFinanzas`
  - `Run_AvisoVencido_Rechaza`
  - `Run_UltimoDia_ReglaPlazoRechaza`
  - `Run_UltimoDia_OverrideProveedorAcepta`
  - `Run_PasoAActivo_NoEnviaLasSimuladas`
  - `Run_PasadoElPlazo_Vencida`
  - `Run_RechazoUsaTipoDeReclamoElegido`: `claim_partial_goods`
  - `Job_UnaCompaniaFalla_SigueConLasDemas`: tres compañías, la segunda lanza y la tercera igual se procesa
- [ ] **Step 2:** Correrlos. Esperado: FAIL.
- [ ] **Step 3:** Implementar y registrar.
- [ ] **Step 4:** Tests en verde.
- [ ] **Step 5:** Commit `feat(facturas-proveedor): vencimientos, envío al operador, avisos y job por compañía`.

### Task 13: Decisiones humanas y asociación de OC

**Files:**
- Create: `P/Servicios/IReviewDecisionService.cs` + `ReviewDecisionService.cs`
- Test: `T/Servicios/ReviewDecisionServiceTests.cs`

**Interfaces:**
- `Task<bool> CanDecideAsync(long reviewId, CancellationToken ct)`: el usuario puede decidir si:
  - es integrante del grupo asignado o del suplente del perfil;
  - o es integrante del grupo de Finanzas y tiene `HasActionAsync("FacturasProveedor.bandeja", "EDIT")`.
- `Task DecideAsync(long reviewId, string action, string? reason, string? claimType, Guid concurrencyStamp, CancellationToken ct)`:
  - `action` es `accept` o `reject`;
  - solo con la revisión en `in_review`, o en `warning` si decide Finanzas;
  - `reject` exige motivo de 5 caracteres o más, y `claimType` dentro de los `claim_*` de `inbox.GetStatusAsync().SupportedActions`;
  - fija `DecidedAction`, `DecisionSource = "user"`, `DecidedBy`, `DecidedAt`, `Reason`, `ClaimType` y `DecisionNumber++`;
  - estado `to_send` en activo, `simulated` en simulación;
  - si el stamp no coincide (`DbUpdateConcurrencyException`) → `InvalidOperationException("Otra persona ya decidió esta factura.")`.
- `Task<string?> GetAcceptWarningAsync(long reviewId, CancellationToken ct)`: recalcula la regla 4 con las OC de la factura; si se supera el saldo, devuelve "Aceptarla supera el saldo de la OC {n} en {monto}". No bloquea.
- `Task AssociatePurchaseOrderAsync(long reviewId, int docNum, CancellationToken ct)`:
  - exige `bandeja` EDIT;
  - `FindPurchaseOrdersAsync(docNum)` debe devolver alguna OC del mismo RUT; si no → `InvalidOperationException` clara;
  - llama `inbox.SetPurchaseOrderRefsAsync` con las manuales existentes más la nueva;
  - si la revisión no es final → `EvaluateOneAsync(reviewId)`;
  - evento `"po_associated"`.

- [ ] **Step 1: Tests que fallan**
  - `Decide_NoIntegrante_Falla`
  - `Decide_Finanzas_PuedeDecidirAviso`
  - `Decide_RechazoSinMotivo_Falla`
  - `Decide_Concurrencia_SegundoFalla`
  - `Decide_Simulacion_QuedaSimulada`
  - `AcceptWarning_SuperaSaldo_Mensaje`
  - `Associate_OcDeOtroProveedor_Falla`
  - `Associate_Valida_Reevalua`
  - `Associate_EnAceptada_SoloActualizaRefs`
- [ ] **Step 2:** Correrlos. Esperado: FAIL.
- [ ] **Step 3:** Implementar y registrar.
- [ ] **Step 4:** Tests en verde.
- [ ] **Step 5:** Commit `feat(facturas-proveedor): decisión de aprobadores y asociación manual de OC`.

### Task 14: Pantallas operativas (Bandeja, Detalle, Mis aprobaciones)

**Files:**
- Create: `P/Pages/FacturasProveedor/Bandeja/Index.cshtml(.cs)`, `Bandeja/Detalle.cshtml(.cs)`, `MisAprobaciones/Index.cshtml(.cs)`
- Create: `P/Servicios/IReviewQueryService.cs` + `ReviewQueryService.cs` (listados, KPI y detalle; une la revisión con `DteReceivedDto`)
- Test: `T/Pages/BandejaPermisosTests.cs`, `T/Servicios/ReviewQueryServiceTests.cs`

**Interfaces:**
- `ReviewQueryService`:
  - `ListAsync(ReviewListFilter(DateOnly? From, DateOnly? To, string StatusGroup, string? Text))`, con `StatusGroup`:
    - `"pending"`: `warning`, `in_review` o `to_send`;
    - `"all"`, `"accepted"`, `"rejected"`;
    - `"simulation"`: `IsSimulated = true` en cualquier estado. La fila trae `SimulatedAction` y `OperatorAction` (la `ResponseAction` del inbox) y `Matches` (true, false, o null si aún no se responde). La tabla muestra la columna "Coincide";
  - `GetKpisAsync()` → `ReviewKpis(int InReview, int Warning, int DueIn2Days, int AcceptedMonth, int RejectedMonth)`;
  - `GetDetailAsync(long id)` → `ReviewDetail(ReviewRow Row, DteReceivedDto Document, IReadOnlyList<FpReviewRuleResult> Rules, IReadOnlyList<DteReceivedLine> Lines, IReadOnlyList<FpReviewEvent> Events, string? AcceptWarning, bool CanDecide, bool CanAssociate)`;
  - `ListMineAsync()`.
- **Páginas**, con la estructura y el texto del boceto v2:
  - **Bandeja** (`@page "/facturas-proveedor/bandeja"`, `MenuCode = "bandeja"`):
    - alerta de vencimientos;
    - `fpr-kpis`;
    - `filter-card` con Desde, Hasta, Estado y texto;
    - tabla con las columnas del boceto (Recibida SII, Proveedor/RUT, Documento, Total, OC, Resultado de reglas, Estado, Plazo y abrir);
    - botón "Leer ahora" (`OnPostLeerAhoraAsync` = `inbox.RequestSyncAsync()` y luego `EvaluatePendingAsync()`).
    - En modo simulación, banner "Modo simulación: el portal no responde al SII".
  - **Detalle** (`@page "/facturas-proveedor/bandeja/{id:long}"`):
    - encabezado con folio, badge y proveedor;
    - botones: Volver, PDF (`OnGetPdfAsync`, solo si `SupportsDocumentDownload`), Rechazar (drawer con tipo de reclamo y motivo), Aceptar, y Asociar OC (drawer con DocNum);
    - `doc-tabs`: Resumen (`dl.row`), Evaluación de reglas, Detalle del documento (oculta sin líneas) e Historial;
    - en simulación, se muestra "Decisión simulada" junto a lo respondido en el operador.
  - **Mis aprobaciones** (`MenuCode = "mis-aprobaciones"`): tabla de pendientes de los grupos del usuario.
  - Todas: `~/` en hrefs, `table-responsive-lg`, `flex-wrap`, sin estilos propios fuera de `facturas-proveedor.css`.

- [ ] **Step 1: Tests que fallan**
  - `Bandeja_SinVer_Forbid`
  - `Detalle_SinEditar_NoMuestraAsociar`
  - `Detalle_PostAceptar_SinPermiso_Forbid`
  - `MisAprobaciones_SoloDeMisGrupos`
  - `Kpis_CuentaVencenEn2Dias`
  - `List_Pendientes_IncluyeAvisoRevisionYPorEnviar`
  - `List_Simulacion_MarcaCoincidencia`: simulada `accept` y operador `accept_content` → `Matches == true`; simulada `reject` y operador `accept_content` → `false`
- [ ] **Step 2:** Correrlos. Esperado: FAIL.
- [ ] **Step 3:** Implementar servicio y páginas.
- [ ] **Step 4:** Tests en verde. También en verde `UrlsRelativasAlPathBaseTests` y `VistasResponsivasTests` (suite de Core), que recorren `Portal SaaS - Plugins`.
- [ ] **Step 5:** Commit `feat(facturas-proveedor): bandeja, detalle con evaluación de reglas y mis aprobaciones`.

### Task 15: Pantallas de configuración

**Files:**
- Create: `P/Pages/FacturasProveedor/Configuracion/Reglas/Index.cshtml(.cs)`, `Proveedores/Index.cshtml(.cs)`, `Aprobadores/Index.cshtml(.cs)`, `ConsultasSap/Index.cshtml(.cs)`
- Test: `T/Pages/ConfiguracionPermisosTests.cs`

**Interfaces:**
- **Reglas:**
  - formulario de parámetros generales: plazo, escalar, hora de corte, si ninguna objeta, procesar desde, modo, grupo de Finanzas (solo lectura, se define en Aprobadores) y correo diario;
  - frecuencia de lectura del operador en solo lectura, desde `inbox.GetStatusAsync()`, con el texto "se configura en Administración › Documentos electrónicos";
  - tabla del catálogo con edición en drawer, generada desde `RuleSettingsDto.Parameters` y `AllowedActions`;
  - pasar a modo Activo pide confirmación con `data-confirm`;
  - **advertencia de cobertura** (spec §7.5): si `max_days` de `oc-antiguedad` + plazo legal es mayor que `InitialHistoryDays` de `inbox.GetStatusAsync()`, se muestra un `alert-warning` "Las facturas anteriores a los {n} días cargados no se cuentan en el saldo de la OC". En Proveedores se muestra la misma advertencia cuando un override amplía `max_days` por sobre ese límite.
- **Proveedores:**
  - tabla con buscador `filter-card`;
  - drawer `drawer-panel--wide`:
    - buscador SAP (`OnGetBuscarProveedorAsync`, que devuelve JSON con `SearchSuppliersAsync`);
    - condición en radios;
    - ajuste por regla, solo con las reglas de `Scope != Pending` más `plazo-por-vencer`;
    - aprobador, suplente, vigencia, nota y activo.
- **Aprobadores:**
  - grupos con integrantes, elegidos con un multiselect desde `ITenantUserAdminService.ListAsync()`, mostrando `FullName (Username)`;
  - marca de Finanzas.
- **Consultas SAP:**
  - las 4 consultas, con editor `textarea`, "Probar" con un valor de muestra y la lista de columnas faltantes (patrón `DSP/Pages/Despachos/Configuracion/ConsultasSap`).
- Permisos: `VIEW` para ver y `EDIT`, `CREATE` o `DELETE` para los handlers de cada menú.

- [ ] **Step 1: Tests que fallan**
  - `Reglas_GuardarSinEditar_Forbid`
  - `Reglas_PostAvisoInvalido_MuestraError`
  - `Reglas_AntiguedadMayorQueHistorial_MuestraAdvertencia`: `max_days` 90, plazo 8, historial 60
  - `Proveedores_SinCrear_Forbid`
  - `ConsultasSap_ProbarSinEditar_Forbid`
- [ ] **Step 2:** Correrlos. Esperado: FAIL.
- [ ] **Step 3:** Implementar las páginas.
- [ ] **Step 4:** Tests del plugin y guardianes de Core en verde.
- [ ] **Step 5:** Commit `feat(facturas-proveedor): pantallas de configuración (reglas, proveedores, aprobadores, consultas SAP)`.

### Task 16: Puesta en marcha (QA, después PROD)

**Files:**
- Modify: `FP/PENDIENTE.md`, `build-all.ps1` (si lista los plugins a publicar), la memoria `modulo-facturas-proveedor-estado.md`

- [ ] **Step 1:** Suites completas en verde (Core y plugin). `.\FP\publish-dist.ps1` deja `dist/Modulo.FacturasProveedor/1.0.0/` con `.deps.json`.
- [ ] **Step 2:** Aplicar la migración `AddDteReception` en `portalsaas_saas_qa` y `ps_comdepor_qa`. Verificar en `__EFMigrationsHistory`. **Pedir confirmación** y aplicar en `portalsaas_saas_prod` y `ps_comdepor`.
- [ ] **Step 3:** Bases del módulo: el usuario las crea o confirma. Nombres propuestos: `ps_comdepor_fp_qa` y `ps_comdepor_fp`, en la instancia de módulos. Aplicar `Inicial` con `dotnet ef database update --project FP/src/Modulo.FacturasProveedor.Migrations.Postgres` (variable de entorno `FACTURAS_PROVEEDOR_CONNECTION_STRING`) y verificar.
- [ ] **Step 4:** Alta en el admin, igual que Despachos:
  - módulo `FacturasProveedor`, contratado a la organización;
  - conexión externa por compañía;
  - perfiles con las acciones de la spec §13.
- [ ] **Step 5:** Configurar la recepción en `/Admin › Compañía › Documentos electrónicos` (QA con caoba): URL `https://caoba.docele.cl/cl-dol`, correo, intervalo 30 y "Probar recepción".
- [ ] **Step 6:** Junction `Portal SaaS - Core\artifacts\plugins\Modulo.FacturasProveedor` → `FP\dist\Modulo.FacturasProveedor`. Reiniciar el Host. El log no debe tener "Colisión de ruta Razor" ni errores de carga.
- [ ] **Step 7:** E2E en QA, en modo **simulación**:
  - probar las 4 consultas SAP con una OC y un RUT reales;
  - crear un grupo de Finanzas, un proveedor de gasto fijo y "procesar desde" = hoy − 7;
  - confirmar que la bandeja carga, la evaluación coincide con lo esperado en 5 facturas reales y nada se envía.
  - Anotar el resultado en `PENDIENTE.md`.
- [ ] **Step 8:** Commit `chore(facturas-proveedor): puesta en marcha en QA (simulación)`. El paso a Activo lo decide Finanzas en la pantalla, después de cerrar con el asesor los puntos [validar] de la spec §16.
