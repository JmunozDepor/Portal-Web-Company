# Motor de Integración ERP — Fase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Trasladar `SapDocumentConnector` a `PortalSaas.Core` con los document services inyectados (estructura lista, sin mapeo DTO real), arreglar el tooling EF de `PortalSaas.Host`, y construir la UI admin de solo lectura/ejecución para las integraciones.

**Architecture:** Tres piezas independientes que no se pisan entre sí: (1) mover un archivo de `PortalSaas.Integrations` a `PortalSaas.Core` sin tocar el resto del motor; (2) agregar paquetes/referencias a un `.csproj` sin tocar código; (3) dos páginas Razor nuevas en `PortalSaas.Host/Pages/Admin/Integraciones/` siguiendo el patrón ya establecido de `Admin/Sessions/Index` (listado + botón de acción con handler nombrado) y `Admin/Organizations/Index` (listado simple).

**Tech Stack:** .NET 8, Razor Pages, EF Core 8.0.8, xUnit + EF Core InMemory.

## Global Constraints

- `IIntegrationConnector` vive en `PortalSaas.Abstractions.Contratos.Integraciones` — cualquier proyecto puede implementarlo, no hay restricción de ubicación para las clases que lo implementan.
- `PortalSaas.Core.csproj` ya referencia `PortalSaas.Abstractions` y `PortalSaas.Data` — no requiere cambios de referencias para alojar el conector.
- `PortalSaas.Integrations.csproj` NO debe referenciar `PortalSaas.Core` — tras esta tarea deja de contener el conector SAP, y sigue sin esa referencia.
- Los tres document services (`SalesDocumentService`, `PurchaseDocumentService`, `InventoryDocumentService`) tienen firma `Task<int> CreateAsync(TDocumentType type, string portalUsername, TDto document, CancellationToken ct = default)` y se registran vía sus interfaces (`ISalesDocumentService`, `IPurchaseDocumentService`, `IInventoryDocumentService`).
- Versión de EF Core consistente en toda la solución: `8.0.8` — usar esa misma para cualquier paquete `Microsoft.EntityFrameworkCore.*` nuevo.
- Páginas admin usan `[Authorize(AuthenticationSchemes = "PlatformAdmin")]`, inyectan `PortalSaasDbContext` por constructor, y usan el layout/estilos ya existentes (`document-list-table`, `document-list-table-wrapper`, `card-ps`) — no crear estilos nuevos.
- Acciones de escritura en páginas de listado usan un `<form method="post" asp-page-handler="...">` por fila con `asp-antiforgery="true"`, redirigiendo con `RedirectToPage` tras la operación (patrón de `Admin/Sessions/Index.cshtml`), no un botón con JS suelto.
- Nombres de propiedades reales (post-fix de la ronda anterior, no renombrar): `IntegrationDefinition.{Id, CompanyId, Nombre, ModuloOrigen, EntidadNegocio, ConectorTipo, ConectorConfigCifrado, Direccion, Activo, ProgramacionCron, NextRunAt}`; `IntegrationRunLog.{Id, IntegrationDefinitionId, IniciadoEn, FinalizadoEn, Resultado, RegistrosProcesados, RegistrosConError, DetalleError, DisparadoPor}`; enums `IntegrationConectorTipo{Sap,Rest,Archivo}`, `IntegrationDireccion{Subida,Bajada,Ambas}`, `IntegrationRunResultado{Exito,Error,Parcial}`, `IntegrationRunDisparadoPor{Programado,Manual}`.
- No construir formulario de creación/edición de `IntegrationDefinition` en este plan (decisión ya tomada en el spec) — las integraciones de prueba se siembran por migración de datos.

---

## File Structure

