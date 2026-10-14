using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using Modulo.AuditoriaInventario.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.AuditoriaInventario.Pages.Ajustes;

/// <summary>
/// Cola de ajustes propuestos, con el gate de aprobación manual obligatorio antes
/// de escribir en SapAdjustmentQueueItem -- ver AjusteService para el mapeo real.
/// </summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAjusteService _ajusteService;
    private readonly INotificationService _notifications;

    public IndexModel(AuditoriaInventarioDbContext db, ICurrentCompanyAccessor currentCompany, ICurrentUserContext currentUser,
        IAjusteService ajusteService, INotificationService notifications)
    {
        _db = db;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _ajusteService = ajusteService;
        _notifications = notifications;
    }

    public IReadOnlyList<InventoryAdjustment> Ajustes { get; private set; } = Array.Empty<InventoryAdjustment>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        // InventoryAdjustment no lleva company_id directo -- llega vía
        // InventoryDifference -> InventorySession.
        Ajustes = await (
            from a in _db.InventoryAdjustments
            join d in _db.InventoryDifferences on a.DifferenceId equals d.Id
            join s in _db.InventorySessions on d.SessionId equals s.Id
            where s.CompanyId == _currentCompany.CompanyId
            orderby a.ProposedAt descending
            select a)
            .Take(200)
            .ToListAsync(ct);
    }

    public async Task<IActionResult> OnPostAprobarAsync(long id, CancellationToken ct)
    {
        var resultado = await _ajusteService.AprobarAsync(_db, id, _currentCompany.CompanyId, _currentUser.UserId, ct);
        if (!resultado.Exitoso)
        {
            ErrorMessage = resultado.Mensaje;
            return RedirectToPage();
        }

        SuccessMessage = resultado.Mensaje;

        // AprobarAsync ya validó compañía y estado -- acá solo hace falta saber a quién avisar.
        var ajuste = await _db.InventoryAdjustments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (ajuste is not null)
        {
            await NotificarProponenteAsync(ajuste, $"Tu ajuste #{ajuste.Id} fue aprobado y quedó en cola para SAP.", NotificationSeverity.Success, ct);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRechazarAsync(long id, CancellationToken ct)
    {
        // Mismo join por CompanyId que OnGetAsync -- InventoryAdjustment no lleva
        // company_id directo, así que el scoping por compañía tiene que pasar por
        // InventoryDifference -> InventorySession.
        var ajuste = await (
            from a in _db.InventoryAdjustments
            join d in _db.InventoryDifferences on a.DifferenceId equals d.Id
            join s in _db.InventorySessions on d.SessionId equals s.Id
            where a.Id == id && s.CompanyId == _currentCompany.CompanyId
            select a)
            .FirstOrDefaultAsync(ct);

        if (ajuste is null || ajuste.Status != "PROPOSED")
        {
            ErrorMessage = "El ajuste ya no está disponible para rechazar.";
            return RedirectToPage();
        }

        ajuste.Status = "REJECTED";
        await _db.SaveChangesAsync(ct);

        SuccessMessage = "Ajuste rechazado.";
        await NotificarProponenteAsync(ajuste, $"Tu ajuste #{ajuste.Id} fue rechazado.", NotificationSeverity.Danger, ct);
        return RedirectToPage();
    }

    /// <summary>Al que propuso el ajuste -- no a quien lo resolvió (si es la misma persona, ya lo ve en pantalla).</summary>
    private async Task NotificarProponenteAsync(InventoryAdjustment ajuste, string mensaje, string severity, CancellationToken ct)
    {
        if (ajuste.ProposedByUserId == _currentUser.UserId)
        {
            return;
        }

        await _notifications.TryEnqueueAsync(ajuste.ProposedByUserId, "Auditoría de inventario: ajuste", mensaje,
            severity, "/auditoria-inventario/ajustes", ct);
    }
}
