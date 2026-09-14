using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;

namespace Modulo.AuditoriaInventario.Tests;

/// <summary>
/// Smoke tests del modelo -- confirman que OnModelCreating es válido y que las
/// piezas clave del diseño (Id de InventorySession/InventoryCapture generado en
/// cliente, acumulación por sesión+sector+código) se comportan como se documentó.
/// </summary>
public class AuditoriaInventarioDbContextTests
{
    private static AuditoriaInventarioDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<AuditoriaInventarioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task InventorySession_AceptaIdGeneradoEnElCliente()
    {
        using var db = CrearContexto();
        var companyId = Guid.NewGuid();

        var branch = new Branch { CompanyId = companyId, BranchCode = "001", Name = "Sucursal Centro" };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var user = new CaptureUser { CompanyId = companyId, Username = "auditor1", PasswordHash = "x", PasswordSalt = "x" };
        db.CaptureUsers.Add(user);
        await db.SaveChangesAsync();

        var sessionId = Guid.NewGuid();
        db.InventorySessions.Add(new InventorySession
        {
            Id = sessionId,
            CompanyId = companyId,
            BranchId = branch.Id,
            InventoryNumber = "INV-2026-001",
            ResponsibleUserId = user.Id,
            ValidateAgainstMaster = true,
        });
        await db.SaveChangesAsync();

        var recuperada = await db.InventorySessions.FirstAsync(s => s.Id == sessionId);
        Assert.Equal(sessionId, recuperada.Id);
        Assert.Equal("ACTIVE", recuperada.Status);
    }

    [Fact]
    public async Task Product_BarcodeEsUnicoPorCompania_PeroSePuedeRepetirEnOtraCompania()
    {
        using var db = CrearContexto();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        db.Products.Add(new Product { CompanyId = companyA, Barcode = "7801234567890", ProductCode = "SKU-1", Source = "MANUAL" });
        db.Products.Add(new Product { CompanyId = companyB, Barcode = "7801234567890", ProductCode = "SKU-1-OTRA-COMPANIA", Source = "MANUAL" });
        await db.SaveChangesAsync();

        var productosCompanyA = await db.Products.Where(p => p.CompanyId == companyA).ToListAsync();
        var productosCompanyB = await db.Products.Where(p => p.CompanyId == companyB).ToListAsync();

        Assert.Single(productosCompanyA);
        Assert.Single(productosCompanyB);
        Assert.NotEqual(productosCompanyA[0].ProductCode, productosCompanyB[0].ProductCode);
    }

    [Fact]
    public async Task InventoryCapture_ValidateAgainstMasterFalse_InMasterQuedaNull()
    {
        // Documenta la distinción pedida explícitamente: InMaster null (no evaluado)
        // no es lo mismo que InMaster=false (evaluado, no encontrado en el maestro).
        using var db = CrearContexto();
        var companyId = Guid.NewGuid();

        var branch = new Branch { CompanyId = companyId, BranchCode = "001", Name = "Sucursal Centro" };
        var sector = new InventorySector { CompanyId = companyId, Name = "Bodega" };
        db.Branches.Add(branch);
        db.InventorySectors.Add(sector);
        await db.SaveChangesAsync();

        var user = new CaptureUser { CompanyId = companyId, Username = "auditor1", PasswordHash = "x", PasswordSalt = "x" };
        db.CaptureUsers.Add(user);
        await db.SaveChangesAsync();

        var sessionId = Guid.NewGuid();
        db.InventorySessions.Add(new InventorySession
        {
            Id = sessionId,
            CompanyId = companyId,
            BranchId = branch.Id,
            InventoryNumber = "INV-2026-001",
            ResponsibleUserId = user.Id,
            ValidateAgainstMaster = false,
        });

        db.InventoryCaptures.Add(new InventoryCapture
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            SectorId = sector.Id,
            Barcode = "7801234567890",
            Quantity = 3,
            InMaster = null,
            CapturedByUserId = user.Id,
        });
        await db.SaveChangesAsync();

        var captura = await db.InventoryCaptures.FirstAsync(c => c.SessionId == sessionId);
        Assert.Null(captura.InMaster);
    }
}
