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
