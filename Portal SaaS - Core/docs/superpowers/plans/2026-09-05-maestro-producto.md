# Maestro de Producto (visor) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agregar una pantalla de solo lectura "Maestro de Producto" en `Modulo.Inventario` que muestre datos generales del artículo (OITM), su código de barra y grupo de artículo (OITB), el precio en una lista de precios seleccionable (OPLN/ITM1), y el stock por almacén (OITW/OWHS).

**Architecture:** Sigue el patrón ya establecido de catálogos de solo lectura en `PortalSaas.Core/Catalogos` (un servicio por tabla/consulta, `IHanaService.QueryAsync<T>` con DTOs non-positional, `CatalogSqlHelper` para búsquedas). La pantalla es una Razor Page independiente (no un motor de documento genérico) que reutiliza el widget JS `wireCatalogSearch` ya usado en Ventas/Compras para el buscador de artículo, con recarga de página al elegir uno y un único fetch AJAX liviano para el precio al cambiar de lista.

**Tech Stack:** ASP.NET Core Razor Pages (.NET 8), SAP HANA vía `IHanaService`, xUnit para tests.

## Global Constraints

- Nunca listar `OITM` completo — solo búsqueda acotada (ya lo garantiza `IItemCatalogService.SearchAsync`, límite 30/100 vía `CatalogSqlHelper.DefaultSearchLimit`).
- Todo SQL nuevo debe usar comillas dobles en nombres de tabla/columna (HANA case-sensitive) y parámetros `:nombre` — nunca un nombre de parámetro repetido en más de una posición del mismo `OR`/`WHERE` (bug real ya confirmado en `ItemCatalogService`).
- DTOs de fila de HANA son `sealed record` non-positional con propiedades `{ get; init; }` cuyo nombre coincide exacto con el alias `AS "Alias"` del SQL (mapeo por reflection, ver `ItemPriceDto`).
- Autorización de página vía `[Authorize]` + `ICurrentUserContext.HasActionAsync(MenuCode, PortalActions.View, ct)`, `MenuCode` con prefijo `"Inventario."` seguido del `Code` del ítem de menú.
- Un plugin nunca referencia a otro plugin (regla dura del proyecto).

---

### Task 1: `ItemMasterDetailService` (datos generales + grupo de artículo)

**Files:**
- Create: `Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/ItemMasterDetailDto.cs`
- Create: `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IItemMasterDetailService.cs`
- Create: `Portal SaaS - Core/src/PortalSaas.Core/Catalogos/ItemMasterDetailService.cs`
- Test: `Portal SaaS - Core/tests/PortalSaas.Core.Tests/ItemMasterDetailServiceTests.cs`

**Interfaces:**
- Produces: `ItemMasterDetailDto { string ItemCode, string ItemName, string? CodeBars, string? ItemGroupCode, string? ItemGroupName }`
- Produces: `IItemMasterDetailService.GetDetailAsync(string itemCode, CancellationToken ct = default) : Task<ItemMasterDetailDto?>` (null si el artículo no existe)

- [ ] **Step 1: Escribir el DTO**

```csharp
// Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/ItemMasterDetailDto.cs
namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Ficha de artículo (OITM + OITB) de la compañía SAP activa -- non-positional, ver
/// el doc-comment de ItemDto (mismo motivo: mapeo por reflection de
/// IHanaService.QueryAsync). A diferencia de ItemDto (usado por la búsqueda en vivo),
/// este DTO trae los campos completos de la ficha del visor Maestro de Producto.
/// </summary>
public sealed record ItemMasterDetailDto
{
    public string ItemCode { get; init; } = null!;
    public string ItemName { get; init; } = null!;
    public string? CodeBars { get; init; }
    public string? ItemGroupCode { get; init; }
    public string? ItemGroupName { get; init; }
}
```

- [ ] **Step 2: Escribir el contrato**

```csharp
// Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IItemMasterDetailService.cs
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Ficha completa de un artículo puntual (OITM + OITB) para el visor Maestro de
/// Producto -- a diferencia de IItemCatalogService (búsqueda en vivo, resultados
/// acotados), este servicio siempre resuelve UN artículo por su código exacto.
/// </summary>
public interface IItemMasterDetailService
{
    /// <summary>Null si el artículo no existe.</summary>
    Task<ItemMasterDetailDto?> GetDetailAsync(string itemCode, CancellationToken ct = default);
}
```

