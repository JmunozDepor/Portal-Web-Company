using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using Modulo.AuditoriaInventario.Servicios;

namespace Modulo.AuditoriaInventario.Tests;

public class DiferenciaEngineTests
{
    private static AuditoriaInventarioDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<AuditoriaInventarioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task CalcularDiferenciasAsync_SobranteYFaltante_CalculaCantidadYMontoCorrectos()
    {
        using var db = CrearContexto();
        var companyId = Guid.NewGuid();

        var branch = new Branch { CompanyId = companyId, BranchCode = "001", Name = "Centro" };
        db.Branches.Add(branch);
        var sector = new InventorySector { CompanyId = companyId, Name = "Bodega" };
        db.InventorySectors.Add(sector);
        var user = new CaptureUser { CompanyId = companyId, Username = "a1", PasswordHash = "x", PasswordSalt = "x" };
        db.CaptureUsers.Add(user);
        await db.SaveChangesAsync();

        var sessionId = Guid.NewGuid();
        db.InventorySessions.Add(new InventorySession
        {
            Id = sessionId,
            CompanyId = companyId,
            BranchId = branch.Id,
            InventoryNumber = "INV-001",
            ResponsibleUserId = user.Id,
        });

        var snapshot = new FrozenInventorySnapshot
        {
            CompanyId = companyId,
            BranchId = branch.Id,
            InventoryNumber = "INV-001",
            LoadedByUserId = Guid.NewGuid(),
            FileName = "congelado.xlsx",
        };
        db.FrozenInventorySnapshots.Add(snapshot);
        await db.SaveChangesAsync();

        db.FrozenInventoryLines.Add(new FrozenInventoryLine { SnapshotId = snapshot.Id, Barcode = "AAA", Quantity = 10, UnitCost = 100m });
        db.FrozenInventoryLines.Add(new FrozenInventoryLine { SnapshotId = snapshot.Id, Barcode = "BBB", Quantity = 5, UnitCost = 50m });

        db.InventoryCaptures.Add(new InventoryCapture { Id = Guid.NewGuid(), SessionId = sessionId, SectorId = sector.Id, Barcode = "AAA", Quantity = 12, CapturedByUserId = user.Id });
        // "BBB" nunca se capturó -- debe salir como faltante total.
        await db.SaveChangesAsync();

        var engine = new DiferenciaEngine();
        var creadas = await engine.CalcularDiferenciasAsync(db, sessionId);

        Assert.Equal(2, creadas);

        var diffAAA = await db.InventoryDifferences.FirstAsync(d => d.Barcode == "AAA");
        Assert.Equal(12, diffAAA.CapturedQuantity);
        Assert.Equal(10, diffAAA.FrozenQuantity);
        Assert.Equal(2, diffAAA.QuantityDiff);
        Assert.Equal(200m, diffAAA.AmountDiff);
        Assert.Equal(sector.Id, diffAAA.SectorId);

        var diffBBB = await db.InventoryDifferences.FirstAsync(d => d.Barcode == "BBB");
        Assert.Equal(0, diffBBB.CapturedQuantity);
        Assert.Equal(5, diffBBB.FrozenQuantity);
        Assert.Equal(-5, diffBBB.QuantityDiff);
        Assert.Equal(-250m, diffBBB.AmountDiff);
        Assert.Null(diffBBB.SectorId);
    }

