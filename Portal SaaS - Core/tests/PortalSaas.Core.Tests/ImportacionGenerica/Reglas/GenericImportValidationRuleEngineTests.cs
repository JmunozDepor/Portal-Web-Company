using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica.Reglas;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica.Reglas;

public sealed class GenericImportValidationRuleEngineTests
{
    private static GenericImportRowDto Row(int rowNumber, decimal? quantity = 1, decimal? discountPercent = null) => new()
    {
        RowNumber = rowNumber,
        GroupingKey = "G1",
        IsValid = true,
        Quantity = quantity,
        DiscountPercent = discountPercent,
    };

    private static GenericImportRowDto RowWithPartner(int rowNumber, string cardCode, string? itemCode = null) => new()
    {
        RowNumber = rowNumber,
        GroupingKey = "G1",
        IsValid = true,
        BusinessPartnerCardCode = cardCode,
        ItemCode = itemCode,
    };

    // ---- Task 5: reglas estructurales fijas ---------------------------------------

    [Fact]
    public async Task PositiveQuantityRule_marca_cantidad_no_positiva()
    {
        var rule = new PositiveQuantityRule();
        var rows = new[] { Row(1, quantity: 1), Row(2, quantity: 0), Row(3, quantity: null) };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?>(), CancellationToken.None);

