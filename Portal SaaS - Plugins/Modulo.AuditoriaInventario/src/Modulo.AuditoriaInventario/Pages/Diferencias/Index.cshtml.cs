using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.Diferencias;

/// <summary>
/// Listado de diferencias calculadas (capturado vs. congelado), materializadas por
/// DiferenciaEngine al cerrar una sesión.
/// </summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(AuditoriaInventarioDbContext db, ICurrentCompanyAccessor currentCompany)
    {
        _db = db;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<DiferenciaRowDto> Diferencias { get; private set; } = Array.Empty<DiferenciaRowDto>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        // InventoryDifference no lleva company_id directo -- llega vía InventorySession.
        Diferencias = await (
            from d in _db.InventoryDifferences
            join s in _db.InventorySessions on d.SessionId equals s.Id
            where s.CompanyId == _currentCompany.CompanyId
            orderby d.CalculatedAt descending
            select new DiferenciaRowDto(
                d.Id, d.SessionId, d.Barcode, d.CapturedQuantity, d.FrozenQuantity,
                d.QuantityDiff, d.AmountDiff, d.CalculatedAt))
            .Take(200)
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
