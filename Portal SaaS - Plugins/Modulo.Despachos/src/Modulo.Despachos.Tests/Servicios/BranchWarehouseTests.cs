using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Bodegas sucursal: las únicas bodegas destino cuyas transferencias llevan guía de traslado (Fase B).</summary>
public class BranchWarehouseTests
{
    private static (MasterDataService Svc, Data.DespachosDbContext Db, FakeCurrentCompanyAccessor Company) Build()
    {
        var db = TestDb.Create();
        var company = new FakeCurrentCompanyAccessor();
        return (new MasterDataService(db, company, new FakeCurrentUserContext()), db, company);
    }

    [Fact]
    public async Task BranchWarehouse_CrearEditarListarYEliminar()
    {
        var (svc, _, _) = Build();
        var id = await svc.SaveBranchWarehouseAsync(new BranchWarehouse { WarehouseCode = " tda01 ", Name = "Tienda Estado" });
        await svc.SaveBranchWarehouseAsync(new BranchWarehouse { Id = id, WarehouseCode = "TDA01", Name = "Tienda Estado 387", Address = "Estado 387" });

        var row = Assert.Single(await svc.ListBranchWarehousesAsync());
        Assert.Equal(("TDA01", "Tienda Estado 387", "Estado 387"), (row.WarehouseCode, row.Name, row.Address));
        Assert.Equal(id, (await svc.FindBranchWarehouseAsync("tda01"))?.Id);

        Assert.True((await svc.DeleteBranchWarehouseAsync(id)).Deleted);
        Assert.Empty(await svc.ListBranchWarehousesAsync(includeInactive: true));
    }

    [Fact]
    public async Task BranchWarehouse_CodigoDuplicado_Rechaza()
    {
        var (svc, _, _) = Build();
        await svc.SaveBranchWarehouseAsync(new BranchWarehouse { WarehouseCode = "TDA01", Name = "A" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SaveBranchWarehouseAsync(new BranchWarehouse { WarehouseCode = "tda01 ", Name = "B" }));
        Assert.Contains("Ya existe", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SaveBranchWarehouseAsync(new BranchWarehouse { WarehouseCode = " ", Name = "B" }));
    }

    [Fact]
    public async Task BranchWarehouse_Inactiva_NoSeListaPorDefecto()
    {
        var (svc, _, _) = Build();
        await svc.SaveBranchWarehouseAsync(new BranchWarehouse { WarehouseCode = "TDA01", Name = "A", IsActive = false });

        Assert.Empty(await svc.ListBranchWarehousesAsync());
        Assert.Single(await svc.ListBranchWarehousesAsync(includeInactive: true));
        Assert.NotNull(await svc.FindBranchWarehouseAsync("TDA01"));
    }

    [Fact]
    public async Task BranchWarehouse_UsadaEnManifiesto_NoSeElimina()
    {
        var (svc, db, company) = Build();
        var id = await svc.SaveBranchWarehouseAsync(new BranchWarehouse { WarehouseCode = "TDA01", Name = "A" });
        db.ManifestDocuments.Add(new ManifestDocument
        {
            Id = Guid.NewGuid(), CompanyId = company.CompanyId, ManifestId = Guid.NewGuid(),
            DocumentKind = DocumentKinds.Transfer, CustomerCode = "TDA01", CustomerName = "A",
        });
        await db.SaveChangesAsync();

        var r = await svc.DeleteBranchWarehouseAsync(id);
        Assert.False(r.Deleted);
        Assert.Contains("desactívela", r.BlockedReason);
    }
}
