# Página "Configuración del Servicio" (Modulo.Wms) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Segunda página Razor real del plugin `Modulo.Wms` — CRUD server-rendered sobre `wms_oracle_service_configs` en la ruta `/wms/configuracion-servicio`, ya reservada en el menú.

**Architecture:** Mismo patrón exacto que "Mapeo de Campos" (ya implementado, revisado y verificado E2E): capa de servicio (`IServiceConfigService`/`ServiceConfigService`) sobre `WmsDbContext`, consumida por una Razor Page clásica. Las ~30 claves fijas de `ConfigKey` viven en una constante compartida nueva en `PortalSaas.Abstractions` (repo `Portal SaaS - Core`), junto a `WmsFieldMapperKeys` ya existente.

**Tech Stack:** .NET 8, ASP.NET Core Razor Pages, EF Core 8 (motor dual Npgsql/SqlServer). Sin proyecto de tests unitarios en este plugin — verificación por `dotnet build` (0 errores/0 advertencias) + verificación manual/HTTP contra el Host real corriendo, misma convención que "Mapeo de Campos".

## Global Constraints

- Un plugin referencia **solo** `PortalSaas.Abstractions`, nunca `PortalSaas.Core` ni `PortalSaas.Host`.
- Toda tabla/consulta usa `company_id` real vía `ICurrentCompanyAccessor.CompanyId`, sin fallback a `Organization`.
- `ConfigKey` es inmutable una vez creada la fila (identidad lógica, `UNIQUE(company_id, config_key)`) — solo `ConfigValue` e `IsActive` se editan en una fila existente.
- `UpdatedBy` se asigna del lado del servidor desde `ICurrentUserContext.Username`, nunca desde un campo del formulario.
- Sin delete físico — solo Activar/Desactivar (soft state).
- Estilo: reusar clases ya existentes del Host (`admin-card`, `admin-table`, `admin-alert`, `admin-form-asignar`, `admin-row-actions`, `btn-erp-primary`, `btn-module-action`, `btn-module-danger`, `form-group`, `form-control`) — no se crea CSS propio de `Modulo.Wms`, ni JS custom.
- Motor dual: ningún código nuevo asume Postgres o SQL Server específicamente.
- `ConfigKey` NO incluye credenciales (`SapSettings:UserName`, `WmsIntegration:User/Pass`, etc.) ni rutas de filesystem del host — esas quedan solo en env/vault del servidor donde corre `WmsSapIntegration.Service`.
- `ConfigValue` es texto libre, sin validación de tipo — el hint (`true/false`, número entero, texto/URL) es solo informativo.

---

## File Structure

```
Portal SaaS - Core/
  src/PortalSaas.Abstractions/Modelos/WmsServiceConfigKeys.cs        -- NUEVO

Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/
  Services/
    IServiceConfigService.cs                                         -- NUEVO
    ServiceConfigService.cs                                          -- NUEVO
  Pages/
    ConfiguracionServicio/
      Index.cshtml                                                   -- NUEVO
      Index.cshtml.cs                                                -- NUEVO
  ModuloWms.cs                                                        -- MODIFICAR (registrar servicio)
```

`Pages/_ViewImports.cshtml` y `_ViewStart.cshtml` ya existen (agregados en el
plan de "Mapeo de Campos") — no hace falta tocarlos.

---

### Task 1: Constante compartida `WmsServiceConfigKeys` en `PortalSaas.Abstractions`

**Files:**
- Create: `Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/WmsServiceConfigKeys.cs`

**Interfaces:**
- Produces: `WmsServiceConfigKeys.Labels` — `IReadOnlyDictionary<string, (string Label, string Hint)>`, clave = `ConfigKey` (ej. `"HostedServices:IHTH_Processor"`), valor = tupla `(Label legible, Hint de tipo esperado)`. Consumido por `Task 2` (validación de `CreateAsync`) y `Task 4` (dropdown + tooltip).

- [ ] **Step 1: Crear el archivo con las claves confirmadas**

