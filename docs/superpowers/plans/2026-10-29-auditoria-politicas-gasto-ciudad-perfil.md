# Auditoría y políticas de gasto por ciudad y perfil — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reemplazar el cálculo manual de "Ajustado a Presupuesto" por un motor
dentro de Modulo.Rendiciones que calcula el monto a reembolsar de cada gasto
según políticas por (Tipo de Gasto × Ciudad) y por perfil de usuario, sin
nunca restringir el monto de la boleta, y que surfacea las advertencias al
rendidor y al auditor.

**Architecture:** Se extiende el modelo existente (`ExpensePolicy`,
`ExpenseType`, `ExpenseReportLine`) con nuevas dimensiones (Ciudad, Perfil de
Política, Período) y un campo calculado (`ReimbursementAmount`). Dos
catálogos nuevos (`City`, `PolicyProfile`) siguen el mismo patrón ya usado
por `DocumentType`/`ExpenseType`. El cálculo corre en `ExpenseService` al
guardar un gasto (reemplaza el bloqueo actual); un servicio nuevo calcula la
advertencia semanal agregada aparte, sin tocar líneas individuales.

**Tech Stack:** .NET 8, EF Core 8 (Npgsql), ASP.NET Core Razor Pages, xUnit.

**Spec:** [docs/superpowers/specs/2026-10-29-auditoria-politicas-gasto-ciudad-perfil-design.md](../specs/2026-10-29-auditoria-politicas-gasto-ciudad-perfil-design.md)

## Global Constraints

- Las migraciones de este plan van contra `RendicionesDbContext` (base
  propia del módulo), generadas en
  `Modulo.Rendiciones.Migrations.Postgres` y `Modulo.Rendiciones.Migrations.SqlServer`
  (patrón dual existente), pero **solo se aplican** contra las bases reales
  Postgres `ps_comdepor_rg` y `ps_comdepor_rg_qa` (no hay base SqlServer real
  hoy para este módulo). Verificar con `dotnet-ef migrations list
  --connection "..."` que no quede "(Pending)" en ninguna de las dos antes
  de cerrar la tarea que la generó.
- Comando de referencia (ajustar ruta de proyecto):
  `dotnet tool run dotnet-ef migrations add <Nombre> --project "Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Migrations.Postgres" --startup-project "Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Migrations.Postgres" --context RendicionesDbContext`
- Toda URL interna nueva usa `~/` (PathBase `/Depor/`) — nunca una ruta
  literal `/rendiciones/...` en un `href`/`asp-page` fuera de las rutas
  `@page` (regla dura del repo, `regla-href-tilde-pathbase-tenant.md`).
- Toda pantalla nueva sigue el Design System unificado (`.card-surface`,
  `.table-ps`, `.btn-primary`/`.btn-secondary`, `.form-control`) y es
  responsive a 375px — copiar la estructura de una pantalla hermana
  existente (`Pages/Configuracion/TiposDocumento/Index` para catálogos
  simples), nunca CSS propio.
- Toda entrada de menú nueva lleva `Icon` (`bi-*`) — regla dura
  (`regla-todo-menu-lleva-icono.md`).
- Ninguna política puede impedir el guardado de un gasto por monto —
  anti-goal explícito del spec. Ningún test ni código de este plan debe
  reintroducir un `throw` por monto superado.
- El perfil de política (`PolicyProfile`) nunca se mezcla con los 3 roles de
  acceso existentes (`RendicionesRoles.Rendidor/Aprobador/Administrador`) —
  tablas y pantallas separadas.
- Nombres de tabla/columna en snake_case, convención ya usada en
  `RendicionesDbContext` (inglés, plural, `id` surrogate, `company_id` en
  toda tabla de negocio).

## Review Focus

- Un usuario sin perfil de política asignado (o con un perfil sin ninguna
  política configurada) nunca debe recibir ninguna advertencia ni ajuste:
  `ReimbursementAmount == Amount` siempre. Cubierto en Task 6 (resolución)
  y Task 7 (cálculo).
- Una línea de un `ExpenseType` con `RequiresQuantity=true` pero
  `Quantity == null` (dato viejo, anterior a este cambio) no debe reventar
  la multiplicación del tope — debe tratarse como `1`. Cubierto en Task 7.
- Las líneas de un informe `Rejected` no deben sumar al acumulado semanal de
  combustible de ese usuario (un informe rechazado no se reembolsó).
  Cubierto en Task 8.
- Editar una línea que ya pertenece a un informe (`Status != "Loose"`) debe
  seguir rechazado por `RequireLooseAsync` igual que hoy, aun con los campos
  nuevos (`CityId`, `Quantity`) en el payload. Cubierto en Task 7.
- Dos políticas "default" (`CityId` y `PolicyProfileId` ambos `null`) para el
  mismo `ExpenseType` deben ser rechazadas con un mensaje entendible, no con
  una excepción cruda de restricción única de Postgres. Cubierto en Task 5.

---

### Task 1: Catálogo de Ciudades

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Models/City.cs`
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/ICityService.cs`
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/CityService.cs`
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Configuracion/Ciudades/Index.cshtml`
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Configuracion/Ciudades/Index.cshtml.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Data/RendicionesDbContext.cs` (nuevo `DbSet<City> Cities`, bloque `modelBuilder.Entity<City>`)
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/ModuloRendiciones.cs` (`services.AddScoped<ICityService, CityService>()`, nuevo `MenuItemDefinition` bajo `grupo-administrador`)
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Migrations.Postgres/Migrations/<timestamp>_AddCities.cs` (generado)
- Create: equivalente en `Modulo.Rendiciones.Migrations.SqlServer`
- Test: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Servicios/CityServiceTests.cs`

