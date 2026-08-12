# Página "Mapeo de Campos" (Modulo.Wms) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar de alta la primera página Razor real del plugin `Modulo.Wms` — CRUD server-rendered sobre `wms_oracle_field_mappings` en la ruta `/wms/mapeo-campos`, ya reservada en el menú.

**Architecture:** Capa de servicio (`IFieldMappingService`/`FieldMappingService`) sobre `WmsDbContext`, consumida por una Razor Page clásica (`Pages/MapeoCampos/Index.cshtml` + code-behind), siguiendo exactamente el patrón ya validado en `Modulo.Rendiciones/Pages/Configuracion/TiposGasto`. Las 6 claves fijas de `MapperKey` viven en una constante compartida nueva en `PortalSaas.Abstractions` (repo `Portal SaaS - Core`, referenciado hoy vía `ProjectReference` temporal).

**Tech Stack:** .NET 8, ASP.NET Core Razor Pages, EF Core 8 (motor dual Npgsql/SqlServer), sin proyecto de tests unitarios en este plugin — la convención de este código base es verificar con `dotnet build` (0 errores/0 advertencias) + verificación manual/HTTP contra el Host real corriendo (ver `ARQUITECTURA.md`), no hay xUnit en `Modulo.Rendiciones` ni en `Modulo.Wms` hoy. Este plan sigue esa misma convención en vez de introducir un framework de test nuevo.

## Global Constraints

- Un plugin referencia **solo** `PortalSaas.Abstractions`, nunca `PortalSaas.Core` ni `PortalSaas.Host` (regla dura, `docs/09-GUIA-DESARROLLO-PLUGINS.md` del Core).
- Toda tabla/consulta usa `company_id` real vía `ICurrentCompanyAccessor.CompanyId`, sin fallback a `Organization`.
- `MapperKey` y `FieldName` son inmutables una vez creada la fila (identidad lógica, `UNIQUE(company_id, mapper_key, field_name)`) — solo `ValueTemplate` e `IsActive` se editan en una fila existente.
- `UpdatedBy` se asigna del lado del servidor desde `ICurrentUserContext.Username`, nunca desde un campo del formulario.
- Sin delete físico — solo Activar/Desactivar (soft state).
- Estilo: reusar clases ya existentes del Host (`admin-card`, `admin-table`, `admin-alert`, `admin-form-asignar`, `admin-row-actions`, `btn-erp-primary`, `btn-module-action`, `btn-module-danger`, `form-group`, `form-control`) definidas en `PortalSaas.Host/wwwroot/css/site.css` — no se crea CSS propio de `Modulo.Wms` para esto.
- Motor dual: ningún código nuevo asume Postgres o SQL Server específicamente.

---

## File Structure

```
Portal SaaS - Core/
  src/PortalSaas.Abstractions/Modelos/WmsFieldMapperKeys.cs      -- NUEVO

Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/
  Pages/
    _ViewImports.cshtml                                          -- NUEVO
    WmsPageModelBase.cs                                           -- NUEVO
    MapeoCampos/
      Index.cshtml                                                -- NUEVO
      Index.cshtml.cs                                             -- NUEVO
  Services/
    IFieldMappingService.cs                                       -- NUEVO
    FieldMappingService.cs                                        -- NUEVO
  ModuloWms.cs                                                     -- MODIFICAR (registrar servicio)
  _ViewStart.cshtml                                                 -- NUEVO
```

---

### Task 1: Constante compartida `WmsFieldMapperKeys` en `PortalSaas.Abstractions`

**Files:**
- Create: `Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/WmsFieldMapperKeys.cs`

**Interfaces:**
- Produces: `WmsFieldMapperKeys.Labels` — `IReadOnlyDictionary<string, string>`, clave = `MapperKey` (ej. `"ORDER_CONFIRM_STOCKTRANSFER"`), valor = label legible. Consumido por `Task 6` (dropdown) y `Task 3` (validación de `CreateAsync`).

- [ ] **Step 1: Crear el archivo con las 6 claves confirmadas**