- [ ] **Step 3: Escribir el test (falla porque `ItemMasterDetailService` no existe)**

```csharp
// Portal SaaS - Core/tests/PortalSaas.Core.Tests/ItemMasterDetailServiceTests.cs
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Catalogos;
using Xunit;

namespace PortalSaas.Core.Tests;

file sealed class HanaServiceFalso : IHanaService
{
    private readonly IReadOnlyList<object> _filas;

    public HanaServiceFalso(IReadOnlyList<object> filas) => _filas = filas;

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sqlParametrizado, object? parametros = null, CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<T>)_filas.Cast<T>().ToList());

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryDynamicAsync(string sqlParametrizado, object? parametros = null, CancellationToken ct = default) =>
        throw new NotSupportedException("No usado por catálogos de solo lectura.");

    public Task<int> ExecuteAsync(string sqlParametrizado, object? parametros = null, CancellationToken ct = default) =>
        throw new NotSupportedException("No usado por catálogos de solo lectura.");

    public Task<string> GetPlatformEngineTypeAsync(CancellationToken ct = default) => Task.FromResult("hana");
}

public class ItemMasterDetailServiceTests
{
    [Fact]
    public async Task GetDetailAsync_DevuelveLaFichaCuandoElArticuloExiste()
    {
        var hana = new HanaServiceFalso(new[]
        {
            new ItemMasterDetailDto
            {
                ItemCode = "A001",
                ItemName = "Artículo de prueba",
                CodeBars = "7801234567890",
                ItemGroupCode = "100",
                ItemGroupName = "Artículos de Venta",
            },
        });
        var servicio = new ItemMasterDetailService(hana);

        var ficha = await servicio.GetDetailAsync("A001");

        Assert.NotNull(ficha);
        Assert.Equal("Artículo de prueba", ficha!.ItemName);
        Assert.Equal("7801234567890", ficha.CodeBars);
        Assert.Equal("Artículos de Venta", ficha.ItemGroupName);
    }

    [Fact]
    public async Task GetDetailAsync_DevuelveNullCuandoElArticuloNoExiste()
    {
        var hana = new HanaServiceFalso(Array.Empty<ItemMasterDetailDto>());
        var servicio = new ItemMasterDetailService(hana);

        var ficha = await servicio.GetDetailAsync("NOEXISTE");

        Assert.Null(ficha);
    }
}
```

- [ ] **Step 4: Correr el test para confirmar que falla**

Run: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter ItemMasterDetailServiceTests`
Expected: FAIL (compilación) — `ItemMasterDetailService` no existe todavía.

- [ ] **Step 5: Implementar el servicio**

```csharp
// Portal SaaS - Core/src/PortalSaas.Core/Catalogos/ItemMasterDetailService.cs
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Ficha de artículo (OITM + OITB) de la compañía SAP activa, para el visor Maestro de Producto.</summary>
public sealed class ItemMasterDetailService : IItemMasterDetailService
{
    private readonly IHanaService _hana;

