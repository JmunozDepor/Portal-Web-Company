using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Tests.Servicios;

public class AmountFormatServiceTests
{
    private static readonly Guid Company = Guid.NewGuid();

    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
    }

    private sealed class StubCompanyAccessor : ICurrentCompanyAccessor
    {
        public Guid CompanyId => Company;
        public string Code => "TEST";
        public string Database => "test";
        public string ServiceLayerUrl => "";
        public string Country => "CL";
        public bool HasCompany => true;
    }

    [Fact]
    public async Task Defaults_to_no_decimals_when_there_is_no_settings_row()
    {
        await using var db = CreateDb(nameof(Defaults_to_no_decimals_when_there_is_no_settings_row));
        var sut = new AmountFormatService(db, new StubCompanyAccessor());

        Assert.False(await sut.ShowDecimalsAsync());
        Assert.Equal("297.024", await sut.FormatAsync(297024m));
    }

    [Fact]
    public async Task Respects_the_company_setting_when_decimals_are_enabled()
    {
        await using var db = CreateDb(nameof(Respects_the_company_setting_when_decimals_are_enabled));
        db.RendicionesSettings.Add(new RendicionesSettings { CompanyId = Company, ShowAmountDecimals = true });
        await db.SaveChangesAsync();

        var sut = new AmountFormatService(db, new StubCompanyAccessor());

        Assert.True(await sut.ShowDecimalsAsync());
        Assert.Equal("297.024,00", await sut.FormatAsync(297024m));
    }

    [Fact]
    public async Task Rounds_when_decimals_are_disabled()
    {
        await using var db = CreateDb(nameof(Rounds_when_decimals_are_disabled));
        db.RendicionesSettings.Add(new RendicionesSettings { CompanyId = Company, ShowAmountDecimals = false });
        await db.SaveChangesAsync();

        var sut = new AmountFormatService(db, new StubCompanyAccessor());

        Assert.Equal("297.024", await sut.FormatAsync(297024.49m));
        Assert.Equal("297.025", await sut.FormatAsync(297024.5m));
    }

    [Fact]
    public async Task Caches_the_preference_for_the_lifetime_of_the_instance()
    {
        await using var db = CreateDb(nameof(Caches_the_preference_for_the_lifetime_of_the_instance));
        db.RendicionesSettings.Add(new RendicionesSettings { CompanyId = Company, ShowAmountDecimals = true });
        await db.SaveChangesAsync();

        var sut = new AmountFormatService(db, new StubCompanyAccessor());
        Assert.True(await sut.ShowDecimalsAsync());

        // Cambiar la fila por fuera no debe afectar a esta instancia -- es Scoped, vive
        // un solo request, y ese request no debe ver un flip a mitad de camino.
        var settings = await db.RendicionesSettings.FirstAsync(x => x.CompanyId == Company);
        settings.ShowAmountDecimals = false;
        await db.SaveChangesAsync();

        Assert.True(await sut.ShowDecimalsAsync());
    }
}