```csharp
namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Los 6 MapperKey fijos que usa WmsSapIntegration.Service (standalone, ver
/// ARQUITECTURA.md de Modulo.Wms) para resolver el UDF de cada documento SAP
/// Service Layer contra wms_oracle_field_mappings. Es un contrato manual de
/// strings entre ese servicio y este módulo -- no hay proyecto compartido
/// entre las dos soluciones. Portado tal cual desde
/// WMS_Suite/WmsPortal.Core/Models/FieldMappingModels.cs (FieldMappingKeys.Labels).
/// Si se agrega un mapeador nuevo del lado del servicio, agregar la entrada
/// correspondiente acá también.
/// </summary>
public static class WmsFieldMapperKeys
{
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["ORDER_CONFIRM_STOCKTRANSFER"] = "Confirmación Orden → Traslado (StockTransfers)",
        ["ORDER_CONFIRM_DELIVERYNOTE"] = "Confirmación Orden → Entrega (DeliveryNotes)",
        ["ORDER_CONFIRM_XDK_STOCKTRANSFER"] = "Confirmación Orden Crossdocking → Traslado (StockTransfers)",
        ["RECEIPT_CONFIRM_STOCKTRANSFER"] = "Confirmación Ingreso → Traslado (StockTransfers)",
        ["RECEIPT_CONFIRM_RETURN"] = "Confirmación Ingreso → Devolución (Returns)",
        ["RECEIPT_CONFIRM_PURCHASE_DELIVERY"] = "Confirmación Ingreso → Recepción de Compra (PurchaseDeliveryNotes)",
    };
}
```

- [ ] **Step 2: Compilar `PortalSaas.Abstractions`**

Run: `dotnet build "Portal SaaS - Core/src/PortalSaas.Abstractions/PortalSaas.Abstractions.csproj"`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit (repo `Portal SaaS - Core`)**

```bash
cd "Portal SaaS - Core"
git add src/PortalSaas.Abstractions/Modelos/WmsFieldMapperKeys.cs
git commit -m "feat: agregar WmsFieldMapperKeys compartido para Modulo.Wms"
```

---

### Task 2: `WmsPageModelBase` — infra compartida de páginas del plugin

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/WmsPageModelBase.cs`

**Interfaces:**
- Produces: clase abstracta `WmsPageModelBase : PageModel` con `[TempData] string? SuccessMessage`, `[TempData] string? ErrorMessage`, `protected static string GetErrorMessage(Exception ex)`. Consumida por `Task 6` (`MapeoCampos.IndexModel : WmsPageModelBase`).

- [ ] **Step 1: Crear el archivo, mismo patrón que `RendicionesPageModelBase`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Modulo.Wms.Pages;

/// <summary>
/// Infra compartida por las páginas de este módulo: mensajes de acción
/// post-redirect y normalización de errores. Duplicado del mismo patrón que
/// Modulo.Rendiciones (RendicionesPageModelBase) -- un plugin nunca comparte
/// código con otro plugin. [Authorize] acá: sin sesión, la resolución de
/// WmsDbContext exige ICurrentCompanyAccessor.HasCompany, que no existe sin
/// login -- sin este atributo, un request anónimo llegaría al handler y
/// crashearía con 500 en vez de redirigir a /Account/Login.
/// </summary>
[Authorize]
public abstract class WmsPageModelBase : PageModel
{
    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    protected static string GetErrorMessage(Exception ex) =>
        ex is InvalidOperationException ? ex.Message : $"No se pudo completar la operación: {ex.Message}";
}
```

- [ ] **Step 2: Compilar el proyecto**