```csharp
namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Whitelist fija de claves de configuración que WmsSapIntegration.Service (standalone,
/// ver ARQUITECTURA.md de Modulo.Wms) acepta desde wms_oracle_service_configs, solo si
/// esa instancia tiene "ConfigSource": "Database" en su propio appsettings.json. Es un
/// contrato manual de strings entre ese servicio y este módulo -- no hay proyecto
/// compartido entre las dos soluciones. Portado tal cual desde
/// WMS_Suite/WmsPortal.Core/Models/ServiceConfigModels.cs (ServiceConfigKeys.Labels).
///
/// Deliberadamente NO incluye: credenciales (SapSettings:UserName/Password,
/// WmsIntegration:User/Pass -- quedan solo en env/vault del servidor, nunca en una
/// tabla editable desde la web) ni WmsSettings:* (rutas de filesystem del host donde
/// corre el servicio, no de la empresa).
/// </summary>
public static class WmsServiceConfigKeys
{
    public static readonly IReadOnlyDictionary<string, (string Label, string Hint)> Labels = new Dictionary<string, (string, string)>
    {
        ["HostedServices:Wms_FileWatcher"] = ("Ingesta legacy por archivo (SFTP)", "true / false"),
        ["HostedServices:IHTH_Processor"] = ("Aplanado IHTH", "true / false"),
        ["HostedServices:SLSH_Processor"] = ("Aplanado SLSH", "true / false"),
        ["HostedServices:SVSH_Processor"] = ("Aplanado SVSH", "true / false"),
        ["HostedServices:WmsInbound_IHTHConfirmProcessor"] = ("Confirmación IHTH → SAP", "true / false"),
        ["HostedServices:WmsInbound_OrderConfirmProcessor"] = ("Confirmación Órdenes → SAP", "true / false"),
        ["HostedServices:WmsInbound_ReceipConfirmProcessor"] = ("Confirmación Ingresos → SAP", "true / false"),
        ["HostedServices:WmsOutbound_ItemBarcodeProcessor"] = ("Envío Códigos de Barra → WMS", "true / false"),
        ["HostedServices:WmsOutbound_ItemProcessor"] = ("Envío Productos → WMS", "true / false"),
        ["HostedServices:WmsOutbound_OrderProcessor"] = ("Envío Órdenes → WMS", "true / false"),
        ["HostedServices:WmsOutbound_ShipmentProcessor"] = ("Envío Ingresos ASN → WMS", "true / false"),
        ["HostedServices:WmsOutbound_StoreProcessor"] = ("Envío Sucursales → WMS", "true / false"),
        ["HostedServices:WmsOutbound_StageErrorProcessor"] = ("Detección de rechazos WMS (stage_*)", "true / false"),
        ["HostedServices:WmsOutbound_ExistsProcessor"] = ("Confirmación de llegada a WMS (entidad final)", "true / false"),
        ["WmsIntegration:BatchSize"] = ("Tamaño de lote por ciclo", "número entero"),
        ["WmsIntegration:InboundIntervalSeconds"] = ("Intervalo confirmaciones WMS→SAP (segundos)", "número entero"),
        ["WmsIntegration:OutboundIntervalSeconds"] = ("Intervalo envío de maestros SAP→WMS (segundos)", "número entero"),
        ["WmsIntegration:MasterDataIntervalSeconds"] = ("Intervalo datos maestros (segundos)", "número entero"),
        ["WmsIntegration:RetentionDays"] = ("Días de retención en tablas de staging", "número entero"),
        ["WmsIntegration:MaxParallelism"] = ("Hilos en paralelo (Parallel.ForEachAsync)", "número entero"),
        ["WmsIntegration:SaveLocalXml"] = ("Guardar XML/JSON local para diagnóstico", "true / false"),
        ["WmsIntegration:ApiUrl"] = ("URL API Oracle WMS", "texto (URL)"),
        ["WmsIntegration:EnvCode"] = ("Código de ambiente WMS", "texto"),
        ["WmsIntegration:ParentCompany"] = ("Empresa padre WMS", "texto"),
        ["WmsIntegration:LgfApiBaseUrl"] = ("URL base LGFAPI de WMS", "texto (URL)"),
        ["WmsIntegration:StageErrorIntervalSeconds"] = ("Intervalo detección de rechazos WMS (segundos)", "número entero"),
        ["WmsIntegration:ExistsIntervalSeconds"] = ("Intervalo confirmación de llegada a WMS (segundos)", "número entero"),
        ["WmsIntegration:ValidationBatchSize"] = ("Tamaño de lote de validación WMS", "número entero"),
        ["WmsIntegration:ExistsDiasHaciaAtras"] = ("Ventana de días para confirmar Órdenes/ASN en WMS (no aplica a Productos)", "número entero"),
        ["SapSettings:ServiceLayerUrl"] = ("URL SAP Service Layer", "texto (URL)"),
        ["SapSettings:CompanyDB"] = ("Base de datos SAP (CompanyDB)", "texto"),
        ["Logging:ReplicateToDatabase"] = ("Replicar log a base de datos (INT_SERVICE_LOG)", "true / false"),
        ["Logging:RetentionDays"] = ("Días de retención en INT_SERVICE_LOG", "número entero"),
    };
}
```