**Interfaces:**
- Produces: `City { long Id; Guid CompanyId; string Name; bool IsActive; }`.
  `ICityService.ListActiveAsync(Guid companyId, CancellationToken ct = default) -> Task<IReadOnlyList<City>>`,
  `ListAllAsync(Guid companyId, CancellationToken ct = default) -> Task<IReadOnlyList<City>>`,
  `CreateAsync(Guid companyId, string name, CancellationToken ct = default) -> Task<long>`,
  `UpdateAsync(long id, Guid companyId, string name, bool isActive, CancellationToken ct = default) -> Task`.

- [ ] **Step 1: Write failing tests for `CityService`**

```csharp
// CreateAsync_PersistsCity_AndListActiveAsync_ReturnsIt
// CreateAsync_DuplicateNameSameCompany_Throws  (mismo criterio que ExpenseTypeService: nombre único por compañía -- usar AnyAsync antes de insertar, mismo patrón que ExpensePolicyService.CreateAsync)
// UpdateAsync_ChangesNameAndIsActive
// ListActiveAsync_ExcludesInactive
```
Usar el mismo fixture de `RendicionesDbContext` en memoria que ya usan los
tests existentes de `ExpenseTypeServiceTests` (mismo directorio) — seguir
ese archivo como plantilla exacta de setup.

- [ ] **Step 2: Run tests, verify they fail** (tipos/servicio no existen todavía)

- [ ] **Step 3: Implement `Models/City.cs`, `Servicios/ICityService.cs`, `Servicios/CityService.cs`**

Copiar la forma exacta de `Models/DocumentType.cs` + `IDocumentTypeService`/`DocumentTypeService` (mismo patrón: `Name` único por `CompanyId`, `IsActive`).

- [ ] **Step 4: Registrar `City` en `RendicionesDbContext`**

`ToTable("expense_cities")`, columnas `id`, `company_id`, `name` (`HasMaxLength(100)`, `IsRequired()`), `is_active`; índice único `(company_id, name)` nombrado `uq_expense_cities_company_id_name`.

- [ ] **Step 5: Generar y aplicar la migración `AddCities`**

Generar en ambos proyectos de migraciones (Postgres + SqlServer). Aplicar
solo la de Postgres contra `ps_comdepor_rg` y `ps_comdepor_rg_qa`. Verificar
con `migrations list --connection` que ninguna quede pendiente.

- [ ] **Step 6: Run tests, verify they pass**

- [ ] **Step 7: Registrar el servicio y el menú en `ModuloRendiciones.cs`**

`MenuItemDefinition { Code = "config-ciudades", ParentCode = "grupo-administrador", Name = "Ciudades", Icon = "bi-geo-alt", PageRoute = "/rendiciones/configuracion/ciudades", Order = <siguiente disponible> }`.

- [ ] **Step 8: Construir la pantalla `Pages/Configuracion/Ciudades/Index`**

Copiar estructura exacta de `Pages/Configuracion/TiposDocumento/Index.cshtml` + `.cshtml.cs` (tabla + alta + activar/desactivar), adaptando los campos a solo `Name`.

- [ ] **Step 9: Compilar el plugin y verificar manualmente la pantalla** (`dotnet build`, luego `publish-dist.ps1` si corresponde al flujo del repo)

- [ ] **Step 10: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones" "Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Migrations.Postgres" "Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Migrations.SqlServer" "Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests"
git commit -m "feat(rendiciones): catálogo de Ciudades"
```

---

### Task 2: `ExpenseType` — cantidad por línea (Movilización/Bip)

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Models/ExpenseType.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/IExpenseTypeService.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/ExpenseTypeService.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Data/RendicionesDbContext.cs` (bloque `ExpenseType`)
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Configuracion/TiposGasto/Index.cshtml` + `.cshtml.cs`
- Create: migración `AddExpenseTypeQuantity` (Postgres + SqlServer)
- Test: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Servicios/ExpenseTypeServiceTests.cs` (extender el existente)

**Interfaces:**
- Consumes: ninguno de otra task.
- Produces: `ExpenseType.RequiresQuantity: bool`, `ExpenseType.QuantityLabel: string?`.
  `IExpenseTypeService.CreateAsync(Guid companyId, string name, string? sapGlAccount, bool isMileage, decimal? ratePerKm, bool requiresQuantity, string? quantityLabel, CancellationToken ct = default) -> Task<long>`,
  `UpdateAsync(long id, Guid companyId, string name, string? sapGlAccount, bool isActive, bool isMileage, decimal? ratePerKm, bool requiresQuantity, string? quantityLabel, CancellationToken ct = default) -> Task`.

- [ ] **Step 1: Write failing tests**

```csharp
// CreateAsync_RequiresQuantityTrue_PersistsLabel
// UpdateAsync_ChangesRequiresQuantityAndLabel
```

- [ ] **Step 2: Run tests, verify they fail**

- [ ] **Step 3: Agregar los dos campos a `ExpenseType`, actualizar firmas de `IExpenseTypeService`/`ExpenseTypeService`**

