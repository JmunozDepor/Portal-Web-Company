using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Tests.Servicios;

public class UserCostCenterServiceTests
{
    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
    }

    private sealed class NotImplementedCostCenterCatalogService : ICostCenterCatalogService
    {
        public Task<IReadOnlyList<CostCenterDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<CostCenterDto>> ListByDimensionAsync(int dimCode, string? searchText = null, int? limit = null, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task CountAssignedByUserAsync_agrupa_en_una_sola_consulta_por_usuario_de_la_compania()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var userWithNone = Guid.NewGuid();

        await using var db = CreateDb(nameof(CountAssignedByUserAsync_agrupa_en_una_sola_consulta_por_usuario_de_la_compania));
        db.UserCostCenters.AddRange(
            new UserCostCenter { CompanyId = companyId, UserId = userA, CostCenterCode = "CC1" },
            new UserCostCenter { CompanyId = companyId, UserId = userA, CostCenterCode = "CC2" },
            new UserCostCenter { CompanyId = companyId, UserId = userB, CostCenterCode = "CC3" },
            new UserCostCenter { CompanyId = otherCompanyId, UserId = userA, CostCenterCode = "CC-OTRA-COMPANIA" });
        await db.SaveChangesAsync();

        var sut = new UserCostCenterService(db);

        var counts = await sut.CountAssignedByUserAsync(companyId);

        Assert.Equal(2, counts[userA]);
        Assert.Equal(1, counts[userB]);
        Assert.False(counts.ContainsKey(userWithNone));
    }

    [Fact]
    public async Task GetAvailableAsync_falls_back_to_local_active_cost_centers_when_user_has_no_assignments()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(nameof(GetAvailableAsync_falls_back_to_local_active_cost_centers_when_user_has_no_assignments));
        db.CostCenters.Add(new CostCenter { CompanyId = companyId, Code = "CC-A", Name = "Activo", IsActive = true, Source = CatalogEntrySource.Sap });
        db.CostCenters.Add(new CostCenter { CompanyId = companyId, Code = "CC-B", Name = "Inactivo", IsActive = false, Source = CatalogEntrySource.Sap });
        db.CostCenters.Add(new CostCenter { CompanyId = Guid.NewGuid(), Code = "CC-C", Name = "Otra compañía", IsActive = true, Source = CatalogEntrySource.Sap });
        await db.SaveChangesAsync();

        var sut = new UserCostCenterService(db);
        var result = await sut.GetAvailableAsync(companyId, userId, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("CC-A", result[0].Code);
    }
}