**Nuevos archivos:**
- `src/PortalSaas.Core/Integraciones/SapDocumentConnector.cs` — el conector, movido y reestructurado.
- `tests/PortalSaas.Core.Tests/Integraciones/SapDocumentConnectorTests.cs` — movido junto con el conector (mismo namespace de test, distinto contenido).
- `src/PortalSaas.Host/Pages/Admin/Integraciones/Index.cshtml` + `.cshtml.cs`
- `src/PortalSaas.Host/Pages/Admin/Integraciones/Bitacora.cshtml` + `.cshtml.cs`

**Eliminados:**
- `src/PortalSaas.Integrations/Connectors/SapDocumentConnector.cs` (reemplazado por el de Core)
- La carpeta `src/PortalSaas.Integrations/Connectors/` queda vacía y se elimina.

**Modificados:**
- `src/PortalSaas.Host/Program.cs` — cambia el `using`/registro de `SapDocumentConnector` a su nueva ubicación.
- `src/PortalSaas.Host/PortalSaas.Host.csproj` — agrega `Microsoft.EntityFrameworkCore.Design` y `ProjectReference` a ambos proyectos de migraciones.

---

### Task 1: Mover `SapDocumentConnector` a `PortalSaas.Core` con document services inyectados

**Files:**
- Create: `src/PortalSaas.Core/Integraciones/SapDocumentConnector.cs`
- Create: `tests/PortalSaas.Core.Tests/Integraciones/SapDocumentConnectorTests.cs` (reemplaza el archivo existente del mismo nombre — hay que borrar el viejo)
- Delete: `src/PortalSaas.Integrations/Connectors/SapDocumentConnector.cs`
- Delete: `tests/PortalSaas.Core.Tests/Integraciones/SapDocumentConnectorTests.cs` (el actual, antes de crear el nuevo — mismo path, contenido distinto)
- Modify: `src/PortalSaas.Host/Program.cs`

**Interfaces:**
- Consumes: `IIntegrationConnector`, `IntegrationRecord` (`PortalSaas.Abstractions.Contratos.Integraciones`, ya existentes); `ISalesDocumentService`, `IPurchaseDocumentService`, `IInventoryDocumentService` (`PortalSaas.Abstractions.Contratos`, ya existentes, ya registrados en DI por `PortalSaas.Core`/`Program.cs` — verificar en el Paso 1 que ya están registrados, no registrarlos de nuevo).
- Produces: `SapDocumentConnector` en namespace `PortalSaas.Core.Integraciones`, con constructor `SapDocumentConnector(ISalesDocumentService, IPurchaseDocumentService, IInventoryDocumentService)` — este es el símbolo que `Program.cs` debe registrar tras este task.

- [ ] **Step 1: Verificar que los tres document services ya están registrados en DI**

Run: `grep -n "ISalesDocumentService\|IPurchaseDocumentService\|IInventoryDocumentService" "src/PortalSaas.Host/Program.cs"`
Expected: las tres interfaces aparecen registradas con `AddScoped` (o similar) ya en `Program.cs`. Si no aparecen, deténgase y reporte `NEEDS_CONTEXT` — este plan asume que ya están registradas porque los módulos de Ventas/Compras/Inventario del Core ya las usan en producción.

- [ ] **Step 2: Borrar el conector y su test viejos**

```bash
git rm src/PortalSaas.Integrations/Connectors/SapDocumentConnector.cs
git rm tests/PortalSaas.Core.Tests/Integraciones/SapDocumentConnectorTests.cs
rmdir src/PortalSaas.Integrations/Connectors 2>/dev/null || true
```

- [ ] **Step 3: Escribir el test que falla**