Todos los demás call sites de `CreateAsync`/`UpdateAsync` (grep `_expenseTypes.CreateAsync`, `_expenseTypes.UpdateAsync` en `Pages/Configuracion/TiposGasto/Index.cshtml.cs`) pasan `requiresQuantity: false, quantityLabel: null` salvo que el formulario ya exponga el campo (Step 6).

- [ ] **Step 4: Registrar las columnas en `RendicionesDbContext`**

`requires_quantity` (`bool`, default `false`), `quantity_label` (`varchar(50)`, nullable).

- [ ] **Step 5: Generar y aplicar la migración `AddExpenseTypeQuantity`** (mismo procedimiento que Task 1, Step 5)

- [ ] **Step 6: Agregar los campos al formulario de `Pages/Configuracion/TiposGasto/Index.cshtml`**

Checkbox "Requiere cantidad" + input de texto "Rótulo" (ej. "Días"), mismo patrón de inputs que el resto de la grilla de ese archivo.

- [ ] **Step 7: Run tests, verify they pass**

- [ ] **Step 8: Commit**

```bash
git commit -m "feat(rendiciones): tipos de gasto con cantidad (días) para topes por día"
```

---

### Task 3: `ExpenseReportLine` — nuevas columnas (Ciudad, Cantidad, Monto a Reembolsar)

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Models/ExpenseReportLine.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Data/RendicionesDbContext.cs` (bloque `ExpenseReportLine`)
- Create: migración `AddExpenseReportLineCityQuantityReimbursement` (Postgres + SqlServer)
- Test: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Servicios/ExpenseServiceTests.cs` (extender el existente, si no existe crear uno nuevo siguiendo el patrón de `ExpenseReportServiceAttachExpensesTests.cs`)

**Interfaces:**
- Consumes: `City` (Task 1), nada más.
- Produces: `ExpenseReportLine.CityId: long?`, `ExpenseReportLine.City: City?` (navegación), `ExpenseReportLine.Quantity: decimal?`, `ExpenseReportLine.ReimbursementAmount: decimal` (no nullable).

Este task es puramente de esquema -- nada en el código todavía calcula ni usa
estos campos (eso es Task 6/7). El default de `ReimbursementAmount` debe
quedar igual a `Amount` para que ningún código existente que lea
`ReimbursementAmount` antes de Task 7 vea `0`.

- [ ] **Step 1: Write a failing test**

```csharp
// CreateAsync_NewLine_ReimbursementAmountDefaultsToAmount
// (guarda una ExpenseReportLine sin tocar los campos nuevos, lee de vuelta y verifica
//  ReimbursementAmount == Amount, CityId == null, Quantity == null)
```

- [ ] **Step 2: Run test, verify it fails** (columnas no existen)

- [ ] **Step 3: Agregar los 3 campos a `ExpenseReportLine`**

`ReimbursementAmount` se inicializa en el setter de `Amount` dentro del
constructor por defecto del tipo (`public decimal ReimbursementAmount { get; set; }`
sin inicializador propio): el valor real se fija en `ExpenseService` (Task
7) antes de `SaveChangesAsync`, así que acá basta con que la columna exista
y acepte el valor que el código ya setea hoy indirectamente a través de
`Amount` en el test de este Step.

- [ ] **Step 4: Registrar las columnas en `RendicionesDbContext`**

`city_id` (nullable, FK a `expense_cities`, `OnDelete(DeleteBehavior.Restrict)`, constraint `fk_expense_report_lines_expense_cities`), `quantity` (`HasPrecision(10, 2)`, nullable), `reimbursement_amount` (`HasPrecision(18, 2)`, `IsRequired()`).

- [ ] **Step 5: Generar la migración**

La migración debe incluir un `UPDATE expense_report_lines SET
reimbursement_amount = amount` (vía `migrationBuilder.Sql(...)`) para las
filas existentes, ya que la columna nueva es `NOT NULL` y las filas de hoy
no tienen valor — sin este paso la migración falla al aplicarse sobre datos
reales. Aplicar contra `ps_comdepor_rg` y `ps_comdepor_rg_qa`.

- [ ] **Step 6: Run test, verify it passes**

- [ ] **Step 7: Commit**

```bash
git commit -m "feat(rendiciones): columnas Ciudad, Cantidad y Monto a Reembolsar en la línea de gasto"
```

---

### Task 4: Catálogo de Perfiles de Política + asignación por usuario

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Models/PolicyProfile.cs`
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Models/UserPolicyProfile.cs`
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/IPolicyProfileService.cs`
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/PolicyProfileService.cs`
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Configuracion/PerfilesPolitica/Index.cshtml` + `.cshtml.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Configuracion/UsuariosRoles/Index.cshtml` + `.cshtml.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Data/RendicionesDbContext.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/ModuloRendiciones.cs`
- Create: migración `AddPolicyProfiles` (Postgres + SqlServer)
- Test: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Servicios/PolicyProfileServiceTests.cs`

