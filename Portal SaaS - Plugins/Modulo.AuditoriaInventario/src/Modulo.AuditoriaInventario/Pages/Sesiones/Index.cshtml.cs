using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.Sesiones;

/// <summary>
/// Listado de sesiones de conteo -- en curso e históricas (abiertas/cerradas), tal
/// como lo pidió el dueño del proyecto para controlar desde el portal. Las sesiones
/// se CREAN desde la PWA de captura (Api/V1), nunca desde acá -- esta pantalla es
/// de solo lectura/seguimiento.
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

    public IReadOnlyList<SesionRowDto> Sesiones { get; private set; } = Array.Empty<SesionRowDto>();

    [BindProperty(SupportsGet = true)]
    public string? EstadoFiltro { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var query = _db.InventorySessions
            .Where(s => s.CompanyId == _currentCompany.CompanyId);

        if (!string.IsNullOrWhiteSpace(EstadoFiltro))
        {
            query = query.Where(s => s.Status == EstadoFiltro);
        }

        var sesiones = await query
            .OrderByDescending(s => s.StartedAt)
            .Take(200)
            .ToListAsync(ct);

        var branchIds = sesiones.Select(s => s.BranchId).Distinct().ToList();
        var branches = await _db.Branches
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        var userIds = sesiones.Select(s => s.ResponsibleUserId).Distinct().ToList();
        var users = await _db.CaptureUsers
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Username, ct);

        Sesiones = sesiones.Select(s => new SesionRowDto(
            s.Id,
            branches.GetValueOrDefault(s.BranchId, "?"),
            s.InventoryNumber,
            users.GetValueOrDefault(s.ResponsibleUserId, "?"),
            s.Status,
            s.ValidateAgainstMaster,
            s.StartedAt,
            s.ClosedAt
        )).ToList();
    }

    public sealed record SesionRowDto(
        Guid Id,
        string BranchName,
        string InventoryNumber,
        string ResponsibleUserName,
        string Status,
        bool ValidateAgainstMaster,
        DateTimeOffset StartedAt,
        DateTimeOffset? ClosedAt);
}
