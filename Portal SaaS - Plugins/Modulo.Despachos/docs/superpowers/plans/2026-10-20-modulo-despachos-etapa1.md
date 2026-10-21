# Módulo Despachos — Etapa 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plugin externo `Modulo.Despachos` con mantenedores, tarifario (valor base + recargos), manifiestos con captura PDF417/manual contra SAP, despacho con snapshot Res.154, retorno por documento, PDF interno, cierre mensual y reporte mensual.

**Architecture:** Plugin Razor Pages externo (mismo molde que `Modulo.Rendiciones`) con base propia motor dual resuelta por `IExternalDatabaseConnectionService`. Lógica de costo y de estados en clases puras (`Dominio/`) testeadas sin base; servicios EF Core (`Servicios/`) testeados con EF InMemory; SAP se lee con `IHanaService` mediante consultas configurables por compañía.

**Tech Stack:** .NET 8, Razor Pages, EF Core 8 (SqlServer 8.0.8 + Npgsql 8.0.10), xUnit 2.5.3 + EF InMemory 8.0.8, ClosedXML 0.104.1, PDFsharp-MigraDoc 6.1.1 (MIT), ZXing.Net 0.16.9 + SkiaSharp 2.88.9 (código de barras del PDF), `@zxing/library` 0.21.3 UMD vendorizado en `wwwroot/lib/zxing/` (lectura PDF417 por cámara).

**Spec:** `Portal SaaS - Plugins/Modulo.Despachos/docs/superpowers/specs/2026-10-20-modulo-despachos-etapa1-design.md`

Rutas relativas a `Portal SaaS - Plugins/Modulo.Despachos/` salvo que se indique otra cosa. Abreviaturas: `P` = `src/Modulo.Despachos/`, `T` = `src/Modulo.Despachos.Tests/`.

Comando de tests (desde `Portal SaaS - Plugins/Modulo.Despachos/`):
`dotnet test src/Modulo.Despachos.Tests/Modulo.Despachos.Tests.csproj --filter "FullyQualifiedName~<Clase>"`

## Global Constraints

- Plugin solo referencia `PortalSaas.Abstractions` (ProjectReference relativa temporal, idéntica a Rendiciones). Nunca Core/Host/otro plugin.
- `ModuleCode` = `"Despachos"`, `Name` = `"Despachos"`, `RequiresExternalDatabase = true`, `ServesOwnWwwRoot = true`, `Version` = `"1.0.0"`.
- Todas las páginas bajo `P/Pages/Despachos/...`, `@page` absoluto `/despachos/...`. Parciales con sufijo `Despachos` (`_EstadoDespachos.cshtml`).
- `[Authorize]` en `DespachosPageModelBase`; permisos SOLO vía `ICurrentUserContext.HasActionAsync("Despachos.<codigo-menu>", PortalActions.X)`; sin permiso → `Forbid()`.
- Todo `MenuItemDefinition` lleva `Icon` `bi-*`.
- Tablas: inglés, plural, snake_case, PK `id`, `company_id` NOT NULL + índice en toda tabla de negocio, FK `<entidad>_id`, `_at`, `is_`, `status` string. Sin `HasColumnType` específico de motor: `HasPrecision(18, 2)` para montos, `HasPrecision(9, 2)` para m³/km/%.
- `DateTime` persistido siempre `DateTimeKind.Utc` (Npgsql 8 revienta con otro Kind). Hora local de negocio = zona `America/Santiago` (fallback Windows `Pacific SA Standard Time`) vía `ChileClock` (Task 2).
- Sin Company activa → `InvalidOperationException("Modulo.Despachos requiere una compañía activa en la sesión -- seleccioná una compañía antes de continuar.")` en el registro del DbContext (copia del patrón Rendiciones).
- Mantenedores: Crear + Editar + Eliminar; eliminar bloqueado con mensaje si hay dependientes; transportistas/vehículos/choferes usados en manifiestos → solo desactivar (`is_active=false`).
- `<input type=number>` con `value` formateado `CultureInfo.InvariantCulture`; montos CLP sin decimales en pantalla.
- UI: copiar patrones de `Modulo.Rendiciones` (`Pages/Configuracion/TiposGasto` = grilla compacta + búsqueda + drawer `data-drawer-open`; `RendicionesUi` = helper de badges), `Portal SaaS - Core/DESIGN.md`. No inventar componentes si existe uno equivalente.
- Avisos solo vía `INotificationService.TryEnqueueAsync` (nunca `EnqueueAsync`).
- Toda transición de manifiesto/documento y todo ajuste de costo/impresión inserta una fila en `manifest_events`.

### Mapeo de permisos (spec §9 → `PortalActions`)

| Menú (`Despachos.<code>`) | VIEW | CREATE | EDIT | DELETE | APPROVE | EXPORT |
|---|---|---|---|---|---|---|
| `manifiestos` | ver | crear borrador | editar borrador / cambiar chofer-vehículo | anular | despachar / cerrar | PDF / Excel |
| `retorno` | ver | — | registrar retorno | — | — | — |
| `tarifario` | ver + simulador | crear | editar | eliminar | — | — |
| `cierre-mensual` | ver | — | ajustar costo de manifiesto | — | cerrar período | — |
| `reportes` | ver | — | — | — | — | Excel |
| cada mantenedor (`transportistas`, `tipos-vehiculo`, `vehiculos`, `choferes`, `zonas`, `origenes`, `turnos`, `motivos-rechazo`, `feriados`, `destinatarios-avisos`, `consultas-sap`) | ver | crear | editar | eliminar | — | — |

Reabrir período = solo `ICurrentUserContext.IsAdmin`.

## Review Focus

1. **Hora de salida en UTC vs hora Chile** — un despacho a las 17:42 hora Chile se guarda como UTC y el recargo "≥ 17:00" debe evaluarse en hora Chile, no UTC; persistir con Kind≠Utc rompe Postgres. Test en Task 8 (`Dispatch_GuardaUtc_YRecargoUsaHoraChile`).
2. **Lectura de escáner sucia** — el lector en modo teclado agrega `\r\n`, espacios o trae caracteres ISO-8859-1 (Ñ en razón social); escanear dos veces el mismo documento. Esperado: parser tolera ruido y devuelve false con basura; repetido = aviso. Tests en Task 2 y Task 8.
3. **Mismo folio en distintos tipos** — folio 1500 existe como factura 33 y guía 52. La búsqueda exige `document_kind`; el TED trae `TD` y se usa. Test en Task 7.
4. **Dos usuarios agregan el mismo documento a dos borradores a la vez** — el chequeo al agregar no basta; `DispatchAsync` revalida y bloquea. Test en Task 8 (`Dispatch_BloqueaSiDocumentoQuedoEnOtroManifiestoActivo`).
5. **Número de manifiesto duplicado por creación concurrente** — índice único `(company_id, number)` + reintento. Test en Task 8 (`CreateDraft_NumeraCorrelativoPorCompania`).