    public ItemMasterDetailService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<ItemMasterDetailDto?> GetDetailAsync(string itemCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT T0."ItemCode" AS "ItemCode", T0."ItemName" AS "ItemName", T0."CodeBars" AS "CodeBars",
                   T0."ItmsGrpCod" AS "ItemGroupCode", T1."ItmsGrpNam" AS "ItemGroupName"
            FROM "OITM" T0
            LEFT JOIN "OITB" T1 ON T1."ItmsGrpCod" = T0."ItmsGrpCod"
            WHERE T0."ItemCode" = :itemCode
            """;

        var filas = await _hana.QueryAsync<ItemMasterDetailDto>(sql, new { itemCode }, ct);
        return filas.FirstOrDefault();
    }
}
```

- [ ] **Step 6: Correr el test para confirmar que pasa**

Run: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter ItemMasterDetailServiceTests`
Expected: PASS (2 tests)

- [ ] **Step 7: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/ItemMasterDetailDto.cs" \
        "Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IItemMasterDetailService.cs" \
        "Portal SaaS - Core/src/PortalSaas.Core/Catalogos/ItemMasterDetailService.cs" \
        "Portal SaaS - Core/tests/PortalSaas.Core.Tests/ItemMasterDetailServiceTests.cs"
git commit -m "feat(inventario): agregar ItemMasterDetailService (ficha OITM+OITB)"
```

---

### Task 2: `ItemStockService` (stock por almacén)

**Files:**
- Create: `Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/WarehouseStockDto.cs`
- Create: `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IItemStockService.cs`
- Create: `Portal SaaS - Core/src/PortalSaas.Core/Catalogos/ItemStockService.cs`
- Test: `Portal SaaS - Core/tests/PortalSaas.Core.Tests/ItemStockServiceTests.cs`

**Interfaces:**
- Produces: `WarehouseStockDto { string WhsCode, string WhsName, decimal OnHand, decimal IsCommited }` con `Available => OnHand - IsCommited`
- Produces: `IItemStockService.GetStockByItemAsync(string itemCode, CancellationToken ct = default) : Task<IReadOnlyList<WarehouseStockDto>>`

- [ ] **Step 1: Escribir el DTO**

```csharp
// Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/WarehouseStockDto.cs
namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Fila de stock de un artículo en un almacén puntual (OITW + OWHS) -- non-positional,
/// ver el doc-comment de ItemDto. Available se calcula acá, no en SAP.
/// </summary>
public sealed record WarehouseStockDto
{
    public string WhsCode { get; init; } = null!;
    public string WhsName { get; init; } = null!;
    public decimal OnHand { get; init; }
    public decimal IsCommited { get; init; }
    public decimal Available => OnHand - IsCommited;
}
```

- [ ] **Step 2: Escribir el contrato**

```csharp
// Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IItemStockService.cs
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Stock por almacén (OITW) de un artículo puntual, para el visor Maestro de Producto.</summary>
public interface IItemStockService
{
    /// <summary>Lista vacía si el artículo no tiene registro en ningún almacén.</summary>
    Task<IReadOnlyList<WarehouseStockDto>> GetStockByItemAsync(string itemCode, CancellationToken ct = default);
}
```

- [ ] **Step 3: Escribir el test (falla porque `ItemStockService` no existe)**

```csharp
// Portal SaaS - Core/tests/PortalSaas.Core.Tests/ItemStockServiceTests.cs
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Catalogos;
using Xunit;

namespace PortalSaas.Core.Tests;

file sealed class HanaServiceFalso : IHanaService
{
    private readonly IReadOnlyList<object> _filas;

    public HanaServiceFalso(IReadOnlyList<object> filas) => _filas = filas;

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sqlParametrizado, object? parametros = null, CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<T>)_filas.Cast<T>().ToList());

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryDynamicAsync(string sqlParametrizado, object? parametros = null, CancellationToken ct = default) =>
        throw new NotSupportedException("No usado por catálogos de solo lectura.");

    public Task<int> ExecuteAsync(string sqlParametrizado, object? parametros = null, CancellationToken ct = default) =>
        throw new NotSupportedException("No usado por catálogos de solo lectura.");

    public Task<string> GetPlatformEngineTypeAsync(CancellationToken ct = default) => Task.FromResult("hana");
}

public class ItemStockServiceTests
{
    [Fact]
    public async Task GetStockByItemAsync_CalculaDisponibleComoOnHandMenosComprometido()
    {
        var hana = new HanaServiceFalso(new[]
        {
            new WarehouseStockDto { WhsCode = "01", WhsName = "Bodega Central", OnHand = 100m, IsCommited = 30m },
            new WarehouseStockDto { WhsCode = "02", WhsName = "Bodega Norte", OnHand = 50m, IsCommited = 0m },
        });
        var servicio = new ItemStockService(hana);

        var stock = await servicio.GetStockByItemAsync("A001");

        Assert.Equal(2, stock.Count);
        Assert.Equal(70m, stock[0].Available);
        Assert.Equal(50m, stock[1].Available);
    }

    [Fact]
    public async Task GetStockByItemAsync_DevuelveListaVaciaSinStockEnNingunAlmacen()
    {
        var hana = new HanaServiceFalso(Array.Empty<WarehouseStockDto>());
        var servicio = new ItemStockService(hana);

        var stock = await servicio.GetStockByItemAsync("SINSTOCK");

        Assert.Empty(stock);
    }
}
```

- [ ] **Step 4: Correr el test para confirmar que falla**

Run: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter ItemStockServiceTests`
Expected: FAIL (compilación) — `ItemStockService` no existe todavía.

- [ ] **Step 5: Implementar el servicio**

```csharp
// Portal SaaS - Core/src/PortalSaas.Core/Catalogos/ItemStockService.cs
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Catalogos;

/// <summary>Stock por almacén (OITW + OWHS) de un artículo puntual, para el visor Maestro de Producto.</summary>
public sealed class ItemStockService : IItemStockService
{
    private readonly IHanaService _hana;

    public ItemStockService(IHanaService hana)
    {
        _hana = hana;
    }

    public async Task<IReadOnlyList<WarehouseStockDto>> GetStockByItemAsync(string itemCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT T0."WhsCode" AS "WhsCode", T1."WhsName" AS "WhsName",
                   T0."OnHand" AS "OnHand", T0."IsCommited" AS "IsCommited"
            FROM "OITW" T0
            JOIN "OWHS" T1 ON T1."WhsCode" = T0."WhsCode"
            WHERE T0."ItemCode" = :itemCode
            ORDER BY T1."WhsName"
            """;

        return await _hana.QueryAsync<WarehouseStockDto>(sql, new { itemCode }, ct);
    }
}
```

- [ ] **Step 6: Correr el test para confirmar que pasa**

Run: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter ItemStockServiceTests`
Expected: PASS (2 tests)

