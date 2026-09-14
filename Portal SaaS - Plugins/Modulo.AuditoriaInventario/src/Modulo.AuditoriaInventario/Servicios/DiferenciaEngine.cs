using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;

namespace Modulo.AuditoriaInventario.Servicios;

public sealed class DiferenciaEngine : IDiferenciaEngine
{
    public async Task<int> CalcularDiferenciasAsync(AuditoriaInventarioDbContext db, Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.InventorySessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException($"La sesión '{sessionId}' no existe.");

        var snapshot = await db.FrozenInventorySnapshots
            .Where(s => s.CompanyId == session.CompanyId && s.BranchId == session.BranchId && s.InventoryNumber == session.InventoryNumber)
            .OrderByDescending(s => s.LoadedAt)
            .FirstOrDefaultAsync(ct);

        if (snapshot is null)
        {
            // Sin congelado cargado todavía para esa sucursal/número de inventario --
            // nada contra qué comparar. La sesión queda cerrada igual.
            return 0;
        }

        var lineasCongeladas = await db.FrozenInventoryLines
            .Where(l => l.SnapshotId == snapshot.Id)
            .ToListAsync(ct);

        var congeladoPorBarcode = lineasCongeladas
            .GroupBy(l => l.Barcode)
            .ToDictionary(
                g => g.Key,
                g => (Quantity: g.Sum(x => x.Quantity), UnitCost: g.Select(x => x.UnitCost).FirstOrDefault(c => c.HasValue) ?? 0m));

        var capturasPorSectorYBarcode = await db.InventoryCaptures
            .Where(c => c.SessionId == sessionId)
            .GroupBy(c => new { c.SectorId, c.Barcode })
            .Select(g => new { g.Key.SectorId, g.Key.Barcode, Quantity = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);

        var existentes = await db.InventoryDifferences.Where(d => d.SessionId == sessionId).ToListAsync(ct);
        db.InventoryDifferences.RemoveRange(existentes);

        var ahora = DateTimeOffset.UtcNow;
        var nuevas = new List<InventoryDifference>();
        var barcodesCapturados = new HashSet<string>();

        foreach (var g in capturasPorSectorYBarcode)
        {
            barcodesCapturados.Add(g.Barcode);
            congeladoPorBarcode.TryGetValue(g.Barcode, out var congelado);
            var quantityDiff = g.Quantity - congelado.Quantity;

            nuevas.Add(new InventoryDifference
            {
                SessionId = sessionId,
                SnapshotId = snapshot.Id,
                SectorId = g.SectorId,
                Barcode = g.Barcode,
                CapturedQuantity = g.Quantity,
                FrozenQuantity = congelado.Quantity,
                QuantityDiff = quantityDiff,
                AmountDiff = quantityDiff * congelado.UnitCost,
                CalculatedAt = ahora,
            });
        }

        // Códigos que estaban en el congelado pero no se capturaron en ninguna sesión --
        // faltante total, sin sector asociado (no se escaneó en ningún lado).
        foreach (var (barcode, congelado) in congeladoPorBarcode)
        {
            if (barcodesCapturados.Contains(barcode))
            {
                continue;
            }

            var quantityDiff = -congelado.Quantity;
            nuevas.Add(new InventoryDifference
            {
                SessionId = sessionId,
                SnapshotId = snapshot.Id,
                SectorId = null,
                Barcode = barcode,
                CapturedQuantity = 0,
                FrozenQuantity = congelado.Quantity,
                QuantityDiff = quantityDiff,
                AmountDiff = quantityDiff * congelado.UnitCost,
                CalculatedAt = ahora,
            });
        }

        db.InventoryDifferences.AddRange(nuevas);
        await db.SaveChangesAsync(ct);
        return nuevas.Count;
    }
}
