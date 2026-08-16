using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using Xunit;

namespace Modulo.Rendiciones.Tests.Servicios;

public class SapCatalogSyncProviderTests
{
    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
    }

    private sealed class FakeCostCenterCatalogService : ICostCenterCatalogService
    {
        private readonly IReadOnlyList<CostCenterDto> _items;
        public FakeCostCenterCatalogService(IReadOnlyList<CostCenterDto> items) => _items = items;

        public Task<IReadOnlyList<CostCenterDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default)
            => Task.FromResult(_items);

        public Task<IReadOnlyList<CostCenterDto>> ListByDimensionAsync(int dimCode, string? searchText = null, int? limit = null, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeGlAccountCatalogService : IGeneralLedgerAccountCatalogService
    {
        private readonly IReadOnlyList<GeneralLedgerAccountDto> _items;
        public FakeGlAccountCatalogService(IReadOnlyList<GeneralLedgerAccountDto> items) => _items = items;

        public Task<IReadOnlyList<GeneralLedgerAccountDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default)
            => Task.FromResult(_items);
    }

    [Fact]
    public async Task SyncCostCentersAsync_creates_new_and_deactivates_missing()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SyncCostCentersAsync_creates_new_and_deactivates_missing));
        db.CostCenters.Add(new CostCenter { CompanyId = companyId, Code = "CC-OLD", Name = "Ya no existe en SAP", IsActive = true, Source = CatalogEntrySource.Sap });
        await db.SaveChangesAsync();

        var sapItems = new List<CostCenterDto> { new() { Code = "CC-NEW", Name = "Centro nuevo" } };
        var sut = new SapCatalogSyncProvider(db, new FakeCostCenterCatalogService(sapItems), new FakeGlAccountCatalogService(new List<GeneralLedgerAccountDto>()));

        var result = await sut.SyncCostCentersAsync(companyId, CancellationToken.None);

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal(1, result.Deactivated);

        var created = await db.CostCenters.FirstAsync(c => c.Code == "CC-NEW" && c.CompanyId == companyId);
        Assert.Equal(CatalogEntrySource.Sap, created.Source);

        var deactivated = await db.CostCenters.FirstAsync(c => c.Code == "CC-OLD" && c.CompanyId == companyId);
        Assert.False(deactivated.IsActive);
    }

    [Fact]
    public async Task SyncCostCentersAsync_updates_name_of_existing_row_and_leaves_manual_rows_from_other_companies_untouched()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SyncCostCentersAsync_updates_name_of_existing_row_and_leaves_manual_rows_from_other_companies_untouched));
        db.CostCenters.Add(new CostCenter { CompanyId = companyId, Code = "CC-1", Name = "Nombre viejo", IsActive = true, Source = CatalogEntrySource.Sap });
        db.CostCenters.Add(new CostCenter { CompanyId = otherCompanyId, Code = "CC-1", Name = "No debe tocarse", IsActive = true, Source = CatalogEntrySource.Manual });
        await db.SaveChangesAsync();

        var sapItems = new List<CostCenterDto> { new() { Code = "CC-1", Name = "Nombre nuevo" } };
        var sut = new SapCatalogSyncProvider(db, new FakeCostCenterCatalogService(sapItems), new FakeGlAccountCatalogService(new List<GeneralLedgerAccountDto>()));

        var result = await sut.SyncCostCentersAsync(companyId, CancellationToken.None);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);

        var updated = await db.CostCenters.FirstAsync(c => c.Code == "CC-1" && c.CompanyId == companyId);
        Assert.Equal("Nombre nuevo", updated.Name);

        var untouched = await db.CostCenters.FirstAsync(c => c.CompanyId == otherCompanyId);
        Assert.Equal("No debe tocarse", untouched.Name);
    }
}