**Interfaces:**
- Produces: `PolicyProfile { long Id; Guid CompanyId; string Name; bool IsActive; }`,
  `UserPolicyProfile { long Id; Guid CompanyId; Guid UserId; long PolicyProfileId; }` (una fila activa por `(CompanyId, UserId)`, igual criterio que `ExpenseApprovalGroupMember`).
  `IPolicyProfileService`:
  - `ListActiveAsync(Guid companyId, CancellationToken ct = default) -> Task<IReadOnlyList<PolicyProfile>>`
  - `ListAllAsync(Guid companyId, CancellationToken ct = default) -> Task<IReadOnlyList<PolicyProfile>>`
  - `CreateAsync(Guid companyId, string name, CancellationToken ct = default) -> Task<long>`
  - `UpdateAsync(long id, Guid companyId, string name, bool isActive, CancellationToken ct = default) -> Task`
  - `GetAssignedProfileIdAsync(Guid companyId, Guid userId, CancellationToken ct = default) -> Task<long?>` (usado por Task 6/8)
  - `AssignAsync(Guid companyId, Guid userId, long? policyProfileId, CancellationToken ct = default) -> Task` (`null` = quitar el perfil asignado)
  - `ListAssignmentsAsync(Guid companyId, CancellationToken ct = default) -> Task<IReadOnlyDictionary<Guid, PolicyProfile>>` (usado por `UsuariosRoles/Index` para mostrar el perfil de cada fila)

- [ ] **Step 1: Write failing tests**

```csharp
// CreateAsync_PersistsProfile
// AssignAsync_SetsProfile_GetAssignedProfileIdAsync_ReturnsIt
// AssignAsync_NullProfileId_ClearsAssignment
// AssignAsync_ReplacesExistingAssignment_OneActivePerUser
// ListAssignmentsAsync_ReturnsOnlyUsersWithAssignment
```

- [ ] **Step 2: Run tests, verify they fail**

- [ ] **Step 3: Implementar `Models/PolicyProfile.cs`, `Models/UserPolicyProfile.cs`, `IPolicyProfileService`, `PolicyProfileService`**

`AssignAsync` hace upsert: busca la fila existente de `(CompanyId, UserId)`,
si `policyProfileId is null` la borra, si no la crea/actualiza — mismo
criterio que `UserCostCenter`/`RendicionesUserRole` (consultar esos
servicios como plantilla de upsert si hace falta).

- [ ] **Step 4: Registrar ambas entidades en `RendicionesDbContext`**

`policy_profiles` (`id`, `company_id`, `name` `HasMaxLength(100)`, `is_active`, índice único `(company_id, name)`); `user_policy_profiles` (`id`, `company_id`, `user_id`, `policy_profile_id` FK con `OnDelete(DeleteBehavior.Restrict)`, índice único `(company_id, user_id)` nombrado `uq_user_policy_profiles_company_id_user_id`).

- [ ] **Step 5: Generar y aplicar la migración `AddPolicyProfiles`**

- [ ] **Step 6: Run tests, verify they pass**

- [ ] **Step 7: Registrar el servicio y el menú en `ModuloRendiciones.cs`**

`Code = "config-perfiles-politica"`, `Icon = "bi-person-badge"`, `PageRoute = "/rendiciones/configuracion/perfiles-politica"`.

- [ ] **Step 8: Construir `Pages/Configuracion/PerfilesPolitica/Index`**

Copiar `Pages/Configuracion/Ciudades/Index` (Task 1, mismo patrón exacto, solo cambia el sustantivo).

- [ ] **Step 9: Agregar el combo "Perfil de política" a `Pages/Configuracion/UsuariosRoles/Index.cshtml`**

Una columna más en la tabla de `UserRoleRow` (agregar `long? PolicyProfileId, string? PolicyProfileName` al record) con un `<select>` + botón "Guardar" propio por fila (mismo patrón de formulario independiente que ya usa `OnPostToggleActivoAsync`, no se mezcla con el guardado por lotes de roles). Nuevo handler `OnPostGuardarPerfilAsync(Guid userId, long? perfilId, CancellationToken ct)` que llama a `_policyProfiles.AssignAsync` y redirige. `LoadAsync` se extiende para traer `ListAssignmentsAsync` y poblar la columna nueva.

- [ ] **Step 10: Compilar y verificar manualmente la pantalla**

- [ ] **Step 11: Commit**

```bash
git commit -m "feat(rendiciones): catálogo de Perfiles de Política y asignación por usuario"
```

---

### Task 5: `ExpensePolicy` extendida (Ciudad, Perfil, Período; sin bloqueo)

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Models/ExpensePolicy.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/IExpensePolicyService.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/ExpensePolicyService.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Data/RendicionesDbContext.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Configuracion/PoliticasGasto/Index.cshtml` + `.cshtml.cs`
- Create: migración `AddExpensePolicyCityProfilePeriod` (Postgres + SqlServer)
- Test: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Servicios/ExpensePolicyServiceTests.cs` (extender el existente)

**Interfaces:**
- Consumes: `City` (Task 1), `PolicyProfile` (Task 4).
- Produces: `ExpensePolicy.CityId: long?`, `ExpensePolicy.PolicyProfileId: long?`, `ExpensePolicy.Period: string` (`"PerDocument"` | `"Weekly"`, usar constantes `ExpensePolicyPeriod.PerDocument`/`.Weekly` en un nuevo `Models/ExpensePolicyPeriod.cs`). `IsBlocking` **eliminado** del modelo y de todas las firmas.
  `IExpensePolicyService.CreateAsync(Guid companyId, long expenseTypeId, long? cityId, long? policyProfileId, string period, decimal? maxAmount, CancellationToken ct = default) -> Task<long>`,
  `UpdateAsync(long id, Guid companyId, decimal? maxAmount, bool isActive, CancellationToken ct = default) -> Task`.