- [ ] **Step 7: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/WarehouseStockDto.cs" \
        "Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IItemStockService.cs" \
        "Portal SaaS - Core/src/PortalSaas.Core/Catalogos/ItemStockService.cs" \
        "Portal SaaS - Core/tests/PortalSaas.Core.Tests/ItemStockServiceTests.cs"
git commit -m "feat(inventario): agregar ItemStockService (stock por almacén OITW+OWHS)"
```

---

### Task 3: `PriceListService.ListAllAsync` (catálogo de listas de precios OPLN)

**Files:**
- Create: `Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/PriceListOptionDto.cs`
- Modify: `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IPriceListService.cs`
- Modify: `Portal SaaS - Core/src/PortalSaas.Core/Catalogos/PriceListService.cs`
- Test: `Portal SaaS - Core/tests/PortalSaas.Core.Tests/PriceListServiceTests.cs`

**Interfaces:**
- Consumes: `IPriceListService.GetPriceAsync(string itemCode, int priceList, CancellationToken ct = default) : Task<decimal?>` (ya existe, sin cambios)
- Produces: `PriceListOptionDto { int ListNum, string ListName }`
- Produces: `IPriceListService.ListAllAsync(CancellationToken ct = default) : Task<IReadOnlyList<PriceListOptionDto>>` (nuevo método de la interfaz existente)

- [ ] **Step 1: Escribir el DTO**

```csharp
// Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/PriceListOptionDto.cs
namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de OPLN (catálogo de listas de precios) -- non-positional, ver el doc-comment de ItemDto.</summary>
public sealed record PriceListOptionDto
{
    public int ListNum { get; init; }
    public string ListName { get; init; } = null!;
}
```

- [ ] **Step 2: Agregar el método a la interfaz**

En `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IPriceListService.cs`, agregar dentro de la interfaz existente (después de `GetPricesAsync`):

```csharp
    /// <summary>
    /// Catálogo completo de listas de precios (OPLN) de la compañía activa -- a
    /// diferencia de OITM/artículo, OPLN tiene pocas filas (unas pocas a unas
    /// decenas), se lista completa sin búsqueda ni límite, para poblar un combo (ver
    /// visor Maestro de Producto).
    /// </summary>
    Task<IReadOnlyList<PriceListOptionDto>> ListAllAsync(CancellationToken ct = default);
