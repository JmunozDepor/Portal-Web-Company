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
    private readonly string? _soloParaCodigo;

    /// <param name="soloParaCodigo">Si viene, el detalle solo se devuelve para ese ItemCode (resto = no existe).</param>
    public ItemMasterDetailServiceFalso(ItemMasterDetailDto? detalle, string? soloParaCodigo = null)
    {
        _detalle = detalle;
        _soloParaCodigo = soloParaCodigo;
    }

    public bool Llamado { get; private set; }

    public Task<ItemMasterDetailDto?> GetDetailAsync(string itemCode, CancellationToken ct = default)
    {
        Llamado = true;
        return Task.FromResult(_soloParaCodigo is null || _soloParaCodigo == itemCode ? _detalle : null);
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

    public Task<IReadOnlyDictionary<(string ItemCode, string WhsCode), decimal>> GetAvailableStockAsync(
        IReadOnlyList<(string ItemCode, string WhsCode)> pairs, CancellationToken ct = default) =>
        throw new NotSupportedException("No usado por el visor Maestro de Producto.");
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

    /// <summary>Barra -> ItemCode que "existe" en este doble.</summary>
    public Dictionary<string, string> Barras { get; } = [];

    public Task<IReadOnlyList<ItemDto>> GetByCodesAsync(IReadOnlyCollection<string> itemCodes, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ItemDto>>(itemCodes.Select(c => new ItemDto { ItemCode = c, ItemName = "Por barra " + c }).ToList());

    public Task<IReadOnlyDictionary<string, string>> GetItemCodesByBarcodesAsync(IReadOnlyCollection<string> barcodes, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(barcodes.Where(Barras.ContainsKey).ToDictionary(b => b, b => Barras[b]));

    public Task<IReadOnlyList<ItemDto>> GetTopAsync(int limit = 30, CancellationToken ct = default)
    {
        GetTopLlamado = true;
        return Task.FromResult<IReadOnlyList<ItemDto>>([new ItemDto { ItemCode = "TOP1", ItemName = "El más vendido" }]);
    }

    public Task<IReadOnlyDictionary<string, bool>> GetActiveStatusAsync(IReadOnlyList<string> itemCodes, CancellationToken ct = default) =>
        throw new NotSupportedException("No usado por el visor Maestro de Producto.");
}

public class ProductMasterIndexModelTests
{
    [Fact]
    public async Task OnGetAsync_CodigoDeBarras_ResuelveElArticuloYMuestraSuFicha()
    {
        var detalle = new ItemMasterDetailDto { ItemCode = "A12831C-103-4X", ItemName = "Zapatilla" };
        var catalogo = new ItemCatalogServiceFalso();
        catalogo.Barras["194435294728"] = "A12831C-103-4X";
        var model = new IndexModel(catalogo, new ItemMasterDetailServiceFalso(detalle, soloParaCodigo: "A12831C-103-4X"),
            new ItemStockServiceFalso([]), new PriceListServiceFalso([], null), new CurrentUserContextFalso());
        model.ItemCode = "194435294728";

        await model.OnGetAsync(CancellationToken.None);

        Assert.False(model.ItemNotFound);
        Assert.Same(detalle, model.Detail);
        Assert.Equal("A12831C-103-4X", model.ItemCode);
    }

    [Fact]
    public async Task OnGetSearchItemsAsync_CodigoDeBarras_VienePrimeroSinRepetir()
    {
        var catalogo = new ItemCatalogServiceFalso();
        catalogo.Barras["194435294728"] = "A001";
        var model = new IndexModel(catalogo, new ItemMasterDetailServiceFalso(null), new ItemStockServiceFalso([]),
            new PriceListServiceFalso([], null), new CurrentUserContextFalso());

        var resultado = await model.OnGetSearchItemsAsync("194435294728", CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(resultado.Value);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"A001\""));
    }

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

/// <summary>Código pegado desde Excel/SAP (bug real 2026-10-20: "no permite pegar" en Maestro de Producto).</summary>
public class ProductMasterItemCodeNormalizationTests
{
    [Theory]
    [InlineData("M9160C-001-10", "M9160C-001-10")]
    [InlineData("  M9160C-001-10  ", "M9160C-001-10")]
    [InlineData("M9160C-001-10\t", "M9160C-001-10")]
    [InlineData("M9160C-001-10\r\n", "M9160C-001-10")]
    [InlineData("M9160C-001-10\tZAPATILLA\t1", "M9160C-001-10")]
    [InlineData(" M9160C-001-10 ", "M9160C-001-10")]
    [InlineData("\r\n\tM9160C-001-10", "M9160C-001-10")]
    public void NormalizeItemCode_DejaSoloElPrimerValorLimpio(string pegado, string esperado) =>
        Assert.Equal(esperado, IndexModel.NormalizeItemCode(pegado));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void NormalizeItemCode_SinValor_DevuelveNull(string? pegado) =>
        Assert.Null(IndexModel.NormalizeItemCode(pegado));
}