```csharp
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Integraciones;
using Xunit;

namespace PortalSaas.Core.Tests.Integraciones;

public class SapDocumentConnectorTests
{
    private sealed class SalesDocumentServiceFalso : ISalesDocumentService
    {
        public Task<bool> CanCreateAsync(SalesDocumentType type, CancellationToken ct = default) => Task.FromResult(true);
        public Task<int> CreateAsync(SalesDocumentType type, string portalUsername, SalesDocumentDto document, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
    }

    private sealed class PurchaseDocumentServiceFalso : IPurchaseDocumentService
    {
        public Task<bool> CanCreateAsync(PurchaseDocumentType type, CancellationToken ct = default) => Task.FromResult(true);
        public Task<int> CreateAsync(PurchaseDocumentType type, string portalUsername, PurchaseDocumentDto document, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
    }

    private sealed class InventoryDocumentServiceFalso : IInventoryDocumentService
    {
        public Task<bool> CanCreateAsync(InventoryDocumentType type, CancellationToken ct = default) => Task.FromResult(true);
        public Task<int> CreateAsync(InventoryDocumentType type, string portalUsername, InventoryDocumentDto document, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
    }

    private static SapDocumentConnector CrearConector()
        => new(new SalesDocumentServiceFalso(), new PurchaseDocumentServiceFalso(), new InventoryDocumentServiceFalso());

    [Fact]
    public void Tipo_EsSap()
    {
        var conector = CrearConector();

        Assert.Equal("Sap", conector.Tipo);
    }

    [Fact]
    public async Task PullAsync_LanzaNotSupportedException()
    {
        var conector = CrearConector();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => conector.PullAsync("{}", CancellationToken.None));
    }

    [Fact]
    public async Task PushAsync_SinTipoDocumento_LanzaNotSupportedException()
    {
        var conector = CrearConector();
        var registros = new List<IntegrationRecord> { new(new Dictionary<string, object?>()) };

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => conector.PushAsync("{}", registros, CancellationToken.None));
        Assert.Contains("TipoDocumento", ex.Message);
    }

    [Theory]
    [InlineData("Sales")]
    [InlineData("Purchase")]
    [InlineData("Inventory")]
    public async Task PushAsync_ConTipoDocumentoConocido_LanzaNotSupportedExceptionDeMapeoPendiente(string tipoDocumento)
    {
        var conector = CrearConector();
        var registros = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["TipoDocumento"] = tipoDocumento }),
        };

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => conector.PushAsync("{}", registros, CancellationToken.None));
        Assert.Contains("mapeo DTO pendiente", ex.Message);
    }

    [Fact]
    public async Task PushAsync_ConTipoDocumentoDesconocido_LanzaNotSupportedExceptionExplicito()
    {
        var conector = CrearConector();
        var registros = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["TipoDocumento"] = "Nomina" }),
        };

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => conector.PushAsync("{}", registros, CancellationToken.None));
        Assert.Contains("Nomina", ex.Message);
    }
}
```

Nota: si `ISalesDocumentService`/`IPurchaseDocumentService`/`IInventoryDocumentService`, `SalesDocumentType`/`PurchaseDocumentType`/`InventoryDocumentType`, o `SalesDocumentDto`/`PurchaseDocumentDto`/`InventoryDocumentDto` no viven exactamente en `PortalSaas.Abstractions.Contratos`/`PortalSaas.Abstractions.Modelos`, ajuste los `using` al namespace real (verificable con `grep -rn "interface ISalesDocumentService" src/PortalSaas.Abstractions/`) — el resto del test no cambia.

- [ ] **Step 4: Ejecutar el test y verificar que falla**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter SapDocumentConnectorTests`
Expected: FAIL — `PortalSaas.Core.Integraciones.SapDocumentConnector` no existe todavía.

- [ ] **Step 5: Implementar `SapDocumentConnector` en `PortalSaas.Core`**

```csharp
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace PortalSaas.Core.Integraciones;

public class SapDocumentConnector : IIntegrationConnector
{
    private readonly ISalesDocumentService _salesDocumentService;
    private readonly IPurchaseDocumentService _purchaseDocumentService;
    private readonly IInventoryDocumentService _inventoryDocumentService;

    public SapDocumentConnector(
        ISalesDocumentService salesDocumentService,
        IPurchaseDocumentService purchaseDocumentService,
        IInventoryDocumentService inventoryDocumentService)
    {
        _salesDocumentService = salesDocumentService;
        _purchaseDocumentService = purchaseDocumentService;
        _inventoryDocumentService = inventoryDocumentService;
    }