Run: `dotnet build "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Modulo.Wms.csproj"`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit (repo `Modulo.Wms`)**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
git add src/Modulo.Wms/Pages/WmsPageModelBase.cs
git commit -m "feat: agregar WmsPageModelBase (infra compartida de páginas)"
```

---

### Task 3: `IFieldMappingService` / `FieldMappingService`

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/IFieldMappingService.cs`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/FieldMappingService.cs`

**Interfaces:**
- Consumes: `WmsDbContext.FieldMappings` (`DbSet<WmsFieldMapping>`, ya existe en `Data/WmsDbContext.cs:29`); `WmsFieldMapping` (`Id: long`, `CompanyId: Guid`, `MapperKey: string`, `FieldName: string`, `ValueTemplate: string`, `IsActive: bool`, `UpdatedAt: DateTimeOffset`, `UpdatedBy: string?`, ya existe en `Models/WmsFieldMapping.cs`).
- Produces: `IFieldMappingService` con `ListAllAsync(Guid companyId, CancellationToken ct = default)`, `CreateAsync(Guid companyId, string mapperKey, string fieldName, string valueTemplate, bool isActive, string updatedBy, CancellationToken ct = default)` → `Task<long>`, `UpdateAsync(long id, Guid companyId, string valueTemplate, bool isActive, string updatedBy, CancellationToken ct = default)` → `Task`. Consumido por `Task 6`.

- [ ] **Step 1: Crear la interfaz**

```csharp
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public interface IFieldMappingService
{
    Task<IReadOnlyList<WmsFieldMapping>> ListAllAsync(Guid companyId, CancellationToken ct = default);

    Task<long> CreateAsync(Guid companyId, string mapperKey, string fieldName, string valueTemplate, bool isActive, string updatedBy, CancellationToken ct = default);

    Task UpdateAsync(long id, Guid companyId, string valueTemplate, bool isActive, string updatedBy, CancellationToken ct = default);
}
```

- [ ] **Step 2: Crear la implementación**

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Wms.Services;

public sealed class FieldMappingService : IFieldMappingService
{
    private readonly WmsDbContext _db;

    public FieldMappingService(WmsDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<WmsFieldMapping>> ListAllAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.FieldMappings
            .Where(m => m.CompanyId == companyId)
            .OrderBy(m => m.MapperKey)
            .ThenBy(m => m.FieldName)
            .ToListAsync(ct);

    public async Task<long> CreateAsync(Guid companyId, string mapperKey, string fieldName, string valueTemplate, bool isActive, string updatedBy, CancellationToken ct = default)
    {
        if (!WmsFieldMapperKeys.Labels.ContainsKey(mapperKey))
        {
            throw new InvalidOperationException("Documento no reconocido.");
        }

        var yaExiste = await _db.FieldMappings.AnyAsync(
            m => m.CompanyId == companyId && m.MapperKey == mapperKey && m.FieldName == fieldName, ct);
        if (yaExiste)
        {
            throw new InvalidOperationException("Ya existe un mapeo para ese documento y campo UDF.");
        }

        var mapping = new WmsFieldMapping
        {
            CompanyId = companyId,
            MapperKey = mapperKey,
            FieldName = fieldName,
            ValueTemplate = valueTemplate,
            IsActive = isActive,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = updatedBy,
        };

        _db.FieldMappings.Add(mapping);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException("Ya existe un mapeo para ese documento y campo UDF.");
        }

        return mapping.Id;
    }

    public async Task UpdateAsync(long id, Guid companyId, string valueTemplate, bool isActive, string updatedBy, CancellationToken ct = default)
    {
        var mapping = await _db.FieldMappings.FirstOrDefaultAsync(m => m.Id == id && m.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("El mapeo no existe o no pertenece a esta compañía.");

        mapping.ValueTemplate = valueTemplate;
        mapping.IsActive = isActive;
        mapping.UpdatedAt = DateTimeOffset.UtcNow;
        mapping.UpdatedBy = updatedBy;

        await _db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 3: Compilar el proyecto**

Run: `dotnet build "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Modulo.Wms.csproj"`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
git add src/Modulo.Wms/Services/IFieldMappingService.cs src/Modulo.Wms/Services/FieldMappingService.cs
git commit -m "feat: agregar FieldMappingService (CRUD de wms_oracle_field_mappings)"
```

---