```

Y agregar `using PortalSaas.Abstractions.Modelos;` al inicio del archivo si no está.

- [ ] **Step 3: Escribir el test (falla porque `PriceListService` no implementa `ListAllAsync` todavía)**

```csharp
// Portal SaaS - Core/tests/PortalSaas.Core.Tests/PriceListServiceTests.cs
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Catalogos;
using Xunit;

namespace PortalSaas.Core.Tests;

file sealed class HanaServiceFalso : IHanaService
{
    private readonly IReadOnlyList<object> _filas;

    public HanaServiceFalso(IReadOnlyList<object> filas) => _filas = filas;

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sqlParametrizado, object? parametros = null, CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<T>)_filas.Cast<T>().ToList());

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryDynamicAsync(string sqlParametrizado, object? parametros = null, CancellationToken ct = default) =>
        throw new NotSupportedException("No usado por catálogos de solo lectura.");

    public Task<int> ExecuteAsync(string sqlParametrizado, object? parametros = null, CancellationToken ct = default) =>
        throw new NotSupportedException("No usado por catálogos de solo lectura.");

    public Task<string> GetPlatformEngineTypeAsync(CancellationToken ct = default) => Task.FromResult("hana");
}

public class PriceListServiceTests
{
    [Fact]
    public async Task ListAllAsync_DevuelveTodasLasListasDePrecio()
    {
        var hana = new HanaServiceFalso(new[]
        {
            new PriceListOptionDto { ListNum = 1, ListName = "Lista de venta general" },
            new PriceListOptionDto { ListNum = 2, ListName = "Lista mayorista" },
        });
        var servicio = new PriceListService(hana);

        var listas = await servicio.ListAllAsync();

        Assert.Equal(2, listas.Count);
        Assert.Contains(listas, l => l.ListNum == 1 && l.ListName == "Lista de venta general");
    }
}
```

- [ ] **Step 4: Correr el test para confirmar que falla**

Run: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter PriceListServiceTests`
Expected: FAIL (compilación) — `PriceListService` no implementa `IPriceListService.ListAllAsync` todavía.

- [ ] **Step 5: Implementar el método**

En `Portal SaaS - Core/src/PortalSaas.Core/Catalogos/PriceListService.cs`, agregar dentro de la clase `PriceListService` (después de `GetPricesAsync`):

```csharp
    public async Task<IReadOnlyList<PriceListOptionDto>> ListAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT "ListNum" AS "ListNum", "ListName" AS "ListName"
            FROM "OPLN"
            ORDER BY "ListNum"
            """;

        return await _hana.QueryAsync<PriceListOptionDto>(sql, ct: ct);
    }
```

Y agregar `using PortalSaas.Abstractions.Modelos;` al inicio del archivo si no está.

- [ ] **Step 6: Correr el test para confirmar que pasa**

Run: `dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter PriceListServiceTests`
Expected: PASS (1 test)

- [ ] **Step 7: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Abstractions/Modelos/PriceListOptionDto.cs" \
        "Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IPriceListService.cs" \
        "Portal SaaS - Core/src/PortalSaas.Core/Catalogos/PriceListService.cs" \
        "Portal SaaS - Core/tests/PortalSaas.Core.Tests/PriceListServiceTests.cs"
git commit -m "feat(inventario): agregar PriceListService.ListAllAsync (catálogo OPLN)"
```

---

### Task 4: Registrar los servicios nuevos en el DI del Host

**Files:**
- Modify: `Portal SaaS - Core/src/PortalSaas.Host/Program.cs:202-260` (junto a los registros existentes de catálogos)

**Interfaces:**
- Consumes: `IItemMasterDetailService`/`ItemMasterDetailService` (Task 1), `IItemStockService`/`ItemStockService` (Task 2)

- [ ] **Step 1: Agregar los registros**

En `Program.cs`, junto a la línea 202 (`builder.Services.AddScoped<IItemCatalogService, ItemCatalogService>();`), agregar:

```csharp
builder.Services.AddScoped<IItemMasterDetailService, ItemMasterDetailService>();
builder.Services.AddScoped<IItemStockService, ItemStockService>();
```

(`IPriceListService` ya está registrado en la línea 216 — no requiere cambios, `ListAllAsync` es un método nuevo de la misma interfaz/implementación ya registrada.)

- [ ] **Step 2: Compilar para confirmar que resuelve**

Run: `dotnet build "Portal SaaS - Core/src/PortalSaas.Host"`
Expected: Build succeeded, 0 errores.

- [ ] **Step 3: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Host/Program.cs"
git commit -m "feat(inventario): registrar ItemMasterDetailService e ItemStockService en el DI"
```