---

### Task 1: Esqueleto del plugin (proyectos, módulo, DbContext vacío, base de páginas)

**Files:**
- Create: `src/Modulo.Despachos/Modulo.Despachos.csproj` (copia del de Rendiciones sin Azure/Skia de OCR; agrega PDFsharp-MigraDoc 6.1.1, ZXing.Net 0.16.9, SkiaSharp 2.88.9 + NativeAssets.Win32 2.88.9, ClosedXML 0.104.1, FileProviders.Embedded 8.0.8; target `PublicarComoPlugin`)
- Create: `src/Modulo.Despachos.Migrations.Postgres/` y `src/Modulo.Despachos.Migrations.SqlServer/` (csproj + `DesignTimeDbContextFactory`, env var `DESPACHOS_CONNECTION_STRING`, DB dev `modulo_despachos_dev`)
- Create: `src/Modulo.Despachos.Tests/Modulo.Despachos.Tests.csproj` (copia de Rendiciones.Tests) + `T/Fakes/FakeCurrentUserContext.cs`, `FakeCurrentCompanyAccessor.cs`, `FakeNotificationService.cs`, `FakeHanaService.cs`, `TestDb.cs`
- Create: `P/ModuloDespachos.cs`, `P/Data/DespachosDbContext.cs`, `P/Pages/_ViewImports.cshtml`, `P/_ViewStart.cshtml`, `P/Pages/DespachosPageModelBase.cs`, `P/Pages/DespachosUi.cs`, `P/wwwroot/css/despachos.css`
- Create: `publish-dist.ps1` (copia del de Rendiciones con `$moduleName = "Modulo.Despachos"`), `PENDIENTE.md`
- Test: `T/ModuloDespachosTests.cs`

**Interfaces:**
- Produces: `DespachosDbContext(DbContextOptions<DespachosDbContext>)`; `TestDb.Create() : DespachosDbContext` (InMemory, nombre único por test); `DespachosPageModelBase(ICurrentUserContext)` con `protected abstract string MenuCode {get;}`, `protected Task<bool> HasActionAsync(string action)` (califica `"Despachos." + MenuCode`), `[TempData] SuccessMessage/ErrorMessage/WarningMessage`, `GetErrorMessage(Exception)`; `DespachosUi.StatusBadgeClass(string status)`, `DespachosUi.StatusLabel(string status)`; `FakeHanaService.Results` (`Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string,object?>>>` por SQL) y `FakeHanaService.LastParameters`.

- [ ] **Step 1: Write the failing test** `T/ModuloDespachosTests.cs`

```csharp
[Fact] public void ModuleCode_EsDespachos() => Assert.Equal("Despachos", new ModuloDespachos().ModuleCode);
[Fact] public void Menu_TodoItemTieneIconoYCodigoUnico()
{
    var items = new ModuloDespachos().GetMenu().ToList();
    Assert.All(items, i => Assert.StartsWith("bi-", i.Icon));
    Assert.Equal(items.Count, items.Select(i => i.Code).Distinct().Count());
    Assert.All(items.Where(i => i.PageRoute != null), i => Assert.StartsWith("/despachos/", i.PageRoute));
}
```

- [ ] **Step 2: Run** `--filter FullyQualifiedName~ModuloDespachosTests` → FAIL (no compila).
- [ ] **Step 3: Implement** proyectos y `ModuloDespachos : IModuloPortal`. `RegisterServices` copia el bloque `AddDbContext` de `ModuloRendiciones` cambiando tipo y mensaje. Menú: raíz `raiz` "Despachos" `bi-truck` Order 170; grupos `grupo-operacion` "Operación" `bi-truck-flatbed`, `grupo-costos` "Costos" `bi-cash-coin`, `grupo-config` "Configuración" `bi-gear`; hojas con los códigos de la tabla de permisos y rutas `/despachos/manifiestos`, `/despachos/retorno`, `/despachos/tarifario`, `/despachos/cierre-mensual`, `/despachos/reportes/mensual`, `/despachos/configuracion/<codigo>`. Iconos: manifiestos `bi-card-list`, retorno `bi-arrow-return-left`, tarifario `bi-tags`, cierre-mensual `bi-calendar-check`, reportes `bi-bar-chart`, transportistas `bi-building`, tipos-vehiculo `bi-truck-front`, vehiculos `bi-truck`, choferes `bi-person-badge`, zonas `bi-geo-alt`, origenes `bi-house-door`, turnos `bi-clock`, motivos-rechazo `bi-x-octagon`, feriados `bi-calendar-x`, destinatarios-avisos `bi-bell`, consultas-sap `bi-database-gear`.
- [ ] **Step 4: Run** → PASS. `dotnet build src/Modulo.Despachos/Modulo.Despachos.csproj` sin warnings nuevos y `dist/Modulo.Despachos/1.0.0/Modulo.Despachos.dll` existe.
- [ ] **Step 5: Commit** `feat(despachos): esqueleto del plugin Modulo.Despachos`

---

### Task 2: Utilidades de dominio (RUT, patente, reloj Chile, TED PDF417)

**Files:**
- Create: `P/Dominio/RutChileno.cs`, `P/Dominio/PlateNormalizer.cs`, `P/Dominio/ChileClock.cs`, `P/Dominio/TedParser.cs`
- Test: `T/Dominio/RutChilenoTests.cs`, `PlateNormalizerTests.cs`, `ChileClockTests.cs`, `TedParserTests.cs`

**Interfaces:**
- Produces: `RutChileno.IsValid(string? rut) : bool`, `RutChileno.Normalize(string rut) : string` (formato `12345678-K`, sin puntos, DV mayúscula); `PlateNormalizer.Normalize(string plate) : string` (mayúsculas, sin espacios/guiones/puntos); `ChileClock.ToLocal(DateTime utc) : DateTime`, `ChileClock.ToUtc(DateTime local) : DateTime` (Kind Utc); `TedParser.TryParse(string? raw, out TedData data) : bool`; `record TedData(string IssuerRut, int DocumentType, long Folio, DateOnly IssueDate, string ReceiverRut, decimal Amount)`.

- [ ] **Step 1: Write failing tests** (valores exactos):