### Task 4: Registrar `FieldMappingService` en `ModuloWms`

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs:65-98`

**Interfaces:**
- Consumes: `IFieldMappingService`/`FieldMappingService` de `Task 3`.
- Produces: `IFieldMappingService` resoluble por DI para cualquier página del plugin.

- [ ] **Step 1: Agregar el `using` y el registro dentro de `RegisterServices`**

En `ModuloWms.cs`, agregar el using al inicio del archivo:

```csharp
using Modulo.Wms.Services;
```

Y dentro de `RegisterServices(IServiceCollection services)` (`ModuloWms.cs:65`), después del bloque `services.AddDbContext<WmsDbContext>(...)` (cierra en la línea 97), agregar:

```csharp
        services.AddScoped<IFieldMappingService, FieldMappingService>();
```

El método completo debe quedar:

```csharp
    public void RegisterServices(IServiceCollection services)
    {
        // WmsDbContext resuelto self-service vía IExternalDatabaseConnectionService --
        // mismo patrón exacto que ModuloRendiciones.RegisterServices. CompanyId
        // siempre obligatorio, sin fallback a Organization (regla dura del proyecto).
        services.AddDbContext<WmsDbContext>((sp, options) =>
        {
            var companyAccessor = sp.GetRequiredService<ICurrentCompanyAccessor>();
            var externalDb = sp.GetRequiredService<IExternalDatabaseConnectionService>();

            if (!companyAccessor.HasCompany)
            {
                throw new InvalidOperationException(
                    "Modulo.Wms requiere una compañía activa en la sesión -- seleccioná una compañía antes de continuar.");
            }

            var connection = externalDb
                .ResolveConnectionAsync(ModuleCode, companyAccessor.CompanyId)
                .GetAwaiter().GetResult();

            switch (connection.EngineType)
            {
                case ExternalDatabaseEngineType.Postgres:
                    options.UseNpgsql(connection.ConnectionString);
                    break;
                case ExternalDatabaseEngineType.SqlServer:
                    options.UseSqlServer(connection.ConnectionString);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Motor de base de datos externa no soportado: '{connection.EngineType}'.");
            }
        });

        services.AddScoped<IFieldMappingService, FieldMappingService>();
    }
```

- [ ] **Step 2: Compilar el proyecto**

Run: `dotnet build "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Modulo.Wms.csproj"`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
git add src/Modulo.Wms/ModuloWms.cs
git commit -m "feat: registrar IFieldMappingService en ModuloWms.RegisterServices"
```

---

### Task 5: Infra Razor Pages del plugin (`_ViewStart`, `_ViewImports`)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/_ViewStart.cshtml`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/_ViewImports.cshtml`

**Interfaces:**
- Produces: layout compartido del Host aplicado a toda página bajo `Pages/`, tag helpers de MVC disponibles en `.cshtml`. Prerrequisito de `Task 6`.

- [ ] **Step 1: Crear `_ViewStart.cshtml`, mismo patrón que `Modulo.Rendiciones`**

```cshtml
@{
    // Toda página de este plugin hereda el shell del Host: sidebar dinámico, topbar.
    Layout = "/Pages/Shared/_Layout.cshtml";
}
```

- [ ] **Step 2: Crear `Pages/_ViewImports.cshtml`**

```cshtml
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```

- [ ] **Step 3: Compilar el proyecto**

