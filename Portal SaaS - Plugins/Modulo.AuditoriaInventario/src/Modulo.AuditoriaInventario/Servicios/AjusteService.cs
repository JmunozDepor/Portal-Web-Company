using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;

namespace Modulo.AuditoriaInventario.Servicios;

public sealed class AjusteService : IAjusteService
{
    public async Task<AprobarAjusteResultado> AprobarAsync(AuditoriaInventarioDbContext db, long adjustmentId, Guid approvedByUserId, CancellationToken ct = default)
    {
        var ajuste = await db.InventoryAdjustments.FirstOrDefaultAsync(a => a.Id == adjustmentId, ct);
        if (ajuste is null || ajuste.Status != "PROPOSED")
        {
            return new AprobarAjusteResultado(false, "El ajuste ya no está disponible para aprobar.");
        }

        var diferencia = await db.InventoryDifferences.FirstOrDefaultAsync(d => d.Id == ajuste.DifferenceId, ct);
        if (diferencia is null)
        {
            return new AprobarAjusteResultado(false, "La diferencia asociada a este ajuste ya no existe.");
        }

        var sesion = await db.InventorySessions.FirstOrDefaultAsync(s => s.Id == diferencia.SessionId, ct);
        if (sesion is null)
        {
            return new AprobarAjusteResultado(false, "La sesión asociada a esta diferencia ya no existe.");
        }

        var sucursal = await db.Branches.FirstOrDefaultAsync(b => b.Id == sesion.BranchId, ct);
        if (sucursal is null || string.IsNullOrWhiteSpace(sucursal.SapCompanyCode) || string.IsNullOrWhiteSpace(sucursal.SapWarehouseCode))
        {
            return new AprobarAjusteResultado(false, "La sucursal no tiene código SAP configurado -- completalo en el catálogo de sucursales antes de aprobar.");
        }

        var producto = await db.Products.FirstOrDefaultAsync(p => p.CompanyId == sesion.CompanyId && p.Barcode == diferencia.Barcode, ct);
        if (producto is null || string.IsNullOrWhiteSpace(producto.SapMaterialCode))
        {
            return new AprobarAjusteResultado(false, $"El código de barra '{diferencia.Barcode}' no tiene material SAP mapeado -- completalo en el maestro de productos antes de aprobar.");
        }

        ajuste.Status = "APPROVED";
        ajuste.ApprovedAt = DateTimeOffset.UtcNow;
        ajuste.ApprovedByUserId = approvedByUserId;

        db.SapAdjustmentQueueItems.Add(new SapAdjustmentQueueItem
        {
            AdjustmentId = ajuste.Id,
            SapCompanyCode = sucursal.SapCompanyCode,
            SapWarehouseCode = sucursal.SapWarehouseCode,
            SapMaterialCode = producto.SapMaterialCode,
            Quantity = diferencia.QuantityDiff,
            Status = "READY",
        });

        await db.SaveChangesAsync(ct);
        return new AprobarAjusteResultado(true, "Ajuste aprobado y encolado hacia SAP.");
    }
}