- [ ] **Step 1: Write failing tests**

```csharp
// CreateAsync_DefaultCityAndProfile_Persists
// CreateAsync_SameTypeDifferentCity_BothAllowed
// CreateAsync_DuplicateTypeAndCityAndProfile_ThrowsFriendlyMessage
// CreateAsync_WeeklyPeriod_IgnoresCityId  (si se pasa un cityId con Period=Weekly, el servicio lo guarda como null -- ver Decisión 6 del spec)
// UpdateAsync_NoLongerAcceptsIsBlocking  (compila sin ese parámetro)
```

- [ ] **Step 2: Run tests, verify they fail**

- [ ] **Step 3: Agregar `Models/ExpensePolicyPeriod.cs` (constantes `PerDocument`/`Weekly`), extender `ExpensePolicy`, actualizar `IExpensePolicyService`/`ExpensePolicyService`**

`CreateAsync` hace `AnyAsync(p => p.CompanyId == companyId && p.ExpenseTypeId == expenseTypeId && p.CityId == cityId && p.PolicyProfileId == policyProfileId)` antes de insertar (mismo patrón ya usado hoy) y lanza `InvalidOperationException` con mensaje entendible si ya existe -- esto cubre el Review Focus de las dos filas "default" a nivel de servicio, antes de que el índice de base lo rechace. Si `period == Weekly`, fuerza `cityId = null` sin importar lo que llegó.

Nota: el test `CreateAsync_DuplicateTypeAndCityAndProfile_ThrowsFriendlyMessage`
cubre el chequeo a nivel de servicio contra el proveedor EF InMemory que
usan los tests de este proyecto -- ese proveedor no aplica el índice único
parcial de la base real. El índice único (Step 4) es una segunda barrera
que solo se puede confirmar contra Postgres real: al aplicar la migración
en `ps_comdepor_rg_qa` (Step 5), verificar a mano insertando dos filas
"default" por SQL directo y confirmando que la segunda falla, antes de dar
el task por cerrado.

- [ ] **Step 4: Actualizar el bloque `ExpensePolicy` en `RendicionesDbContext`**

Quitar columna `is_blocking`. Agregar `city_id` (FK a `expense_cities`, `OnDelete(DeleteBehavior.Restrict)`), `policy_profile_id` (FK a `policy_profiles`, `OnDelete(DeleteBehavior.Restrict)`), `period` (`varchar(20)`, `IsRequired()`, default `"PerDocument"`). Índice único parcial: como EF Core no expone partial unique index de forma fluida con `HasFilter` portable entre proveedores con columnas nullable combinadas de forma simple, usar `e.HasIndex(x => new { x.CompanyId, x.ExpenseTypeId, x.CityId, x.PolicyProfileId }).IsUnique().HasFilter(null)` no alcanza para tratar NULLs como iguales -- resolver agregando dos columnas computadas no mapeadas a propiedades (`city_key` = `COALESCE(city_id, 0)`, `profile_key` = `COALESCE(policy_profile_id, 0)`) vía `HasComputedColumnSql` y poner el índice único sobre `(company_id, expense_type_id, city_key, profile_key)` en su lugar.

- [ ] **Step 5: Generar y aplicar la migración `AddExpensePolicyCityProfilePeriod`**

Incluir en la migración un `UPDATE expense_policies SET period = 'PerDocument'` para filas existentes (ya cubierto por el default de columna, verificar igual) y confirmar que la columna `is_blocking` se dropea sin que ningún otro código la siga leyendo (grep `IsBlocking` en todo el repo del plugin antes de aplicar).

- [ ] **Step 6: Run tests, verify they pass**

- [ ] **Step 7: Rehacer `Pages/Configuracion/PoliticasGasto/Index`**

La grilla deja de agrupar "un tipo sin política" vs. "con política": lista
TODAS las filas de `ExpensePolicy` (ya no hay `ExpenseTypesWithoutPolicy`),
agrupadas visualmente por `ExpenseType.Name`. El formulario "Nueva política"
permite elegir cualquier Tipo de Gasto (sin filtrar), más selects de Ciudad
(opcional, "Cualquier ciudad"), Perfil (opcional, "Cualquier usuario") y
Período (`PerDocument`/`Weekly`, con el select de Ciudad oculto vía JS
simple cuando se elige Semanal, igual que `ImportarExcel` ya oculta/muestra
campos condicionalmente en este módulo). Quitar el checkbox "Bloqueante" y
el texto que lo explica.

- [ ] **Step 8: Compilar y verificar manualmente la pantalla**

- [ ] **Step 9: Commit**

```bash
git commit -m "feat(rendiciones): políticas de gasto por ciudad y perfil, sin bloqueo por monto"
```

---

### Task 6: Resolución de la política aplicable

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/IExpensePolicyService.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/ExpensePolicyService.cs`
- Test: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Servicios/ExpensePolicyServiceResolutionTests.cs`

**Interfaces:**
- Consumes: `IPolicyProfileService.GetAssignedProfileIdAsync` (Task 4), `ExpensePolicy` extendida (Task 5).
- Produces: `IExpensePolicyService.FindApplicablePolicyAsync(Guid companyId, long expenseTypeId, long? cityId, long? userProfileId, string period, CancellationToken ct = default) -> Task<ExpensePolicy?>`.