Run: `dotnet build "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Modulo.Wms.csproj"`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
git add src/Modulo.Wms/_ViewStart.cshtml "src/Modulo.Wms/Pages/_ViewImports.cshtml"
git commit -m "feat: agregar infra Razor Pages base del plugin (_ViewStart/_ViewImports)"
```

---

### Task 6: Página `Pages/MapeoCampos/Index`

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/MapeoCampos/Index.cshtml.cs`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/MapeoCampos/Index.cshtml`

**Interfaces:**
- Consumes: `IFieldMappingService` (`Task 3`), `WmsPageModelBase` (`Task 2`), `ICurrentCompanyAccessor.CompanyId` (`PortalSaas.Abstractions.Contratos`, ya usado en `ModuloWms.cs`), `ICurrentUserContext.Username` (`PortalSaas.Abstractions.Contratos`), `WmsFieldMapperKeys.Labels` (`Task 1`), `WmsFieldMapping` (`Models/WmsFieldMapping.cs`).
- Produces: página en ruta `/wms/mapeo-campos`, coincide con `PageRoute = "/wms/mapeo-campos"` ya declarado en `ModuloWms.cs:42`.

- [ ] **Step 1: Crear el code-behind**

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Wms.Pages.MapeoCampos;

public sealed class IndexModel : WmsPageModelBase
{
    private readonly IFieldMappingService _fieldMappings;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ICurrentUserContext _currentUser;

    public IndexModel(IFieldMappingService fieldMappings, ICurrentCompanyAccessor currentCompany, ICurrentUserContext currentUser)
    {
        _fieldMappings = fieldMappings;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
    }

    public IReadOnlyList<WmsFieldMapping> Mappings { get; private set; } = Array.Empty<WmsFieldMapping>();

    public IReadOnlyDictionary<string, string> MapperKeyLabels => WmsFieldMapperKeys.Labels;

    [BindProperty]
    public NewMappingInput New { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Mappings = await _fieldMappings.ListAllAsync(_currentCompany.CompanyId, ct);
    }

    public async Task<IActionResult> OnPostCrearAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Mappings = await _fieldMappings.ListAllAsync(_currentCompany.CompanyId, ct);
            return Page();
        }

        try
        {
            await _fieldMappings.CreateAsync(_currentCompany.CompanyId, New.MapperKey, New.FieldName, New.ValueTemplate, New.IsActive, _currentUser.Username, ct);
            SuccessMessage = "Mapeo creado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGuardarAsync(long id, string valueTemplate, bool activo, CancellationToken ct) =>
        await UpdateAsync(id, valueTemplate, activo, ct);

    public async Task<IActionResult> OnPostDesactivarAsync(long id, string valueTemplate, CancellationToken ct) =>
        await UpdateAsync(id, valueTemplate, activo: false, ct);

    public async Task<IActionResult> OnPostActivarAsync(long id, string valueTemplate, CancellationToken ct) =>
        await UpdateAsync(id, valueTemplate, activo: true, ct);

    private async Task<IActionResult> UpdateAsync(long id, string valueTemplate, bool activo, CancellationToken ct)
    {
        try
        {
            await _fieldMappings.UpdateAsync(id, _currentCompany.CompanyId, valueTemplate, activo, _currentUser.Username, ct);
            SuccessMessage = "Mapeo actualizado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public sealed class NewMappingInput
    {
        [Required]
        public string MapperKey { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string FieldName { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string ValueTemplate { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }
}
```

- [ ] **Step 2: Crear la vista**

