# Guías Res. 154 desde el manifiesto (Fase A) + Pasarela DTE P0 — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Objetivo:** al despachar un manifiesto de facturas, emitir por FACELE las guías de despacho electrónicas (52), con los datos de transporte de la Res. 154 y una línea por artículo. Todo pasa por una pasarela DTE de plataforma que después se reutiliza para otros documentos y proveedores.

**Arquitectura:**
- **Plataforma (`Portal SaaS - Core`):**
  - un modelo canónico del DTE del SII y sus contratos en `PortalSaas.Abstractions`;
  - un registro central de emisiones (`dte_emissions`) con una máquina de estados idempotente y un reconciliador en segundo plano;
  - un adaptador FACELE (SOAP DoceleOL) elegido por la configuración DTE de la compañía de la sesión.
- **Despachos:**
  - una consulta SAP nueva trae las líneas de cada factura;
  - un planificador puro agrupa los documentos en guías;
  - un servicio de guías persiste el plan (`transport_guides`) y emite por la pasarela al despachar;
  - si la emisión falla, el manifiesto queda en "guías pendientes" y se puede reintentar.

**Tecnología:** .NET 8, Razor Pages, EF Core 8.0.8 (Postgres + SQL Server, migraciones duales), xUnit + EF InMemory, `HttpClient` SOAP a mano (sin WCF), PDFsharp-MigraDoc 6.1.1 (ya en Despachos) para unir PDF.

**Specs:**
- Plataforma: `Portal SaaS - Core/docs/superpowers/specs/2026-10-22-pasarela-documentos-electronicos-design.md` (alcance **P0**).
- Despachos: `Portal SaaS - Plugins/Modulo.Despachos/docs/superpowers/specs/2026-10-22-modulo-despachos-etapa2-guias-res154-design.md` (**Fase A**, §5.2, §5.3b, §6).