- [ ] **Step 1: Write failing tests (los 4 niveles de fallback del spec + ningún match)**

```csharp
// Level1_CityAndProfileMatch_Wins
// Level2_CityMatchesProfileNull_WinsWhenNoLevel1
// Level3_ProfileMatchesCityNull_WinsWhenNoLevel1Or2
// Level4_BothNull_WinsWhenNothingElseMatches
// NoPolicyAtAll_ReturnsNull
// UserWithoutProfile_OnlyLevels2And4Considered  (nunca matchea una política con PolicyProfileId != null)
// InactivePolicy_NeverMatches
```

- [ ] **Step 2: Run tests, verify they fail**

- [ ] **Step 3: Implementar `FindApplicablePolicyAsync` en `ExpensePolicyService`**

Traer las políticas activas de `(companyId, expenseTypeId, period)` con una
sola query y elegir en memoria la de mayor especificidad según el orden del
spec (Decisión 4) -- el volumen por tipo es bajo (a lo sumo unas pocas filas
por ciudad/perfil), no hace falta optimizar con SQL puro.

- [ ] **Step 4: Run tests, verify they pass**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(rendiciones): resolución de política por ciudad y perfil con fallback"
```

---

### Task 7: Cálculo del reembolso en `ExpenseService` (sin bloqueo)

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/ExpenseValidationResult.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/IExpensePolicyService.cs` (firma de `ValidateAsync`)
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/ExpensePolicyService.cs` (`ValidateAsync`)
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/ExpenseService.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Gastos/Detalle.cshtml.cs` (call sites de `CreateAsync`/`UpdateAsync` ya existentes; agregar `CityId`/`Quantity` al payload)
- Test: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Servicios/ExpenseServiceReimbursementTests.cs`

**Interfaces:**
- Consumes: `IExpensePolicyService.FindApplicablePolicyAsync` (Task 6), `IPolicyProfileService.GetAssignedProfileIdAsync` (Task 4), `ExpenseType.RequiresQuantity` (Task 2), `ExpenseReportLine.CityId/Quantity/ReimbursementAmount` (Task 3).
- Produces: `ExpenseValidationResult(decimal ReimbursementAmount, IReadOnlyList<string> Warnings)` (reemplaza el `bool Blocked` actual). `ExpenseService.CreateAsync`/`UpdateAsync` ya no lanzan por monto superado.

- [ ] **Step 1: Write failing tests**

```csharp
// CreateAsync_AmountUnderCap_ReimbursementEqualsAmount_NoWarning
// CreateAsync_AmountOverCap_ReimbursementCapped_WarningAdded
// CreateAsync_NoApplicablePolicy_ReimbursementEqualsAmount
// CreateAsync_QuantityMultiplier_CapIsMaxAmountTimesQuantity  (Bip: MaxAmount=1700, Quantity=3 -> tope 5100)
// CreateAsync_RequiresQuantityTrue_QuantityNull_TreatedAsOne
// CreateAsync_WeeklyPolicyExists_DoesNotAffectReimbursementAmount  (solo políticas PerDocument tocan el cálculo acá)
// CreateAsync_NeverThrowsForAmountOverCap  (reemplaza el test viejo que esperaba throw -- actualizar/borrar el test existente de bloqueo si queda alguno en ExpensePolicyServiceTests o ExpenseServiceTests)
// UpdateAsync_StillRequiresLooseStatus  (Review Focus: el guard RequireLooseAsync sigue vigente con los campos nuevos)
```

- [ ] **Step 2: Run tests, verify they fail**

- [ ] **Step 3: Reescribir `ExpenseValidationResult` y la firma de `ValidateAsync`**

`ValidateAsync(Guid companyId, Guid userId, long? expenseTypeId, long? cityId, decimal amount, decimal quantity, string? documentNumber, string? supplierTaxId, long? expenseIdToExclude, CancellationToken ct = default) -> Task<ExpenseValidationResult>`.
Dentro: resuelve el perfil del usuario (`GetAssignedProfileIdAsync`), busca
la política `PerDocument` aplicable (`FindApplicablePolicyAsync`), calcula
`tope = policy.MaxAmount * quantity` y `reimbursement = Min(amount, tope)`
si hay tope, si no `reimbursement = amount`; agrega la advertencia de texto
si `amount > tope` (mismo tono que el mensaje actual, mencionando tipo,
ciudad y montos). La detección de duplicados por número de documento se
mantiene igual que hoy.

- [ ] **Step 4: Actualizar `ExpenseService.CreateAsync`/`UpdateAsync`**

Quitar el `if (result.Blocked) throw`. Setear
`expense.ReimbursementAmount = result.ReimbursementAmount;` antes de
`SaveChangesAsync`. Pasar `expense.CityId` y `expense.Quantity ?? 1` a
`ValidateAsync`.

- [ ] **Step 5: Actualizar `Pages/Gastos/Detalle.cshtml.cs`**

Agregar `CityId`/`Quantity` a `ExpenseInput` y al `ExpenseReportLine` que se
arma en `OnPostGuardarAsync`, condicionados a que el `ExpenseType` elegido
tenga una política por ciudad activa (Ciudad) o `RequiresQuantity` (Cantidad)
-- la UI completa de estos campos es Task 10, acá solo se agregan al modelo
de binding para que el flujo compile y los datos lleguen a `ExpenseService`.

- [ ] **Step 6: Run tests, verify they pass**

- [ ] **Step 7: Commit**

```bash
git commit -m "feat(rendiciones): cálculo de monto a reembolsar, sin bloqueo por política"
```

---

### Task 8: Advertencia semanal agregada

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/IWeeklyExpenseLimitService.cs`
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/WeeklyExpenseLimitService.cs`
- Create: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/WeeklyLimitWarning.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/ModuloRendiciones.cs` (`services.AddScoped<IWeeklyExpenseLimitService, WeeklyExpenseLimitService>()`)
- Test: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Servicios/WeeklyExpenseLimitServiceTests.cs`

**Interfaces:**
- Consumes: `ExpensePolicy` (Period=Weekly, Task 5), `IPolicyProfileService.GetAssignedProfileIdAsync` (Task 4), `ExpenseReportLine.ReimbursementAmount` (Task 3/7).
- Produces: `WeeklyLimitWarning(string ExpenseTypeName, DateOnly WeekStart, DateOnly WeekEnd, decimal TotalReimbursed, decimal MaxAmount, decimal Excess)`.
  `IWeeklyExpenseLimitService.GetWarningsAsync(Guid companyId, Guid userId, DateOnly anyDateInWeek, CancellationToken ct = default) -> Task<IReadOnlyList<WeeklyLimitWarning>>` (solo devuelve las que exceden -- lista vacía si todo está dentro del tope o el usuario no tiene perfil con política semanal).

- [ ] **Step 1: Write failing tests**

```csharp
// NoProfileAssigned_ReturnsEmpty
// ProfileWithoutWeeklyPolicy_ReturnsEmpty
// UnderCap_ReturnsEmpty
// OverCap_ReturnsWarningWithCorrectExcess
// ExcludesLinesFromRejectedReports  (Review Focus)
// IncludesLooseAndInReportLines  (sueltas + en informe Pending/Approved, no Rejected)
// WeekBoundary_MondayToSunday_LinesOutsideWeekNotCounted
```

- [ ] **Step 2: Run tests, verify they fail**

- [ ] **Step 3: Implementar `WeeklyExpenseLimitService`**

Calcular lunes/domingo de la semana de `anyDateInWeek`
(`DayOfWeek.Monday` como ancla, `ISOWeek` o resta manual de días). Para
cada `ExpensePolicy` activa con `Period == Weekly` cuyo `PolicyProfileId`
coincide con el perfil del usuario (o es `null`), sumar
`ReimbursementAmount` de `ExpenseReportLine` de ese `ExpenseType`, ese
usuario, `Date` dentro de la semana, y
`ExpenseReportId == null || ExpenseReport.Status != "Rejected"`. Si la suma
excede `MaxAmount`, agregar un `WeeklyLimitWarning`.

- [ ] **Step 4: Run tests, verify they pass**

- [ ] **Step 5: Registrar el servicio en `ModuloRendiciones.cs`**

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(rendiciones): advertencia de tope semanal acumulado por perfil"
```