```cshtml
@page "/wms/mapeo-campos"
@model Modulo.Wms.Pages.MapeoCampos.IndexModel
@{
    ViewData["Title"] = "Mapeo de Campos";
}

<div class="card-ps admin-card">
    <div class="admin-card-header">
        <h2>Mapeo de Campos</h2>
    </div>

@if (Model.SuccessMessage is not null)
{
    <div class="admin-alert admin-alert-success">@Model.SuccessMessage</div>
}
@if (Model.ErrorMessage is not null)
{
    <div class="admin-alert admin-alert-danger">@Model.ErrorMessage</div>
}

<table class="admin-table">
    <thead><tr><th>Documento</th><th>Campo UDF</th><th>Valor / Plantilla</th><th>Activo</th><th>Actualizado</th><th class="admin-col-accion"></th></tr></thead>
    <tbody>
        @foreach (var m in Model.Mappings)
        {
            var formId = $"mapeo-{m.Id}";
            <tr class="@(m.IsActive ? "" : "admin-fila-inactiva")">
                <td>@(Model.MapperKeyLabels.GetValueOrDefault(m.MapperKey, m.MapperKey))</td>
                <td><code>@m.FieldName</code></td>
                <td><input type="text" name="valueTemplate" value="@m.ValueTemplate" class="form-control" form="@formId" /></td>
                <td>
                    <input type="hidden" name="activo" value="@m.IsActive" form="@formId" />
                    @(m.IsActive ? "Sí" : "No")
                </td>
                <td>@m.UpdatedAt.ToString("yyyy-MM-dd HH:mm") @(m.UpdatedBy is not null ? $"({m.UpdatedBy})" : "")</td>
                <td class="admin-col-accion">
                    <div class="admin-row-actions">
                        <button type="submit" class="btn-module-action" form="@formId">Guardar</button>
                        @if (m.IsActive)
                        {
                            <form asp-page="./Index" asp-page-handler="Desactivar" asp-route-id="@m.Id" asp-route-valueTemplate="@m.ValueTemplate" method="post" style="display:inline-block;">
                                <button type="submit" class="btn-module-action btn-module-danger">Desactivar</button>
                            </form>
                        }
                        else
                        {
                            <form asp-page="./Index" asp-page-handler="Activar" asp-route-id="@m.Id" asp-route-valueTemplate="@m.ValueTemplate" method="post" style="display:inline-block;">
                                <button type="submit" class="btn-module-action">Activar</button>
                            </form>
                        }
                    </div>
                </td>
            </tr>
        }
        @if (Model.Mappings.Count == 0)
        {
            <tr><td colspan="6" class="admin-muted">Sin mapeos todavía.</td></tr>
        }
    </tbody>
</table>

@* Un <form> no puede abrirse en una <td> y cerrarse en otra (el parser lo corta en
   el primer </td>), y tampoco puede ser hijo directo de <tr> -- por eso viven acá
   afuera, vacíos, y los inputs/botones de cada fila se asocian por id vía form="...". *@
@foreach (var m in Model.Mappings)
{
    <form id="mapeo-@m.Id" asp-page="./Index" asp-page-handler="Guardar" asp-route-id="@m.Id"></form>
}

<span class="admin-subtitulo">Nuevo mapeo</span>
<form method="post" asp-page-handler="Crear" class="admin-form-asignar">
    <div asp-validation-summary="All" class="text-danger" style="grid-column:1/-1;"></div>
    <div class="form-group">
        <label asp-for="New.MapperKey">Documento</label>
        <select asp-for="New.MapperKey" class="form-control">
            @foreach (var kv in Model.MapperKeyLabels)
            {
                <option value="@kv.Key">@kv.Value</option>
            }
        </select>
    </div>
    <div class="form-group">
        <label asp-for="New.FieldName">Campo UDF</label>
        <input asp-for="New.FieldName" class="form-control" placeholder="U_..." />
    </div>
    <div class="form-group">
        <label asp-for="New.ValueTemplate">Valor / Plantilla</label>
        <input asp-for="New.ValueTemplate" class="form-control" placeholder="Ej: Y (literal) o {lpn} / {header.MessageId}" />
    </div>
    <div class="form-group form-row-check">
        <label><input asp-for="New.IsActive" type="checkbox" /> Activo (se envía a SAP)</label>
    </div>
    <button type="submit" class="btn-erp-primary">Crear</button>
</form>
<p class="admin-muted">
    Literal (ej. <code>Y</code>) o plantilla con placeholders: <code>{lpn}</code> / <code>{shipmentNbr}</code>
    / <code>{header.NombreColumna}</code> (cualquier columna de la fila de staging origen).
</p>
</div>
```

- [ ] **Step 3: Compilar el proyecto**

Run: `dotnet build "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Modulo.Wms.csproj"`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
git add "src/Modulo.Wms/Pages/MapeoCampos/Index.cshtml.cs" "src/Modulo.Wms/Pages/MapeoCampos/Index.cshtml"
git commit -m "feat: agregar página Mapeo de Campos (/wms/mapeo-campos)"
```

---

### Task 7: Verificación E2E contra el Host real

**Files:** ninguno (verificación manual, sin cambios de código).

**Interfaces:** ninguna — este task valida el resultado integrado de `Task 1`–`Task 6`.

- [ ] **Step 1: Confirmar fila de conexión externa para `module_code = "Wms"`**

Este es un pendiente previo documentado en `ARQUITECTURA.md` ("Falta explícitamente"): sin una fila en `module_external_connections` para `module_code = "Wms"` de la compañía de prueba, `WmsDbContext` lanza `InvalidOperationException` al primer acceso. Verificar que existe antes de probar la página; si no existe, es un bloqueante fuera de este plan — avisar en vez de improvisar una fila de prueba.

- [ ] **Step 2: Publicar el build actualizado y levantar el Host**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms"
dotnet build
```

