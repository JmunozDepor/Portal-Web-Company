using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.Congelados;

/// <summary>
/// Listado de congelados cargados. La carga real (parseo del Excel exportado desde
/// el punto de venta, contra un Nro. de Inventario) queda pendiente -- ver
/// PENDIENTE.md. Esta versión solo lista lo ya cargado (FrozenInventorySnapshot),
/// para dejar la pantalla y el modelo de datos ya conectados de punta a punta.
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

    public IReadOnlyList<CongeladoRowDto> Congelados { get; private set; } = Array.Empty<CongeladoRowDto>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var snapshots = await _db.FrozenInventorySnapshots
            .Where(s => s.CompanyId == _currentCompany.CompanyId)
            .OrderByDescending(s => s.LoadedAt)
            .Take(200)
            .ToListAsync(ct);

        var branchIds = snapshots.Select(s => s.BranchId).Distinct().ToList();
        var branches = await _db.Branches
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        var lineCounts = await _db.FrozenInventoryLines
            .Where(l => snapshots.Select(s => s.Id).Contains(l.SnapshotId))
            .GroupBy(l => l.SnapshotId)
            .Select(g => new { SnapshotId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SnapshotId, x => x.Count, ct);

        Congelados = snapshots.Select(s => new CongeladoRowDto(
            s.Id,
            branches.GetValueOrDefault(s.BranchId, "?"),
            s.InventoryNumber,
            s.FileName,
            lineCounts.GetValueOrDefault(s.Id, 0),
            s.LoadedAt
        )).ToList();
    }

    public sealed record CongeladoRowDto(
        long Id,
        string BranchName,
        string InventoryNumber,
        string FileName,
        int LineCount,
        DateTimeOffset LoadedAt);
}