---

### Task 9: Rewire de consumidores — el reembolso es el monto que se paga

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/ExpenseFundService.cs` (línea ~36)
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/ClosingReportLine.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Servicios/ClosingReportService.cs` (línea ~88)
- Test: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Servicios/ExpenseFundServiceTests.cs` (extender)
- Test: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones.Tests/Servicios/ClosingReportServiceTests.cs` (extender)

**Interfaces:**
- Consumes: `ExpenseReportLine.ReimbursementAmount` (Task 3/7).
- Produces: `ClosingReportLine` gana el campo `decimal ReimbursementAmount` (se agrega al final del record para no romper el orden posicional de construcciones existentes, o se nombra explícitamente en cada construcción -- decisión del builder).

- [ ] **Step 1: Write failing tests**

```csharp
// CalculatePendingBalanceAsync_UsesReimbursementAmount_NotAmount
//   (línea con Amount=10000, ReimbursementAmount=6000 en un informe Approved;
//    el saldo pendiente del fondo debe reflejar 6000 restado, no 10000)
// GetLinesAsync_ClosingReportLine_IncludesBothAmountAndReimbursementAmount
```

- [ ] **Step 2: Run tests, verify they fail**

- [ ] **Step 3: Cambiar `ExpenseFundService.CalculatePendingBalanceAsync`**

`SumAsync(d => (decimal?)d.ReimbursementAmount, ct)` en vez de `d.Amount`.

- [ ] **Step 4: Agregar `ReimbursementAmount` a `ClosingReportLine` y a la construcción en `ClosingReportService.GetLinesAsync`**

`Amount` se mantiene en el record (monto de boleta, visible para quien
reciba el export); se agrega `ReimbursementAmount` como el monto real
exportado/contabilizado.

- [ ] **Step 5: Run tests, verify they pass**

- [ ] **Step 6: Commit**

```bash
git commit -m "fix(rendiciones): el saldo de fondos y el cierre contable usan el monto a reembolsar, no el de la boleta"
```

---

### Task 10: UI del rendidor — Ciudad, Cantidad y advertencia al digitar

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Gastos/Detalle.cshtml`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Gastos/Detalle.cshtml.cs` (ya tiene `CityId`/`Quantity` en `ExpenseInput` desde Task 7; falta cargar `Cities` y exponerlos a la vista)