    [Fact]
    public async Task CalcularDiferenciasAsync_DiferenciaConAjusteExistente_NoLaBorraYRecalculaElResto()
    {
        // InventoryAdjustment.DifferenceId -> InventoryDifference.Id es
        // DeleteBehavior.Restrict contra una base real -- si RemoveRange intentara
        // borrar una diferencia que ya tiene un ajuste propuesto encima, Postgres/
        // SqlServer tirarían DbUpdateException. El InMemory provider no valida FKs,
        // así que este test no lo detectaría por sí solo -- lo que valida es que el
        // motor deliberadamente EXCLUYE esas filas del RemoveRange (ver
        // DiferenciaEngine.CalcularDiferenciasAsync) para que, llegado el día en que
        // exista una base real con esa constraint, jamás se le manden a borrar.
        using var db = CrearContexto();
        var companyId = Guid.NewGuid();

        var branch = new Branch { CompanyId = companyId, BranchCode = "001", Name = "Centro" };
        db.Branches.Add(branch);
        var sector = new InventorySector { CompanyId = companyId, Name = "Bodega" };
        db.InventorySectors.Add(sector);
        var user = new CaptureUser { CompanyId = companyId, Username = "a1", PasswordHash = "x", PasswordSalt = "x" };
        db.CaptureUsers.Add(user);
        await db.SaveChangesAsync();

        var sessionId = Guid.NewGuid();
        db.InventorySessions.Add(new InventorySession
        {
            Id = sessionId,
            CompanyId = companyId,
            BranchId = branch.Id,
            InventoryNumber = "INV-002",
            ResponsibleUserId = user.Id,
        });

        var snapshot = new FrozenInventorySnapshot
        {
            CompanyId = companyId,
            BranchId = branch.Id,
            InventoryNumber = "INV-002",
            LoadedByUserId = Guid.NewGuid(),
            FileName = "congelado.xlsx",
        };
        db.FrozenInventorySnapshots.Add(snapshot);
        await db.SaveChangesAsync();

        db.FrozenInventoryLines.Add(new FrozenInventoryLine { SnapshotId = snapshot.Id, Barcode = "AAA", Quantity = 10, UnitCost = 100m });
        db.FrozenInventoryLines.Add(new FrozenInventoryLine { SnapshotId = snapshot.Id, Barcode = "BBB", Quantity = 5, UnitCost = 50m });
        db.InventoryCaptures.Add(new InventoryCapture { Id = Guid.NewGuid(), SessionId = sessionId, SectorId = sector.Id, Barcode = "AAA", Quantity = 12, CapturedByUserId = user.Id });
        await db.SaveChangesAsync();

        var engine = new DiferenciaEngine();
        await engine.CalcularDiferenciasAsync(db, sessionId);

        var diffAAA = await db.InventoryDifferences.FirstAsync(d => d.Barcode == "AAA");
        var diffBBBOriginalId = (await db.InventoryDifferences.FirstAsync(d => d.Barcode == "BBB")).Id;

        db.InventoryAdjustments.Add(new InventoryAdjustment { DifferenceId = diffAAA.Id, ProposedByUserId = Guid.NewGuid() });
        await db.SaveChangesAsync();

        // El motor no excluye la diferencia AAA de "nuevas" (siempre recalcula desde
        // las capturas actuales) -- lo único que evita es DELETE sobre la fila que ya
        // tiene un ajuste encima. Entonces tras el segundo cálculo quedan: la fila AAA
        // original (preservada, con su ajuste), una fila AAA recalculada nueva, y una
        // fila BBB recalculada nueva (la BBB original sí se pudo borrar).
        var creadas = await engine.CalcularDiferenciasAsync(db, sessionId);

        Assert.Equal(2, creadas);

        var diffAAAOriginalTrasRecalculo = await db.InventoryDifferences.SingleOrDefaultAsync(d => d.Id == diffAAA.Id);
        Assert.NotNull(diffAAAOriginalTrasRecalculo);
        Assert.Equal("AAA", diffAAAOriginalTrasRecalculo!.Barcode);

        var diffBBBDespues = await db.InventoryDifferences.SingleAsync(d => d.Barcode == "BBB");
        Assert.NotEqual(diffBBBOriginalId, diffBBBDespues.Id);

        Assert.Equal(3, await db.InventoryDifferences.CountAsync(d => d.SessionId == sessionId));
        Assert.Equal(2, await db.InventoryDifferences.CountAsync(d => d.SessionId == sessionId && d.Barcode == "AAA"));
    }

    [Fact]
    public async Task CalcularDiferenciasAsync_SinCongeladoCargado_NoCreaNadaYDevuelveCero()
    {
        using var db = CrearContexto();
        var companyId = Guid.NewGuid();
        var branch = new Branch { CompanyId = companyId, BranchCode = "001", Name = "Centro" };
        db.Branches.Add(branch);
        var user = new CaptureUser { CompanyId = companyId, Username = "a1", PasswordHash = "x", PasswordSalt = "x" };
        db.CaptureUsers.Add(user);
        await db.SaveChangesAsync();

        var sessionId = Guid.NewGuid();
        db.InventorySessions.Add(new InventorySession
        {
            Id = sessionId,
            CompanyId = companyId,
            BranchId = branch.Id,
            InventoryNumber = "INV-SIN-CONGELADO",
            ResponsibleUserId = user.Id,
        });
        await db.SaveChangesAsync();

        var engine = new DiferenciaEngine();
        var creadas = await engine.CalcularDiferenciasAsync(db, sessionId);

        Assert.Equal(0, creadas);
        Assert.Empty(await db.InventoryDifferences.ToListAsync());
    }
}
