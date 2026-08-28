using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
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

    [BindProperty(SupportsGet = true)]
    public Guid? CompanyId { get; set; }

    public List<IntegrationDefinition> Integraciones { get; private set; } = [];
    public List<Company> Companies { get; private set; } = [];
    public Dictionary<Guid, string> CompanyNames { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync(ct);
        CompanyNames = Companies.ToDictionary(c => c.Id, c => c.Name);

        var query = _db.IntegrationDefinitions.AsQueryable();
        if (CompanyId is { } companyId)
        {
            query = query.Where(d => d.CompanyId == companyId);
        }

        Integraciones = await query
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

        if (!definicion.Activo)
        {
            TempData["Mensaje"] = $"No se puede ejecutar: '{definicion.Nombre}' está inactiva.";
            return RedirectToPage();
        }

        definicion.NextRunAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        TempData["Mensaje"] = $"'{definicion.Nombre}' quedó marcada para ejecutarse en el próximo ciclo (hasta 1 minuto).";
        return RedirectToPage();
    }
}
