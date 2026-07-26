# Pendiente — Modulo.Rendiciones

Estado: **fase 1 (andamiaje) completa**. Repo nuevo, contrato de plugin, entidades y
`DbContext` portados con la convención de nombres de la plataforma. Nada de esto
compila todavía de punta a punta contra el portal real — ver bloqueos abajo.

Este documento sigue `docs/09-GUIA-DESARROLLO-PLUGINS.md` del repo
`Proyecto Saas Portal` como referencia obligatoria. No renombrar el módulo ni usar
ningún nombre de producto comercial existente en el mercado.

## Hecho en esta fase

- Repo Git inicializado en `C:\PROYECTOS\Modulo.Rendiciones`.
- `src/Modulo.Rendiciones/Modulo.Rendiciones.csproj` — mismo patrón que los plugins
  del portal (net8.0, x64, `PublicarComoPlugin`). Referencia **temporal** por
  `ProjectReference` relativa a `PortalSaas.Abstractions` del repo hermano (ver TODO
  explícito en el `.csproj` — reemplazar por `PackageReference` NuGet cuando exista
  el paquete versionado).
- `ModuloRendiciones.cs` (`IModuloPortal`) — `ModuleCode = "Rendiciones"`, menú de 3
  grupos (Rendidor/Aprobador/Administrador) portado 1:1 del original.
  `RegisterServices` queda con TODOs comentados (ningún servicio portado todavía, ver
  abajo).
- 12 entidades en `Models/`, traducidas a inglés/PascalCase con la convención de BD
  de la plataforma aplicada (`docs/01-CONVENCION-NOMBRES-BD.md`):

  | Original (español) | Nuevo (inglés) | Tabla |
  |---|---|---|
  | `FondoPorRendir` | `ExpenseFund` | `expense_funds` |
  | `RendicionGasto` | `ExpenseReport` | `expense_reports` |
  | `RendicionGastoDetalle` | `ExpenseReportLine` | `expense_report_lines` |
  | `RendicionGastoAccion` | `ExpenseReportAction` | `expense_report_actions` |
  | `ComprobanteAdjunto` | `ExpenseReceipt` | `expense_receipts` |
  | `TipoGasto` | `ExpenseType` | `expense_types` |
  | `TipoDocumento` | `DocumentType` | `document_types` |
  | `PoliticaGasto` | `ExpensePolicy` | `expense_policies` |
  | `CentroCostoUsuario` | `UserCostCenter` | `user_cost_centers` |
  | `GrupoAprobacionRendicion` | `ExpenseApprovalGroup` | `expense_approval_groups` |
  | `GrupoAprobacionRendicionNivel` | `ExpenseApprovalGroupLevel` | `expense_approval_group_levels` |
  | `GrupoAprobacionRendicionMiembro` | `ExpenseApprovalGroupMember` | `expense_approval_group_members` |

- `Data/RendicionesDbContext.cs` — Fluent API completa (`ToTable`, `HasColumnName`
  snake_case explícito para las 12 entidades, FKs, índices únicos).

## Decisiones de diseño tomadas en esta fase (documentadas, no asumidas en silencio)

1. **`CompanyId` (Guid) en vez de duplicar `OrganizationId`** en cada tabla. El
   original scopeaba por `EmpresaCodigo` (código de compañía SAP); acá se tradujo a
   `CompanyId` (FK lógica a `companies.id` de la plataforma), que ya resuelve a
   `organization_id` vía `companies.organization_id` — cumple la regla dura
   ("`organization_id` directo o vía `company_id`", `CLAUDE.md`) sin duplicar la
   columna. Como esta base es propia del plugin (no la compartida de la plataforma),
   no hay FK real de EF Core hacia `companies` — es una columna simple con la
   convención de nombre correcta, documentado en el DbContext.
2. **PK `long`/`bigint identity` para las 12 tablas**, no `Guid`/`uuid`. El original
   usaba `int identity` (con una excepción `long` en `RendicionGastoAccion`).
   `docs/01-CONVENCION-NOMBRES-BD.md` reserva `uuid` para entidades "que se
   referencian entre sí o pueden generarse fuera de la base" — acá el grafo de
   entidades es cerrado dentro del propio plugin (nada se genera fuera de esta base,
   nada se referencia desde la plataforma central), así que `bigint identity` es
   consistente con el criterio de "catálogos/tablas de bajo-medio volumen", aplicado
   parejo a las 12 en vez de mezclar dos tipos de PK sin necesidad real. Si en el
   futuro `ExpenseReport`/`ExpenseFund` necesitan ser referenciados desde fuera del
   plugin (ej. otro módulo, o generación de id en el cliente antes de guardar),
   reconsiderar puntualmente esas dos.
3. **`ExpenseApprovalGroupLevel` y `ExpenseApprovalGroupMember` ganaron un `Id`
   propio** que el original no tenía (PK compuesta) — regla dura de esta plataforma:
   toda tabla lleva `id` surrogate, nunca una PK compuesta de claves de negocio.
   Unicidad original preservada vía índice único (`uq_..._group_id_level` /
   `uq_..._group_id_user_id`).
4. **Timestamps `DateTimeOffset`** (antes `DateTime`) en las columnas `_at`, mismo
   criterio que el resto de la plataforma (`timestamptz` real, con zona horaria).