Las rutas de este plan son relativas a la raíz del repo (`C:\PROYECTOS\Proyecto Portal Web-Company\`). Abreviaturas:
- `CORE` = `Portal SaaS - Core`;
- `DSP` = `Portal SaaS - Plugins/Modulo.Despachos`.

## Global Constraints

- **Aislamiento por compañía:** toda tabla nueva lleva `company_id`. El proveedor y las credenciales se resuelven **solo** por `ICurrentCompanyAccessor.CompanyId`, sin respaldo a otra compañía ni a la organización. Sin compañía en sesión, se lanza `InvalidOperationException` con un mensaje claro.
- **Motor dual:** `CORE/src/PortalSaas.Data` nunca referencia un paquete de proveedor. Los valores por defecto (Guid, fechas) se generan en C#. Hay que generar **las dos** migraciones (PostgreSql y SqlServer) con los comandos de `CORE/CLAUDE.md` ("Cómo generar/aplicar migraciones").
- **Nombres de BD:** inglés, plural, `snake_case`, `id`, `<entidad>_id`, `_at`, `is_`, `status`.
- **Credenciales:**
  - contraseña del proveedor cifrada con `ISecretoCifradoService` y de solo escritura en pantalla;
  - nunca en logs, en mensajes de excepción, en el repositorio ni en los fixtures de tests;
  - en los tests de integración, solo por las variables de entorno `FACELE_USER` / `FACELE_PASS`.
- **Mensajes al usuario en español.** Las violaciones de reglas lanzan `InvalidOperationException` con un texto apto para el usuario (patrón de Despachos).
- **Avisos por la campana:** `INotificationService.TryEnqueueAsync` (regla de plataforma) cuando una guía falla.
- **Formularios:** cualquier `<input type="number">` con `value` interpolado usa `CultureInfo.InvariantCulture`.
- **XML a FACELE:** DTE estándar del SII (`<DTE version="1.0"><Documento ID=...>`), **sin** `Folio` (o 0), sin `TED`, sin `TmstFirma`, sin firma, codificado **ISO-8859-1**.
  - Orden de `Transporte`: `Patente`, `PatenteCarro`, `RUTTrans`, `Chofer(RUTChofer, NombreChofer)`, `DirDest`, `CmnaDest`, `CiudadDest`, `Aduana`, `FchSalida`, `HraSalida`, `FchLlegada`. No existe `HraLlegada`.
  - En la guía 52 con montos 0 va siempre `TasaIVA` 19.
- **Límites del formato:** `Detalle` máximo **60** líneas por DTE y `Referencia` máximo **40**.
- **FACELE:**
  - `uuid` = `emission_key`;
  - el PDF viene en base64 **dos veces**;
  - nunca se llama `obtieneDTE` sobre un folio liberado o anulado;
  - la autenticación va en los headers **HTTP** `facele.user` / `facele.pass`.
- **Migraciones aplicadas:**
  - plataforma: en las 4 bases (`portalsaas_saas_prod`, `portalsaas_saas_qa`, `ps_comdepor`, `ps_comdepor_qa`), regla `regla-migraciones-4-bases`;
  - Despachos: en `ps_comdepor_dsp` y `ps_comdepor_dsp_qa`.
  - **Pedir confirmación al usuario antes de aplicar en producción.**
- **Comandos de test:**
  - Core: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests"`;
  - Despachos: `dotnet test "Portal SaaS - Plugins/Modulo.Despachos/src/Modulo.Despachos.Tests"` (línea base: 302 en verde).

## Review Focus

1. **Corte de red después de que `generaDTE` asignó folio:** el reintento no debe crear un segundo folio. FACELE rechaza el uuid repetido con "UUID [...] ya utilizado para DTE: [... Folio: N ...]"; la pasarela toma ese N como el folio y confirma (Task 6, test `Emit_UuidYaUtilizado_RecuperaFolioYConfirma`).
2. **Doble clic en Despachar o dos usuarios a la vez:** no se planifican dos juegos de guías ni dos emisiones. Índices únicos `(company_id, emission_key)` en `dte_emissions` y `(manifest_id, sequence)` en `transport_guides`; el estado del manifiesto se revalida dentro de la escritura (Task 11, test `Dispatch_DosVeces_NoDuplicaGuias`).
3. **Credenciales en errores:** un SOAP fault o un HTTP 405 nunca exponen la contraseña ni los headers en `last_error`, en logs o en pantalla (Task 5, test `Soap_Http405_ErrorSinCredenciales`).
4. **Caracteres en nombres y direcciones:**
   - `&`, `<` y comillas en la razón social ("Comercial Depor & Cía") quedan escapados;
   - las tildes y la Ñ viajan en ISO-8859-1;
   - un carácter fuera de Latin-1 ("–", "“", emoji) se reemplaza, no rompe el envío (Task 4, test `Build_EscapaYNormalizaLatin1`).
5. **Cambio de compañía:**
   - una emisión o una guía de la compañía A nunca se lee ni se reintenta desde la B;
   - el reconciliador fija `ICurrentCompanyOverride` por compañía y usa la configuración de esa compañía (Task 6 `Get_OtraCompania_Null`, Task 7 `Reconciler_UsaConfiguracionDeCadaCompania`).

---

## Parte 1 — Plataforma (P0)

### Task 1: Modelo canónico DTE, contratos y reglas de validación

**Files:**
- Create: `CORE/src/PortalSaas.Abstractions/Modelos/Dte/DteDocument.cs`. Records del modelo canónico: `DteDocument`, `DteIdDoc`, `DteIssuerOverride`, `DteReceiver`, `DteTransport`, `DteDriver`, `DteCustoms`, `DteTotals`, `DteLine`, `DteReference`, `DteExtraField`.
- Create: `CORE/src/PortalSaas.Abstractions/Modelos/Dte/DteEmissionModels.cs`. Contiene `DteEmissionRequest`, `DteEmissionResult`, `DteEmissionStatuses`, `DtePreviewResult`, `DteConfigurationStatus`, `DteValidationError`.
- Create: `CORE/src/PortalSaas.Abstractions/Contratos/IDteEmissionService.cs`
- Create: `CORE/src/PortalSaas.Core/DocumentosElectronicos/DteRules.cs`
- Test: `CORE/tests/PortalSaas.Core.Tests/DocumentosElectronicos/DteRulesTests.cs`

**Interfaces:**
- Produces (Abstractions, namespace `PortalSaas.Abstractions.Modelos.Dte`):
  - `DteDocument(DteIdDoc IdDoc, DteIssuerOverride? Issuer, DteReceiver Receiver, DteTransport? Transport, DteTotals? Totals, IReadOnlyList<DteLine> Lines, IReadOnlyList<DteReference> References, IReadOnlyList<DteExtraField> Extras)`
    - `Totals` null = la plataforma los calcula desde las líneas.
  - `DteIdDoc(int DteType, DateOnly IssueDate, int? TransferIndicator, int? DispatchType)`
    - `TransferIndicator` = `IndTraslado`; `DispatchType` = `TipoDespacho`.
  - `DteIssuerOverride(string? OriginAddress, string? OriginCommune, string? OriginCity)`
    - Sobrescribe solo `DirOrigen` / `CmnaOrigen` / `CiudadOrigen`. RUT, razón social, giro y Acteco salen siempre de la configuración.
  - `DteReceiver(string TaxId, string Name, string? Activity, string? Address, string? Commune, string? City)`
  - `DteTransport(string? Plate, string? TrailerPlate, string? CarrierTaxId, DteDriver? Driver, string? DestinationAddress, string? DestinationCommune, string? DestinationCity, DteCustoms? Customs, DateOnly? DepartureDate, TimeOnly? DepartureTime, DateOnly? ArrivalDate)`
  - `DteDriver(string TaxId, string Name)`
  - `DteCustoms(decimal? NetWeightKg, int? TotalPackages, string? CarrierName)`
  - `DteTotals(decimal Net, decimal Exempt, decimal VatRate, decimal Vat, decimal Total)`
  - `DteLine(int LineNumber, string? ItemCode, string Name, string? Description, decimal Quantity, string? Unit, decimal? UnitPrice, decimal Amount)`
  - `DteReference(int LineNumber, string DocumentType, long? Folio, DateOnly Date, int? ReasonCode, string? Reason)`
    - `DocumentType` es string porque admite "33", "801", "HES".
  - `DteExtraField(string Key, string Value)`
  - `DteEmissionStatuses`, constantes string: `Pending = "pending"`, `FolioReserved = "folio_reserved"`, `Issued = "issued"`, `Failed = "failed"`, `Released = "released"`, `Abandoned = "abandoned"`. Además `IsFinal(string)`, verdadero para issued, released y abandoned.
  - `DteEmissionRequest(Guid EmissionKey, string SourceModule, string SourceRef, DteDocument Document)`
  - `DteEmissionResult(Guid EmissionKey, string Status, long? Folio, string? Error, string ProviderCode, string Environment)`
  - `DteValidationError(string Field, string Message)`
  - `DtePreviewResult(IReadOnlyList<DteValidationError> Errors, byte[]? Pdf)`
  - `DteConfigurationStatus(bool IsConfigured, string? ProviderCode, string? Environment, string? IssuerTaxId, IReadOnlyList<int> SupportedTypes, string? Error)`
- Produces (contrato `PortalSaas.Abstractions.Contratos.IDteEmissionService`). Todos los métodos reciben `CancellationToken ct = default`.
  - `Task<DteConfigurationStatus> GetConfigurationAsync(...)`
  - `Task<IReadOnlyList<DteValidationError>> ValidateAsync(DteDocument doc, ...)`
  - `Task<DtePreviewResult> PreviewAsync(DteDocument doc, ...)`
  - `Task<DteEmissionResult> EmitAsync(DteEmissionRequest request, ...)`
  - `Task<DteEmissionResult?> GetAsync(Guid emissionKey, ...)`
  - `Task<byte[]> GetPdfAsync(Guid emissionKey, bool cedible, int copies, ...)`
  - `Task AbandonAsync(Guid emissionKey, string reason, ...)`: libera el folio reservado y no confirmado, o marca abandonada una emisión pendiente. Falla si ya está emitida.
- Produces (Core): `public static class DteRules` con `IReadOnlyList<DteValidationError> Validate(DteDocument doc)`.
  - Reglas comunes:
    - RUT del receptor válido (módulo 11, mismo algoritmo que `RutChileno` de Despachos; se copia en `CORE/src/PortalSaas.Core/DocumentosElectronicos/Rut.cs`);
    - nombre del receptor no vacío;
    - entre 1 y 60 líneas, con cantidad > 0 y nombre no vacío;
    - máximo 40 referencias.
  - Reglas del tipo 52:
    - `TransferIndicator` entre 1 y 9;
    - `Transport` no nulo con `Plate`, `CarrierTaxId` (RUT válido), `Driver` (RUT válido y nombre), `DestinationAddress`, `DestinationCommune`, `DepartureDate`, `DepartureTime` y `ArrivalDate`;
    - `ArrivalDate` no puede ser anterior a `DepartureDate`;
    - `Receiver.Activity` no vacío.

- [ ] **Step 1: Tests que fallan.** En `DteRulesTests`, con un helper `Guia52Valida()` que arma un documento completo:
  - `Validate_Guia52Completa_SinErrores`: `Assert.Empty(DteRules.Validate(Guia52Valida()))`.
  - `Validate_Guia52SinTransporte_ExigeBloque`: errores con `Field == "Transporte"`.
  - `[Theory] Validate_Guia52_CampoTransporteFaltante(string campo)`, con los casos `Patente`, `RUTTrans`, `RUTChofer`, `NombreChofer`, `DirDest`, `CmnaDest`, `FchSalida`, `HraSalida`, `FchLlegada`. Cada uno espera exactamente un error con ese `Field`.
  - `Validate_LlegadaAntesDeSalida_Error`
  - `Validate_RutInvalido_Error`: con `CarrierTaxId = "76030680-1"`, el error `Field == "RUTTrans"`.
  - `Validate_MasDe60Lineas_Error` y `Validate_MasDe40Referencias_Error`
  - `Validate_Guia52SinIndTraslado_Error`
- [ ] **Step 2:** `dotnet test ... --filter DteRulesTests`. Esperado: no compila o falla.
- [ ] **Step 3:** Crear los records, el contrato, `Rut.cs` y `DteRules`. Los `Field` usan el nombre del tag SII (`"Patente"`, `"RUTTrans"`, `"Detalle"`, `"Referencia"`, `"IndTraslado"`, `"GiroRecep"`, `"RUTRecep"`).
- [ ] **Step 4:** Los tests pasan.
- [ ] **Step 5:** Commit `feat(dte): modelo canónico DTE, contrato de emisión y reglas de validación`.

### Task 2: Persistencia de plataforma (`company_dte_settings`, `dte_emissions`)

**Files:**
- Create: `CORE/src/PortalSaas.Data/Entities/CompanyDteSetting.cs`
- Create: `CORE/src/PortalSaas.Data/Entities/DteEmission.cs`
- Modify: `CORE/src/PortalSaas.Data/PortalSaasDbContext.cs` (DbSets + configuración en el mismo estilo que `CompanyModuleConnection`)
- Create: migración `AddDteGateway` en `CORE/src/PortalSaas.Data.Migrations.PostgreSql` y en `CORE/src/PortalSaas.Data.Migrations.SqlServer`

**Interfaces:**
- Produces `CompanyDteSetting` (tabla `company_dte_settings`):
  - columnas: `Id long`, `CompanyId Guid` (único, FK companies), `ProviderCode string(20)`, `Environment string(20)` ("certification" | "production"), `BaseUrl string(300)`, `Username string(100)`, `SecretKey string?` (cifrado), `IsEnabled bool`;
  - datos del emisor: `IssuerTaxId string(12)`, `IssuerName string(100)`, `IssuerActivity string(80)` (giro), `IssuerActivityCode int` (Acteco), `IssuerAddress string(70)`, `IssuerCommune string(20)`, `IssuerCity string(20)`, `IssuerSiiBranchCode long?`;
  - `UpdatedAt DateTimeOffset`.
- Produces `DteEmission` (tabla `dte_emissions`):
  - columnas: `Id long`, `CompanyId Guid`, `EmissionKey Guid`, `SourceModule string(50)`, `SourceRef string(100)`, `DteType int`, `ProviderCode string(20)`, `IssuerTaxId string(12)`, `Environment string(20)`, `Folio long?`, `Status string(20)`, `Attempts int`, `LastError string?(1000)`, `RequestHash string(64)`, `RequestJson string` (el `DteDocument` serializado), `XmlSent string?`, `XmlSigned string?`;
  - fechas: `CreatedAt`, `ReservedAt?`, `IssuedAt?`, `UpdatedAt` (DateTimeOffset), `CreatedBy Guid?`;
  - índice único `(company_id, emission_key)`; índice `(company_id, status, updated_at)` para el reconciliador.

- [ ] **Step 1:** Crear las entidades y su configuración. Máximos de largo según el formato SII: `RznSoc` 100, `GiroEmis` 80, `DirOrigen` 70, `CmnaOrigen` 20.
- [ ] **Step 2:** Generar las dos migraciones con los comandos de `CORE/CLAUDE.md`. Revisar que ninguna use funciones propias de un motor.
- [ ] **Step 3:** `dotnet build "Portal SaaS - Core/PortalSaas.sln"`. Esperado: 0 errores. Correr la suite de Core: sigue en verde.
- [ ] **Step 4:** Commit `feat(dte): tablas company_dte_settings y dte_emissions (migraciones Postgres y SQL Server)`.

### Task 3: Configuración DTE por compañía (servicio + pantalla /Admin)

**Files:**
- Create: `CORE/src/PortalSaas.Core/DocumentosElectronicos/DteSettingsService.cs`. Clase `DteSettingsService` + DTOs `DteSettingsDto` y `DteSettingsInput` en el mismo archivo. Interfaz interna de Core, no de Abstractions, porque solo la usa /Admin.
- Create: `CORE/src/PortalSaas.Core/DocumentosElectronicos/IDteProvider.cs` (contrato de proveedor + `DteProviderCapabilities` + `DteProviderContext`)
- Create: `CORE/src/PortalSaas.Core/DocumentosElectronicos/DteProviderRegistry.cs`
- Create: `CORE/src/PortalSaas.Host/Pages/Admin/Organizations/Companies/Dte/Index.cshtml(.cs)`
- Modify: `CORE/src/PortalSaas.Host/Pages/Admin/Organizations/Companies/Index.cshtml` (enlace "Documentos electrónicos" junto a "Conexiones externas")
- Test: `CORE/tests/PortalSaas.Core.Tests/DocumentosElectronicos/DteSettingsServiceTests.cs`

**Interfaces:**
- Produces `IDteProvider` (Core):
  - `string Code`
  - `DteProviderCapabilities Capabilities`
  - `Task<byte[]> PreviewAsync(DteProviderContext ctx, string xml, int dteType, CancellationToken ct)`
  - `Task<DteReserveResult> ReserveAsync(DteProviderContext ctx, string xml, int dteType, Guid emissionKey, CancellationToken ct)`
  - `Task<DteConfirmResult> ConfirmAsync(DteProviderContext ctx, int dteType, long folio, CancellationToken ct)`, que devuelve el XML timbrado
  - `Task ReleaseAsync(DteProviderContext ctx, int dteType, long folio, CancellationToken ct)`
  - `Task<byte[]> GetPdfAsync(DteProviderContext ctx, int dteType, long folio, bool cedible, int copies, CancellationToken ct)`
  - `Task TestAsync(DteProviderContext ctx, CancellationToken ct)`, solo lectura
  - `string BuildXml(DteDocument doc, DteIssuerData issuer)`
- Tipos auxiliares:
  - `DteProviderContext(string BaseUrl, string Username, string Password, string IssuerTaxId, string Environment)`
  - `DteIssuerData(string TaxId, string Name, string Activity, int ActivityCode, string Address, string Commune, string City, long? SiiBranchCode)`
  - `DteReserveResult(bool Ok, long? Folio, string? Error, bool UuidAlreadyUsed)`
  - `DteConfirmResult(bool Ok, string? SignedXml, string? Error)`
  - `DteProviderCapabilities(IReadOnlyList<int> SupportedTypes, bool TwoPhase, bool SupportsPreview, bool SupportsRelease)`
- Los errores del proveedor (rechazos de negocio) vuelven en `Error`. Los de red lanzan `DteProviderUnavailableException`, que se define en `IDteProvider.cs`.
- `DteProviderRegistry(IEnumerable<IDteProvider>)` con `IDteProvider Get(string code)`, que lanza `InvalidOperationException` si el código no existe, y `IReadOnlyList<string> Codes`.
- `DteSettingsService(PortalSaasDbContext db, ISecretoCifradoService crypto, DteProviderRegistry providers)`:
  - `Task<DteSettingsDto?> GetAsync(Guid companyId)`. El DTO trae `HasSecret` y nunca la contraseña.
  - `Task SaveAsync(Guid companyId, DteSettingsInput input)`. Contraseña vacía conserva la actual; no se puede habilitar sin contraseña; el proveedor debe existir en el registro; valida el RUT emisor.
  - `Task DeleteAsync(Guid companyId)`. Se bloquea si hay `dte_emissions` de la compañía en estado no final.
  - `Task TestAsync(Guid companyId)`. Llama a `provider.TestAsync`; los errores salen como `InvalidOperationException` con un texto sin credenciales.
  - `internal Task<(DteProviderContext Ctx, DteIssuerData Issuer, IDteProvider Provider)?> ResolveAsync(Guid companyId)`. Lo usa Task 6; devuelve null si la configuración no existe o está deshabilitada.

- [ ] **Step 1: Tests que fallan** (InMemory + `SecretoCifradoService` real, como en los tests existentes):
  - `Save_CifraLaClave_YGetNoLaDevuelve`: el DTO trae `HasSecret == true`, y `SecretKey` en la base es distinto del texto plano.
  - `Save_ClaveVacia_ConservaLaAnterior`
  - `Save_ProveedorDesconocido_Falla`
  - `Save_HabilitarSinClave_Falla`
  - `Delete_ConEmisionesPendientes_Falla`
  - `Resolve_Deshabilitada_Null`
- [ ] **Step 2:** Correrlos. Esperado: fallan.
- [ ] **Step 3:** Implementar el servicio, el registro y el contrato. La página `/Admin/Organizations/Companies/{companyId}/Dte` sigue el patrón de `ExternalConnections/Edit.cshtml.cs`:
  - `[Authorize(AuthenticationSchemes = "PlatformAdmin")]`, `[TempData] ErrorMessage`;
  - formulario con un select de proveedor (`registry.Codes`) y otro de ambiente;
  - contraseña con `type=password` y placeholder "(sin cambios)" si `HasSecret`;
  - botones Guardar, Probar conexión (handler `OnPostTestAsync`) y Eliminar (con `confirm(...)`).
- [ ] **Step 4:** Tests en verde y build del Host.
- [ ] **Step 5:** Commit `feat(dte): configuración DTE por compañía con clave cifrada y pantalla en /Admin`.

### Task 4: FACELE — constructor del XML del DTE

**Files:**
- Create: `CORE/src/PortalSaas.Core/DocumentosElectronicos/Proveedores/Facele/FaceleXmlBuilder.cs`
- Create: `CORE/tests/PortalSaas.Core.Tests/DocumentosElectronicos/Facele/Fixtures/`, con copias de `DSP/docs/facele/ejemplo-guia52-venta-res154.xml` y `ejemplo-guia52-traslado-interno-res154.xml` (contenido `EmbeddedResource` o `CopyToOutputDirectory`)
- Test: `CORE/tests/PortalSaas.Core.Tests/DocumentosElectronicos/Facele/FaceleXmlBuilderTests.cs`

**Interfaces:**
- Produces `public static class FaceleXmlBuilder`:
  - `string Build(DteDocument doc, DteIssuerData issuer)`: string del XML con la declaración `<?xml version="1.0" encoding="ISO-8859-1"?>`;
  - `byte[] Encode(string xml)`: bytes en ISO-8859-1.
- Reglas del constructor:
  - `Documento ID` = `"T{tipo}"`; sin `Folio`;
  - orden de `Encabezado`: `IdDoc` (`TipoDTE`, `FchEmis`, `TipoDespacho`, `IndTraslado`), `Emisor` (`RUTEmisor`, `RznSoc`, `GiroEmis`, `Acteco`, `DirOrigen`, `CmnaOrigen`, `CiudadOrigen`; el override del documento manda sobre los tres últimos), `Receptor` (`RUTRecep`, `RznSocRecep`, `GiroRecep`, `DirRecep`, `CmnaRecep`, `CiudadRecep`), `Transporte` (orden de Global Constraints; `PatenteCarro` = "SIN DATO" si viene null; `Aduana` solo si hay peso o bultos: `NombreTransp`, `TotBultos`, `PesoNeto`; `HraSalida` en formato `HH:mm:ss`) y `Totales`;
  - si `Totals` es null, se calcula desde las líneas: `MntNeto` = Σ `Amount`, `TasaIVA` 19, `IVA` = redondeo de neto × 0,19, `MntTotal` = neto + IVA (todo 0 cuando las líneas valen 0);
  - `Detalle` (`NroLinDet`, `CdgItem` con `TpoCodigo` "INT1" y `VlrCodigo`, `NmbItem`, `DscItem`, `QtyItem`, `UnmdItem`, `PrcItem`, `MontoItem`);
  - `Referencia` (`NroLinRef`, `TpoDocRef`, `FolioRef`, `FchRef`, `CodRef`, `RazonRef`);
  - números con `InvariantCulture`, sin separador de miles, decimales solo si los hay; fechas `yyyy-MM-dd`;
  - largos truncados a los máximos SII: `NmbItem` 80, `RznSocRecep` 100, `GiroRecep` 40, `DirRecep` 70, `CmnaRecep` 20, `DirDest` 70, `CmnaDest` 20;
  - texto escapado con `XmlWriter`; los caracteres fuera de Latin-1 se reemplazan por su equivalente sin acentos o por `?`.

- [ ] **Step 1: Tests que fallan.**
  - `Build_Guia52Venta_IgualAlEjemploProbado`: arma el `DteDocument` equivalente al fixture de venta y compara con `XNode.DeepEquals` después de normalizar espacios.
  - `Build_TrasladoInterno_TotalesCeroConTasaIva`: `MntNeto` 0, `TasaIVA` 19, `IVA` 0, `MntTotal` 0.
  - `Build_TransporteEnOrdenRes154`: secuencia de nombres de hijos de `Transporte` igual a la lista de Global Constraints, filtrada a los presentes.
  - `Build_SinCarro_PatenteCarroSinDato`
  - `Build_EscapaYNormalizaLatin1`: con razón social `"Comercial Depor & Cía – Ñuñoa “Sur”"`, el XML se parsea, `Encode` no lanza y el texto decodificado contiene `"& Cía"` y `"Ñuñoa"`.
  - `Build_SinFolioNiTed`
- [ ] **Step 2:** Correrlos. Esperado: fallan.
- [ ] **Step 3:** Implementar con `XmlWriter` sobre un `StringWriter` (con `Encoding` Latin-1 en la declaración).
- [ ] **Step 4:** Tests en verde.
- [ ] **Step 5:** Commit `feat(dte): FACELE construye el XML del DTE (formato SII 2.5, Res. 154)`.

### Task 5: FACELE — cliente SOAP y adaptador `IDteProvider`

**Files:**
- Create: `CORE/src/PortalSaas.Core/DocumentosElectronicos/Proveedores/Facele/FaceleSoapClient.cs`
- Create: `CORE/src/PortalSaas.Core/DocumentosElectronicos/Proveedores/Facele/FaceleDteProvider.cs`
- Create: `CORE/tests/PortalSaas.Core.Tests/DocumentosElectronicos/Facele/Fixtures/*.resp.xml`. Son respuestas reales de caoba copiadas desde la carpeta de pruebas de la sesión: `genera1.resp.xml`, `obtiene103xml.resp.xml`, un uuid duplicado, `genera-tag-invalido.resp.xml` y un `obtieneBorrador`. El base64 del PDF se recorta a un PDF mínimo válido; **revisar que no tengan credenciales**.
- Test: `CORE/tests/PortalSaas.Core.Tests/DocumentosElectronicos/Facele/FaceleDteProviderTests.cs` (con un `HttpMessageHandler` falso)
- Test: `CORE/tests/PortalSaas.Core.Tests/DocumentosElectronicos/Facele/FaceleCaobaIntegrationTests.cs` (se salta si faltan `FACELE_USER` / `FACELE_PASS`)

**Interfaces:**
- Consumes: `IDteProvider`, `DteProviderContext`, `DteReserveResult` y `DteConfirmResult` (Task 3), y `FaceleXmlBuilder` (Task 4).
- Produces `FaceleSoapClient(HttpClient http)` con `Task<XDocument> CallAsync(DteProviderContext ctx, string service, string operation, IReadOnlyList<(string Name, string Value)> parameters, CancellationToken ct)`.
  - `service`: "DoceleOLService", con namespace `http://www.facele.cl/DoceleOL/`, o "DocumentosEmitidosService", con namespace `http://ws.docele.cl/DocumentosEmitidos/`.
  - URL = `BaseUrl.TrimEnd('/') + "/" + service`, con los headers HTTP `facele.user` / `facele.pass` y `SOAPAction` = ns + operación.
  - HTTP distinto de 200 o un timeout lanzan `DteProviderUnavailableException`, con un mensaje que lleva **solo** el código HTTP y la operación.
- Produces `FaceleDteProvider : IDteProvider`, con `Code = "FACELE"` y capacidades: tipos {33, 34, 52, 56, 61}, `TwoPhase = true`, `SupportsPreview = true`, `SupportsRelease = true`.
  - Preview = `obtieneBorrador(formato=PDF)`
  - Reserve = `generaDTE(rutEmisor, tipoDTE, formato=XML, xml, uuid)`:
    - con `estadoOperacion` 1, lee `folioDTE`;
    - con `estadoOperacion` 0 y una glosa que calza con `UUID \[.*\] ya utilizado para DTE: \[.*Folio: (\d+)` → `UuidAlreadyUsed = true` y `Folio` = el número capturado;
    - con otra glosa → `Ok = false` y `Error = descripcionOperacion`.
  - Confirm = `obtieneDTE(formato=XML)`, que devuelve el `<XML>` timbrado.
  - Release = `controversia(operacion=libera)`
  - GetPdf = `obtieneDTE(formato=PDF, cedible, cantidad)`, decodificando el base64 **dos veces**.
  - Test = `sinConfirmar(rutEmisor)`
  - BuildXml delega en `FaceleXmlBuilder`.
- El cliente se registra con `services.AddHttpClient<FaceleSoapClient>(c => c.Timeout = TimeSpan.FromSeconds(60))`.

- [ ] **Step 1: Tests que fallan** con el handler falso:
  - `Reserve_Ok_DevuelveFolio`: fixture `genera1.resp.xml`; verifica también que el request lleva los headers `facele.user` / `facele.pass` y el `uuid`.
  - `Reserve_UuidYaUtilizado_DevuelveFolioOriginal`
  - `Reserve_ErrorEsquema_DevuelveGlosa`
  - `Confirm_DevuelveXmlTimbrado`: contiene `<TED`.
  - `GetPdf_DecodificaDobleBase64`: los bytes empiezan con `%PDF`.
  - `Soap_Http405_ErrorSinCredenciales`: el handler responde 405; la excepción es `DteProviderUnavailableException` y su `Message` no contiene ni el usuario ni la contraseña de prueba.
- [ ] **Step 2:** Correrlos. Esperado: fallan.
- [ ] **Step 3:** Implementar. Al parsear, buscar los elementos por `LocalName` sin importar mayúsculas, porque `obtieneDTE` devuelve `<XML>` y `Obtener` devuelve `<xml>`.
- [ ] **Step 4:** Tests en verde. Integración opcional `Caoba_BorradorGuia52_DevuelvePdf`: solo `obtieneBorrador`, sin consumir folio.
- [ ] **Step 5:** Commit `feat(dte): adaptador FACELE (DoceleOL) para emisión en dos fases`.

### Task 6: Servicio de emisión idempotente (`DteEmissionService`)

**Files:**
- Create: `CORE/src/PortalSaas.Core/DocumentosElectronicos/DteEmissionService.cs`
- Modify: `CORE/src/PortalSaas.Host/Program.cs`. Registros:
  - `IDteEmissionService` → `DteEmissionService` (scoped);
  - `DteSettingsService` (scoped);
  - `DteProviderRegistry` (scoped);
  - `IDteProvider` → `FaceleDteProvider` (scoped);
  - `AddHttpClient<FaceleSoapClient>`.
- Test: `CORE/tests/PortalSaas.Core.Tests/DocumentosElectronicos/DteEmissionServiceTests.cs` (con un `FakeDteProvider` scripteable)

**Interfaces:**
- Consumes: `DteSettingsService.ResolveAsync` (Task 3), `DteRules.Validate` (Task 1), `IDteProvider` (Task 3).
- Produces `DteEmissionService(PortalSaasDbContext db, ICurrentCompanyAccessor company, ICurrentUserContext user, DteSettingsService settings, TimeProvider time) : IDteEmissionService`, más `internal Task<DteEmissionResult> AdvanceAsync(DteEmission row, CancellationToken ct)`, que reutiliza Task 7.
- Algoritmo de `EmitAsync`. Cada cambio de estado se guarda con `SaveChangesAsync` **antes** de la siguiente llamada al proveedor.
  1. Sin compañía en sesión, o `ResolveAsync` null → `InvalidOperationException` ("La compañía no tiene configurados los documentos electrónicos…").
  2. `DteRules.Validate`: si hay errores → `InvalidOperationException` con los mensajes unidos por "; ".
  3. Calcular el hash: SHA-256 del `RequestJson` (System.Text.Json, opciones fijas).
  4. Buscar la fila por `(CompanyId, EmissionKey)`.
     - Si existe y el hash es distinto: con `Status == failed` y `Folio == null` se reemplazan el request y el hash; si no → `InvalidOperationException` ("La emisión ya existe con otro contenido; genere una clave nueva.").
     - Si no existe → insertar en `pending`, con el `issuer_tax_id`, el ambiente y el proveedor de la configuración.
  5. Llamar a `AdvanceAsync`.
- Pasos de `AdvanceAsync`:
  - **Fila `pending` o `failed` sin folio:**
    - `Attempts++`; `XmlSent = provider.BuildXml(doc, issuer)`; `ReserveAsync`;
    - si `Ok` o `UuidAlreadyUsed` → `folio_reserved` con `Folio`;
    - si no → `failed` con `LastError`.
  - **Fila `folio_reserved`:** `ConfirmAsync`; si `Ok` → `issued` con `XmlSigned` e `IssuedAt`; si no → queda en `folio_reserved` con `LastError`.
  - **`DteProviderUnavailableException`:** la fila queda en su estado actual con `LastError` = mensaje y el resultado vuelve con ese estado; no lanza.
  - **Fila `issued`:** se devuelve sin llamar al proveedor.
- `GetAsync` solo busca en la compañía actual. `GetPdfAsync` exige `issued`.
- `AbandonAsync`: con `folio_reserved` → `ReleaseAsync` y `released`; con `pending` o `failed` → `abandoned`; con `issued` → `InvalidOperationException`.
- `PreviewAsync`: errores de validación más el PDF, solo si no hay errores y el proveedor `SupportsPreview`.

- [ ] **Step 1: Tests que fallan.**
  - `Emit_Exito_PasaPorReservaYConfirma`: estado `issued`, folio 103, `XmlSigned` lleno; el proveedor recibió `uuid == EmissionKey`.
  - `Emit_DosVecesMismaClave_UnaSolaReserva`: el contador de reservas del fake es 1.
  - `Emit_UuidYaUtilizado_RecuperaFolioYConfirma`
  - `Emit_CaidaEntreReservaYConfirmacion_ReintentoSoloConfirma`: el fake lanza `DteProviderUnavailableException` en `Confirm` la primera vez; la fila queda en `folio_reserved`; el segundo `EmitAsync` confirma sin volver a reservar.
  - `Emit_RechazoNegocio_FailedConGlosa_YReintentoConContenidoCorregido`
  - `Emit_MismaClaveOtroContenido_TrasReserva_Falla`
  - `Emit_SinConfiguracion_Falla`
  - `Emit_DocumentoInvalido_NoLlamaAlProveedor`
  - `Get_OtraCompania_Null`
  - `Abandon_FolioReservado_Libera` y `Abandon_Emitida_Falla`
- [ ] **Step 2:** Correrlos. Esperado: fallan.
- [ ] **Step 3:** Implementar y registrar en `Program.cs`.
- [ ] **Step 4:** Suite de Core en verde y build del Host.
- [ ] **Step 5:** Commit `feat(dte): servicio de emisión idempotente con registro central y recuperación`.

### Task 7: Reconciliador en segundo plano

**Files:**
- Create: `CORE/src/PortalSaas.Core/DocumentosElectronicos/DteReconciliationHostedService.cs`
- Modify: `CORE/src/PortalSaas.Host/Program.cs` (`AddHostedService<DteReconciliationHostedService>()`)
- Test: `CORE/tests/PortalSaas.Core.Tests/DocumentosElectronicos/DteReconciliationTests.cs`

**Interfaces:**
- Consumes: `DteEmissionService.AdvanceAsync` (Task 6) e `ICurrentCompanyOverride`.
- Produces:
  - `DteReconciliationHostedService(IServiceScopeFactory scopes, ILogger<...> log, TimeProvider time) : BackgroundService`, con un ciclo cada **5 minutos**;
  - `internal static Task<int> RunOnceAsync(IServiceProvider scoped, DateTimeOffset now, CancellationToken ct)`. Devuelve cuántas filas avanzó.
- `RunOnceAsync`:
  - toma las filas en `pending` o `folio_reserved` con `UpdatedAt < now - 10 min` y `Attempts < 20`, agrupadas por `CompanyId`;
  - para cada compañía abre un scope, fija `ICurrentCompanyOverride.Set(companyId)` y llama a `AdvanceAsync` por fila;
  - una excepción en una compañía se loguea (sin credenciales) y no detiene a las demás.
  - **Nunca toca `failed`:** un rechazo de negocio requiere que el módulo corrija.

- [ ] **Step 1: Tests que fallan.**
  - `Reconciler_FolioReservadoViejo_Confirma`
  - `Reconciler_FilaReciente_NoSeToca`
  - `Reconciler_Failed_NoSeToca`
  - `Reconciler_UsaConfiguracionDeCadaCompania`: dos compañías con un `BaseUrl` distinto; el fake registra el `ctx.BaseUrl` de cada llamada y debe calzar con su compañía.
- [ ] **Step 2:** Correrlos. Esperado: fallan.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Tests en verde.
- [ ] **Step 5:** Commit `feat(dte): reconciliador de emisiones pendientes por compañía`.

---

## Parte 2 — Despachos (Fase A)

### Task 8: Consulta SAP de líneas de factura

**Files:**
- Modify: `DSP/src/Modulo.Despachos/Models/Constantes.cs`. Agregar `public static class SapQueryKinds { public const string InvoiceLines = "invoice_lines"; }`, que se guarda en `sap_document_queries.document_kind`.
- Create: `DSP/src/Modulo.Despachos/Sql/Seeds/hana_invoice_lines.sql` (`EmbeddedResource`, como los otros seeds)
- Modify: `DSP/src/Modulo.Despachos/Servicios/SapQuerySeeds.cs`, `ISapDocumentLookupService.cs`, `SapDocumentLookupService.cs`
- Modify: `DSP/src/Modulo.Despachos/Pages/Despachos/Configuracion/ConsultasSap/Index.cshtml(.cs)`. Muestra una tercera consulta, "Líneas de factura", y su prueba usa un `doc_entry`.
- Modify: `DSP/src/Modulo.Despachos.Tests/Fakes/FakeSapDocumentLookupService.cs`
- Test: `DSP/src/Modulo.Despachos.Tests/Servicios/SapDocumentLookupServiceTests.cs` (agregar casos)

**Interfaces:**
- Produces:
  - `SapDocumentLine(int LineNum, string ItemCode, string ItemName, decimal Quantity, string? Unit, decimal? WeightKg)`
  - `SapDocumentLines(string? CustomerActivity, string? DestinationCity, IReadOnlyList<SapDocumentLine> Lines)`
  - en `ISapDocumentLookupService`: `Task<SapDocumentLines> GetLinesAsync(string queryKind, int docEntry, CancellationToken ct = default)`. Sin consulta activa → `InvalidOperationException` ("Configure la consulta SAP de líneas de factura en Despachos > Consultas SAP.").
- Contrato de columnas de `invoice_lines` (parámetro `:doc_entry`):
  - `line_num`, `item_code`, `item_name`, `quantity`, `unit`, `weight_kg`;
  - `customer_activity` y `destination_city`, repetidos en cada fila (se toman de la primera).
- `ValidateQueryAsync` y `ProbeAsync` aceptan el kind `invoice_lines`, exigen `:doc_entry` en vez de `:folio` y validan este contrato.
- Seed HANA:
  - `INV1` (`LineNum`, `ItemCode`, `Dscription`, `Quantity`, `unitMsr`);
  - peso = `Quantity` × `OITM."SWeight1"`, convertido a kg si `SWght1Unit` lo exige; si no se puede determinar, NULL [validar unidad con el admin SAP];
  - `OCRD."Notes"` como giro, con un comentario que avisa que el campo depende de cada SAP;
  - `INV12."CityS"` como ciudad;
  - `WHERE T0."DocEntry" = :doc_entry`.
- `FakeSapDocumentLookupService` gana un `Dictionary<int, SapDocumentLines> Lines`.

- [ ] **Step 1: Tests que fallan.**
  - `ValidateQuery_LineasSinDocEntry_Error`
  - `ValidateQuery_LineasFaltaColumna_ReportaFaltantes`: falta `weight_kg`.
  - `GetLines_MapeaContrato` (con `FakeHanaService`)
  - `GetLines_SinConsulta_MensajeClaro`
- [ ] **Step 2:** Correrlos. Esperado: fallan.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** La suite de Despachos queda en verde.
- [ ] **Step 5:** Commit `feat(despachos): consulta SAP configurable de líneas de factura`.

### Task 9: Modelo de guías, ajustes y estado "guías pendientes"

**Files:**
- Create: `DSP/src/Modulo.Despachos/Models/TransportGuide.cs` (`TransportGuide`, `TransportGuideDocument`)
- Modify: `DSP/src/Modulo.Despachos/Models/DespachosSetting.cs`. Agrega:
  - `GuidesEnabled bool` (default false);
  - `TransferIndicator int` (default **1**, [validar asesor]);
  - `DispatchType int?` (default **2**).
- Modify: `DSP/src/Modulo.Despachos/Models/ManifestConstants.cs`:
  - `ManifestStatuses.PendingGuides = "pending_guides"`, que se agrega a `All`;
  - `TransportGuideStatuses`: `Planned = "planned"`, `Issued = "issued"`, `Failed = "failed"`, `Pending = "pending"` (en curso o por reintentar), `Abandoned = "abandoned"`;
  - eventos `GuideIssued = "guide_issued"`, `GuideFailed = "guide_failed"`, `GuidesPlanned = "guides_planned"`.
- Modify: `DSP/src/Modulo.Despachos/Data/DespachosDbContext.cs`
- Modify: `DSP/src/Modulo.Despachos/Pages/DespachosUi.cs`. Agrega `"pending_guides" => "PendingGuides"` y la etiqueta "Guías pendientes", con la clase de badge de advertencia.
- Create: migración `GuiasRes154` en `DSP/src/Modulo.Despachos.Migrations.Postgres` y en `DSP/src/Modulo.Despachos.Migrations.SqlServer`

**Interfaces:**
- Produces `TransportGuide` (tabla `transport_guides`):
  - `Id Guid`, `CompanyId Guid`, `ManifestId Guid`, `Sequence int` (índice único `(manifest_id, sequence)`), `EmissionKey Guid` (único);
  - `ReceiverTaxId`, `ReceiverName`, `DestinationAddress`, `DestinationCommune`, `DestinationCity?`;
  - `TransferIndicator int`, `LineCount int`, `TotalQuantity decimal`, `WeightKg decimal?`;
  - `Status string`, `Folio long?`, `LastError string?`;
  - `CreatedAt`, `IssuedAt?`, `Documents List<TransportGuideDocument>`.
- Produces `TransportGuideDocument` (tabla `transport_guide_documents`): `Id long`, `CompanyId`, `TransportGuideId Guid`, `ManifestDocumentId Guid`.
- *Ruling:* sin tabla de líneas, porque el XML enviado queda en `dte_emissions`.

- [ ] **Step 1:** Crear las entidades y la configuración. Generar las dos migraciones (mismo comando que las migraciones existentes de Despachos, ver `DSP/docs/superpowers/plans/2026-10-20-modulo-despachos-etapa1.md`).
- [ ] **Step 2:** Revisar los lugares que filtran por estado. `DocumentReportService`, `DispatchReportService`, `ManifestService.FindActiveElsewhereAsync` y el filtro de `Manifiestos/Index` deben tratar `pending_guides` así:
  - **no** cuenta como despachado en los reportes;
  - **sí** bloquea el folio en otro manifiesto, igual que despachado.
  - Agregar el test `DocumentReport_ExcluyeGuiasPendientes` en `DocumentReportServiceTests`.
- [ ] **Step 3:** La suite de Despachos queda en verde.
- [ ] **Step 4:** Commit `feat(despachos): modelo de guías de traslado y estado guías pendientes`.

### Task 10: Planificador de guías (dominio puro)

**Files:**
- Create: `DSP/src/Modulo.Despachos/Dominio/Guias/GuidePlanner.cs`
- Test: `DSP/src/Modulo.Despachos.Tests/Dominio/Guias/GuidePlannerTests.cs`

**Interfaces:**
- Consumes: los records DTE de Abstractions (Task 1) y `SapDocumentLines` (Task 8).
- Produces:
  - `GuidePlanInput(Manifest Manifest, IReadOnlyList<(ManifestDocument Doc, SapDocumentLines Lines)> Documents, Func<long?, string?> CommuneName, int TransferIndicator, int? DispatchType, DateOnly IssueDate, TimeOnly DepartureTime)`.
  - `PlannedGuide(int Sequence, IReadOnlyList<Guid> ManifestDocumentIds, DteDocument Document)`
  - `GuidePlan(IReadOnlyList<PlannedGuide> Guides, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)`
  - `public static class GuidePlanner { GuidePlan Plan(GuidePlanInput input); }`
- Reglas:
  - **Llave de grupo:** `RutChileno.Normalize(CustomerTaxId)` + dirección (trim, mayúsculas, espacios colapsados) + `DestinationCommuneId`. El orden de las guías es el de aparición del primer documento de cada grupo.
  - **Detalle:** consolidado por `ItemCode` dentro del grupo, sumando `Quantity` y `WeightKg`. `Name` = `ItemName` de la primera aparición; `Unit` = la primera no nula; `UnitPrice` null; `Amount` 0.
  - **Referencias:** una por documento, `DocumentType` = `SiiDocumentType.ToString()`, `Folio`, `Date` = `DocumentDate`, `ReasonCode` null.
  - **División:** recorrer los documentos del grupo y acumular en la guía actual mientras las líneas consolidadas sean ≤ 60 y las referencias ≤ 40; si no caben, abrir una guía nueva. Un documento con más de 60 artículos distintos por sí solo se reparte en trozos de 60 líneas, y cada trozo lo referencia.
  - **Transporte:** desde el snapshot (`Plate`, `TrailerPlate`, `CarrierTaxId`, `DriverTaxId` / `DriverName`); `DestinationAddress` y comuna del documento; `DestinationCity` desde `Lines.DestinationCity`; `DepartureDate` = `IssueDate`; `DepartureTime`; `ArrivalDate` = `EstimatedArrivalDate`. `Customs` lleva peso solo si **todas** las líneas tienen peso (si no, una advertencia "Peso no disponible para N artículos; la guía sale sin peso"), más `TotalPackages` = Σ `Packages`.
  - **Emisor:** override con `OriginAddress` / `OriginCommune` del snapshot.
  - **Receptor:** `Activity` = `Lines.CustomerActivity`.
  - **Errores (bloquean):**
    - documento sin comuna resuelta ("Folio N: la comuna de destino no está resuelta");
    - documento sin líneas;
    - `EstimatedArrivalDate` nula ("Ingrese la fecha de llegada estimada (obligatoria en la guía de despacho, Res. 154)");
    - documento que no es factura ("Fase A solo emite guías para facturas").
  - Cada guía planificada pasa además por `DteRules`: el planificador **no** la referencia (está en Core), así que esa validación la hace Task 11 vía `IDteEmissionService.ValidateAsync`.

- [ ] **Step 1: Tests que fallan.**
  - `Plan_DosFacturasMismoDestino_UnaGuiaConDosReferencias`
  - `Plan_DestinosDistintos_UnaGuiaPorDestino`
  - `Plan_ConsolidaPorArticulo`: el mismo `ItemCode` en dos facturas da una línea con la cantidad sumada.
  - `Plan_MasDe60Articulos_Divide`: con 70 artículos distintos en dos facturas salen dos guías de ≤ 60 líneas.
  - `Plan_FacturaSola_Con130Articulos_TresGuiasQueLaReferencian`
  - `Plan_SinFechaLlegada_Error`
  - `Plan_ComunaNoResuelta_Error`
  - `Plan_PesoParcial_SinAduanaPesoYAdvertencia`
  - `Plan_TransporteDesdeSnapshot`
- [ ] **Step 2:** Correrlos. Esperado: fallan.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Tests en verde.
- [ ] **Step 5:** Commit `feat(despachos): planificador de guías por destino con detalle por artículo`.

### Task 11: Servicio de guías e integración con Despachar

**Files:**
- Create: `DSP/src/Modulo.Despachos/Servicios/ITransportGuideService.cs`, `TransportGuideService.cs`
- Modify: `DSP/src/Modulo.Despachos/Servicios/ManifestService.cs` (`DispatchAsync`, `AnnulAsync`), `IManifestService.cs`
- Modify: `DSP/src/Modulo.Despachos/ModuloDespachos.cs` (registro DI)
- Create: `DSP/src/Modulo.Despachos.Tests/Fakes/FakeDteEmissionService.cs`
- Modify: `DSP/src/Modulo.Despachos.Tests/Servicios/ManifestTestEnv.cs`. Inyecta `FakeDteEmissionService` y `TransportGuideService`, con guías deshabilitadas por defecto para que los 302 tests existentes no cambien.
- Test: `DSP/src/Modulo.Despachos.Tests/Servicios/TransportGuideServiceTests.cs`

**Interfaces:**
- Consumes: `IDteEmissionService` (Task 1), `GuidePlanner` (Task 10), `ISapDocumentLookupService.GetLinesAsync` (Task 8), `TransportGuide` (Task 9).
- Produces `ITransportGuideService`:
  - `Task<GuidePreview> PreviewAsync(Guid manifestId)`: plan sin persistir, más errores de configuración DTE, del planificador y de `ValidateAsync` por guía;
  - `Task<byte[]> PreviewPdfAsync(Guid manifestId, int sequence)`;
  - `Task<GuideEmissionOutcome> EmitAsync(Guid manifestId)`: persiste el plan la primera vez y emite las pendientes;
  - `Task<byte[]> GetPdfAsync(Guid manifestId, int? sequence)`: null = todas unidas con PDFsharp;
  - `Task AbandonPendingAsync(Guid manifestId, string reason)`.
- Tipos:
  - `GuidePreview(bool Enabled, IReadOnlyList<PlannedGuideView> Guides, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)`
  - `PlannedGuideView(int Sequence, string ReceiverName, string Destination, int Documents, int Lines, decimal Quantity, decimal? WeightKg)`
  - `GuideEmissionOutcome(bool AllIssued, int Issued, int Pending, IReadOnlyList<string> Errors)`
- Constantes: `SourceModule = "despachos"`; `SourceRef = $"manifest:{manifest.Number}/guide:{sequence}"`.
- Cambio en `ManifestService.DispatchAsync`, cuando `DespachosSetting.GuidesEnabled`:
  1. Tras las validaciones de Etapa 1, llama a `PreviewAsync`; si hay errores, devuelve `DispatchResult(false, errores)` **antes** de cambiar nada.
  2. Sella la salida y toma el snapshot como hoy, deja el estado `pending_guides` y guarda. Los costos se calculan igual que hoy.
  3. Llama a `EmitAsync`.
     - Si `AllIssued` → `dispatched`, los documentos pasan a despachado y se registran los eventos `guide_issued`.
     - Si no → queda en `pending_guides`, con el evento `guide_failed` y el aviso `TryEnqueueAsync` al usuario ("Manifiesto N° X: N guía(s) sin emitir: <error>"). El `DispatchResult` es `Success = false` con el error y la advertencia "El camión no debe salir sin guía; reintente desde la pestaña Guías."
  - Con `GuidesEnabled` en false, el comportamiento de Etapa 1 no cambia.
- `IManifestService` gana `Task<DispatchResult> RetryGuidesAsync(Guid manifestId)`. Exige `pending_guides`, llama a `EmitAsync` y aplica el mismo cierre del paso 3.
- `AnnulAsync` desde `pending_guides`:
  - permitido solo si **ninguna** guía quedó `issued`; entonces llama a `AbandonPendingAsync`;
  - si alguna quedó emitida → `InvalidOperationException` ("Hay guías emitidas: no se puede anular el manifiesto; consulte cómo anular la guía (pendiente con FACELE).").
- Concurrencia: el cambio a `pending_guides` revalida el estado con `RequireStatus` y la escritura usa el índice único `(manifest_id, sequence)`. Si un segundo `DispatchAsync` llega con el manifiesto ya en `pending_guides`, recibe el error de estado de siempre.

- [ ] **Step 1: Tests que fallan.**
  - `Dispatch_ConGuias_EmiteYDespacha`
  - `Dispatch_ConGuias_ErrorPlan_NoCambiaNada`: el estado sigue en borrador y no hay filas de guía.
  - `Dispatch_ProveedorCaido_QuedaGuiasPendientesYAvisa`
  - `RetryGuides_SoloEmiteLasPendientes`: en el fake, la guía 1 ya está emitida; el reintento solo llama `EmitAsync` con la clave de la guía 2.
  - `RetryGuides_MismaEmissionKey`
  - `Dispatch_DosVeces_NoDuplicaGuias`
  - `Annul_GuiasPendientesSinEmitidas_Abandona` y `Annul_ConGuiaEmitida_Falla`
  - `Dispatch_GuiasDeshabilitadas_ComportamientoEtapa1`
  - `GetPdf_Todas_UneLosPdf`: el fake devuelve dos PDF de 1 página; el resultado tiene 2 páginas.
- [ ] **Step 2:** Correrlos. Esperado: fallan.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** La suite de Despachos queda en verde (302 existentes + nuevos).
- [ ] **Step 5:** Commit `feat(despachos): emisión de guías al despachar con reintento y estado guías pendientes`.

### Task 12: Pantallas — pestaña Guías, ajustes y estados

**Files:**
- Modify: `DSP/src/Modulo.Despachos/Pages/Despachos/Manifiestos/Detalle.cshtml(.cs)`:
  - pestaña **Guías** entre Documentos y Retorno, visible si `GuidesEnabled`;
  - handlers `OnGetGuiaBorradorAsync(int sequence)` (PDF inline), `OnGetGuiasPdfAsync(int? sequence)` y `OnPostReintentarGuiasAsync`;
  - estado `pending_guides` con un aviso arriba: "Guías pendientes: el camión no debe salir. [Reintentar]".
- Modify: `DSP/src/Modulo.Despachos/Pages/Despachos/Configuracion/Origenes/Index.cshtml(.cs)`. En la sección del emisor (donde está `IssuerTaxId`) agrega los campos:
  - "Emitir guías de despacho al despachar" (checkbox);
  - "Indicador de traslado" (select con los 9 valores SII);
  - "Tipo de despacho" (select 1, 2 o 3);
  - un estado de solo lectura de `IDteEmissionService.GetConfigurationAsync()`: proveedor, ambiente, RUT emisor, o el error si no está configurado.
- Modify: `DSP/src/Modulo.Despachos/Pages/Despachos/Manifiestos/Index.cshtml`. El filtro de estado incluye "Guías pendientes".
- Modify: `DSP/src/Modulo.Despachos/wwwroot/css/despachos.css`. Color del badge `pending_guides`, de tipo advertencia.
- Test: `DSP/src/Modulo.Despachos.Tests/Pages/ManifiestosPermisosTests.cs`. Agrega los casos de permisos de los nuevos handlers.

**Interfaces:**
- Consumes: `ITransportGuideService` y `IManifestService.RetryGuidesAsync` (Task 11).
- Permisos:
  - ver el borrador y los PDF: la acción VIEW del menú de manifiestos;
  - reintentar: la misma acción que Despachar.
- Contenido de la pestaña Guías:
  - **en borrador:** tabla de `PlannedGuideView` ("Se emitirán N guías"), con errores en rojo, advertencias y un botón "Ver borrador" por guía;
  - **después de despachar:** secuencia, receptor, destino, folio, estado (badge), último error, botón PDF por guía y "Imprimir todas".

- [ ] **Step 1: Tests que fallan.**
  - `Guias_BorradorPdf_RequiereVer`
  - `Guias_Reintentar_RequiereDespachar`
- [ ] **Step 2:** Implementar las pantallas siguiendo el patrón de pestañas existente (`data-tab-btn` / `data-tab-panel`) y `InlinePdfResult` para los PDF.
- [ ] **Step 3:** Suite en verde. Correr el Host local contra QA y revisar a mano la pestaña en borrador con las guías deshabilitadas y habilitadas.
- [ ] **Step 4:** Commit `feat(despachos): pestaña Guías con vista previa, impresión y reintento`.

### Task 13: Migraciones aplicadas y prueba de punta a punta en certificación

**Files:**
- Modify: `C:\Users\Jorge\.claude\projects\c--PROYECTOS-Proyecto-Portal-Web-Company\memory\modulo-despachos-estado.md` y `pasarela-dte-plataforma.md` (estado)

- [ ] **Step 1:** Aplicar `AddDteGateway` en `portalsaas_saas_qa` y `ps_comdepor_qa`, y `GuiasRes154` en `ps_comdepor_dsp_qa`. Verificar que las tablas existen.
- [ ] **Step 2:** **Pedir confirmación al usuario** y aplicar en `portalsaas_saas_prod`, `ps_comdepor` y `ps_comdepor_dsp`. Verificar.
- [ ] **Step 3:** En /Admin, configurar DEPOR_TEST con FACELE certificación (`https://caoba.docele.cl/cl-dol-auth`). **El usuario ingresa la contraseña en la pantalla.** Luego "Probar conexión".
- [ ] **Step 4:** En Despachos > Orígenes, habilitar las guías y configurar la consulta "Líneas de factura" con una factura real de QA.
- [ ] **Step 5:** Armar un manifiesto con 2 o 3 facturas de dos destinos, revisar la vista previa y despachar. Verificar:
  - las guías quedan emitidas en caoba;
  - el PDF se imprime;
  - las referencias apuntan a las facturas.
  - Simular una caída (URL inválida en la configuración) y comprobar que el manifiesto queda en `pending_guides`, con aviso en la campana, y que el reintento no duplica folios (`sinConfirmar` vacío).
- [ ] **Step 6:** Actualizar las memorias y hacer el commit de cualquier ajuste. Recordar al usuario:
  - desplegar el Host y el plugin Despachos juntos;
  - la plantilla PDF de FACELE sigue sin imprimir los datos de transporte (spec Despachos §13).
