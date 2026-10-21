using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;

namespace Modulo.Despachos.Tests.Servicios;

public class CommuneSeederTests
{
    [Fact]
    public async Task CommuneSeeder_Inserta346Comunas_YEsIdempotente()
    {
        var db = TestDb.Create();
        var companyId = Guid.NewGuid();

        await CommuneSeeder.EnsureSeededAsync(db, companyId);
        await CommuneSeeder.EnsureSeededAsync(db, companyId);

        Assert.Equal(346, await db.Communes.CountAsync(c => c.CompanyId == companyId));
        Assert.Equal(16, await db.Communes.Select(c => c.RegionCode).Distinct().CountAsync());
    }

    [Fact]
    public async Task CommuneSeeder_SembrarOtraCompania_NoAfectaALaPrimera()
    {
        var db = TestDb.Create();
        await CommuneSeeder.EnsureSeededAsync(db, Guid.NewGuid());
        await CommuneSeeder.EnsureSeededAsync(db, Guid.NewGuid());
        Assert.Equal(692, await db.Communes.CountAsync());
    }

    [Theory]
    [InlineData("nunoa", "Ñuñoa")]
    [InlineData("NUÑOA", "Ñuñoa")]
    [InlineData("Concepcion", "Concepción")]
    [InlineData("  vina del mar ", "Viña del Mar")]
    public async Task FindCommuneByName_IgnoraTildesYMayusculas(string search, string expected)
    {
        var db = TestDb.Create();
        var company = new FakeCurrentCompanyAccessor();
        await CommuneSeeder.EnsureSeededAsync(db, company.CompanyId);
        var svc = new MasterDataService(db, company, new FakeCurrentUserContext());

        var found = await svc.FindCommuneByNameAsync(search);

        Assert.Equal(expected, found?.Name);
    }

    [Fact]
    public async Task FindCommuneByName_NoExiste_DevuelveNull()
    {
        var db = TestDb.Create();
        var company = new FakeCurrentCompanyAccessor();
        await CommuneSeeder.EnsureSeededAsync(db, company.CompanyId);
        var svc = new MasterDataService(db, company, new FakeCurrentUserContext());

        Assert.Null(await svc.FindCommuneByNameAsync("Atlantida"));
        Assert.Null(await svc.FindCommuneByNameAsync(""));
    }
}
