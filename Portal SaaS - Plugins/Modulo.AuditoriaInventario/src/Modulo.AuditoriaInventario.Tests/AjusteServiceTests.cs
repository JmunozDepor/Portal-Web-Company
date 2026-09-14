using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using Modulo.AuditoriaInventario.Servicios;

namespace Modulo.AuditoriaInventario.Tests;

public class AjusteServiceTests
{
    private static AuditoriaInventarioDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<AuditoriaInventarioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(AuditoriaInventarioDbContext Db, InventoryAdjustment Ajuste, Guid CompanyId)> PrepararEscenarioAsync(
        string? sapCompanyCode, string? sapWarehouseCode, string? sapMaterialCode)
    {
        var db = CrearContexto();
        var companyId = Guid.NewGuid();

        var branch = new Branch { CompanyId = companyId, BranchCode = "001", Name = "Centro", SapCompanyCode = sapCompanyCode, SapWarehouseCode = sapWarehouseCode };
        db.Branches.Add(branch);
        var user = new CaptureUser { CompanyId = companyId, Username = "a1", PasswordHash = "x", PasswordSalt = "x" };
        db.CaptureUsers.Add(user);
        await db.SaveChangesAsync();

        var sessionId = Guid.NewGuid();
        db.InventorySessions.Add(new InventorySession { Id = sessionId, CompanyId = companyId, BranchId = branch.Id, InventoryNumber = "INV-1", ResponsibleUserId = user.Id });

        var snapshot = new FrozenInventorySnapshot { CompanyId = companyId, BranchId = branch.Id, InventoryNumber = "INV-1", LoadedByUserId = Guid.NewGuid(), FileName = "x.xlsx" };
        db.FrozenInventorySnapshots.Add(snapshot);
        await db.SaveChangesAsync();

        if (sapMaterialCode is not null)
        {
            db.Products.Add(new Product { CompanyId = companyId, Barcode = "AAA", ProductCode = "SKU-1", SapMaterialCode = sapMaterialCode });
        }

        var diferencia = new InventoryDifference
        {
            SessionId = sessionId,
            SnapshotId = snapshot.Id,
            Barcode = "AAA",
            CapturedQuantity = 12,
            FrozenQuantity = 10,
            QuantityDiff = 2,
            AmountDiff = 200m,
        };
        db.InventoryDifferences.Add(diferencia);
        await db.SaveChangesAsync();

        var ajuste = new InventoryAdjustment { DifferenceId = diferencia.Id, ProposedByUserId = Guid.NewGuid() };
        db.InventoryAdjustments.Add(ajuste);
        await db.SaveChangesAsync();

        return (db, ajuste, companyId);
    }

    [Fact]
    public async Task AprobarAsync_ConMapeoSapCompleto_EncolaYCambiaEstado()
    {
        var (db, ajuste, companyId) = await PrepararEscenarioAsync("1000", "WH01", "MAT-AAA");
        var service = new AjusteService();
        var aprobadoPor = Guid.NewGuid();

        var resultado = await service.AprobarAsync(db, ajuste.Id, companyId, aprobadoPor);

        Assert.True(resultado.Exitoso);
        var recargado = await db.InventoryAdjustments.FirstAsync(a => a.Id == ajuste.Id);
        Assert.Equal("APPROVED", recargado.Status);
        Assert.Equal(aprobadoPor, recargado.ApprovedByUserId);

        var cola = await db.SapAdjustmentQueueItems.FirstAsync(q => q.AdjustmentId == ajuste.Id);
        Assert.Equal("1000", cola.SapCompanyCode);
        Assert.Equal("WH01", cola.SapWarehouseCode);
        Assert.Equal("MAT-AAA", cola.SapMaterialCode);
        Assert.Equal(2, cola.Quantity);
        Assert.Equal("READY", cola.Status);
    }

    [Fact]
    public async Task AprobarAsync_SinCodigoSapEnSucursal_RechazaSinCambiarEstado()
    {
        var (db, ajuste, companyId) = await PrepararEscenarioAsync(null, null, "MAT-AAA");
        var service = new AjusteService();

        var resultado = await service.AprobarAsync(db, ajuste.Id, companyId, Guid.NewGuid());

        Assert.False(resultado.Exitoso);
        var recargado = await db.InventoryAdjustments.FirstAsync(a => a.Id == ajuste.Id);
        Assert.Equal("PROPOSED", recargado.Status);
        Assert.Empty(await db.SapAdjustmentQueueItems.ToListAsync());
    }

    [Fact]
    public async Task AprobarAsync_SinProductoMapeadoASap_RechazaSinCambiarEstado()
    {
        var (db, ajuste, companyId) = await PrepararEscenarioAsync("1000", "WH01", null);
        var service = new AjusteService();

        var resultado = await service.AprobarAsync(db, ajuste.Id, companyId, Guid.NewGuid());

        Assert.False(resultado.Exitoso);
        Assert.Empty(await db.SapAdjustmentQueueItems.ToListAsync());
    }

    [Fact]
    public async Task AprobarAsync_AjusteDeOtraCompania_RechazaSinEncolar()
    {
        var (db, ajuste, _) = await PrepararEscenarioAsync("1000", "WH01", "MAT-AAA");
        var service = new AjusteService();
        var otraCompanyId = Guid.NewGuid();

        var resultado = await service.AprobarAsync(db, ajuste.Id, otraCompanyId, Guid.NewGuid());

        Assert.False(resultado.Exitoso);
        var recargado = await db.InventoryAdjustments.FirstAsync(a => a.Id == ajuste.Id);
        Assert.Equal("PROPOSED", recargado.Status);
        Assert.Empty(await db.SapAdjustmentQueueItems.ToListAsync());
    }
}