```csharp
// RutChileno
Assert.True(RutChileno.IsValid("76.086.428-5")); Assert.False(RutChileno.IsValid("76086428-4"));
Assert.Equal("12345678-5", RutChileno.Normalize("12.345.678-5"));
// Plate
Assert.Equal("ABCD12", PlateNormalizer.Normalize(" ab-cd 12 "));
// ChileClock: 2026-11-10 20:42 UTC = 17:42 Chile (UTC-3, horario de verano)
Assert.Equal(new TimeOnly(17,42), TimeOnly.FromDateTime(ChileClock.ToLocal(new DateTime(2026,11,10,20,42,0,DateTimeKind.Utc))));
Assert.Equal(DateTimeKind.Utc, ChileClock.ToUtc(new DateTime(2026,11,10,17,42,0)).Kind);
// TED
const string ted = "<TED version=\"1.0\"><DD><RE>76086428-5</RE><TD>33</TD><F>154321</F><FE>2026-10-19</FE><RR>96509660-4</RR><RSR>COMERCIAL ÑUÑOA</RSR><MNT>119000</MNT><IT1>ZAPATILLA</IT1></DD><FRMT algoritmo=\"SHA1withRSA\">x</FRMT></TED>";
Assert.True(TedParser.TryParse("\r\n " + ted + "\r\n", out var d));
Assert.Equal((33, 154321L, "76086428-5", "96509660-4", 119000m), (d.DocumentType, d.Folio, d.IssuerRut, d.ReceiverRut, d.Amount));
Assert.Equal(new DateOnly(2026,10,19), d.IssueDate);
Assert.False(TedParser.TryParse("154321", out _)); Assert.False(TedParser.TryParse("<TED><DD><TD>x</TD></DD>", out _));
```

- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Dominio"` → FAIL.
- [ ] **Step 3: Implement.** `TedParser`: recorta hasta `<TED`…`</TED>`, lee con `XDocument.Parse` dentro de try/catch (sin resolver DTD), exige RE/TD/F/FE/RR/MNT numéricos válidos. `ChileClock`: `TimeZoneInfo.TryFindSystemTimeZoneById("America/Santiago")` con fallback `"Pacific SA Standard Time"`.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(despachos): utilidades RUT, patente, hora Chile y parser TED`

---

### Task 3: Modelo y servicios de mantenedores (+ migración Inicial)

**Files:**
- Create: `P/Models/Carrier.cs`, `VehicleType.cs`, `Vehicle.cs`, `Driver.cs`, `Shift.cs`, `Commune.cs`, `Zone.cs`, `ZoneCommune.cs`, `Origin.cs`, `RejectionReason.cs`, `Holiday.cs`, `NotificationRecipient.cs`, `P/Models/Constantes.cs` (clases estáticas de valores string: `CarrierKinds{Own,ThirdParty,Marketplace,Pickup}` = `own|third_party|marketplace|pickup`, `RejectionAppliesTo{Rejection,NotDelivered}`)
- Create: `P/Servicios/IMasterDataService.cs` + `MasterDataService.cs`, `P/Servicios/CommuneSeeder.cs`, `P/Sql/Seeds/comunas.csv` (EmbeddedResource)
- Modify: `P/Data/DespachosDbContext.cs` (DbSets + mapping de 12 tablas del spec §4.1 + `notification_recipients(user_id)`)
- Create: migraciones `Inicial` en los dos proyectos de migraciones
- Test: `T/Servicios/MasterDataServiceTests.cs`, `T/Servicios/CommuneSeederTests.cs`

**Interfaces:**
- Consumes: `RutChileno`, `PlateNormalizer` (Task 2), `ICurrentCompanyAccessor`.
- Produces: `IMasterDataService` con, por entidad E ∈ {Carrier, VehicleType, Vehicle, Driver, Shift, Zone, Origin, RejectionReason, Holiday, NotificationRecipient}: `Task<IReadOnlyList<E>> List<E>sAsync(bool includeInactive = false)`, `Task<long> Save<E>Async(E entity)` (insert si `Id==0`), `Task<DeleteResult> Delete<E>Async(long id)`; `record DeleteResult(bool Deleted, string? BlockedReason)`; `Task<IReadOnlyList<Commune>> ListCommunesAsync()`; `Task<Commune?> FindCommuneByNameAsync(string name)` (comparación sin tildes/mayúsculas). `CommuneSeeder.EnsureSeededAsync(DespachosDbContext db, Guid companyId)`. Todas las entidades llevan `long Id`, `Guid CompanyId`, `bool IsActive` (si aplica), auditoría `CreatedAt/CreatedBy/UpdatedAt/UpdatedBy`.

- [ ] **Step 1: Write failing tests**
  - `SaveCarrier_RechazaRutInvalido_SalvoRetiro`: kind `third_party` con RUT `76086428-4` → `InvalidOperationException` mensaje contiene "RUT"; kind `pickup` sin RUT → guarda.
  - `SaveVehicle_NormalizaPatente`: guarda `"ab-cd 12"` → `Plate == "ABCD12"`; segunda con misma patente en la compañía → `InvalidOperationException` "ya existe".
  - `SaveDriver_ValidaRutYNormaliza`.
  - `DeleteVehicleType_BloqueadoSiHayVehiculos`: `Deleted == false`, `BlockedReason` contiene "vehículos".
  - `DeleteZone_SinDependientes_BorraYSusComunas`.
  - `ListCarriers_SoloDeLaCompaniaActiva` (dos CompanyId).
  - `CommuneSeeder_Inserta346Comunas_YEsIdempotente` (2 llamadas → 346).
  - `FindCommuneByName_IgnoraTildesYMayusculas`: `"nunoa"` encuentra `"Ñuñoa"`; `"Concepcion"` encuentra `"Concepción"`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~MasterDataServiceTests|FullyQualifiedName~CommuneSeederTests"` → FAIL.
- [ ] **Step 3: Implement.** `comunas.csv` = `cut_code;name;region_code;region_name`, 346 filas del listado oficial CUT (SUBDERE). El bloqueo "usado en manifiestos" para Carrier/Vehicle/Driver se agrega en Task 8 (aquí solo dependientes de mantenedores). Índices únicos `(company_id, plate)`, `(company_id, tax_id)` en drivers, `(company_id, code)` en catálogos con código.
- [ ] **Step 4: Run** → PASS. Generar migraciones: `dotnet ef migrations add Inicial --project src/Modulo.Despachos.Migrations.Postgres` y `--project src/Modulo.Despachos.Migrations.SqlServer`; ambas compilan.
- [ ] **Step 5: Commit** `feat(despachos): mantenedores y migración inicial`

---

### Task 4: Pantallas de mantenedores

**Files:**
- Create: `P/Pages/Despachos/Configuracion/{Transportistas,TiposVehiculo,Vehiculos,Choferes,Zonas,Origenes,Turnos,MotivosRechazo,Feriados,DestinatariosAvisos}/Index.cshtml(.cs)`
- Test: `T/Pages/MaestrosPermisosTests.cs`

**Interfaces:**
- Consumes: `IMasterDataService` (Task 3), `DespachosPageModelBase` (Task 1), `IUserContactLookupService`/catálogo de usuarios de Abstractions para elegir destinatarios (el mismo que usa Rendiciones en `UsuariosRoles`).

