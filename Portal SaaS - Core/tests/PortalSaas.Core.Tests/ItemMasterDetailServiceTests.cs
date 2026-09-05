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