- [ ] **Step 2: Compilar `PortalSaas.Abstractions`**

Run: `dotnet build "Portal SaaS - Core/src/PortalSaas.Abstractions/PortalSaas.Abstractions.csproj"`
Expected: `Compilación correcta. 0 Advertencia(s) 0 Errores`

- [ ] **Step 3: Commit (repo `Portal SaaS - Core`)**

```bash
cd "Portal SaaS - Core"
git add src/PortalSaas.Abstractions/Modelos/WmsServiceConfigKeys.cs
git commit -m "feat: agregar WmsServiceConfigKeys compartido para Modulo.Wms"
```

---

### Task 2: `IServiceConfigService` / `ServiceConfigService`

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/IServiceConfigService.cs`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/ServiceConfigService.cs`

**Interfaces:**
- Consumes: `WmsDbContext.ServiceConfigs` (`DbSet<WmsServiceConfig>`, ya existe en `Data/WmsDbContext.cs`); `WmsServiceConfig` (`Id: long`, `CompanyId: Guid`, `ConfigKey: string`, `ConfigValue: string?`, `IsActive: bool`, `UpdatedAt: DateTimeOffset`, `UpdatedBy: string?`, ya existe en `Models/WmsServiceConfig.cs`); `WmsServiceConfigKeys.Labels` (`Task 1`).
- Produces: `IServiceConfigService` con `ListAllAsync(Guid companyId, CancellationToken ct = default)`, `CreateAsync(Guid companyId, string configKey, string? configValue, bool isActive, string updatedBy, CancellationToken ct = default)` → `Task<long>`, `UpdateAsync(long id, Guid companyId, string? configValue, bool isActive, string updatedBy, CancellationToken ct = default)` → `Task`. Consumido por `Task 4`.

- [ ] **Step 1: Crear la interfaz**

```csharp
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public interface IServiceConfigService
{
    Task<IReadOnlyList<WmsServiceConfig>> ListAllAsync(Guid companyId, CancellationToken ct = default);

    Task<long> CreateAsync(Guid companyId, string configKey, string? configValue, bool isActive, string updatedBy, CancellationToken ct = default);

    Task UpdateAsync(long id, Guid companyId, string? configValue, bool isActive, string updatedBy, CancellationToken ct = default);
}
```

- [ ] **Step 2: Crear la implementación**