    public string Tipo => "Sap";

    public Task<IReadOnlyList<IntegrationRecord>> PullAsync(
        string conectorConfigJson,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException(
            "SapDocumentConnector.PullAsync no está implementado: la bajada (descarga desde SAP) " +
            "está explícitamente fuera de alcance del motor de integración por ahora.");
    }

    public async Task PushAsync(
        string conectorConfigJson,
        IReadOnlyList<IntegrationRecord> registros,
        CancellationToken cancellationToken)
    {
        foreach (var registro in registros)
        {
            var tipoDocumento = registro["TipoDocumento"] as string;

            if (tipoDocumento is null)
            {
                throw new NotSupportedException(
                    "SapDocumentConnector.PushAsync requiere que el IntegrationRecord traiga un campo " +
                    "'TipoDocumento' ('Sales', 'Purchase' o 'Inventory') para saber a qué document service " +
                    "de SAP enviarlo. Este campo debe venir del IntegrationFieldMapping de la integración.");
            }

            switch (tipoDocumento)
            {
                case "Sales":
                    throw new NotSupportedException(
                        "SapDocumentConnector.PushAsync para 'Sales': mapeo DTO pendiente de caso real de " +
                        "negocio. ISalesDocumentService.CreateAsync está inyectado y listo, pero construir " +
                        "un SalesDocumentDto a partir de un IntegrationRecord genérico requiere un caso de " +
                        "uso concreto que todavía no existe (ver spec 2026-08-15, punto 'ajuste de alcance').");
                case "Purchase":
                    throw new NotSupportedException(
                        "SapDocumentConnector.PushAsync para 'Purchase': mapeo DTO pendiente de caso real de " +
                        "negocio. IPurchaseDocumentService.CreateAsync está inyectado y listo, pero construir " +
                        "un PurchaseDocumentDto a partir de un IntegrationRecord genérico requiere un caso de " +
                        "uso concreto que todavía no existe (ver spec 2026-08-15, punto 'ajuste de alcance').");
                case "Inventory":
                    throw new NotSupportedException(
                        "SapDocumentConnector.PushAsync para 'Inventory': mapeo DTO pendiente de caso real de " +
                        "negocio. IInventoryDocumentService.CreateAsync está inyectado y listo, pero construir " +
                        "un InventoryDocumentDto a partir de un IntegrationRecord genérico requiere un caso de " +
                        "uso concreto que todavía no existe (ver spec 2026-08-15, punto 'ajuste de alcance').");
                default:
                    throw new NotSupportedException(
                        $"SapDocumentConnector.PushAsync: TipoDocumento '{tipoDocumento}' no reconocido. " +
                        "Valores soportados: 'Sales', 'Purchase', 'Inventory'.");
            }
        }

        await Task.CompletedTask;
    }
}
```

- [ ] **Step 6: Ejecutar el test y verificar que pasa**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter SapDocumentConnectorTests`
Expected: PASS, 6/6.

- [ ] **Step 7: Actualizar el registro DI en `Program.cs`**

Ubicar la línea existente (de la ronda anterior):
```csharp
using PortalSaas.Integrations.Connectors;
...
builder.Services.AddSingleton<IIntegrationConnector, SapDocumentConnector>();
```

Cambiar el `using PortalSaas.Integrations.Connectors;` por `using PortalSaas.Core.Integraciones;`. La línea de registro `AddSingleton<IIntegrationConnector, SapDocumentConnector>()` no cambia (mismo nombre de tipo, namespace distinto, resuelto por el `using`).

Nota: `SapDocumentConnector` ahora depende de `ISalesDocumentService`/etc., que son servicios `Scoped` (siguiendo el patrón de los demás servicios de documento) — pero el registro es `AddSingleton<IIntegrationConnector, SapDocumentConnector>()`. Si al ejecutar el Paso 9 aparece un error de "Cannot consume scoped service from singleton", cambiar el registro a `AddScoped<IIntegrationConnector, SapDocumentConnector>()` en su lugar (el `IntegrationSyncHostedService` ya resuelve conectores por scope en cada ciclo vía `GetServices<IIntegrationConnector>()`, así que `Scoped` funciona igual de bien ahí).