- [ ] **Step 1: Write failing test** `OnGet_SinPermisoView_Forbid` y `OnPostDelete_SinPermisoDelete_Forbid` para `Transportistas.IndexModel` con `FakeCurrentUserContext` sin acciones (resultado `ForbidResult`).
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** cada página como copia estructural de `Modulo.Rendiciones/.../Configuracion/TiposGasto/Index.cshtml` (grilla compacta, búsqueda, orden por columna, drawer de alta/edición, botón eliminar con `confirm`). Mensajes de `DeleteResult.BlockedReason` en `ErrorMessage`. Zonas: multiselect de comunas. Choferes y vehículos: select de transportista; vehículos con check "es carro".
- [ ] **Step 4: Run** → PASS. Verificación manual en Host local: crear/editar/eliminar un registro de cada pantalla; log del Host sin `Colisión de ruta Razor`.
- [ ] **Step 5: Commit** `feat(despachos): pantallas de mantenedores`

---

### Task 5: Motor de costo (valor base + recargos) — puro

**Files:**
- Create: `P/Models/RateCard.cs`, `P/Models/RateSurcharge.cs` (entidades, mapeadas en Task 6)
- Create: `P/Dominio/Tarifas/TripCostInput.cs`, `RateCardResolver.cs`, `IRateCalculator.cs` + `RateCalculators.cs` (6 estrategias), `ISurchargeCondition.cs` + `SurchargeConditions.cs`, `CostEngine.cs`, `CostResult.cs`
- Test: `T/Dominio/Tarifas/RateCardResolverTests.cs`, `CostEngineTests.cs`, `SurchargeConditionTests.cs`

**Interfaces:**
- Produces:
  - `record TripLocality(long CommuneId, long? ZoneId, string RegionCode, string CommuneName)`
  - `record TripCostInput(long CarrierId, long? ShiftId, long? VehicleTypeId, decimal? CapacityM3, DateOnly DispatchDate, TimeOnly LocalDepartureTime, bool IsHoliday, decimal? Km, int Documents, int Units, int Packages, IReadOnlyList<TripLocality> Localities)`
  - `RateCardResolver.Resolve(IEnumerable<RateCard> cards, TripCostInput trip, TripLocality locality) : RateResolution`; `record RateResolution(RateCard? Card, bool IsAmbiguous)`
  - `IRateCalculator { string Method {get;} decimal Calculate(RateCard card, TripCostInput trip); }` — métodos `fixed_per_trip` (`Amount`), `per_m3` (`Amount × CapacityM3`), `per_km` (`Amount × Km`), `per_package`, `per_document`, `no_charge` (0); resultado `max(calc, MinAmount ?? 0)`.
  - `ISurchargeCondition { string Type {get;} SurchargeEvaluation Evaluate(JsonElement parameters, TripCostInput trip); }`; `record SurchargeEvaluation(bool IsMet, int ExtraUnits, string Explanation)`. Tipos y parámetros: `distinct_zones_over {"threshold":1}`, `distinct_communes_over {"threshold":n}`, `departure_time_from {"time":"17:00"}`, `shift_is {"shiftId":n}`, `weekday_in {"days":[6,0]}` (DayOfWeek), `is_holiday {}`, `documents_over`/`packages_over {"threshold":n}`, `vehicle_type_is {"vehicleTypeId":n}`. `ExtraUnits` = valor − umbral para los `*_over`, 1 para el resto.
  - `CostEngine(IEnumerable<IRateCalculator>, IEnumerable<ISurchargeCondition>)`, `CostResult Calculate(TripCostInput trip, IReadOnlyList<RateCard> cards, IReadOnlyList<RateSurcharge> surcharges)`; `record CostResult(string Status /* ok|no_rate|ambiguous */, decimal? Total, decimal BaseAmount, IReadOnlyList<CostLine> Lines)`; `record CostLine(string Kind /* base|surcharge */, long? CommuneId, long? RateCardId, long? RateSurchargeId, decimal Amount, string Explanation)`.
  - `RateCard`: condiciones nullable `CarrierId, ShiftId, RegionCode, ZoneId, CommuneId, VehicleTypeId, M3From, M3To`; `ValidFrom, ValidTo`; `CalculationMethod, Amount, MinAmount, ParametersJson, Priority, IsActive`. `RateSurcharge`: `CarrierId?`, `Name`, `ValidFrom, ValidTo`, `Priority`, `IsActive`, `ConditionType`, `ConditionParametersJson`, `EffectType` (`fixed_amount|percent_of_base|fixed_per_extra_unit`), `Value`.

- [ ] **Step 1: Write failing tests** (valores del spec):
  - `Resolver_ComunaGanaAZonaYRegion`: tarifas región `06` $200.000, zona Sur $180.000, comuna Rancagua $150.000 → elige comuna.
  - `Resolver_EmpatePrioridad_GanaMayorPriority`; `Resolver_EmpateTotal_IsAmbiguous`; `Resolver_IgnoraFueraDeVigenciaEInactivas`; `Resolver_TramoM3_UsaCapacidad` (M3From 20, M3To 40, capacidad 30 → calza; 45 → no).
  - `Engine_EjemploJefeDespachos`: base Rancagua $150.000 (`fixed_per_trip`), localidades Rancagua (zona Sur) + Santiago Centro (zona RM, base $90.000), recargo `departure_time_from 17:00` → `percent_of_base 20`, recargo `distinct_zones_over 1` → `fixed_amount 25000`, salida 17:42 → `Total == 205000m`, `BaseAmount == 150000m`, 4 líneas (2 base: 150000 y 0; 2 surcharge: 30000 y 25000), explicación del horario contiene `"17:42"` y `"17:00"`.
  - `Engine_SalidaAntesDeLas17_SinRecargoHorario` (16:59 → 175000).
  - `Engine_PorcentajesNoEnCascada`: dos recargos 20% y 10% sobre base 100000 → 130000.
  - `Engine_FixedPerExtraUnit`: 3 zonas, umbral 1, $10.000 → +20000.
  - `Engine_LocalidadSinTarifa_StatusNoRate_TotalNull`; `Engine_Ambigua_StatusAmbiguous`.
  - `Engine_MinAmount`: `per_package` $500 × 10 bultos, min $8.000 → base 8000.
  - `Engine_NoCharge_RetiroCero`.
  - `Engine_RecargoDeOtroTransportista_NoAplica`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Tarifas"` → FAIL.
