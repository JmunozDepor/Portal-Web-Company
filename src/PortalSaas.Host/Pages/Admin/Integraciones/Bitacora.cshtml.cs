using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities.Integraciones;

namespace PortalSaas.Host.Pages.Admin.Integraciones;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class BitacoraModel : PageModel
{
    private const int MaximoRegistros = 100;

    private readonly PortalSaasDbContext _db;

    public BitacoraModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public IntegrationDefinition Integracion { get; private set; } = null!;
    public List<IntegrationRunLog> Ejecuciones { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var integracion = await _db.IntegrationDefinitions.FindAsync([id], ct);
        if (integracion is null)
        {
            return NotFound();
        }

        Integracion = integracion;
        Ejecuciones = await _db.IntegrationRunLogs
            .Where(l => l.IntegrationDefinitionId == id)
            .OrderByDescending(l => l.IniciadoEn)
            .Take(MaximoRegistros)
            .ToListAsync(ct);

        return Page();
    }
}
