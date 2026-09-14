using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;

namespace Modulo.AuditoriaInventario.Pages.Diferencias;

/// <summary>
/// Listado de diferencias calculadas (capturado vs. congelado). El motor que las
/// calcula (InventoryDifference materializado al cerrar una sesión) queda
/// pendiente -- ver PENDIENTE.md. Esta pantalla ya lista lo que exista en
/// InventoryDifference.
/// </summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;

    public IndexModel(AuditoriaInventarioDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<DiferenciaRowDto> Diferencias { get; private set; } = Array.Empty<DiferenciaRowDto>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        // Nota: InventoryDifference no lleva company_id directo (llega vía
        // InventorySession/FrozenInventorySnapshot) -- cuando se implemente el motor,
        // filtrar acá por la Company actual a través de esos joins.
        Diferencias = await _db.InventoryDifferences
            .OrderByDescending(d => d.CalculatedAt)
            .Take(200)
            .Select(d => new DiferenciaRowDto(
                d.Id, d.SessionId, d.Barcode, d.CapturedQuantity, d.FrozenQuantity,
                d.QuantityDiff, d.AmountDiff, d.CalculatedAt))
            .ToListAsync(ct);
    }

    public sealed record DiferenciaRowDto(
        long Id,
        Guid SessionId,
        string Barcode,
        int CapturedQuantity,
        int FrozenQuantity,
        int QuantityDiff,
        decimal AmountDiff,
        DateTimeOffset CalculatedAt);
}