```csharp
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Npgsql;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Wms.Services;

public sealed class ServiceConfigService : IServiceConfigService
{
    private readonly WmsDbContext _db;

    public ServiceConfigService(WmsDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<WmsServiceConfig>> ListAllAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.ServiceConfigs
            .Where(c => c.CompanyId == companyId)
            .OrderBy(c => c.ConfigKey)
            .ToListAsync(ct);

    public async Task<long> CreateAsync(Guid companyId, string configKey, string? configValue, bool isActive, string updatedBy, CancellationToken ct = default)
    {
        if (!WmsServiceConfigKeys.Labels.ContainsKey(configKey))
        {
            throw new InvalidOperationException("Parámetro no reconocido.");
        }

        var yaExiste = await _db.ServiceConfigs.AnyAsync(
            c => c.CompanyId == companyId && c.ConfigKey == configKey, ct);
        if (yaExiste)
        {
            throw new InvalidOperationException("Ya existe una configuración para ese parámetro.");
        }

        var config = new WmsServiceConfig
        {
            CompanyId = companyId,
            ConfigKey = configKey,
            ConfigValue = configValue,
            IsActive = isActive,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = updatedBy,
        };

        _db.ServiceConfigs.Add(config);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Ya existe una configuración para ese parámetro.");
        }

        return config.Id;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
    {
        PostgresException pg => pg.SqlState == "23505",
        SqlException sql => sql.Number is 2601 or 2627,
        _ => false,
    };

    public async Task UpdateAsync(long id, Guid companyId, string? configValue, bool isActive, string updatedBy, CancellationToken ct = default)
    {
        var config = await _db.ServiceConfigs.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("La configuración no existe o no pertenece a esta compañía.");

        if (configValue is not null && configValue.Length > 500)
        {
            throw new InvalidOperationException("El valor no puede superar los 500 caracteres.");
        }

        config.ConfigValue = configValue;
        config.IsActive = isActive;
        config.UpdatedAt = DateTimeOffset.UtcNow;
        config.UpdatedBy = updatedBy;

        await _db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 3: Compilar el proyecto**

Run: `dotnet build "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Modulo.Wms.csproj"`
Expected: `Compilación correcta. 0 Advertencia(s) 0 Errores`

- [ ] **Step 4: Commit**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
git add src/Modulo.Wms/Services/IServiceConfigService.cs src/Modulo.Wms/Services/ServiceConfigService.cs
git commit -m "feat: agregar ServiceConfigService (CRUD de wms_oracle_service_configs)"
```

---

### Task 3: Registrar `ServiceConfigService` en `ModuloWms`

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/ModuloWms.cs`

**Interfaces:**
- Consumes: `IServiceConfigService`/`ServiceConfigService` de `Task 2`.
- Produces: `IServiceConfigService` resoluble por DI para cualquier página del plugin.

- [ ] **Step 1: Agregar la línea de registro dentro de `RegisterServices`**

En `ModuloWms.cs`, dentro de `RegisterServices(IServiceCollection services)`, después de la línea `services.AddScoped<IFieldMappingService, FieldMappingService>();`, agregar:

```csharp
        services.AddScoped<IServiceConfigService, ServiceConfigService>();
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
        services.AddScoped<IServiceConfigService, ServiceConfigService>();
    }
```

(El `using Modulo.Wms.Services;` ya existe en el archivo desde la entrega anterior — no hace falta agregarlo de nuevo.)

- [ ] **Step 2: Compilar el proyecto**

Run: `dotnet build "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Modulo.Wms.csproj"`
Expected: `Compilación correcta. 0 Advertencia(s) 0 Errores`

- [ ] **Step 3: Commit**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
git add src/Modulo.Wms/ModuloWms.cs
git commit -m "feat: registrar IServiceConfigService en ModuloWms.RegisterServices"
```

---

### Task 4: Página `Pages/ConfiguracionServicio/Index`

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/ConfiguracionServicio/Index.cshtml.cs`
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Pages/ConfiguracionServicio/Index.cshtml`

**Interfaces:**
- Consumes: `IServiceConfigService` (`Task 2`), `WmsPageModelBase` (ya existe, `Pages/WmsPageModelBase.cs`), `ICurrentCompanyAccessor.CompanyId`, `ICurrentUserContext.Username` (`PortalSaas.Abstractions.Contratos`), `WmsServiceConfigKeys.Labels` (`Task 1`), `WmsServiceConfig` (`Models/WmsServiceConfig.cs`).
- Produces: página en ruta `/wms/configuracion-servicio`, coincide con `PageRoute = "/wms/configuracion-servicio"` ya declarado en `ModuloWms.cs`.

- [ ] **Step 1: Crear el code-behind**

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Wms.Pages.ConfiguracionServicio;

public sealed class IndexModel : WmsPageModelBase
{
    private readonly IServiceConfigService _serviceConfigs;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ICurrentUserContext _currentUser;

    public IndexModel(IServiceConfigService serviceConfigs, ICurrentCompanyAccessor currentCompany, ICurrentUserContext currentUser)
    {
        _serviceConfigs = serviceConfigs;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
    }