**Interfaces:**
- Consumes: `ICityService.ListActiveAsync` (Task 1), `ExpenseType.RequiresQuantity/QuantityLabel` (Task 2), `ExpenseValidationResult`/`ExpenseSavedResult.Warnings` (Task 7, ya fluye hoy a `WarningMessage`).

- [ ] **Step 1: Agregar `Cities` a `DetalleModel` y cargarlo en `LoadCatalogsAsync`**

`public IReadOnlyList<City> Cities { get; private set; } = Array.Empty<City>();` poblado con `await _cities.ListActiveAsync(...)` (inyectar `ICityService` en el constructor).

- [ ] **Step 2: Agregar los campos a `Detalle.cshtml`**

Combo "Ciudad" (`asp-for="Input.CityId"`, opciones de `Model.Cities`) y
campo "Cantidad" (`asp-for="Input.Quantity"`) con su rótulo tomado de
`ExpenseType.QuantityLabel` cuando el tipo elegido lo tiene -- mostrar/
ocultar ambos campos con el mismo patrón JS mínimo que ya usa esta página
para el bloque de kilometraje (`Origin`/`Destination` cuando `IsMileage`),
siguiendo esa sección como plantilla exacta de show/hide por tipo elegido.

- [ ] **Step 3: Verificar que la advertencia de política siga mostrándose igual que hoy**

`WarningMessage` ya se setea en `OnPostGuardarAsync` desde
`warnings` (sin cambios de código necesarios, viene de Task 7) -- solo
confirmar manualmente que el mensaje menciona el monto a reembolsar cuando
corresponde.

- [ ] **Step 4: Compilar y probar manualmente**: crear un gasto "Almuerzo" en una ciudad con tope, monto superior al tope, verificar que se guarda igual y aparece la advertencia; crear un gasto "Movilización (Bip)" con Cantidad=3 y verificar el tope multiplicado.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(rendiciones): Ciudad y Cantidad en el formulario de gasto, advertencia de política visible"
```

---

### Task 11: Vista del auditor — ajustes por línea y advertencia semanal

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Informes/Detalle.cshtml.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Informes/Detalle.cshtml`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Aprobaciones/Index.cshtml.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Rendiciones/src/Modulo.Rendiciones/Pages/Aprobaciones/Index.cshtml`

**Interfaces:**
- Consumes: `ExpenseReportLine.Amount/ReimbursementAmount` (Task 3/7), `IWeeklyExpenseLimitService.GetWarningsAsync` (Task 8).

- [ ] **Step 1: Agregar `WeeklyWarnings` a `DetalleModel`**

`public IReadOnlyList<WeeklyLimitWarning> WeeklyWarnings { get; private set; }`, poblado en `LoadAsync` con `await _weeklyLimits.GetWarningsAsync(_currentCompany.CompanyId, report.UserId, DateOnly.FromDateTime(DateTime.Today), ct)` (inyectar `IWeeklyExpenseLimitService`).

- [ ] **Step 2: Mostrar el ajuste por línea en `Informes/Detalle.cshtml`**

En el `<details class="rnd-review-line">` de cada gasto (ya rediseñado en
una sesión anterior con `rnd-card-icon`/`rnd-card-main`/`rnd-card-sub`),
cuando `line.ReimbursementAmount < line.Amount` agregar un badge "Ajustado
a tope" junto al monto, y dentro del detalle expandido una línea "Boleta:
@line.Amount.ToString("N0") · Reembolsable: @line.ReimbursementAmount.ToString("N0")".

- [ ] **Step 3: Agregar la sección de advertencias semanales**

Un bloque aparte (antes o después de la lista de gastos, no dentro del
`<details>` de ninguna línea puntual, porque es por semana/tipo) que
itera `Model.WeeklyWarnings` y muestra el texto ya armado por el servicio
(tipo, semana, gastado, tope, exceso).

- [ ] **Step 4: Cambiar el total mostrado del informe a `ReimbursementAmount`**

Dondequiera que `Informes/Detalle.cshtml` sume `Report.Lines.Sum(l => l.Amount)` para mostrar el total a aprobar, cambiar a `Sum(l => l.ReimbursementAmount)`, manteniendo el total de `Amount` visible como dato secundario (ej. "Total boletas: $X · Total a reembolsar: $Y" cuando difieren).

- [ ] **Step 5: Repetir los pasos 1-4 en `Pages/Aprobaciones/Index`** (mismo patrón de `IndexModel`/`Index.cshtml` ya usado para `SubmitterDisplayName`, agregar el mismo tipo de resumen por informe en la lista de Pendientes)

- [ ] **Step 6: Compilar y probar manualmente**: aprobar un informe con una línea ajustada y verificar que el total que se muestra antes de aprobar sea el reembolsable, no el de boletas.

- [ ] **Step 7: Commit**

```bash
git commit -m "feat(rendiciones): vista de auditor con ajustes por política y advertencia semanal"
```

---

## After all tasks

Correr la suite completa (`dotnet test` sobre `Modulo.Rendiciones.Tests`),
confirmar 0 fallos, y verificar con `dotnet-ef migrations list --connection
"..."` contra `ps_comdepor_rg` y `ps_comdepor_rg_qa` que no quede ninguna
migración de este plan pendiente. Usar
superpowers:finishing-a-development-branch para decidir cómo integrar la
rama.
