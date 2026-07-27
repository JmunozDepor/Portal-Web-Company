using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Plans;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public IndexModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public List<Plan> Plans { get; private set; } = [];

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        Plans = await _db.Plans.OrderBy(p => p.Code).ToListAsync();
    }

    // Bloquea el borrado si el plan ya se usó de verdad (suscripción o licencia
    // on-premise) -- borrarlo ahí dejaría filas de historial apuntando a un plan
    // inexistente. PlanModule (qué módulos incluye) se cascadea sola, no bloquea.
    public async Task<IActionResult> OnPostDeleteAsync(long id)
    {
        var inUse = await _db.Subscriptions.AnyAsync(s => s.PlanId == id)
            || await _db.OnPremiseLicenses.AnyAsync(l => l.PlanId == id);
        if (inUse)
        {
            ErrorMessage = "No se puede eliminar: el plan está asignado a al menos una suscripción o licencia.";
            return RedirectToPage();
        }

        var plan = await _db.Plans.Include(p => p.PlanModules).FirstOrDefaultAsync(p => p.Id == id);
        if (plan is not null)
        {
            _db.Plans.Remove(plan);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage();
    }
}
