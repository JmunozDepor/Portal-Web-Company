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

    [Fact]
    public async Task SyncCostCentersAsync_flips_manual_row_to_sap_when_code_collides()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SyncCostCentersAsync_flips_manual_row_to_sap_when_code_collides));
        db.CostCenters.Add(new CostCenter { CompanyId = companyId, Code = "CC-1", Name = "Centro manual", IsActive = true, Source = CatalogEntrySource.Manual });
        await db.SaveChangesAsync();

        var sapItems = new List<CostCenterDto> { new() { Code = "CC-1", Name = "Centro manual" } };
        var sut = new SapCatalogSyncProvider(db, new FakeCostCenterCatalogService(sapItems), new FakeGlAccountCatalogService(new List<GeneralLedgerAccountDto>()));

        var result = await sut.SyncCostCentersAsync(companyId, CancellationToken.None);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);

        var updated = await db.CostCenters.FirstAsync(c => c.Code == "CC-1" && c.CompanyId == companyId);
        Assert.Equal(CatalogEntrySource.Sap, updated.Source);
    }

    [Fact]
    public async Task SyncCostCentersAsync_empty_sap_response_deactivates_all_existing_rows()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SyncCostCentersAsync_empty_sap_response_deactivates_all_existing_rows));
        db.CostCenters.Add(new CostCenter { CompanyId = companyId, Code = "CC-1", Name = "Centro 1", IsActive = true, Source = CatalogEntrySource.Sap });
        db.CostCenters.Add(new CostCenter { CompanyId = companyId, Code = "CC-2", Name = "Centro 2", IsActive = true, Source = CatalogEntrySource.Sap });
        await db.SaveChangesAsync();

        var sut = new SapCatalogSyncProvider(db, new FakeCostCenterCatalogService(new List<CostCenterDto>()), new FakeGlAccountCatalogService(new List<GeneralLedgerAccountDto>()));

        var result = await sut.SyncCostCentersAsync(companyId, CancellationToken.None);

        Assert.Equal(0, result.Created);
        Assert.Equal(2, result.Deactivated);
        Assert.All(await db.CostCenters.Where(c => c.CompanyId == companyId).ToListAsync(), c => Assert.False(c.IsActive));
    }

    [Fact]
    public async Task SyncCostCentersAsync_skips_duplicate_code_in_sap_response_and_warns()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SyncCostCentersAsync_skips_duplicate_code_in_sap_response_and_warns));

        var sapItems = new List<CostCenterDto>
        {
            new() { Code = "CC-1", Name = "Primero" },
            new() { Code = "CC-1", Name = "Duplicado" },
        };
        var sut = new SapCatalogSyncProvider(db, new FakeCostCenterCatalogService(sapItems), new FakeGlAccountCatalogService(new List<GeneralLedgerAccountDto>()));

        var result = await sut.SyncCostCentersAsync(companyId, CancellationToken.None);

        Assert.Equal(1, result.Created);
        Assert.Single(result.Warnings);
        Assert.Contains("duplicado", result.Warnings[0]);

        var rows = await db.CostCenters.Where(c => c.CompanyId == companyId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("Primero", rows[0].Name);
    }

    [Fact]
    public async Task SyncCostCentersAsync_skips_items_with_blank_code_or_name_and_warns()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SyncCostCentersAsync_skips_items_with_blank_code_or_name_and_warns));

        var sapItems = new List<CostCenterDto>
        {
            new() { Code = "  ", Name = "Sin código" },
            new() { Code = "CC-1", Name = " " },
            new() { Code = "CC-2", Name = "Válido" },
        };
        var sut = new SapCatalogSyncProvider(db, new FakeCostCenterCatalogService(sapItems), new FakeGlAccountCatalogService(new List<GeneralLedgerAccountDto>()));

        var result = await sut.SyncCostCentersAsync(companyId, CancellationToken.None);

        Assert.Equal(1, result.Created);
        Assert.Equal(2, result.Warnings.Count);

        var rows = await db.CostCenters.Where(c => c.CompanyId == companyId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("CC-2", rows[0].Code);
    }

    [Fact]
    public async Task SyncGlAccountsAsync_creates_new_and_deactivates_missing()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SyncGlAccountsAsync_creates_new_and_deactivates_missing));
        db.GlAccounts.Add(new GlAccount { CompanyId = companyId, Code = "GL-OLD", Name = "Ya no existe en SAP", IsActive = true, Source = CatalogEntrySource.Sap });
        await db.SaveChangesAsync();

        var sapItems = new List<GeneralLedgerAccountDto> { new() { AccountCode = "GL-NEW", AccountName = "Cuenta nueva" } };
        var sut = new SapCatalogSyncProvider(db, new FakeCostCenterCatalogService(new List<CostCenterDto>()), new FakeGlAccountCatalogService(sapItems));

        var result = await sut.SyncGlAccountsAsync(companyId, CancellationToken.None);

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal(1, result.Deactivated);

        var created = await db.GlAccounts.FirstAsync(g => g.Code == "GL-NEW" && g.CompanyId == companyId);
        Assert.Equal(CatalogEntrySource.Sap, created.Source);

        var deactivated = await db.GlAccounts.FirstAsync(g => g.Code == "GL-OLD" && g.CompanyId == companyId);
        Assert.False(deactivated.IsActive);
    }

    [Fact]
    public async Task SyncGlAccountsAsync_flips_manual_row_to_sap_when_code_collides()
    {
        var companyId = Guid.NewGuid();
        await using var db = CreateDb(nameof(SyncGlAccountsAsync_flips_manual_row_to_sap_when_code_collides));
        db.GlAccounts.Add(new GlAccount { CompanyId = companyId, Code = "GL-1", Name = "Cuenta manual", IsActive = true, Source = CatalogEntrySource.Manual });
        await db.SaveChangesAsync();

        var sapItems = new List<GeneralLedgerAccountDto> { new() { AccountCode = "GL-1", AccountName = "Cuenta manual" } };
        var sut = new SapCatalogSyncProvider(db, new FakeCostCenterCatalogService(new List<CostCenterDto>()), new FakeGlAccountCatalogService(sapItems));

        var result = await sut.SyncGlAccountsAsync(companyId, CancellationToken.None);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);

        var updated = await db.GlAccounts.FirstAsync(g => g.Code == "GL-1" && g.CompanyId == companyId);
        Assert.Equal(CatalogEntrySource.Sap, updated.Source);
    }
}