        Assert.False(result.ContainsKey(1));
        Assert.True(result.ContainsKey(2));
        Assert.True(result.ContainsKey(3));
    }

    [Fact]
    public async Task ValidDiscountPercentRule_marca_descuento_fuera_de_rango()
    {
        var rule = new ValidDiscountPercentRule();
        var rows = new[] { Row(1, discountPercent: 10), Row(2, discountPercent: -1), Row(3, discountPercent: 101) };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?>(), CancellationToken.None);

        Assert.False(result.ContainsKey(1));
        Assert.True(result.ContainsKey(2));
        Assert.True(result.ContainsKey(3));
    }

    // ---- Task 6: CustomerActiveInSap / ItemActiveInSap --------------------------

    [Fact]
    public async Task CustomerActiveInSapRule_marca_cliente_inactivo_en_Venta()
    {
        var rule = new CustomerActiveInSapRule(
            new FakeCustomerCatalogService(new Dictionary<string, bool> { ["C001"] = false, ["C002"] = true }),
            new FakeSupplierCatalogService(new Dictionary<string, bool>()));
        var rows = new[] { RowWithPartner(1, "C001"), RowWithPartner(2, "C002") };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?>(), CancellationToken.None);

        Assert.True(result.ContainsKey(1));
        Assert.False(result.ContainsKey(2));
    }

    [Fact]
    public async Task CustomerActiveInSapRule_usa_proveedores_en_Compra()
    {
        var rule = new CustomerActiveInSapRule(
            new FakeCustomerCatalogService(new Dictionary<string, bool>()),
            new FakeSupplierCatalogService(new Dictionary<string, bool> { ["P001"] = false }));
        var rows = new[] { RowWithPartner(1, "P001") };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Purchase, new Dictionary<string, object?>(), CancellationToken.None);

        Assert.True(result.ContainsKey(1));
    }

    [Fact]
    public async Task ItemActiveInSapRule_marca_articulo_inactivo()
    {
        var rule = new ItemActiveInSapRule(new FakeItemCatalogService(new Dictionary<string, bool> { ["I001"] = false, ["I002"] = true }));
        var rows = new[] { RowWithPartner(1, "C001", "I001"), RowWithPartner(2, "C001", "I002") };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?>(), CancellationToken.None);

        Assert.True(result.ContainsKey(1));
        Assert.False(result.ContainsKey(2));
    }

    // ---- Task 7: PriceVsFixedList / PriceVsCustomerList ------------------------

    private static GenericImportRowDto RowWithPrice(int rowNumber, string itemCode, decimal unitPrice, string cardCode = "C001") => new()
    {
        RowNumber = rowNumber,
        GroupingKey = "G1",
        IsValid = true,
        ItemCode = itemCode,
        UnitPrice = unitPrice,
        BusinessPartnerCardCode = cardCode,
    };

    [Fact]
    public async Task PriceVsFixedListRule_marca_precio_fuera_de_tolerancia()
    {
        var rule = new PriceVsFixedListRule(new FakePriceListService(new Dictionary<string, decimal> { ["I001"] = 100m }));
        var rows = new[] { RowWithPrice(1, "I001", 100m), RowWithPrice(2, "I001", 80m) };
        var parameters = new Dictionary<string, object?> { ["priceListNum"] = 1, ["tolerancePercent"] = 5m };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, parameters, CancellationToken.None);

        Assert.False(result.ContainsKey(1));
        Assert.True(result.ContainsKey(2));
    }

    [Fact]
    public async Task PriceVsCustomerListRule_usa_la_lista_asignada_al_cliente()
    {
        var rule = new PriceVsCustomerListRule(
            new FakePriceListService(new Dictionary<string, decimal> { ["I001"] = 200m }),
            new FakeBusinessPartnerDefaultsService(priceListCode: 2));
        var rows = new[] { RowWithPrice(1, "I001", 200m), RowWithPrice(2, "I001", 150m) };
        var parameters = new Dictionary<string, object?> { ["tolerancePercent"] = 0m };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, parameters, CancellationToken.None);

        Assert.False(result.ContainsKey(1));
        Assert.True(result.ContainsKey(2));
    }

    [Fact]
    public async Task PriceVsCustomerListRule_sin_lista_asignada_no_valida_nada()
    {
        var rule = new PriceVsCustomerListRule(
            new FakePriceListService(new Dictionary<string, decimal> { ["I001"] = 200m }),
            new FakeBusinessPartnerDefaultsService(priceListCode: null));
        var rows = new[] { RowWithPrice(1, "I001", 1m) };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?> { ["tolerancePercent"] = 0m }, CancellationToken.None);

        Assert.Empty(result);
    }

    // ---- Task 8: StockAvailable ----------------------------------------------

    private static GenericImportRowDto RowWithStock(int rowNumber, string itemCode, string warehouse, decimal quantity) => new()
    {
        RowNumber = rowNumber,
        GroupingKey = "G1",
        IsValid = true,
        ItemCode = itemCode,
        Warehouse = warehouse,
        Quantity = quantity,
    };

    [Fact]
    public async Task StockAvailableRule_suma_demanda_de_varios_documentos_del_mismo_archivo()
    {
        var rule = new StockAvailableRule(new FakeItemStockService(new Dictionary<(string, string), decimal> { [("I001", "01")] = 100m }));
        var rows = new[]
        {
            RowWithStock(1, "I001", "01", 60m),
            RowWithStock(2, "I001", "01", 60m), // suma 120 > 100 disponible
        };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?> { ["tolerancePercent"] = 0m }, CancellationToken.None);

        Assert.True(result.ContainsKey(1));
        Assert.True(result.ContainsKey(2));
    }

    [Fact]
    public async Task StockAvailableRule_valida_cada_bodega_de_forma_independiente()
    {
        var rule = new StockAvailableRule(new FakeItemStockService(new Dictionary<(string, string), decimal>
        {
            [("I001", "01")] = 10m,
            [("I001", "02")] = 100m,
        }));
        var rows = new[]
        {
            RowWithStock(1, "I001", "01", 50m), // no alcanza en 01
            RowWithStock(2, "I001", "02", 50m), // sí alcanza en 02
        };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?> { ["tolerancePercent"] = 0m }, CancellationToken.None);

        Assert.True(result.ContainsKey(1));
        Assert.False(result.ContainsKey(2));
    }

    [Fact]
    public async Task StockAvailableRule_respeta_la_tolerancia()
    {
        var rule = new StockAvailableRule(new FakeItemStockService(new Dictionary<(string, string), decimal> { [("I001", "01")] = 100m }));
        var rows = new[] { RowWithStock(1, "I001", "01", 105m) }; // 5% de más

        var conTolerancia = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?> { ["tolerancePercent"] = 10m }, CancellationToken.None);
        var sinTolerancia = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?> { ["tolerancePercent"] = 0m }, CancellationToken.None);

        Assert.False(conTolerancia.ContainsKey(1));
        Assert.True(sinTolerancia.ContainsKey(1));
    }

    [Fact]
    public async Task StockAvailableRule_usa_SourceWarehouse_en_Inventario()
    {
        var rule = new StockAvailableRule(new FakeItemStockService(new Dictionary<(string, string), decimal> { [("I001", "01")] = 10m }));
        var row = new GenericImportRowDto { RowNumber = 1, GroupingKey = "G1", IsValid = true, ItemCode = "I001", SourceWarehouse = "01", Quantity = 50m };

        var result = await rule.ValidateAsync([row], GenericImportModule.Inventory, new Dictionary<string, object?> { ["tolerancePercent"] = 0m }, CancellationToken.None);

        Assert.True(result.ContainsKey(1));
    }

    // ---- Task 9: CustomerBranchValid ----------------------------------------

    private static GenericImportRowDto RowWithBranch(int rowNumber, string cardCode, string branch) => new()
    {
        RowNumber = rowNumber,
        GroupingKey = "G1",
        IsValid = true,
        BusinessPartnerCardCode = cardCode,
        Branch = branch,
    };

    [Fact]
    public async Task CustomerBranchValidRule_marca_sucursal_que_no_pertenece_al_cliente()
    {
        var rule = new CustomerBranchValidRule(new FakeCustomerShipToAddressService(
            new Dictionary<string, IReadOnlyList<string>> { ["C001"] = ["SUC-CENTRAL", "SUC-NORTE"] }));
        var rows = new[] { RowWithBranch(1, "C001", "SUC-CENTRAL"), RowWithBranch(2, "C001", "SUC-INEXISTENTE") };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?>(), CancellationToken.None);

        Assert.False(result.ContainsKey(1));
        Assert.True(result.ContainsKey(2));
    }

    // ---- Fakes escritos a mano (este proyecto no usa Moq/NSubstitute) ----------

    private sealed class FakeCustomerCatalogService : ICustomerCatalogService
    {
        private readonly IReadOnlyDictionary<string, bool> _active;
        public FakeCustomerCatalogService(IReadOnlyDictionary<string, bool> active) => _active = active;
        public Task<IReadOnlyList<CustomerDto>> ListAsync(CustomerFilter? filter = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CustomerDto>>([]);
        public Task<CustomerDto?> GetAsync(string cardCode, CancellationToken ct = default) => Task.FromResult<CustomerDto?>(null);
        public Task<IReadOnlyDictionary<string, bool>> GetActiveStatusAsync(IReadOnlyList<string> cardCodes, CancellationToken ct = default) => Task.FromResult(_active);
    }

    private sealed class FakeSupplierCatalogService : ISupplierCatalogService
    {
        private readonly IReadOnlyDictionary<string, bool> _active;
        public FakeSupplierCatalogService(IReadOnlyDictionary<string, bool> active) => _active = active;
        public Task<IReadOnlyList<SupplierDto>> ListAsync(SupplierFilter? filter = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SupplierDto>>([]);
        public Task<SupplierDto?> GetAsync(string cardCode, CancellationToken ct = default) => Task.FromResult<SupplierDto?>(null);
        public Task<IReadOnlyDictionary<string, bool>> GetActiveStatusAsync(IReadOnlyList<string> cardCodes, CancellationToken ct = default) => Task.FromResult(_active);
    }

    private sealed class FakeItemCatalogService : IItemCatalogService
    {
        private readonly IReadOnlyDictionary<string, bool> _active;
        public FakeItemCatalogService(IReadOnlyDictionary<string, bool> active) => _active = active;
        public Task<IReadOnlyList<ItemDto>> SearchAsync(string text, int limit = 30, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ItemDto>>([]);
        public Task<IReadOnlyList<ItemDto>> GetByCodesAsync(IReadOnlyCollection<string> itemCodes, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ItemDto>>([]);
        public Task<IReadOnlyList<ItemDto>> GetTopAsync(int limit = 30, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ItemDto>>([]);
        public Task<IReadOnlyDictionary<string, bool>> GetActiveStatusAsync(IReadOnlyList<string> itemCodes, CancellationToken ct = default) => Task.FromResult(_active);
    }

    private sealed class FakePriceListService : IPriceListService
    {
        private readonly IReadOnlyDictionary<string, decimal> _prices;
        public FakePriceListService(IReadOnlyDictionary<string, decimal> prices) => _prices = prices;
        public Task<decimal?> GetPriceAsync(string itemCode, int priceList, CancellationToken ct = default) => Task.FromResult(_prices.TryGetValue(itemCode, out var p) ? p : (decimal?)null);
        public Task<IReadOnlyDictionary<string, decimal>> GetPricesAsync(IReadOnlyCollection<string> itemCodes, int priceList, CancellationToken ct = default) => Task.FromResult(_prices);
        public Task<IReadOnlyList<PriceListOptionDto>> ListAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PriceListOptionDto>>([]);
    }

    private sealed class FakeBusinessPartnerDefaultsService : IBusinessPartnerDefaultsService
    {
        private readonly int? _priceListCode;
        public FakeBusinessPartnerDefaultsService(int? priceListCode) => _priceListCode = priceListCode;
        public Task<BusinessPartnerDefaultsDto?> GetAsync(string cardCode, CancellationToken ct = default) =>
            Task.FromResult<BusinessPartnerDefaultsDto?>(new BusinessPartnerDefaultsDto { CardCode = cardCode, PriceListCode = _priceListCode });
    }

    private sealed class FakeCustomerShipToAddressService : ICustomerShipToAddressService
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _addressesByCardCode;
        public FakeCustomerShipToAddressService(IReadOnlyDictionary<string, IReadOnlyList<string>> addressesByCardCode) => _addressesByCardCode = addressesByCardCode;
        public Task<IReadOnlyList<string>> GetShipToAddressCodesAsync(string cardCode, CancellationToken ct = default) =>
            Task.FromResult(_addressesByCardCode.GetValueOrDefault(cardCode, []));
    }

    private sealed class FakeItemStockService : IItemStockService
    {
        private readonly IReadOnlyDictionary<(string, string), decimal> _available;
        public FakeItemStockService(IReadOnlyDictionary<(string, string), decimal> available) => _available = available;
        public Task<IReadOnlyList<WarehouseStockDto>> GetStockByItemAsync(string itemCode, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WarehouseStockDto>>([]);
        public Task<IReadOnlyDictionary<(string ItemCode, string WhsCode), decimal>> GetAvailableStockAsync(IReadOnlyList<(string ItemCode, string WhsCode)> pairs, CancellationToken ct = default)
        {
            var result = new Dictionary<(string ItemCode, string WhsCode), decimal>();
            foreach (var pair in pairs)
            {
                if (_available.TryGetValue((pair.ItemCode, pair.WhsCode), out var qty))
                {
                    result[pair] = qty;
                }
            }
            return Task.FromResult<IReadOnlyDictionary<(string ItemCode, string WhsCode), decimal>>(result);
        }
    }
}