---

### Task 5: Página "Maestro de Producto" (buscador + ficha)

**Files:**
- Create: `Portal SaaS - Core/plugins/Modulo.Inventario/Pages/ProductMaster/Index.cshtml`
- Create: `Portal SaaS - Core/plugins/Modulo.Inventario/Pages/ProductMaster/Index.cshtml.cs`
- Modify: `Portal SaaS - Core/plugins/Modulo.Inventario/ModuloInventario.cs`

**Interfaces:**
- Consumes: `IItemCatalogService.SearchAsync(string text, int limit = 30, CancellationToken ct = default)` (existente), `IItemMasterDetailService.GetDetailAsync` (Task 1), `IItemStockService.GetStockByItemAsync` (Task 2), `IPriceListService.ListAllAsync`/`GetPriceAsync` (Task 3 / existente), `ICurrentUserContext.HasActionAsync(string menuCode, string action, CancellationToken ct)` (existente)

- [ ] **Step 1: Escribir el PageModel**

```csharp
// Portal SaaS - Core/plugins/Modulo.Inventario/Pages/ProductMaster/Index.cshtml.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Inventario.Pages.ProductMaster;

/// <summary>
/// Visor de solo lectura del maestro de artículos de SAP -- datos generales, código de
/// barra y grupo de artículo (OITM/OITB), precio en una lista de precios seleccionable
/// (OPLN/ITM1) y stock por almacén (OITW/OWHS). No es un motor de documento genérico
/// (no hereda de las bases Index/DetailGeneric*DocumentModelBase) -- es una consulta
/// puntual sobre un único artículo por código exacto.
/// </summary>
[Authorize]
public sealed class IndexModel : PageModel
{
    private const string MenuCode = "Inventario.maestroproducto";

    private readonly IItemCatalogService _items;
    private readonly IItemMasterDetailService _itemMaster;
    private readonly IItemStockService _stock;
    private readonly IPriceListService _priceLists;
    private readonly ICurrentUserContext _currentUser;

    public IndexModel(
        IItemCatalogService items,
        IItemMasterDetailService itemMaster,
        IItemStockService stock,
        IPriceListService priceLists,
        ICurrentUserContext currentUser)
    {
        _items = items;
        _itemMaster = itemMaster;
        _stock = stock;
        _priceLists = priceLists;
        _currentUser = currentUser;
    }

    [BindProperty(SupportsGet = true)]
    public string? ItemCode { get; set; }

    public ItemMasterDetailDto? Detail { get; private set; }
    public bool ItemNotFound { get; private set; }
    public IReadOnlyList<PriceListOptionDto> PriceLists { get; private set; } = [];
    public int SelectedPriceList { get; private set; }
    public decimal? SelectedPrice { get; private set; }
    public IReadOnlyList<WarehouseStockDto> Stocks { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await _currentUser.HasActionAsync(MenuCode, PortalActions.View, ct))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(ItemCode))
        {
            return Page();
        }

        Detail = await _itemMaster.GetDetailAsync(ItemCode, ct);
        if (Detail is null)
        {
            ItemNotFound = true;
            return Page();
        }

        PriceLists = await _priceLists.ListAllAsync(ct);
        // Preselección: la lista de menor ListNum (pedido explícito, sin lógica de
        // negocio asociada a cliente/configuración).
        SelectedPriceList = PriceLists.Count > 0 ? PriceLists.Min(l => l.ListNum) : 0;
        SelectedPrice = PriceLists.Count > 0
            ? await _priceLists.GetPriceAsync(ItemCode, SelectedPriceList, ct)
            : null;
        Stocks = await _stock.GetStockByItemAsync(ItemCode, ct);

        return Page();
    }

    /// <summary>Búsqueda en vivo del artículo -- mismo mecanismo que Modulo.Ventas (nunca se precarga OITM completo).</summary>
    public async Task<JsonResult> OnGetSearchItemsAsync(string text, CancellationToken ct)
    {
        var items = await _items.SearchAsync(text ?? string.Empty, ct: ct);
        return new JsonResult(items.Select(i => new { i.ItemCode, i.ItemName }));
    }

    /// <summary>Fetch AJAX liviano al cambiar el combo de lista de precios -- evita recargar toda la ficha.</summary>
    public async Task<JsonResult> OnGetPriceAsync(string itemCode, int priceList, CancellationToken ct)
    {
        var price = await _priceLists.GetPriceAsync(itemCode, priceList, ct);
        return new JsonResult(new { price });
    }
}
```