Confirmar que el `Target PublicarComoPlugin` copió los binarios a `dist/Modulo.Wms/1.0.0/`. Seguir el mismo procedimiento ya usado para dejar el plugin visible al Host real (ver "Falta explícitamente" en `ARQUITECTURA.md`: hoy no hay `publish-dist.ps1`, copiar manualmente a `Portal SaaS - Core/artifacts/plugins/Modulo.Wms/1.0.0/` si todavía no está ahí). Levantar `PortalSaas.Host`.

- [ ] **Step 3: Verificación HTTP/manual del flujo completo**

Con sesión iniciada y compañía activa seleccionada, navegar a `/wms/mapeo-campos` y confirmar:
- La página responde 200 (no 500 por falta de conexión externa ni por falta de sesión).
- El menú "Integración WMS → Mapeo de Campos" navega correctamente.
- Crear un mapeo nuevo (elegir un Documento del dropdown, `Campo UDF` = `U_TEST`, `Valor / Plantilla` = `{lpn}`) — confirmar que aparece en la tabla con el mensaje de éxito.
- Editar el `Valor / Plantilla` de esa fila y Guardar — confirmar que persiste tras recargar.
- Desactivar esa fila — confirmar que la fila se marca inactiva (no desaparece) y que el botón cambia a "Activar".
- Intentar crear un segundo mapeo con el mismo Documento + mismo `Campo UDF` — confirmar el mensaje de error "Ya existe un mapeo para ese documento y campo UDF." y que no se duplica la fila.

- [ ] **Step 4: Actualizar `ARQUITECTURA.md`**

En la sección "Falta explícitamente, no iniciado todavía" de
`Portal SaaS - Plugins/Modulo.Wms/ARQUITECTURA.md`, quitar o marcar como
resuelto el ítem "Páginas Razor del port de `WmsPortal.Web`" en lo que
respecta a Mapeo de Campos (las otras dos páginas — Configuración del
Servicio, Estado del Servicio — siguen pendientes).

- [ ] **Step 5: Commit final**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
git add ARQUITECTURA.md
git commit -m "docs: marcar página Mapeo de Campos como completada en ARQUITECTURA.md"
```

---

## Self-Review

**Spec coverage:**
- Patrón server-rendered Razor Pages (no modal AJAX) → `Task 6`.
- Dropdown fijo de 6 `MapperKey` → `Task 1` + `Task 6`.
- `MapperKey`/`FieldName` bloqueados en edición → `Task 6` (code-behind: `OnPostGuardarAsync` no recibe `mapperKey`/`fieldName`; vista: esas dos columnas se renderizan como texto plano, no como input).
- Soft state (Activar/Desactivar, sin delete) → `Task 6`.
- Scoping por `ICurrentCompanyAccessor.CompanyId` → `Task 6`.
- `UpdatedBy` = `ICurrentUserContext.Username`, automático → `Task 6`.
- Estilo con clases existentes del Host → `Task 6` (vista usa `admin-*`/`btn-erp-primary`/`btn-module-action`, sin CSS nuevo).
- Validación de duplicado (`UNIQUE`) con mensaje claro → `Task 3`.

**Placeholder scan:** sin TBD/TODO; todos los steps de código traen el archivo completo, no fragmentos "similar a".

**Type consistency:** `IFieldMappingService.CreateAsync`/`UpdateAsync` (Task 3) coinciden exactamente con las llamadas desde `IndexModel` (Task 6): mismos nombres de parámetro y orden. `WmsFieldMapperKeys.Labels` (Task 1) es el mismo tipo (`IReadOnlyDictionary<string,string>`) consumido en `Task 3` (validación) y `Task 6` (dropdown + label de tabla).