    public IReadOnlyList<WmsServiceConfig> Configs { get; private set; } = Array.Empty<WmsServiceConfig>();

    public IReadOnlyDictionary<string, (string Label, string Hint)> ConfigKeyInfo => WmsServiceConfigKeys.Labels;

    [BindProperty]
    public NewConfigInput New { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Configs = await _serviceConfigs.ListAllAsync(_currentCompany.CompanyId, ct);
    }

    public async Task<IActionResult> OnPostCrearAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Configs = await _serviceConfigs.ListAllAsync(_currentCompany.CompanyId, ct);
            return Page();
        }

        try
        {
            await _serviceConfigs.CreateAsync(_currentCompany.CompanyId, New.ConfigKey, New.ConfigValue, New.IsActive, _currentUser.Username, ct);
            SuccessMessage = "Parámetro creado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGuardarAsync(long id, string? configValue, bool activo, CancellationToken ct) =>
        await UpdateAsync(id, configValue, activo, ct);

    public async Task<IActionResult> OnPostDesactivarAsync(long id, string? configValue, CancellationToken ct) =>
        await UpdateAsync(id, configValue, activo: false, ct);

    public async Task<IActionResult> OnPostActivarAsync(long id, string? configValue, CancellationToken ct) =>
        await UpdateAsync(id, configValue, activo: true, ct);

    private async Task<IActionResult> UpdateAsync(long id, string? configValue, bool activo, CancellationToken ct)
    {
        try
        {
            await _serviceConfigs.UpdateAsync(id, _currentCompany.CompanyId, configValue, activo, _currentUser.Username, ct);
            SuccessMessage = "Parámetro actualizado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public sealed class NewConfigInput
    {
        [Required]
        public string ConfigKey { get; set; } = string.Empty;

        [StringLength(500)]
        public string? ConfigValue { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
```

- [ ] **Step 2: Crear la vista**

```cshtml
@page "/wms/configuracion-servicio"
@model Modulo.Wms.Pages.ConfiguracionServicio.IndexModel
@{
    ViewData["Title"] = "Configuración del Servicio";
}

<div class="card-ps admin-card">
    <div class="admin-card-header">
        <h2>Configuración del Servicio</h2>
    </div>

@if (Model.SuccessMessage is not null)
{
    <div class="admin-alert admin-alert-success">@Model.SuccessMessage</div>
}
@if (Model.ErrorMessage is not null)
{
    <div class="admin-alert admin-alert-danger">@Model.ErrorMessage</div>
}

<div class="admin-alert">
    Estos valores solo tienen efecto si la instancia de <code>WmsSapIntegration.Service</code>
    de esta compañía tiene <code>"ConfigSource": "Database"</code> en su propio
    <code>appsettings.json</code> (por defecto es <code>"File"</code> -- configuración
    autónoma, sin depender de este Portal). El Portal no tiene forma de saber qué modo
    tiene la instancia real corriendo en el servidor, así que si editás algo acá y no
    ves efecto, confirmá primero ese valor con quien administra el servidor.
</div>

<table class="admin-table">
    <thead><tr><th>Parámetro</th><th>Valor</th><th>Activo</th><th>Actualizado</th><th class="admin-col-accion"></th></tr></thead>
    <tbody>
        @foreach (var c in Model.Configs)
        {
            var formId = $"config-{c.Id}";
            var label = Model.ConfigKeyInfo.TryGetValue(c.ConfigKey, out var info) ? info.Label : c.ConfigKey;
            <tr class="@(c.IsActive ? "" : "admin-fila-inactiva")">
                <td>@label<br /><small class="admin-muted"><code>@c.ConfigKey</code></small></td>
                <td><input type="text" name="configValue" value="@c.ConfigValue" class="form-control" form="@formId" /></td>
                <td>
                    <input type="hidden" name="activo" value="@(c.IsActive ? "true" : "false")" form="@formId" />
                    @(c.IsActive ? "Sí" : "No")
                </td>
                <td>@c.UpdatedAt.ToString("yyyy-MM-dd HH:mm") @(c.UpdatedBy is not null ? $"({c.UpdatedBy})" : "")</td>
                <td class="admin-col-accion">
                    <div class="admin-row-actions">
                        <button type="submit" class="btn-module-action" form="@formId">Guardar</button>
                        @if (c.IsActive)
                        {
                            <form asp-page="./Index" asp-page-handler="Desactivar" asp-route-id="@c.Id" asp-route-configValue="@c.ConfigValue" method="post" style="display:inline-block;">
                                <button type="submit" class="btn-module-action btn-module-danger">Desactivar</button>
                            </form>
                        }
                        else
                        {
                            <form asp-page="./Index" asp-page-handler="Activar" asp-route-id="@c.Id" asp-route-configValue="@c.ConfigValue" method="post" style="display:inline-block;">
                                <button type="submit" class="btn-module-action">Activar</button>
                            </form>
                        }
                    </div>
                </td>
            </tr>
        }
        @if (Model.Configs.Count == 0)
        {
            <tr><td colspan="5" class="admin-muted">Sin parámetros todavía.</td></tr>
        }
    </tbody>
</table>

@* Un <form> no puede abrirse en una <td> y cerrarse en otra (el parser lo corta en
   el primer </td>), y tampoco puede ser hijo directo de <tr> -- por eso viven acá
   afuera, vacíos, y los inputs/botones de cada fila se asocian por id vía form="...". *@
@foreach (var c in Model.Configs)
{
    <form id="config-@c.Id" asp-page="./Index" asp-page-handler="Guardar" asp-route-id="@c.Id"></form>
}

<span class="admin-subtitulo">Nuevo parámetro</span>
<form method="post" asp-page-handler="Crear" class="admin-form-asignar">
    <div asp-validation-summary="All" class="text-danger" style="grid-column:1/-1;"></div>
    <div class="form-group">
        <label asp-for="New.ConfigKey">Parámetro</label>
        <select asp-for="New.ConfigKey" class="form-control">
            @foreach (var kv in Model.ConfigKeyInfo)
            {
                <option value="@kv.Key" title="@kv.Value.Hint">@kv.Value.Label — @kv.Key</option>
            }
        </select>
    </div>
    <div class="form-group">
        <label asp-for="New.ConfigValue">Valor</label>
        <input asp-for="New.ConfigValue" class="form-control" placeholder="Ver el tipo esperado al pasar el mouse sobre la opción elegida" />
    </div>
    <div class="form-group form-row-check">
        <label><input asp-for="New.IsActive" type="checkbox" /> Activo (sobreescribe el valor de appsettings)</label>
    </div>
    <button type="submit" class="btn-erp-primary">Crear</button>
</form>
<p class="admin-muted">
    El valor es texto libre -- pasá el mouse sobre cada opción de "Parámetro" para ver
    el tipo esperado (<code>true / false</code>, número entero, texto). La
    interpretación real queda del lado de <code>WmsSapIntegration.Service</code> al
    leerlo.
</p>
</div>
```

- [ ] **Step 3: Compilar el proyecto**

Run: `dotnet build "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Modulo.Wms.csproj"`
Expected: `Compilación correcta. 0 Advertencia(s) 0 Errores`

- [ ] **Step 4: Commit**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
git add "src/Modulo.Wms/Pages/ConfiguracionServicio/Index.cshtml.cs" "src/Modulo.Wms/Pages/ConfiguracionServicio/Index.cshtml"
git commit -m "feat: agregar página Configuración del Servicio (/wms/configuracion-servicio)"
```

---

### Task 5: Verificación E2E contra el Host real

**Files:** ninguno (verificación manual, sin cambios de código).

**Interfaces:** ninguna — este task valida el resultado integrado de `Task 1`–`Task 4`.

- [ ] **Step 1: Publicar el build actualizado**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
./publish-dist.ps1
```

Esto detiene `PortalSaas.Host` si está corriendo, compila en Release, limpia
`dist/Modulo.Wms/1.0.0/` y lo deja visible en `artifacts/plugins/Modulo.Wms/`
del Host vía la junction ya existente (agregada en la entrega de "Mapeo de
Campos") — sin paso manual de copia.

- [ ] **Step 2: Levantar `PortalSaas.Host` y confirmar que carga el módulo**

El propio dueño del proyecto levanta el Host (Visual Studio o
`dotnet run`/`build-all.ps1`, según el flujo ya usado en la entrega anterior).
Confirmar en el log: `Módulo Wms v1.0.0 cargado (4 entradas de menú)`.

- [ ] **Step 3: Verificación HTTP/manual del flujo completo**

Con sesión iniciada (usuario con `IsAdmin=true` o con acceso real a la
compañía activa, y compañía con conexión externa `module_code="Wms"` ya
configurada — ver Task 7 de la entrega anterior para cómo crearla),
navegar a `/wms/configuracion-servicio` y confirmar:
- La página responde 200.
- El menú "Integración WMS → Configuración del Servicio" navega
  correctamente.
- Crear un parámetro nuevo (elegir uno del dropdown, ej.
  `WmsIntegration:BatchSize`, Valor = `500`) — confirma con "Parámetro
  creado." y aparece en la tabla.
- Editar el Valor de esa fila y Guardar — confirmar que persiste tras
  recargar Y que la fila permanece activa (no reproducir el bug de
  `bool`-como-atributo-HTML ya corregido en Mapeo de Campos; este código
  parte ya con `value="@(c.IsActive ? "true" : "false")"` correcto desde
  el Step 2 de Task 4, así que no debería reaparecer, pero confirmarlo en
  vivo de todas formas).
- Desactivar esa fila — confirmar que queda marcada inactiva (no
  desaparece) y que el botón cambia a "Activar".
- Intentar crear un segundo parámetro con el mismo `ConfigKey` — confirmar
  el mensaje "Ya existe una configuración para ese parámetro." y que no se
  duplica la fila.

- [ ] **Step 4: Actualizar `ARQUITECTURA.md`**

En la sección "Falta explícitamente, no iniciado todavía" de
`Portal SaaS - Plugins/Modulo.Wms/ARQUITECTURA.md`, mover "Configuración del
Servicio" de pendiente a completada (mismo formato que se usó para "Mapeo de
Campos"), dejando solo "Estado del Servicio" como página Razor pendiente.

- [ ] **Step 5: Commit final**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
git add ARQUITECTURA.md
git commit -m "docs: marcar página Configuración del Servicio como completada en ARQUITECTURA.md"
```

---

## Self-Review

**Spec coverage:**
- Página separada de "Estado del Servicio" → `Task 4` (solo CRUD, sin tabla de heartbeat).
- `ConfigKey` por dropdown fijo (~30 claves) → `Task 1` + `Task 4`.
- `ConfigKey` bloqueado en edición → `Task 4` (`OnPostGuardarAsync` no recibe `configKey`; vista renderiza el Parámetro como texto plano en filas existentes).
- Sin validación de tipo sobre `ConfigValue` (texto libre) → `Task 2`/`Task 4` (solo `[StringLength(500)]`, sin parseo de tipo).
- Soft state (Activar/Desactivar) → `Task 4`.
- Scoping por `ICurrentCompanyAccessor.CompanyId` → `Task 2`/`Task 4`.
- `UpdatedBy` automático → `Task 4`.
- Estilo con clases existentes, sin JS → `Task 4` (hint mostrado vía atributo `title` nativo del navegador, no JS).
- Caja informativa sobre `ConfigSource: Database` → `Task 4`.
- Validación de duplicado con mensaje claro y catch acotado a violación de unicidad (mismo fix ya aplicado en Mapeo de Campos) → `Task 2`.

**Placeholder scan:** sin TBD/TODO; todos los steps de código traen el archivo completo.

**Type consistency:** `IServiceConfigService.CreateAsync`/`UpdateAsync` (Task 2) coinciden exactamente con las llamadas desde `IndexModel` (Task 4): mismos nombres de parámetro y orden. `WmsServiceConfigKeys.Labels` (Task 1) es el mismo tipo (`IReadOnlyDictionary<string, (string Label, string Hint)>`) consumido en `Task 2` (validación) y `Task 4` (dropdown + tooltip). El fix de `bool`-como-atributo-HTML (encontrado en la entrega anterior) ya se aplica desde el Step 2 de `Task 4` (`value="@(c.IsActive ? "true" : "false")"`), no reintroducido.
