using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;

namespace Modulo.AuditoriaInventario.Pages.Ajustes;

/// <summary>
/// Cola de ajustes propuestos, con el gate de aprobación manual obligatorio antes
/// de escribir en SapAdjustmentQueueItem (decisión explícita del dueño del
/// proyecto: nunca envío automático a SAP). El flujo de "proponer ajuste desde una
/// diferencia" está pendiente -- ver PENDIENTE.md. Esta pantalla ya permite
/// aprobar/rechazar lo que exista en estado PROPOSED.
/// </summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;

    public IndexModel(AuditoriaInventarioDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<InventoryAdjustment> Ajustes { get; private set; } = Array.Empty<InventoryAdjustment>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Ajustes = await _db.InventoryAdjustments
            .OrderByDescending(a => a.ProposedAt)
            .Take(200)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Aprobación manual -- el único punto del sistema donde un InventoryAdjustment
    /// pasa a QUEUED y genera la fila correspondiente en SapAdjustmentQueueItem.
    /// TODO: mapeo real a SapCompanyCode/SapWarehouseCode/SapMaterialCode (pendiente
    /// de definir los campos de mapeo SAP en Branch/Product) -- placeholder acá.
    /// </summary>
    public async Task<IActionResult> OnPostAprobarAsync(long id, CancellationToken ct)
    {
        var ajuste = await _db.InventoryAdjustments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (ajuste is null || ajuste.Status != "PROPOSED")
        {
            ErrorMessage = "El ajuste ya no está disponible para aprobar.";
            return RedirectToPage();
        }

        ajuste.Status = "APPROVED";
        ajuste.ApprovedAt = DateTimeOffset.UtcNow;
        // TODO: ApprovedByUserId = ICurrentUserContext.UserId cuando se inyecte acá.

        await _db.SaveChangesAsync(ct);

        SuccessMessage = "Ajuste aprobado.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRechazarAsync(long id, CancellationToken ct)
    {
        var ajuste = await _db.InventoryAdjustments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (ajuste is null || ajuste.Status != "PROPOSED")
        {
            ErrorMessage = "El ajuste ya no está disponible para rechazar.";
            return RedirectToPage();
        }

        ajuste.Status = "REJECTED";
        await _db.SaveChangesAsync(ct);

        SuccessMessage = "Ajuste rechazado.";
        return RedirectToPage();
    }
}
