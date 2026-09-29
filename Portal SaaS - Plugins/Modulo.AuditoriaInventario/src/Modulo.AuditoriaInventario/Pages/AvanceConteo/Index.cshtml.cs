using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.AvanceConteo;

/// <summary>
/// Estado de avance del conteo por sesión: congelado vs. contado, calculado EN VIVO
/// desde InventoryCaptures/FrozenInventoryLines -- a propósito, NO desde
/// InventoryDifference (esa tabla la llena IDiferenciaEngine solo cuando la sesión
/// SE CIERRA desde la PWA, ver AuditoriaInventarioApiService.UpsertSesionAsync). Acá
/// el pedido es justamente poder seguir el avance de sesiones ACTIVAS, así que no
/// podemos depender de un cálculo que todavía no corrió.
///
/// El congelado se resuelve por (BranchId, InventoryNumber) -- no hay FK directa a
/// InventorySession (ver Congelados/Index.cshtml.cs) -- tomando el snapshot más
/// reciente si se recargó el archivo más de una vez.
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

    public IReadOnlyList<AvanceRowDto> Filas { get; private set; } = Array.Empty<AvanceRowDto>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var companyId = _currentCompany.CompanyId;

        var sesiones = await _db.InventorySessions
            .Where(s => s.CompanyId == companyId)
            .OrderByDescending(s => s.StartedAt)
            .Take(200)
            .ToListAsync(ct);

        var branchIds = sesiones.Select(s => s.BranchId).Distinct().ToList();
        var branches = await _db.Branches
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        var sesionIds = sesiones.Select(s => s.Id).ToList();
        var contadoPorSesion = await _db.InventoryCaptures
            .Where(c => sesionIds.Contains(c.SessionId))
            .GroupBy(c => c.SessionId)
            .Select(g => new { SessionId = g.Key, Total = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.SessionId, x => x.Total, ct);

        // Último snapshot por (BranchId, InventoryNumber) -- puede haber más de uno si
        // el congelado se recargó, siempre gana el más reciente (mismo criterio que
        // DiferenciaEngine).
        var snapshots = await _db.FrozenInventorySnapshots
            .Where(s => s.CompanyId == companyId)
            .OrderByDescending(s => s.LoadedAt)
            .ToListAsync(ct);
        var ultimoSnapshotPorCombinacion = snapshots
            .GroupBy(s => (s.BranchId, s.InventoryNumber))
            .ToDictionary(g => g.Key, g => g.First());

        var snapshotIds = ultimoSnapshotPorCombinacion.Values.Select(s => s.Id).ToList();
        var congeladoPorSnapshot = await _db.FrozenInventoryLines
            .Where(l => snapshotIds.Contains(l.SnapshotId))
            .GroupBy(l => l.SnapshotId)
            .Select(g => new { SnapshotId = g.Key, Total = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.SnapshotId, x => x.Total, ct);

        Filas = sesiones.Select(s =>
        {
            var contado = contadoPorSesion.GetValueOrDefault(s.Id, 0);
            int? congelado = ultimoSnapshotPorCombinacion.TryGetValue((s.BranchId, s.InventoryNumber), out var snap)
                ? congeladoPorSnapshot.GetValueOrDefault(snap.Id, 0)
                : null;
            // Mismo signo que DiferenciaEngine: positivo = sobrante, negativo = faltante.
            int? diferencia = congelado.HasValue ? contado - congelado.Value : null;

            return new AvanceRowDto(
                branches.GetValueOrDefault(s.BranchId, "?"),
                s.InventoryNumber,
                s.Status,
                congelado,
                contado,
                diferencia);
        }).ToList();
    }

    public sealed record AvanceRowDto(
        string BranchName,
        string InventoryNumber,
        string Status,
        int? Congelado,
        int Contado,
        int? Diferencia);
}