- [ ] **Step 8: Compilar la solución completa**

Run: `dotnet build PortalSaas.sln`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 9: Ejecutar toda la suite de tests**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj`
Expected: todos los tests pasan (los 123 anteriores + los 6 nuevos de `SapDocumentConnectorTests`, menos los 2 que tenía el archivo viejo = 127 total).

- [ ] **Step 10: Commit**

```bash
git add -A src/PortalSaas.Core/Integraciones/ tests/PortalSaas.Core.Tests/Integraciones/SapDocumentConnectorTests.cs src/PortalSaas.Host/Program.cs
git add src/PortalSaas.Integrations/Connectors/ 2>/dev/null || true
git commit -m "feat: trasladar SapDocumentConnector a PortalSaas.Core con document services inyectados"
```

---

### Task 2: Fix de tooling EF en `PortalSaas.Host`

**Files:**
- Modify: `src/PortalSaas.Host/PortalSaas.Host.csproj`

**Interfaces:**
- Consumes: nada de tareas anteriores.
- Produces: nada consumido por otras tareas — este task es puramente de tooling, verificable de forma aislada.

- [ ] **Step 1: Agregar el paquete y las referencias**

En `src/PortalSaas.Host/PortalSaas.Host.csproj`, dentro del `<ItemGroup>` de `PackageReference` (junto a `Microsoft.EntityFrameworkCore.SqlServer`):

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.8">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

En el `<ItemGroup>` de `ProjectReference` (junto a `PortalSaas.Integrations`):

```xml
<ProjectReference Include="..\PortalSaas.Data.Migrations.PostgreSql\PortalSaas.Data.Migrations.PostgreSql.csproj" />
<ProjectReference Include="..\PortalSaas.Data.Migrations.SqlServer\PortalSaas.Data.Migrations.SqlServer.csproj" />
```

- [ ] **Step 2: Compilar**

Run: `dotnet build src/PortalSaas.Host/PortalSaas.Host.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 3: Verificar que el comando de migraciones documentado en el plan original funciona ahora**

Run: `dotnet ef migrations add VerificarToolingHost --project src/PortalSaas.Data.Migrations.PostgreSql --startup-project src/PortalSaas.Host`
Expected: el comando genera una migración nueva sin error (antes fallaba porque Host no tenía el paquete Design ni la referencia al proyecto de migraciones).

- [ ] **Step 4: Revertir la migración de verificación (no se necesita, era solo para probar el tooling)**

Run: `dotnet ef migrations remove --project src/PortalSaas.Data.Migrations.PostgreSql --startup-project src/PortalSaas.Host`
Expected: la migración `VerificarToolingHost` generada en el Paso 3 se elimina; `git status --short` no debe mostrar archivos nuevos bajo `src/PortalSaas.Data.Migrations.PostgreSql/Migrations/`.

- [ ] **Step 5: Confirmar que no quedaron archivos huérfanos**

Run: `git status --short src/PortalSaas.Data.Migrations.PostgreSql/`
Expected: sin salida (working tree limpio en esa carpeta).

- [ ] **Step 6: Commit**

```bash
git add src/PortalSaas.Host/PortalSaas.Host.csproj
git commit -m "fix: agregar EF Design y referencias de migraciones a PortalSaas.Host"
```

---

### Task 3: UI admin — `Index.cshtml` (listado + ejecutar ahora)

**Files:**
- Create: `src/PortalSaas.Host/Pages/Admin/Integraciones/Index.cshtml.cs`
- Create: `src/PortalSaas.Host/Pages/Admin/Integraciones/Index.cshtml`