- [ ] **Step 3: Implement.** Especificidad = cantidad de condiciones no nulas, con desempate geográfico comuna(3) > zona(2) > región(1) sumado al conteo. Base del viaje = máximo de las bases por localidad; líneas de las demás localidades con `Amount = 0` y explicación "incluida en destino de mayor valor". Recargos filtrados por `CarrierId == null || == trip.CarrierId`, vigencia por `DispatchDate`, ordenados por `Priority`; `percent_of_base` = `BaseAmount × Value / 100`, redondeo a peso (`MidpointRounding.AwayFromZero`).
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(despachos): motor de costo con valor base y recargos`

---

### Task 6: Tarifario persistido + pantallas + simulador

**Files:**
- Modify: `P/Data/DespachosDbContext.cs` (`rate_cards`, `rate_surcharges`)
- Create: `P/Servicios/ITariffService.cs` + `TariffService.cs`
- Create: `P/Pages/Despachos/Tarifario/Index.cshtml(.cs)` (tabs Valores base / Recargos / Simulador)
- Create: migración `Tarifario` (dos proyectos)
- Test: `T/Servicios/TariffServiceTests.cs`

**Interfaces:**
- Consumes: `CostEngine`, `RateCard`, `RateSurcharge` (Task 5), `IMasterDataService` (Task 3).
- Produces: `ITariffService`: `ListRateCardsAsync()`, `SaveRateCardAsync(RateCard)`, `DeleteRateCardAsync(long)`, `ListSurchargesAsync()`, `SaveSurchargeAsync(RateSurcharge)`, `DeleteSurchargeAsync(long)`, `Task<CostResult> SimulateAsync(SimulationInput input)`; `record SimulationInput(long CarrierId, long? ShiftId, long VehicleId, DateOnly DispatchDate, TimeOnly LocalDepartureTime, IReadOnlyList<long> CommuneIds, int Documents, int Units, int Packages, decimal? Km)`; `Task<(IReadOnlyList<RateCard>, IReadOnlyList<RateSurcharge>)> LoadActiveAsync()`; `Task<TripCostInput> BuildTripAsync(SimulationInput input)` (resuelve zona/región de cada comuna, capacidad del vehículo o de su tipo, feriado; lo usa también `ManifestCostService`).

- [ ] **Step 1: Write failing tests**
  - `SaveSurcharge_ValidaParametrosSegunTipo`: `departure_time_from` con `{"time":"25:00"}` → `InvalidOperationException`; `distinct_zones_over` sin `threshold` → error.
  - `SaveRateCard_RechazaMetodoDesconocido` y `M3From > M3To`.
  - `Simulate_UsaCapacidadDelTipoSiVehiculoNoLaDefine`.
  - `Simulate_MarcaFeriado` (holiday 2026-12-08 → `IsHoliday` true en el trip).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** (pantalla según patrón de Task 4; simulador muestra `CostResult.Lines` con explicación). **Step 4: Run** → PASS; migración `Tarifario` generada en ambos proyectos.
- [ ] **Step 5: Commit** `feat(despachos): tarifario, recargos y simulador`

---

### Task 7: Consultas SAP configurables + búsqueda de documentos

**Files:**
- Create: `P/Models/SapDocumentQuery.cs` (`company_id`, `document_kind`, `sql_text`, `is_active`, `validated_at`), `P/Servicios/ISapDocumentLookupService.cs` + `SapDocumentLookupService.cs`, `P/Sql/Seeds/hana_invoice.sql`, `P/Sql/Seeds/hana_transfer_guide.sql`
- Create: `P/Pages/Despachos/Configuracion/ConsultasSap/Index.cshtml(.cs)` (editar SQL, botón "Probar" con folio)
- Modify: `DespachosDbContext` (`sap_document_queries`, único `(company_id, document_kind)`); migración `ConsultasSap`
- Test: `T/Servicios/SapDocumentLookupServiceTests.cs`

**Interfaces:**
- Consumes: `IHanaService.QueryDynamicAsync(sql, new { folio })`, `FakeHanaService` (Task 1).
- Produces: `DocumentKinds { Invoice = "invoice", TransferGuide = "transfer_guide" }`; `record SapDocumentInfo(string DocumentKind, int SiiDocumentType, long Folio, int DocEntry, int DocNum, DateOnly DocumentDate, string CustomerCode, string CustomerName, string? CustomerTaxId, string? DestinationAddress, string? DestinationCommune, string? CustomerPo, int? BaseOrderEntry, decimal Units, int Packages, decimal TotalAmount, bool IsCanceled, decimal? WeightKg, decimal? VolumeM3)`; `ISapDocumentLookupService.FindAsync(string documentKind, long folio, CancellationToken ct) : Task<SapDocumentInfo?>`; `ValidateQueryAsync(string sql, long sampleFolio) : Task<QueryValidationResult>`; `record QueryValidationResult(bool IsValid, IReadOnlyList<string> MissingColumns, string? Error)`; `DocumentKindFor(int siiType) : string` (33/34 → invoice, 52 → transfer_guide).
- Contrato de columnas obligatorias (alias exactos, minúsculas, entre comillas en HANA): `doc_entry, doc_num, folio, sii_document_type, document_date, customer_code, customer_name, customer_tax_id, destination_address, destination_commune, customer_po, base_order_entry, units, packages, total_amount, is_canceled`; opcionales `weight_kg, volume_m3`. Lectura de claves case-insensitive.

- [ ] **Step 1: Write failing tests**
  - `Find_UsaLaConsultaDelTipo_YPasaFolio`: `FakeHanaService` con respuesta para el SQL de `invoice`; `FindAsync("invoice", 1500)` → `CustomerName` esperado y `LastParameters` contiene `folio = 1500`.
  - `Find_MismoFolioOtroTipo_NoMezcla`: folio 1500 configurado solo para `transfer_guide` → `FindAsync("invoice",1500)` = null.
  - `Find_SinConsultaConfigurada_LanzaMensajeClaro` ("No hay consulta SAP configurada para facturas").
  - `Find_ClavesEnMayusculas_SeLeenIgual` (HANA devuelve `DOC_ENTRY`).
  - `Validate_ReportaColumnasFaltantes` (falta `packages` → `MissingColumns == ["packages"]`).
  - `Validate_RechazaSentenciasNoSelect` (`UPDATE`/`DELETE`/`;` múltiple → inválida, sin ejecutar).
  - `DocumentKindFor_33_34_52`.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.** Seeds HANA (se cargan al crear la compañía en el módulo si no hay fila; editables):
  - `hana_invoice.sql`: `OINV T0` por `T0."FolioNum" = :folio`; `"FolioPref"` → `sii_document_type`; destino desde `INV12` (`"StreetS"`, `"CountyS"`); `"LicTradNum"`, `"NumAtCard"`, `"DocTotal"`, `"CANCELED"='Y'`; `units` = `SUM(INV1."Quantity")`; `packages` = `COUNT(DISTINCT ODLN."U_NX_LPN")` vía `INV1."BaseEntry"/"BaseLine"` → `DLN1` → `ODLN` (igual que `Codigo OLD/.../Manifiesto.cshtml.cs` `GetFacturaQuery`); `base_order_entry` = `MIN(DLN1."BaseEntry")` con `DLN1."BaseType" = 17`.
  - `hana_transfer_guide.sql`: `"@INX_ODGC" H` por `H."U_FoliNo" = :folio`; `52`; destino `OWHS` por `H."U_ToWhsCode"` (`"Street"`, `"County"`); `customer_code/name` = bodega; `customer_tax_id` = `OADM."TaxIdNum"`; `units` = `SUM(WTR1."Quantity")` de las `OWTR` en `"@INX_DGC1"."U_BseEntry"`; `packages` = `COUNT(DISTINCT DGC1."U_CardBord")`; `total_amount` = `SUM(DGC1."U_Amount")`; `is_canceled` = `H."Canceled"='Y'`.
  - Validación: solo una sentencia que empiece con `SELECT`/`WITH`; parámetros solo `:folio`.
- [ ] **Step 4: Run** → PASS. **Verificación contra QA real**: en la pantalla "Probar" ejecutar cada seed con un folio real de factura y de guía de `CLPRDDEPOR` QA; resultado coincide con el documento en SAP (cliente, unidades, bultos). Anotar folios usados en `PENDIENTE.md`.
- [ ] **Step 5: Commit** `feat(despachos): consultas SAP configurables y búsqueda por folio`

---

### Task 8: Manifiesto — modelo, armado, despacho, costo (servicio)

**Files:**
- Create: `P/Models/Manifest.cs`, `ManifestDocument.cs`, `ManifestCostLine.cs`, `ManifestEvent.cs`; constantes `ManifestStatuses{Draft="draft",Dispatched="dispatched",Returned="returned",Closed="closed",Annulled="annulled"}`, `DeliveryStatuses{Pending="pending",Dispatched="dispatched",Delivered="delivered",PartiallyDelivered="partially_delivered",Rejected="rejected",NotDelivered="not_delivered"}`, `CaptureMethods{Pdf417="pdf417",Manual="manual"}`, `ManifestEventTypes` (`created, document_added, document_removed, dispatched, vehicle_changed, return_registered, closed, annulled, cost_overridden, printed, reprinted`)
- Create: `P/Servicios/IManifestService.cs` + `ManifestService.cs`, `P/Servicios/IManifestCostService.cs` + `ManifestCostService.cs`
- Modify: `DespachosDbContext` (4 tablas; `manifests.id` y `manifest_documents.id` = `Guid`; único `(company_id, number)`); `MasterDataService` (bloqueo de borrado de Carrier/Vehicle/Driver usados en manifiestos → "desactívelo"); migración `Manifiestos`
- Test: `T/Servicios/ManifestServiceTests.cs`, `ManifestCostServiceTests.cs`

**Interfaces:**
- Consumes: `ISapDocumentLookupService`, `SapDocumentInfo`, `DocumentKinds` (Task 7); `ITariffService.BuildTripAsync`/`LoadActiveAsync`, `CostEngine` (Tasks 5–6); `IMasterDataService.FindCommuneByNameAsync` (Task 3); `TedParser`, `ChileClock` (Task 2); `TimeProvider`.
- Produces:
  - `record ManifestHeaderInput(DateOnly DispatchDate, long? ShiftId, long OriginId, long CarrierId, long VehicleId, long? TrailerId, long DriverId, DateOnly? EstimatedArrivalDate, TimeOnly? EstimatedArrivalTime, TimeOnly? PlannedDepartureTime, string? ServiceOrder, string? Remarks, int? OdometerStart)`
  - `record DocumentCapture(string? RawScan, string? DocumentKind, long? Folio)` (RawScan = TED; si no, kind+folio manual)
  - `record AddDocumentResult(string Outcome /* added|duplicate_in_this|in_other_manifest|not_found|canceled|invalid_scan|other_issuer */, Guid? ManifestDocumentId, int? OtherManifestNumber, IReadOnlyList<string> Warnings)`
  - `record DispatchResult(bool Success, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)`
  - `IManifestService`: `CreateDraftAsync(ManifestHeaderInput) : Task<Guid>`; `UpdateDraftHeaderAsync(Guid, ManifestHeaderInput)`; `AddDocumentAsync(Guid manifestId, DocumentCapture) : Task<AddDocumentResult>`; `RemoveDocumentAsync(Guid manifestId, Guid manifestDocumentId)`; `DispatchAsync(Guid) : Task<DispatchResult>`; `ChangeVehicleOrDriverAsync(Guid, long vehicleId, long? trailerId, long driverId, string reason)`; `GetAsync(Guid) : Task<Manifest?>` (incluye documentos, líneas de costo, eventos); `ListAsync(ManifestFilter) : Task<IReadOnlyList<ManifestListItem>>`.
  - `IManifestCostService.RecalculateAsync(Manifest m) : Task<CostResult>` (escribe `cost_calculated`, `rate_card_id` de la base ganadora, reemplaza `manifest_cost_lines`, recalcula `cost_final = cost_override ?? cost_calculated`; status `no_rate`/`ambiguous` → `cost_calculated = null`).
  - Columnas `manifests`: las del spec §4.3 + `cost_status` (`ok|no_rate|ambiguous`).

- [ ] **Step 1: Write failing tests**
  - `CreateDraft_NumeraCorrelativoPorCompania`: compañía A crea 2 → números 1 y 2; compañía B crea 1 → 1. Simular conflicto (fila con número 3 insertada entre lectura y escritura vía hook del test) → el servicio reintenta y asigna 4.
  - `AddDocument_PorTed_ResuelveTipoYFolio`: TED con `TD=33`, `F=154321`, `RE` = RUT de la compañía (de `FakeCurrentCompanyAccessor`/configuración) → busca `invoice`/154321, `Outcome == "added"`, `CaptureMethod == "pdf417"`, snapshot de cliente/destino/unidades/bultos/`CustomerPo`/`BaseOrderEntry` copiado.
  - `AddDocument_TedDeOtroEmisor_OtherIssuer`.
  - `AddDocument_Repetido_DuplicateInThis_NoError` (no agrega fila nueva).
  - `AddDocument_EnOtroManifiestoActivo_InOtherManifest` con `OtherManifestNumber`; si el otro está `annulled` o el documento allí quedó `not_delivered` → `added`.
  - `AddDocument_ComunaNoResuelta_AgregaConWarning` y `AddDocument_FechaPosteriorADespacho_Warning`.
  - `AddDocument_AnuladoEnSap_Canceled`; `AddDocument_ManifiestoNoBorrador_Lanza`.
  - `Dispatch_ValidaChoferTransportistaYDocumentos`: sin documentos → error "al menos un documento"; chofer con RUT inválido → error; chofer de otro transportista → warning (no error); licencia vencida a la fecha de despacho → warning.
  - `Dispatch_GuardaUtc_YRecargoUsaHoraChile`: `TimeProvider` fijo `2026-11-10T20:42:00Z`, recargo ≥17:00 → `DepartedAt.Kind == Utc`, `DepartedAt == 2026-11-10 20:42Z`, línea de recargo horario presente.
  - `Dispatch_TomaSnapshotRes154`: `CarrierTaxId`, `DriverTaxId`, `DriverName`, `Plate`, `TrailerPlate`, `OriginAddress`, `OriginCommune` iguales a los maestros; cambiar luego el maestro no altera el manifiesto.
  - `Dispatch_DocumentosPasanADispatched_YEventos`.
  - `Dispatch_BloqueaSiDocumentoQuedoEnOtroManifiestoActivo`: dos borradores con el mismo folio; despachar el primero OK; el segundo → error con número del primero.
  - `Dispatch_SinTarifa_PermiteConCostStatusNoRate`.
  - `ChangeVehicle_PostDespacho_RenuevaSnapshotRecalculaYRegistraEvento`; con período cerrado → `InvalidOperationException` "período cerrado".
  - `DeleteVehicle_UsadoEnManifiesto_Bloqueado`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~ManifestServiceTests|FullyQualifiedName~ManifestCostServiceTests"` → FAIL.
