using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities.Integraciones;

namespace PortalSaas.Host.Pages.Admin.Integraciones;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public IndexModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public List<IntegrationDefinition> Integraciones { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Integraciones = await _db.IntegrationDefinitions
            .OrderBy(d => d.Nombre)
            .ToListAsync(ct);
    }

    public async Task<IActionResult> OnPostEjecutarAsync(Guid id, CancellationToken ct)
    {
        var definicion = await _db.IntegrationDefinitions.FindAsync([id], ct);
        if (definicion is null)
        {
            return NotFound();
        }

        definicion.NextRunAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        TempData["Mensaje"] = $"'{definicion.Nombre}' quedó marcada para ejecutarse en el próximo ciclo (hasta 1 minuto).";
        return RedirectToPage();
    }
}