**Interfaces:**
- Consumes: `PortalSaasDbContext.IntegrationDefinitions` (`PortalSaas.Data`, ya existente); `IntegrationDefinition`, `IntegrationDireccion`, `IntegrationConectorTipo` (`PortalSaas.Data.Entities.Integraciones`).
- Produces: página en ruta `/Admin/Integraciones` con handler POST `OnPostEjecutarAsync(Guid id)` — la Tarea 4 (`Bitacora.cshtml`) enlaza a esta página vía `asp-route-id`, y viceversa; ambas viven en el mismo directorio `Pages/Admin/Integraciones/`.

- [ ] **Step 1: Crear el PageModel**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities.Integraciones;

namespace PortalSaas.Host.Pages.Admin.Integraciones;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public IndexModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public List<IntegrationDefinition> Integraciones { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Integraciones = await _db.IntegrationDefinitions
            .OrderBy(d => d.Nombre)
            .ToListAsync(ct);
    }

    public async Task<IActionResult> OnPostEjecutarAsync(Guid id, CancellationToken ct)
    {
        var definicion = await _db.IntegrationDefinitions.FindAsync([id], ct);
        if (definicion is null)
        {
            return NotFound();
        }

        definicion.NextRunAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        TempData["Mensaje"] = $"'{definicion.Nombre}' quedó marcada para ejecutarse en el próximo ciclo (hasta 1 minuto).";
        return RedirectToPage();
    }
}
```

- [ ] **Step 2: Crear la vista Razor**

```cshtml
@page
@model PortalSaas.Host.Pages.Admin.Integraciones.IndexModel
@{
    ViewData["Title"] = "Integraciones";
}

<div class="d-flex justify-content-between align-items-center mb-3">
    <div>
        <h1 class="h3 mb-0">Integraciones</h1>
        <span class="text-muted">@Model.Integraciones.Count integración(es) configurada(s)</span>
    </div>
</div>

@if (TempData["Mensaje"] is string mensaje)
{
    <div class="alert alert-success">@mensaje</div>
}

