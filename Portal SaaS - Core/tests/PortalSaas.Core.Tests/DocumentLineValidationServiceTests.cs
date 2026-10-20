using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Catalogos;
using Xunit;

namespace PortalSaas.Core.Tests;

file sealed class StockFalso : IItemStockService
{
    public Dictionary<(string ItemCode, string WhsCode), decimal> Disponible { get; } = [];
    public bool Llamado { get; private set; }

    public Task<IReadOnlyList<WarehouseStockDto>> GetStockByItemAsync(string itemCode, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyDictionary<(string ItemCode, string WhsCode), decimal>> GetAvailableStockAsync(
        IReadOnlyList<(string ItemCode, string WhsCode)> pairs, CancellationToken ct = default)
    {
        Llamado = true;
        return Task.FromResult<IReadOnlyDictionary<(string ItemCode, string WhsCode), decimal>>(
            pairs.Where(Disponible.ContainsKey).ToDictionary(p => p, p => Disponible[p]));
    }
}

file sealed class PreciosFalso : IPriceListService
{
    public Dictionary<string, ItemPriceQuote> Lista { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ItemPriceQuote> Especiales { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<decimal?> GetPriceAsync(string itemCode, int priceList, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<string, decimal>> GetPricesAsync(IReadOnlyCollection<string> itemCodes, int priceList, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<PriceListOptionDto>> ListAllAsync(CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<string, ItemPriceQuote>> GetPriceQuotesAsync(IReadOnlyCollection<string> itemCodes, int priceList, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, ItemPriceQuote>>(itemCodes.Where(Lista.ContainsKey).ToDictionary(c => c, c => Lista[c]));

    public Task<IReadOnlyDictionary<string, ItemPriceQuote>> GetSpecialPriceQuotesAsync(string cardCode, IReadOnlyCollection<string> itemCodes, DateTime date, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, ItemPriceQuote>>(itemCodes.Where(Especiales.ContainsKey).ToDictionary(c => c, c => Especiales[c]));
}

public class DocumentLineValidationServiceTests
{
    private static DocumentLineValidationRequest Stock(params DocumentLineCheck[] lines) => new(lines, ValidateStock: true, ValidatePrice: false);
    private static DocumentLineValidationRequest Precio(params DocumentLineCheck[] lines) => new(lines, ValidateStock: false, ValidatePrice: true, CardCode: "C1", PriceList: 2);

    [Fact]
    public async Task Stock_SumaTodasLasLineasDelMismoArticuloYBodega()
    {
        var stock = new StockFalso();
        stock.Disponible[("A1", "B01")] = 5;
        var servicio = new DocumentLineValidationService(stock, new PreciosFalso());

        // 3 + 3 = 6 > 5: las DOS líneas se marcan, aunque cada una sola entraría.
        var issues = await servicio.ValidateAsync(Stock(new DocumentLineCheck(0, "A1", "B01", 3, null), new DocumentLineCheck(1, "a1", "b01", 3, null)));

        Assert.Equal([0, 1], issues.Select(i => i.Key));
        Assert.All(issues, i => Assert.Equal("Quantity", i.Field));
    }

    [Fact]
    public async Task Stock_Suficiente_OBodegasDistintas_NoMarca()
    {
        var stock = new StockFalso();
        stock.Disponible[("A1", "B01")] = 5;
        stock.Disponible[("A1", "B02")] = 1;
        var servicio = new DocumentLineValidationService(stock, new PreciosFalso());

        var issues = await servicio.ValidateAsync(Stock(new DocumentLineCheck(0, "A1", "B01", 5, null), new DocumentLineCheck(1, "A1", "B02", 1, null)));

        Assert.Empty(issues);
    }

    [Fact]
    public async Task Stock_SinRegistroEnBodega_EsDisponibleCero()
    {
        var servicio = new DocumentLineValidationService(new StockFalso(), new PreciosFalso());

        var issues = await servicio.ValidateAsync(Stock(new DocumentLineCheck(0, "A1", "B09", 1, null)));

        Assert.Single(issues);
    }

    [Fact]
    public async Task Precio_DistintoALaLista_Marca_IgualNoMarca()
    {
        var precios = new PreciosFalso();
        precios.Lista["A1"] = new ItemPriceQuote(1000m, "CLP");
        var servicio = new DocumentLineValidationService(new StockFalso(), precios);

        var issues = await servicio.ValidateAsync(Precio(new DocumentLineCheck(0, "A1", null, 1, 900m), new DocumentLineCheck(1, "A1", null, 1, 1000m)));

        var issue = Assert.Single(issues);
        Assert.Equal(0, issue.Key);
        Assert.Equal("UnitPrice", issue.Field);
    }

    [Fact]
    public async Task Precio_ElPrecioEspecialGanaALaLista()
    {
        var precios = new PreciosFalso();
        precios.Lista["A1"] = new ItemPriceQuote(1000m, null);
        precios.Especiales["A1"] = new ItemPriceQuote(800m, null);
        var servicio = new DocumentLineValidationService(new StockFalso(), precios);

        Assert.Empty(await servicio.ValidateAsync(Precio(new DocumentLineCheck(0, "A1", null, 1, 800m))));
        Assert.Single(await servicio.ValidateAsync(Precio(new DocumentLineCheck(0, "A1", null, 1, 1000m))));
    }

    [Fact]
    public async Task Precio_SinReferencia_SinPrecioOMonedaDistinta_NoSeValida()
    {
        var precios = new PreciosFalso();
        precios.Lista["A1"] = new ItemPriceQuote(10m, "USD");
        var servicio = new DocumentLineValidationService(new StockFalso(), precios);

        var issues = await servicio.ValidateAsync(Precio(
            new DocumentLineCheck(0, "SIN-LISTA", null, 1, 5m),
            new DocumentLineCheck(1, "A1", null, 1, null),
            new DocumentLineCheck(2, "A1", null, 1, 9000m, "CLP")));

        Assert.Empty(issues);
    }

    [Fact]
    public async Task SinFlags_NoConsultaNada()
    {
        var stock = new StockFalso();
        var servicio = new DocumentLineValidationService(stock, new PreciosFalso());

        var issues = await servicio.ValidateAsync(new DocumentLineValidationRequest([new(0, "A1", "B01", 99, 1m)], false, false));

        Assert.Empty(issues);
        Assert.False(stock.Llamado);
    }
}