- [ ] **Step 3: Implement.** RUT emisor de la compañía: nueva columna en tabla de ajustes del módulo `despachos_settings(company_id, issuer_tax_id)` editable en Configuración > Orígenes (cabecera de la pantalla). Comuna destino: `FindCommuneByNameAsync(SapDocumentInfo.DestinationCommune)`. Número: `MAX(number)+1` y `SaveChanges`; ante `DbUpdateException` por índice único reintenta hasta 3 veces.
- [ ] **Step 4: Run** → PASS; migración `Manifiestos` generada en ambos proyectos.
- [ ] **Step 5: Commit** `feat(despachos): manifiesto, armado, despacho y costo`

---

### Task 9: Retorno, anulación, ajuste de costo, cierre de manifiesto, avisos

**Files:**
- Modify: `P/Servicios/IManifestService.cs` + `ManifestService.cs`
- Test: `T/Servicios/ManifestReturnTests.cs`

**Interfaces:**
- Consumes: Task 8; `INotificationService.TryEnqueueAsync`; `IMasterDataService.ListNotificationRecipientsAsync`.
- Produces: `record DocumentReturnInput(Guid ManifestDocumentId, string DeliveryStatus, long? RejectionReasonId, string? Comments)`; `RegisterReturnAsync(Guid manifestId, IReadOnlyList<DocumentReturnInput> documents, int? odometerEnd) : Task` (con `odometerEnd` guarda km = fin − inicio y recalcula costo, necesario para `per_km`); `AnnulAsync(Guid, string reason)`; `OverrideCostAsync(Guid, decimal amount, string reason)`; `CloseAsync(Guid)`.