@if (Model.Integraciones.Count == 0)
{
    <p class="text-muted">No hay integraciones configuradas.</p>
}
else
{
    <div class="document-list-table-wrapper">
        <table class="table document-list-table mb-0">
            <thead>
                <tr>
                    <th>Nombre</th>
                    <th>Módulo origen</th>
                    <th>Conector</th>
                    <th>Dirección</th>
                    <th>Estado</th>
                    <th>Próxima ejecución</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
                @foreach (var integracion in Model.Integraciones)
                {
                    <tr>
                        <td>@integracion.Nombre</td>
                        <td>@integracion.ModuloOrigen</td>
                        <td>@integracion.ConectorTipo</td>
                        <td>@integracion.Direccion</td>
                        <td>
                            @if (integracion.Activo)
                            {
                                <span class="badge bg-success">Activa</span>
                            }
                            else
                            {
                                <span class="badge bg-secondary">Inactiva</span>
                            }
                        </td>
                        <td>@(integracion.NextRunAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "-")</td>
                        <td class="text-end">
                            <a asp-page="/Admin/Integraciones/Bitacora" asp-route-id="@integracion.Id" class="btn btn-sm btn-outline-secondary">Ver bitácora</a>
                            <form method="post" asp-page-handler="Ejecutar" asp-route-id="@integracion.Id" asp-antiforgery="true" class="d-inline">
                                <button type="submit" class="btn btn-sm btn-outline-primary" @(integracion.Activo ? "" : "disabled")>Ejecutar ahora</button>
                            </form>
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
}
```

Nota sobre el atributo `disabled` condicional (`@(integracion.Activo ? "" : "disabled")`): NO usar un valor booleano C# crudo aquí — el patrón conocido en este codebase para atributos booleanos en Razor es renderizar el string `"disabled"` o cadena vacía, tal como está escrito arriba (ya sigue el patrón correcto).

- [ ] **Step 3: Verificar manualmente sin ejecutar el Host completo**

Run: `dotnet build src/PortalSaas.Host/PortalSaas.Host.csproj`
Expected: Build succeeded, 0 warnings, 0 errors (las vistas Razor se compilan como parte del build).

- [ ] **Step 4: Ejecutar toda la suite de tests para confirmar que no se rompió nada**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj`
Expected: mismo conteo que al final de la Tarea 1, todos en PASS (esta tarea no agrega tests nuevos — es una página Razor sin lógica de negocio propia más allá de una query y un `SaveChangesAsync`, cubierta por verificación manual/build, siguiendo el mismo patrón que las páginas admin existentes de este proyecto, ninguna de las cuales tiene tests unitarios).

- [ ] **Step 5: Commit**

```bash
git add src/PortalSaas.Host/Pages/Admin/Integraciones/Index.cshtml src/PortalSaas.Host/Pages/Admin/Integraciones/Index.cshtml.cs
git commit -m "feat: agregar página admin de listado/ejecución de integraciones"
```

---

### Task 4: UI admin — `Bitacora.cshtml` (historial de ejecuciones)

**Files:**
- Create: `src/PortalSaas.Host/Pages/Admin/Integraciones/Bitacora.cshtml.cs`
- Create: `src/PortalSaas.Host/Pages/Admin/Integraciones/Bitacora.cshtml`

**Interfaces:**
- Consumes: `PortalSaasDbContext.IntegrationDefinitions`, `PortalSaasDbContext.IntegrationRunLogs`; `IntegrationDefinition`, `IntegrationRunLog`, `IntegrationRunResultado` (`PortalSaas.Data.Entities.Integraciones`); enlazada desde `Index.cshtml` (Tarea 3) vía `asp-route-id`.
- Produces: página en ruta `/Admin/Integraciones/Bitacora?id={guid}` — no produce nada consumido por otra tarea (es la hoja final del árbol de páginas).

- [ ] **Step 1: Crear el PageModel**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities.Integraciones;

namespace PortalSaas.Host.Pages.Admin.Integraciones;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class BitacoraModel : PageModel
{
    private const int MaximoRegistros = 100;

    private readonly PortalSaasDbContext _db;

    public BitacoraModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public IntegrationDefinition Integracion { get; private set; } = null!;
    public List<IntegrationRunLog> Ejecuciones { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var integracion = await _db.IntegrationDefinitions.FindAsync([id], ct);
        if (integracion is null)
        {
            return NotFound();
        }

        Integracion = integracion;
        Ejecuciones = await _db.IntegrationRunLogs
            .Where(l => l.IntegrationDefinitionId == id)
            .OrderByDescending(l => l.IniciadoEn)
            .Take(MaximoRegistros)
            .ToListAsync(ct);

        return Page();
    }
}
```

- [ ] **Step 2: Crear la vista Razor**

```cshtml
@page "{id:guid}"
@model PortalSaas.Host.Pages.Admin.Integraciones.BitacoraModel
@{
    ViewData["Title"] = "Bitácora de integración";
}

<div class="d-flex justify-content-between align-items-center mb-3">
    <div>
        <h1 class="h3 mb-0">Bitácora: @Model.Integracion.Nombre</h1>
        <span class="text-muted">Últimas @Model.Ejecuciones.Count ejecución(es)</span>
    </div>
    <a asp-page="/Admin/Integraciones/Index" class="btn btn-outline-secondary">Volver</a>
</div>

@if (Model.Ejecuciones.Count == 0)
{
    <p class="text-muted">Todavía no hay ejecuciones registradas para esta integración.</p>
}
else
{
    <div class="document-list-table-wrapper">
        <table class="table document-list-table mb-0">
            <thead>
                <tr>
                    <th>Inicio</th>
                    <th>Fin</th>
                    <th>Resultado</th>
                    <th>Procesados</th>
                    <th>Con error</th>
                    <th>Disparado por</th>
                    <th>Detalle del error</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var ejecucion in Model.Ejecuciones)
                {
                    <tr>
                        <td>@ejecucion.IniciadoEn.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")</td>
                        <td>@(ejecucion.FinalizadoEn?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "-")</td>
                        <td>
                            @switch (ejecucion.Resultado)
                            {
                                case IntegrationRunResultado.Exito:
                                    <span class="badge bg-success">Éxito</span>
                                    break;
                                case IntegrationRunResultado.Parcial:
                                    <span class="badge bg-warning text-dark">Parcial</span>
                                    break;
                                default:
                                    <span class="badge bg-danger">Error</span>
                                    break;
                            }
                        </td>
                        <td>@ejecucion.RegistrosProcesados</td>
                        <td>@ejecucion.RegistrosConError</td>
                        <td>@ejecucion.DisparadoPor</td>
                        <td>@(ejecucion.DetalleError ?? "-")</td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
}
```

- [ ] **Step 3: Compilar**

Run: `dotnet build src/PortalSaas.Host/PortalSaas.Host.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 4: Ejecutar toda la suite de tests**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj`
Expected: mismo conteo que al final de la Tarea 3, todos en PASS.

- [ ] **Step 5: Verificar el enlace cruzado entre páginas**

Run: `grep -n "asp-page=\"/Admin/Integraciones/Bitacora\"" src/PortalSaas.Host/Pages/Admin/Integraciones/Index.cshtml && grep -n "asp-page=\"/Admin/Integraciones/Index\"" src/PortalSaas.Host/Pages/Admin/Integraciones/Bitacora.cshtml`
Expected: ambas líneas se encuentran (confirma que el enlace bidireccional entre Tarea 3 y Tarea 4 quedó bien escrito).

- [ ] **Step 6: Commit**

```bash
git add src/PortalSaas.Host/Pages/Admin/Integraciones/Bitacora.cshtml src/PortalSaas.Host/Pages/Admin/Integraciones/Bitacora.cshtml.cs
git commit -m "feat: agregar página admin de bitácora de ejecuciones de integraciones"
```

---

## Self-Review

**1. Cobertura del spec:** Punto 1 (conector real, alcance ajustado) → Task 1. Punto 2 (fix tooling) → Task 2. Punto 3 (UI admin, sin creación) → Tasks 3-4. Puntos fuera de alcance (5, 6, 7, 8) → no tienen task, correctamente, según lo acordado en el spec.

**2. Placeholder scan:** sin "TBD"/"TODO" genéricos. Los `NotSupportedException` de Task 1 llevan mensaje explícito y están justificados por el ajuste de alcance documentado en el spec — no son placeholders vagos, son documentación ejecutable de un límite real.

**3. Consistencia de tipos:** `SapDocumentConnector` mantiene la firma de `IIntegrationConnector` sin cambios (`Tipo`, `PullAsync`, `PushAsync`) — el único cambio es el constructor, que no es parte de la interfaz. `IntegrationRecord["TipoDocumento"]` usado en Task 1 es coherente con el indexador `object? this[string campo]` ya definido en el motor base. Los nombres de propiedad de `IntegrationDefinition`/`IntegrationRunLog` usados en Tasks 3-4 (`Nombre`, `ModuloOrigen`, `ConectorTipo`, `Direccion`, `Activo`, `NextRunAt`, `IniciadoEn`, `FinalizadoEn`, `Resultado`, `RegistrosProcesados`, `RegistrosConError`, `DisparadoPor`, `DetalleError`) coinciden exactamente con los nombres reales post-fix confirmados en la investigación previa al plan.

**4. Riesgo señalado explícitamente:** Task 1, Paso 7, deja una nota sobre el posible conflicto de lifetime (`Singleton` vs `Scoped`) entre `SapDocumentConnector` y los document services que ahora inyecta — no se puede confirmar sin ejecutar el build/test real, así que el plan da la instrucción exacta de qué cambiar si el error aparece, en vez de asumir silenciosamente que no va a pasar.