5. **Nombres de propiedad de negocio traducidos**, no solo la tabla — `Monto`→
   `Amount`, `FechaGasto`→`Date` (columna física `expense_date`, evita choque con
   `DateTime`/palabra reservada en algunos motores), `Estado`→`Status`, `Glosa`→
   `Notes`, `Folio`→`DocumentNumber`, `RutProveedor`→`SupplierTaxId`
   (RUT es específico de Chile, se generalizó a "tax id" pensando en un producto
   multi-país — confirmar con el dueño del proyecto si esto es lo esperado antes de
   avanzar a la UI de captura, que sí va a mostrar la etiqueta correcta en español).

## Bloqueos reales — nada de esto se puede resolver dentro de este repo solo

1. **No existe (todavía) en `PortalSaas.Abstractions` un equivalente a
   `ISqlServerService`** (conexión self-service a una base SQL Server externa ajena
   al SAP de la organización, resuelta por `ModuleCode` + compañía). Confirmado
   contra el `CLAUDE.md` del portal: está explícitamente listado como "diferido a
   propósito (YAGNI)" — nadie lo había necesitado hasta este plugin.
   **Prerrequisito real antes de que `RendicionesDbContext` se pueda registrar de
   verdad** (`ModuloRendiciones.RegisterServices` tiene el TODO comentado
   esperando esto). Se resuelve en el repo del portal, no acá.
2. **No existe todavía un paquete NuGet publicado de `PortalSaas.Abstractions`** —
   el `.csproj` usa una `ProjectReference` relativa cruzando repos como parche
   temporal (documentado ahí mismo), que solo funciona si este repo y
   "Proyecto Saas Portal" son carpetas hermanas en la misma máquina. No sirve para
   un build de CI real ni para distribuir el plugin como producto separado de
   verdad.
3. **Confirmar `RutProveedor`→`SupplierTaxId`** (decisión 5 arriba) con el dueño del
   proyecto antes de construir las páginas de captura.

## Qué falta portar (fase 2 en adelante, en el orden sugerido)

### Servicios (`Servicios/` del original → `Servicios/` acá), 10 interfaces + impl.:
- `IFondoPorRendirService` / `FondoPorRendirService` → `IExpenseFundService`
- `ITipoGastoService` / `TipoGastoService` → `IExpenseTypeService`
- `ITipoDocumentoService` / `TipoDocumentoService` → `IDocumentTypeService`
- `ICentroCostoUsuarioService` / `CentroCostoUsuarioService` → `IUserCostCenterService`
- `IAlmacenamientoAdjuntosService` / `AlmacenamientoAdjuntosService` → `IAttachmentStorageService`
- `IPoliticaGastoService` / `PoliticaGastoService` → `IExpensePolicyService`
- `IGastoService` / `GastoService` → `IExpenseService`
- `IExtractorComprobanteService` / `AzureDocumentIntelligenceExtractorService` → `IReceiptExtractorService`
- `IRoutingService` / `AzureMapsRoutingService` → sin cambio de nombre, típed HttpClient
- `IGrupoAprobacionRendicionesService` / `GrupoAprobacionRendicionesService` → `IExpenseApprovalGroupService`
- `IRendicionGastoService` / `RendicionGastoService` → `IExpenseReportService`
- `IReporteCierreService` / `ReporteCierreService` → `IClosingReportService`

Más los DTOs de soporte: `ComprobanteExtraidoDto`, `GastoGuardadoResultado`,
`LineaReporteCierre`, `ResultadoRutaDto`, `ResultadoValidacionGasto` (traducir
nombres igual que las entidades).

### Páginas (`Pages/` del original, ~20 archivos .cshtml/.cshtml.cs):
`Aprobaciones/`, `Configuracion/{CentrosCostoUsuario,ConsumoServicios,GruposAprobacion,
PoliticasGasto,TiposDocumento,TiposGasto}/`, `FondosPorRendir/`, `Gastos/{Comprobante,
ComprobanteAccesoBase,ComprobanteArchivo,Detalle,Importar,Index}`, `Informes/`,
`RendicionesPageModelBase.cs`, `RendicionesUi.cs`, `Reportes/Cierre.cshtml`,
`Shared/_MensajesRendiciones.cshtml`. Aplicar los gotchas de
`docs/09-GUIA-DESARROLLO-PLUGINS.md` §4-5 del portal (nombres de vista sin colisión —
acá no aplica tanto por ser el único plugin en este repo, pero si algún día conviven
más plugins en el mismo `artifacts/plugins/`, revisar; antiforgery explícito;
cultura invariante en inputs numéricos) desde el primer commit de páginas, no
como fix posterior.

### Integraciones externas (requieren configuración propia, no la del portal):
- Azure Document Intelligence (OCR de comprobantes) — endpoint/clave por
  organización/instalación, nunca hardcodeado.
- Azure Maps (kilometraje) — mismo criterio.

### SQL / migraciones:
Los 12 scripts `Sql/*.sql` del original (`001_fondo_por_rendir.sql` ...
`999_validar_esquema.sql`) están en dialecto HANA/SQL Server directo, sin EF Core
Migrations. Para este proyecto, evaluar si usar EF Core Migrations (consistente con
el resto de la plataforma) en vez de scripts SQL sueltos — pendiente de decidir con
el dueño del proyecto, no asumido.

### Vocabulario a revisar antes de UI visible:
Confirmar que ningún texto de UI (labels, nombres de página, mensajes) coincida con
terminología de marca de un producto comercial existente — el agrupamiento
Rendidor/Aprobador/Administrador ya se mantuvo neutro a propósito.