- [ ] **Step 1: Write failing tests**
  - `Return_ParcialRechazadoNoEntregado_ExigenMotivo` (sin motivo → `InvalidOperationException`).
  - `Return_TodosConEstado_ManifiestoPasaAReturned`; parcial → sigue `dispatched`.
  - `Return_RechazoNotificaACadaDestinatario`: 2 destinatarios → `FakeNotificationService` recibe 2 llamadas `TryEnqueueAsync` con título que contiene `"Rechazo"`, mensaje con número de manifiesto, folio, cliente, motivo, comentario y `url == "/despachos/manifiestos/{id}"`.
  - `Return_NoEntregado_LiberaDocumento`: luego `AddDocumentAsync` en otro manifiesto → `added`, ambos viajes quedan en eventos.
  - `Annul_LiberaDocumentos_YNoBorra`.
  - `OverrideCost_ExigeMotivo_YCostFinalEsOverride`.
  - `Close_SoloDesdeReturned`.
  - `Return_ConOdometro_RecalculaCostoPerKm`: inicio 1000, fin 1180, tarifa `per_km` $900 → `cost_calculated == 162000`; `odometerEnd < odometerStart` → `InvalidOperationException`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(despachos): retorno, anulación, ajuste de costo y avisos`

---

### Task 10: PDF interno del manifiesto

**Files:**
- Create: `P/Servicios/IManifestPdfService.cs` + `ManifestPdfService.cs`, `P/Servicios/BarcodeRenderer.cs`
- Test: `T/Servicios/ManifestPdfServiceTests.cs`

**Interfaces:**
- Consumes: `IManifestService.GetAsync` (Task 8).
- Produces: `IManifestPdfService.RenderAsync(Guid manifestId, bool isCopy) : Task<byte[]>` (registra evento `printed`/`reprinted` con usuario); `BarcodeRenderer.Code128Png(string text) : byte[]`.

- [ ] **Step 1: Write failing tests**
  - `Render_DevuelvePdfValido`: bytes empiezan con `%PDF`.
  - `Render_Copia_IncluyeMarcaCopia` (texto extraído con PdfSharp `PdfReader`/contenido contiene `"COPIA"`); original no la tiene.
  - `Render_AgrupaPorDestino`: 3 documentos en 2 comunas → el texto contiene ambas comunas como encabezados de grupo y los totales (documentos, unidades, bultos).
  - `Render_RegistraEventoPrinted`.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** con MigraDoc (contenido del spec §8). Fuentes: `GlobalFontSettings.UseWindowsFontsUnderWindows = true` (servidor IIS Windows x64). Código de barras Code128 del número con ZXing.Net → PNG con SkiaSharp → imagen MigraDoc `base64:`.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(despachos): PDF interno del manifiesto`

---

### Task 11: Pantallas de manifiesto (listado, armado con escaneo, detalle, retorno)

**Files:**
- Create: `P/Pages/Despachos/Manifiestos/Index.cshtml(.cs)` (listado + filtros), `Nuevo.cshtml(.cs)` (`/despachos/manifiestos/nuevo`), `Detalle.cshtml(.cs)` (`/despachos/manifiestos/{id:guid}`, tabs Cabecera/Documentos/Retorno/Costo/Historial, handlers `Pdf`, `Despachar`, `Anular`, `CambiarVehiculo`, `AjustarCosto`, `Cerrar`), `P/Pages/Despachos/Retorno/Index.cshtml(.cs)`, `P/Pages/Despachos/Shared/_EstadoDespachos.cshtml`
- Create: `P/wwwroot/js/despachos-scan.js`, `P/wwwroot/lib/zxing/zxing.min.js` (`@zxing/library` 0.21.3 UMD, licencia Apache-2.0 junto al archivo)
- Test: `T/Pages/ManifiestosPermisosTests.cs`

**Interfaces:**
- Consumes: `IManifestService`, `IManifestPdfService`, `IMasterDataService`, `ITariffService`.

