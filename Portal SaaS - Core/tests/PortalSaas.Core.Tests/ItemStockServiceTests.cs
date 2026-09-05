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
