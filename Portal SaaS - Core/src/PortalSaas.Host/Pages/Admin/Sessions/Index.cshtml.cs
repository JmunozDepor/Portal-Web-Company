using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;

namespace PortalSaas.Host.Pages.Admin.Sessions;

/// <summary>
/// "Clientes conectados" -- vista global de sesiones de portal activas (ver
/// IUserSessionService), agrupadas por organización en el markup (Index.cshtml). El
/// filtro de organización es opcional -- sin él, trae la vista global completa.
/// </summary>
[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly IUserSessionService _sessions;
    private readonly PortalSaasDbContext _db;

    public IndexModel(IUserSessionService sessions, PortalSaasDbContext db)
    {
        _sessions = sessions;
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public Guid? OrganizationId { get; set; }

    public List<SelectListItem> Organizations { get; private set; } = [];

    public List<IGrouping<string, UserSessionDto>> SessionsByOrganization { get; private set; } = [];

    public int TotalSessions { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Organizations = await _db.Organizations
            .OrderBy(o => o.LegalName)
            .Select(o => new SelectListItem(o.LegalName, o.Id.ToString()))
            .ToListAsync(ct);

        var sessions = await _sessions.ListActiveAsync(OrganizationId, ct);
        TotalSessions = sessions.Count;
        SessionsByOrganization = sessions.GroupBy(s => s.OrganizationName).ToList();
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid sessionId, Guid? organizationId, CancellationToken ct)
    {
        await _sessions.RevokeAsync(sessionId, ct);
        return RedirectToPage(new { organizationId });
    }
}
