using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;

namespace Modulo.AuditoriaInventario.Servicios;

/// <summary>
/// Congelado vs. contado por producto, para UNA tarea de conteo puntual -- fuente
/// única usada tanto por la grilla en pantalla (Pages/AvanceConteo/Detalle) como
/// por el export a Excel (Pages/AvanceConteo/Index.OnGetExportarDetalleAsync), para
/// no mantener la misma cuenta en dos lugares. Calculado EN VIVO (no depende de
/// InventoryDifference, que solo se materializa al cerrar la sesión) -- funciona
/// igual para tareas todavía ACTIVAS. Mismo criterio de valorización que
/// IDiferenciaEngine: UnitCost viene del congelado, 0 si un código contado no tiene
/// costo cargado.
/// </summary>
public static class DetalleConteoBuilder
{
    public sealed record FilaProducto(
        string Barcode,
        string? ProductCode,
        string? Description,
        string? Brand,
        int? Congelado,
        int Contado,
        int? DiferenciaCantidad,
        decimal UnitCost,
        decimal? MontoCongelado,
        decimal MontoContado,
        decimal? MontoDiferencia);

    public sealed record Resultado(InventorySession Sesion, string BranchName, IReadOnlyList<FilaProducto> Filas)
    {
        public int TotalCongelado => Filas.Sum(f => f.Congelado ?? 0);
        public int TotalContado => Filas.Sum(f => f.Contado);
        public int TotalDiferencia => TotalContado - TotalCongelado;
        public decimal TotalMontoCongelado => Filas.Sum(f => f.MontoCongelado ?? 0m);
        public decimal TotalMontoContado => Filas.Sum(f => f.MontoContado);
        public decimal TotalMontoDiferencia => TotalMontoContado - TotalMontoCongelado;
    }

    /// <summary>Null si la sesión no existe o no pertenece a la compañía (el llamador decide cómo reaccionar -- 404, redirect, etc.).</summary>
    public static async Task<Resultado?> ConstruirAsync(AuditoriaInventarioDbContext db, Guid companyId, Guid sessionId, CancellationToken ct)
    {
        var sesion = await db.InventorySessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.CompanyId == companyId, ct);
        if (sesion is null) return null;

        var branchName = await db.Branches
            .Where(b => b.Id == sesion.BranchId)
            .Select(b => b.Name)
            .FirstOrDefaultAsync(ct) ?? "?";

        var capturas = await db.InventoryCaptures.Where(c => c.SessionId == sessionId).ToListAsync(ct);

        var snapshot = await db.FrozenInventorySnapshots
            .Where(s => s.CompanyId == companyId && s.BranchId == sesion.BranchId && s.InventoryNumber == sesion.InventoryNumber)
            .OrderByDescending(s => s.LoadedAt)
            .FirstOrDefaultAsync(ct);
        var lineasCongeladas = snapshot is null
            ? new List<FrozenInventoryLine>()
            : await db.FrozenInventoryLines.Where(l => l.SnapshotId == snapshot.Id).ToListAsync(ct);
        var congeladoPorBarcode = lineasCongeladas
            .GroupBy(l => l.Barcode)
            .ToDictionary(g => g.Key, g => (Quantity: g.Sum(x => x.Quantity), UnitCost: g.Select(x => x.UnitCost).FirstOrDefault(c => c.HasValue) ?? 0m));

        var contadoPorBarcode = capturas
            .GroupBy(c => c.Barcode)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var todosLosBarcodes = contadoPorBarcode.Keys.Union(congeladoPorBarcode.Keys).OrderBy(b => b).ToList();

        // Incluye también los barcodes que solo están en el congelado (nunca se
        // escanearon, faltante total) -- así esas líneas también muestran
        // descripción/marca en vez de quedar en blanco.
        var productos = await db.Products
            .Where(p => p.CompanyId == companyId && todosLosBarcodes.Contains(p.Barcode))
            .ToDictionaryAsync(p => p.Barcode, p => p, ct);

        var filas = new List<FilaProducto>();
        foreach (var barcode in todosLosBarcodes)
        {
            var contado = contadoPorBarcode.GetValueOrDefault(barcode, 0);
            var tieneCongelado = congeladoPorBarcode.TryGetValue(barcode, out var congelado);
            var unitCost = tieneCongelado ? congelado.UnitCost : 0m;
            productos.TryGetValue(barcode, out var producto);
            int? diferenciaCantidad = tieneCongelado ? contado - congelado.Quantity : null;

            filas.Add(new FilaProducto(
                barcode,
                producto?.ProductCode,
                producto?.Description,
                producto?.Brand,
                tieneCongelado ? congelado.Quantity : null,
                contado,
                diferenciaCantidad,
                unitCost,
                tieneCongelado ? congelado.Quantity * unitCost : null,
                contado * unitCost,
                diferenciaCantidad.HasValue ? diferenciaCantidad.Value * unitCost : null));
        }

        return new Resultado(sesion, branchName, filas);
    }
}