- [ ] **Step 2: Escribir la vista**

```cshtml
@* Portal SaaS - Core/plugins/Modulo.Inventario/Pages/ProductMaster/Index.cshtml *@
@page
@model Modulo.Inventario.Pages.ProductMaster.IndexModel
@{
    ViewData["Title"] = "Maestro de Producto";
}

<h1 class="page-title">Maestro de Producto</h1>

<form method="get" class="form-row product-master-search-form">
    <div class="form-group">
        <label class="form-label" for="product-master-search-input">Artículo</label>
        <input id="product-master-search-input" name="ItemCode" list="product-master-datalist"
               class="form-control product-master-search-input" autocomplete="off"
               placeholder="Código o nombre del artículo" value="@Model.ItemCode" />
        <datalist id="product-master-datalist"></datalist>
    </div>
    <div class="form-group">
        <button type="submit" class="btn btn-primary">Ver ficha</button>
    </div>
</form>

@if (Model.ItemNotFound)
{
    <div class="alert alert-warning">Artículo no encontrado.</div>
}

@if (Model.Detail is not null)
{
    <section class="doc-tab-seccion">
        <h2>Datos generales</h2>
        <div class="form-row">
            <div class="form-group">
                <label class="form-label">Código</label>
                <input class="form-control" value="@Model.Detail.ItemCode" disabled />
            </div>
            <div class="form-group">
                <label class="form-label">Descripción</label>
                <input class="form-control" value="@Model.Detail.ItemName" disabled />
            </div>
            <div class="form-group">
                <label class="form-label">Código de barra</label>
                <input class="form-control" value="@(Model.Detail.CodeBars ?? "—")" disabled />
            </div>
            <div class="form-group">
                <label class="form-label">Grupo de artículo</label>
                <input class="form-control" value="@(Model.Detail.ItemGroupName ?? "—")" disabled />
            </div>
        </div>
    </section>

    <section class="doc-tab-seccion">
        <h2>Precio</h2>
        <div class="form-row">
            <div class="form-group">
                <label class="form-label" for="product-master-pricelist-select">Lista de precios</label>
                <select id="product-master-pricelist-select" class="form-select product-master-pricelist-select">
                    @foreach (var lista in Model.PriceLists)
                    {
                        <option value="@lista.ListNum" selected="@(lista.ListNum == Model.SelectedPriceList)">@lista.ListName</option>
                    }
                </select>
            </div>
            <div class="form-group">
                <label class="form-label">Precio</label>
                <input id="product-master-price-output" class="form-control" value="@(Model.SelectedPrice?.ToString("N2") ?? "Sin precio")" disabled />
            </div>
        </div>
    </section>

    <section class="doc-tab-seccion">
        <h2>Stock por almacén</h2>
        @if (Model.Stocks.Count == 0)
        {
            <p>Sin stock registrado.</p>
        }
        else
        {
            <table class="table">
                <thead>
                    <tr>
                        <th>Almacén</th>
                        <th>En stock</th>
                        <th>Comprometido</th>
                        <th>Disponible</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var fila in Model.Stocks)
                    {
                        <tr>
                            <td>@fila.WhsCode — @fila.WhsName</td>
                            <td>@fila.OnHand.ToString("N2")</td>
                            <td>@fila.IsCommited.ToString("N2")</td>
                            <td>@fila.Available.ToString("N2")</td>
                        </tr>
                    }
                </tbody>
            </table>
        }
    </section>
}

<script src="~/js/catalog-search.js"></script>
<script>
    wireCatalogSearch({
        inputSelector: '.product-master-search-input',
        datalistId: 'product-master-datalist',
        handlerName: 'SearchItems',
        minChars: 2,
        getValue: function (item) { return item.itemCode; },
        getLabel: function (item) { return item.itemCode + ' — ' + item.itemName; },
    });

    // Cambio de lista de precios -- fetch liviano, no recarga la página completa (ver
    // OnGetPriceAsync). itemCode viene del querystring actual (?ItemCode=...), la
    // página solo llega hasta acá cuando Model.Detail no es null.
    (function () {
        var select = document.querySelector('.product-master-pricelist-select');
        var output = document.getElementById('product-master-price-output');
        if (!select) {
            return;
        }
        var itemCode = new URLSearchParams(window.location.search).get('ItemCode');
        select.addEventListener('change', function () {
            fetch('?handler=Price&itemCode=' + encodeURIComponent(itemCode) + '&priceList=' + encodeURIComponent(select.value))
                .then(function (r) { return r.json(); })
                .then(function (data) {
                    output.value = data.price === null ? 'Sin precio' : Number(data.price).toFixed(2);
                })
                .catch(function (error) { console.error('product-master price fetch: ', error); });
        });
    })();
</script>
```