- [ ] **Step 1: Write failing tests**: `Despachar_SinApprove_Forbid`, `Anular_SinDelete_Forbid`, `Pdf_SinExport_Forbid`, `RegistrarRetorno_SinEditEnRetorno_Forbid`, `AjustarCosto_SinEditEnCierreMensual_Forbid`.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.** Armado: input con foco permanente; Enter → POST AJAX `?handler=AgregarDocumento` con `RawScan`; respuesta JSON con `Outcome` → color/sonido (verde added, ámbar duplicate/warnings, rojo resto) y contador; alternativa manual tipo+folio; botón "Cámara" abre lector PDF417 con `ZXing.BrowserPDF417Reader`. Retorno: input "N° manifiesto" (lector del Code128) → grilla con todos en "Entregado" por defecto, motivo obligatorio al cambiar estado. Badges vía `DespachosUi`.
- [ ] **Step 4: Run** → PASS. Verificación manual en Host local con lector de mano o cámara: escanear 2 facturas y 1 guía de QA, despachar, abrir PDF, registrar un rechazo y ver el aviso en la campana del destinatario.
- [ ] **Step 5: Commit** `feat(despachos): pantallas de manifiesto, escaneo y retorno`

---

### Task 12: Cierre mensual y reporte mensual

**Files:**
- Create: `P/Models/MonthlyClosure.cs`, `P/Servicios/IMonthlyClosureService.cs` + `MonthlyClosureService.cs`, `P/Servicios/IDispatchReportService.cs` + `DispatchReportService.cs`
- Create: `P/Pages/Despachos/CierreMensual/Index.cshtml(.cs)`, `P/Pages/Despachos/Reportes/Mensual.cshtml(.cs)`
- Modify: `DespachosDbContext` (`monthly_closures`, único `(company_id, period)`); `ManifestService` (mutaciones de manifiestos con `dispatch_date` en período cerrado → `InvalidOperationException` "período cerrado"); migración `CierreMensual`
- Test: `T/Servicios/MonthlyClosureServiceTests.cs`, `DispatchReportServiceTests.cs`

**Interfaces:**
- Produces: `record ClosureResult(bool Closed, IReadOnlyList<string> Blockers)`; `IMonthlyClosureService.CloseAsync(int year, int month) : Task<ClosureResult>`, `ReopenAsync(int year, int month, string reason)` (exige `IsAdmin`), `IsClosedAsync(DateOnly date) : Task<bool>`; `IDispatchReportService.GetMonthlyAsync(int year, int month) : Task<MonthlyDispatchReport>`, `ExportMonthlyExcelAsync(int year, int month) : Task<byte[]>`; `record MonthlyDispatchReport(decimal TotalCost, decimal TotalUnits, int TotalDocuments, int TotalPackages, IReadOnlyList<DailyRow> Days)`; `record DailyRow(DateOnly Date, decimal Cost, decimal Units, int Documents, int Packages, IReadOnlyList<DailyDetailRow> Details)`; `record DailyDetailRow(string ShiftName, string CarrierName, decimal Cost, decimal Units, int Documents, int Packages)`.

- [ ] **Step 1: Write failing tests**
  - `Close_BloqueaConBorradoresDespachadosOSinTarifa`: lista de bloqueos con números de manifiesto.
  - `Close_CongelaCostFinal`: tras cerrar, cambiar tarifa y llamar `RecalculateAsync` no modifica `cost_final` (lanza "período cerrado").
  - `Reopen_SoloAdmin_YRegistraEvento`.
  - `Report_AgrupaPorDiaTurnoTransportista`: 3 manifiestos (01/09 Diurno TSM $597.047; 02/09 Diurno TSM $2.250.125 y Diurno MTO $385.000) → `TotalCost == 3232172`, día 02/09 con 2 detalles y subtotal 2635125; manifiestos `annulled` excluidos; período por `dispatch_date`.
  - `ExportExcel_GeneraHojaConTotales` (ClosedXML abre el archivo, celda de total = 3232172).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** (pantalla del reporte replica la forma de la captura del usuario: 4 KPIs + tabla día expandible a turno × transportista con subtotal). **Step 4: Run** → PASS; migración `CierreMensual` generada.
- [ ] **Step 5: Commit** `feat(despachos): cierre mensual y reporte mensual`

---

### Task 13: Puesta en marcha (QA y PROD)

**Files:**
- Modify: `PENDIENTE.md` (estado real de cada paso), `C:\Users\Jorge\.claude\projects\c--PROYECTOS-Proyecto-Portal-Web-Company\memory\` (memoria "Módulo Despachos — estado")

- [ ] **Step 1:** Suite completa `dotnet test src/Modulo.Despachos.Tests/Modulo.Despachos.Tests.csproj` → 0 fallos; `.\publish-dist.ps1` → `dist/Modulo.Despachos/1.0.0/` sin `.pdb` y con `Modulo.Despachos.deps.json`.
- [ ] **Step 2:** Crear las bases **PostgreSQL** del módulo (nombres definidos por el usuario): QA `ps_comdepor_dsp_qa` (conexión externa `onpremise_depor_despacho_qa`) y PROD `ps_comdepor_dsp` (conexión externa `onpremise_depor_despacho`), en la instancia de módulos (mismo servidor y criterio que `ps_comdepor_rg[_qa]` de Rendiciones); aplicar migraciones con `dotnet ef database update --project src/Modulo.Despachos.Migrations.Postgres` (env `DESPACHOS_CONNECTION_STRING`) contra QA y PROD; verificar con `SELECT` a `__EFMigrationsHistory` que las 5 migraciones (`Inicial`, `Tarifario`, `ConsultasSap`, `Manifiestos`, `CierreMensual`) están en ambas.
- [ ] **Step 3:** En el admin del portal (QA y PROD): alta del módulo `Despachos` en módulos de plataforma, contratarlo a la organización, conexión externa por compañía (`onpremise_depor_despacho_qa` en QA, `onpremise_depor_despacho` en PROD, motor Postgres) apuntando a la base creada, perfiles con las acciones de la tabla de permisos.
- [ ] **Step 4:** Junction `Portal SaaS - Core\artifacts\plugins\Modulo.Despachos` → `dist\Modulo.Despachos` (mismo esquema que Rendiciones), reiniciar Host; log sin `Colisión de ruta Razor` ni errores de carga del plugin.
- [ ] **Step 5:** E2E en QA: cargar mantenedores reales (TSM, MTO, RETIRO, vehículos, choferes), una tarifa base + recargo 17:00 + recargo 2 zonas, consultas SAP probadas, armar manifiesto escaneando documentos reales, despachar, PDF, retorno con un rechazo, cierre de un período de prueba y reporte. Registrar resultado en `PENDIENTE.md` y en memoria.
- [ ] **Step 6: Commit** `chore(despachos): puesta en marcha etapa 1`
