using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Catalogos;
using Xunit;

namespace PortalSaas.Core.Tests;

/// <summary>
/// IHanaService real está atado a la compañía SAP activa (una conexión por
/// Company.Id, ver ICurrentCompanyAccessor/SapConnectionProvider) -- estos
/// catálogos no reciben ni guardan un identificador de compañía propio, así que la
/// única forma de que "una compañía vea datos de otra" sería un catálogo con estado
/// compartido/estático entre instancias. Este fake simula dos compañías con dos
/// instancias de IHanaService completamente independientes (mismo criterio que
/// probaría un consumidor real vía DI Scoped, una instancia nueva por request) y
/// confirma que cada una devuelve únicamente sus propios datos -- sin importar el
/// orden de las llamadas ni que ambos servicios compartan el mismo tipo de
/// catálogo.
/// </summary>
file sealed class HanaServiceFalso : IHanaService
{
    private readonly IReadOnlyList<object> _filas;

    public HanaServiceFalso(IReadOnlyList<object> filas)
    {
        _filas = filas;
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sqlParametrizado, object? parametros = null, CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<T>)_filas.Cast<T>().ToList());

    public Task<int> ExecuteAsync(string sqlParametrizado, object? parametros = null, CancellationToken ct = default) =>
        throw new NotSupportedException("No usado por catálogos de solo lectura.");

    public Task<string> GetPlatformEngineTypeAsync(CancellationToken ct = default) => Task.FromResult("hana");
}

public class BranchAndSeriesCatalogServiceTests
{
    [Fact]
    public async Task BranchCatalogService_NuncaMezclaSucursalesDeOtraCompania()
    {
        var hanaCompaniaA = new HanaServiceFalso(new[]
        {
            new Abstractions.Modelos.BranchDto { BranchCode = 1, BranchName = "Sucursal A1" },
            new Abstractions.Modelos.BranchDto { BranchCode = 2, BranchName = "Sucursal A2" },
        });
        var hanaCompaniaB = new HanaServiceFalso(new[]
        {
            new Abstractions.Modelos.BranchDto { BranchCode = 1, BranchName = "Sucursal B1" },
        });

        var servicioA = new BranchCatalogService(hanaCompaniaA);
        var servicioB = new BranchCatalogService(hanaCompaniaB);

        var sucursalesA = await servicioA.ListAsync();
        var sucursalesB = await servicioB.ListAsync();

        Assert.Equal(2, sucursalesA.Count);
        Assert.Single(sucursalesB);
        Assert.DoesNotContain(sucursalesA, s => s.BranchName == "Sucursal B1");
        Assert.DoesNotContain(sucursalesB, s => s.BranchName.StartsWith("Sucursal A"));
    }

    [Fact]
    public async Task SeriesCatalogService_NuncaMezclaSeriesDeOtraCompania()
    {
        var hanaCompaniaA = new HanaServiceFalso(new[]
        {
            new Abstractions.Modelos.SeriesDto { SeriesCode = 10, SeriesName = "Serie Principal A" },
        });
        var hanaCompaniaB = new HanaServiceFalso(new[]
        {
            new Abstractions.Modelos.SeriesDto { SeriesCode = 20, SeriesName = "Serie Principal B" },
            new Abstractions.Modelos.SeriesDto { SeriesCode = 21, SeriesName = "Serie Sucursal B" },
        });

        var servicioA = new SeriesCatalogService(hanaCompaniaA);
        var servicioB = new SeriesCatalogService(hanaCompaniaB);

        var seriesA = await servicioA.ListAsync("17");
        var seriesB = await servicioB.ListAsync("17");

        Assert.Single(seriesA);
        Assert.Equal(2, seriesB.Count);
        Assert.DoesNotContain(seriesA, s => s.SeriesName.Contains("B"));
        Assert.DoesNotContain(seriesB, s => s.SeriesName.Contains("Principal A"));
    }
}
