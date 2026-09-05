# Maestro de Producto (visor) — diseño

Fecha: 2026-09-05
Módulo: `Modulo.Inventario`

## Objetivo

Agregar un visor de solo lectura del maestro de artículos de SAP dentro del módulo de
Inventario, equivalente a la ficha de artículo de SAP Business One: datos generales,
código de barra, grupo de artículo, precio según lista de precios seleccionable, y
stock por almacén.

No es un formulario de captura/edición — es una consulta. No reemplaza ni modifica
`IItemCatalogService` (búsqueda) ni `IPriceListService` (precio puntual), los reutiliza.

## Alcance

Una sola pantalla: **"Maestro de Producto"**, ruta `/inventario/maestro-producto`,
nueva entrada de menú bajo la raíz de Inventario (`ModuloInventario.GetMenu()`).

Fuera de alcance: listado paginado de todos los artículos (OITM puede tener cientos de
miles de filas — nunca se lista completa, solo se busca), edición de precios o stock,
múltiples códigos de barra por unidad de medida (se usa el campo único `CodeBars` de
OITM).

## Flujo de uso

1. La página muestra un buscador tipo autocomplete (reutiliza el widget JS
   `wireCatalogSearch` ya usado en Ventas/Compras) contra el handler existente
   `SearchItems` → `IItemCatalogService.SearchAsync`.
2. Al elegir un artículo, el buscador navega a la misma página con
   `?itemCode=<código>` (recarga de página completa — GET simple, sin SPA).
3. `OnGetAsync(string? itemCode)`:
   - Si `itemCode` es null/vacío: solo muestra el buscador.
   - Si viene: carga la ficha completa (ver "Datos a mostrar"). Si el artículo no
     existe, muestra "Artículo no encontrado" en vez de la ficha.
4. Dentro de la ficha, el combo de lista de precios dispara un fetch AJAX liviano
   (`OnGetPriceAsync`) para traer solo el precio en la lista elegida, sin recargar la
   página completa.

## Datos a mostrar

**Datos generales** (nuevo: `ItemMasterDetailDto` / `IItemMasterDetailService`):
- Código de artículo (`OITM.ItemCode`)
- Descripción (`OITM.ItemName`)
- Código de barra (`OITM.CodeBars`)
- Grupo de artículo: código y nombre (`OITM.ItmsGrpCod` → join `OITB.ItmsGrpNam`)

**Lista de precios** (extiende `IPriceListService` existente):
- Combo con todas las listas (`OPLN.ListNum`, `OPLN.ListName`) vía nuevo método
  `ListAllAsync()`. Pocas filas (a diferencia de OITM) — se lista completa, no se
  busca.
- Preselección: la lista de menor `ListNum`.
- Precio del artículo en la lista elegida: reutiliza
  `IPriceListService.GetPriceAsync(itemCode, priceList)` ya existente (retorna `null`
  si el artículo no tiene precio definido en esa lista — mostrar "Sin precio").

**Stock por almacén** (nuevo: `WarehouseStockDto` / `IItemStockService`):
- Tabla con una fila por almacén donde el artículo tiene registro en `OITW`, join
  `OWHS` para el nombre del almacén.
- Columnas: Almacén (código + nombre), En stock (`OnHand`), Comprometido
  (`IsCommited`), Disponible (`OnHand - IsCommited`, calculado en el servicio o en la
  vista).
- No depende de la lista de precios seleccionada — es independiente y siempre
  completa.

## Contratos nuevos (`PortalSaas.Abstractions`)

```csharp
// Modelos/ItemMasterDetailDto.cs
public sealed record ItemMasterDetailDto
{
    public string ItemCode { get; init; } = null!;
    public string ItemName { get; init; } = null!;
    public string? CodeBars { get; init; }
    public string? ItemGroupCode { get; init; }
    public string? ItemGroupName { get; init; }
}

// Contratos/IItemMasterDetailService.cs
public interface IItemMasterDetailService
{
    // null si el artículo no existe.
    Task<ItemMasterDetailDto?> GetDetailAsync(string itemCode, CancellationToken ct = default);
}

// Modelos/WarehouseStockDto.cs
public sealed record WarehouseStockDto
{
    public string WhsCode { get; init; } = null!;
    public string WhsName { get; init; } = null!;
    public decimal OnHand { get; init; }
    public decimal IsCommited { get; init; }
    public decimal Available => OnHand - IsCommited;
}

// Contratos/IItemStockService.cs
public interface IItemStockService
{
    Task<IReadOnlyList<WarehouseStockDto>> GetStockByItemAsync(string itemCode, CancellationToken ct = default);
}

// Modelos/PriceListOptionDto.cs
public sealed record PriceListOptionDto
{
    public int ListNum { get; init; }
    public string ListName { get; init; } = null!;
}

// Contratos/IPriceListService.cs — agregar:
Task<IReadOnlyList<PriceListOptionDto>> ListAllAsync(CancellationToken ct = default);
```

## Implementaciones nuevas (`PortalSaas.Core/Catalogos`)

- `ItemMasterDetailService.cs` — implementa `IItemMasterDetailService`, SQL sobre
  `OITM` + `OITB` (usar `CatalogSqlHelper` donde aplique).
- `ItemStockService.cs` — implementa `IItemStockService`, SQL sobre `OITW` + `OWHS`.
- `PriceListService.cs` — agregar `ListAllAsync()` sobre `OPLN`.
- Registrar `IItemMasterDetailService` e `IItemStockService` en el DI del Host
  (`PortalSaas.Host/Program.cs`), junto a los demás catálogos.

## Frontend (`Modulo.Inventario/Pages/ProductMaster`)

- `Index.cshtml` / `Index.cshtml.cs`.
- `OnGetAsync(string? itemCode)`: orquesta las 3 llamadas (detalle, listas de precio,
  stock) cuando `itemCode` viene informado.
- `OnGetPriceAsync(string itemCode, int priceList)`: handler AJAX, retorna JSON
  `{ price: decimal? }`.
- Buscador reutiliza `wireCatalogSearch` + handler `SearchItems` ya existente en el
  patrón de Ventas/Compras (ver `_TabGeneralVentas.cshtml` como referencia).
- Nueva entrada de menú en `ModuloInventario.GetMenu()`: código
  `"maestroproducto"`, ruta `/inventario/maestro-producto`.

## Manejo de errores

- Artículo no encontrado (`GetDetailAsync` retorna null): mensaje "Artículo no
  encontrado", sin lanzar excepción.
- Artículo sin código de barra / sin grupo asignado: mostrar "—" en el campo
  correspondiente (no es un error).
- Artículo sin stock en ningún almacén: tabla de stock vacía con mensaje "Sin stock
  registrado".
- Artículo sin precio en la lista elegida: "Sin precio" (ya es el comportamiento de
  `GetPriceAsync` retornando null).

## Testing

- Unit tests de `ItemMasterDetailService`, `ItemStockService` y
  `PriceListService.ListAllAsync()` contra fakes de `IHanaService` (mismo patrón que
  los tests existentes de catálogos).
- Test de página: `OnGetAsync` con itemCode válido/ inválido/ vacío.
