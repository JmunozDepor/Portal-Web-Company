using Modulo.Inventario.Pages.ProductMaster;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using Xunit;

namespace PortalSaas.Core.Tests;

/// <summary>
/// Test de página pedido explícitamente por el spec (docs/superpowers/specs/
/// 2026-09-05-maestro-producto-design.md, sección "Testing"): OnGetAsync con
/// itemCode válido/inválido/vacío. Fakes simples de las interfaces de servicio
/// (no de IHanaService) -- mismo criterio de estilo que HanaServiceFalso en
/// PriceListServiceTests.cs, pero acá el visor depende de servicios ya resueltos,
/// no de IHanaService directo.
/// </summary>
file sealed class CurrentUserContextFalso : ICurrentUserContext
{
    public Guid UserId { get; } = Guid.NewGuid();
    public string Username { get; } = "usuario-de-prueba";
    public bool IsAdmin { get; } = false;
    public Guid OrganizationId { get; } = Guid.NewGuid();

    // No es lo que se está testeando acá -- eso ya lo cubrió la revisión de
    // autorización -- siempre concede el permiso de vista.
    public Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default) =>
        Task.FromResult(true);
}

file sealed class ItemMasterDetailServiceFalso : IItemMasterDetailService
{
    private readonly ItemMasterDetailDto? _detalle;

    public ItemMasterDetailServiceFalso(ItemMasterDetailDto? detalle) => _detalle = detalle;

    public bool Llamado { get; private set; }

    public Task<ItemMasterDetailDto?> GetDetailAsync(string itemCode, CancellationToken ct = default)
    {
        Llamado = true;
        return Task.FromResult(_detalle);
    }
}

file sealed class ItemStockServiceFalso : IItemStockService
{
    private readonly IReadOnlyList<WarehouseStockDto> _stocks;

    public ItemStockServiceFalso(IReadOnlyList<WarehouseStockDto> stocks) => _stocks = stocks;

    public bool Llamado { get; private set; }

    public Task<IReadOnlyList<WarehouseStockDto>> GetStockByItemAsync(string itemCode, CancellationToken ct = default)
    {
        Llamado = true;
        return Task.FromResult(_stocks);
    }
}

file sealed class PriceListServiceFalso : IPriceListService
{
    private readonly IReadOnlyList<PriceListOptionDto> _listas;
    private readonly decimal? _precio;

    public PriceListServiceFalso(IReadOnlyList<PriceListOptionDto> listas, decimal? precio)
    {
        _listas = listas;
        _precio = precio;
    }

    public bool ListAllLlamado { get; private set; }
    public bool GetPriceLlamado { get; private set; }

    public Task<decimal?> GetPriceAsync(string itemCode, int priceList, CancellationToken ct = default)
    {
        GetPriceLlamado = true;
        return Task.FromResult(_precio);
    }

    public Task<IReadOnlyDictionary<string, decimal>> GetPricesAsync(IReadOnlyCollection<string> itemCodes, int priceList, CancellationToken ct = default) =>
        throw new NotSupportedException("No usado por el visor Maestro de Producto.");

    public Task<IReadOnlyList<PriceListOptionDto>> ListAllAsync(CancellationToken ct = default)
    {
        ListAllLlamado = true;
        return Task.FromResult(_listas);
    }
}

file sealed class ItemCatalogServiceFalso : IItemCatalogService
{
    public string? SearchTextRecibido { get; private set; }
    public bool GetTopLlamado { get; private set; }

    public Task<IReadOnlyList<ItemDto>> SearchAsync(string text, int limit = 30, CancellationToken ct = default)
    {
        SearchTextRecibido = text;
        return Task.FromResult<IReadOnlyList<ItemDto>>([new ItemDto { ItemCode = "A001", ItemName = "Artículo de prueba" }]);
    }

    public Task<IReadOnlyList<ItemDto>> GetByCodesAsync(IReadOnlyCollection<string> itemCodes, CancellationToken ct = default) =>
        throw new NotSupportedException("No usado por el visor Maestro de Producto.");

    public Task<IReadOnlyList<ItemDto>> GetTopAsync(int limit = 30, CancellationToken ct = default)
    {
        GetTopLlamado = true;
        return Task.FromResult<IReadOnlyList<ItemDto>>([new ItemDto { ItemCode = "TOP1", ItemName = "El más vendido" }]);
    }
}