- [ ] **Step 3: Agregar la entrada de menú**

En `Portal SaaS - Core/plugins/Modulo.Inventario/ModuloInventario.cs`, dentro de `GetMenu()`, agregar después de la línea de `"traslados"`:

```csharp
yield return new MenuItemDefinition { Code = "maestroproducto", ParentCode = "raiz", Name = "Maestro de Producto", Icon = "bi bi-upc-scan", PageRoute = "/inventario/maestro-producto", Order = 3 };
```

- [ ] **Step 4: Compilar**

Run: `dotnet build "Portal SaaS - Core/plugins/Modulo.Inventario"`
Expected: Build succeeded, 0 errores.

- [ ] **Step 5: Commit**

```bash
git add "Portal SaaS - Core/plugins/Modulo.Inventario/Pages/ProductMaster/Index.cshtml" \
        "Portal SaaS - Core/plugins/Modulo.Inventario/Pages/ProductMaster/Index.cshtml.cs" \
        "Portal SaaS - Core/plugins/Modulo.Inventario/ModuloInventario.cs"
git commit -m "feat(inventario): agregar pantalla Maestro de Producto (ficha + precio + stock)"
```

---

### Task 6: Verificación manual en navegador

**Files:** ninguno (solo verificación, sin cambios de código)

- [ ] **Step 1: Levantar el portal**

Run: `powershell -File "Portal SaaS - Core/build-all.ps1"` (o el script que ya usa el proyecto para levantar Central/OnPremise — ver sesiones anteriores)

- [ ] **Step 2: Verificar el menú**

Entrar como usuario con acceso al módulo Inventario y confirmar que aparece "Maestro de Producto" en el menú, bajo Inventario.

- [ ] **Step 3: Verificar el flujo completo**

1. Abrir `/inventario/maestro-producto`, confirmar que solo se ve el buscador (sin ficha).
2. Escribir 2+ caracteres de un artículo real, confirmar que el `<datalist>` sugiere resultados.
3. Elegir uno y enviar el formulario, confirmar que la ficha muestra código/descripción/código de barra/grupo de artículo correctos.
4. Cambiar la lista de precios en el combo, confirmar que el precio se actualiza sin recargar la página (Network tab: solo el fetch a `?handler=Price`, no una navegación completa).
5. Confirmar que la tabla de stock por almacén muestra los almacenes reales con cantidades correctas.
6. Probar con un código de artículo inexistente (`?ItemCode=NOEXISTE`), confirmar el mensaje "Artículo no encontrado".

- [ ] **Step 4: Reportar resultado**

Si algo falla, volver a la tarea correspondiente y corregir antes de dar la funcionalidad por terminada (regla dura: no reclamar éxito sin verificación real en navegador para cambios de UI).