public class ProductMasterIndexModelTests
{
    [Fact]
    public async Task OnGetAsync_ItemCodeVacio_DevuelvePageSinConsultarNada()
    {
        var itemMaster = new ItemMasterDetailServiceFalso(null);
        var stock = new ItemStockServiceFalso([]);
        var priceLists = new PriceListServiceFalso([], null);
        var model = new IndexModel(new ItemCatalogServiceFalso(), itemMaster, stock, priceLists, new CurrentUserContextFalso());
        model.ItemCode = null;

        var resultado = await model.OnGetAsync(CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Mvc.RazorPages.PageResult>(resultado);
        Assert.False(model.ItemNotFound);
        Assert.Null(model.Detail);
        Assert.False(itemMaster.Llamado);
        Assert.False(stock.Llamado);
        Assert.False(priceLists.ListAllLlamado);
    }

    [Fact]
    public async Task OnGetAsync_ItemCodeInexistente_MarcaItemNotFound()
    {
        var itemMaster = new ItemMasterDetailServiceFalso(null);
        var stock = new ItemStockServiceFalso([]);
        var priceLists = new PriceListServiceFalso([], null);
        var model = new IndexModel(new ItemCatalogServiceFalso(), itemMaster, stock, priceLists, new CurrentUserContextFalso());
        model.ItemCode = "NO-EXISTE";

        var resultado = await model.OnGetAsync(CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Mvc.RazorPages.PageResult>(resultado);
        Assert.True(model.ItemNotFound);
        Assert.Null(model.Detail);
    }

    [Fact]
    public async Task OnGetAsync_ItemCodeExistente_LlenaDetalleListasYStock()
    {
        var detalle = new ItemMasterDetailDto { ItemCode = "A001", ItemName = "Artículo de prueba" };
        var stocks = new List<WarehouseStockDto>
        {
            new() { WhsCode = "01", WhsName = "Central", OnHand = 10m, IsCommited = 2m },
        };
        var listas = new List<PriceListOptionDto>
        {
            new() { ListNum = 2, ListName = "Lista mayorista" },
            new() { ListNum = 1, ListName = "Lista de venta general" },
        };
        var itemMaster = new ItemMasterDetailServiceFalso(detalle);
        var stock = new ItemStockServiceFalso(stocks);
        var priceLists = new PriceListServiceFalso(listas, 1234.56m);
        var model = new IndexModel(new ItemCatalogServiceFalso(), itemMaster, stock, priceLists, new CurrentUserContextFalso());
        model.ItemCode = "A001";

        var resultado = await model.OnGetAsync(CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Mvc.RazorPages.PageResult>(resultado);
        Assert.False(model.ItemNotFound);
        Assert.NotNull(model.Detail);
        Assert.Equal("A001", model.Detail!.ItemCode);
        Assert.Equal(2, model.PriceLists.Count);
        // Preselección: la lista de menor ListNum (1, no 2 pese a venir primero en el catálogo).
        Assert.Equal(1, model.SelectedPriceList);
        Assert.Equal(1234.56m, model.SelectedPrice);
        Assert.True(priceLists.GetPriceLlamado);
        Assert.Single(model.Stocks);
        Assert.True(stock.Llamado);
    }

    [Fact]
    public async Task OnGetSearchItemsAsync_TextoAsterisco_LlamaGetTopEnVezDeSearch()
    {
        var items = new ItemCatalogServiceFalso();
        var model = new IndexModel(items, new ItemMasterDetailServiceFalso(null), new ItemStockServiceFalso([]), new PriceListServiceFalso([], null), new CurrentUserContextFalso());

        var resultado = await model.OnGetSearchItemsAsync("*", CancellationToken.None);

        Assert.True(items.GetTopLlamado);
        Assert.Null(items.SearchTextRecibido);
    }

    [Fact]
    public async Task OnGetSearchItemsAsync_TextoNormal_LlamaSearchEnVezDeGetTop()
    {
        var items = new ItemCatalogServiceFalso();
        var model = new IndexModel(items, new ItemMasterDetailServiceFalso(null), new ItemStockServiceFalso([]), new PriceListServiceFalso([], null), new CurrentUserContextFalso());

        var resultado = await model.OnGetSearchItemsAsync("chuck taylor", CancellationToken.None);

        Assert.False(items.GetTopLlamado);
        Assert.Equal("chuck taylor", items.SearchTextRecibido);
    }
}
