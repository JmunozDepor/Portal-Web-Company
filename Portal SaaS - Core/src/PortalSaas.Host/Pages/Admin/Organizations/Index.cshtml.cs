using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly IOrganizationAccessGateService _accessGate;

    public IndexModel(PortalSaasDbContext db, IOrganizationAccessGateService accessGate)
    {
        _db = db;
        _accessGate = accessGate;
    }

    public List<Organization> Organizations { get; private set; } = [];

    /// <summary>Ids de organizaciones sin licencia/suscripción vigente -- ver IOrganizationAccessGateService.CheckAccessAsync (misma lógica real que bloquea el login, no un cálculo aparte).</summary>
    public HashSet<Guid> OrganizationsWithoutAccess { get; private set; } = [];

    public int ActiveCount => Organizations.Count(o => o.Status == OrganizationStatus.Active);

    public int OnPremiseCount => Organizations.Count(o => o.Mode == OrganizationMode.OnPremise);

    public int SaasCount => Organizations.Count(o => o.Mode == OrganizationMode.Saas);

    public int CreatedThisMonthCount => Organizations.Count(o =>
        o.CreatedAt.Year == DateTimeOffset.UtcNow.Year && o.CreatedAt.Month == DateTimeOffset.UtcNow.Month);

    public async Task OnGetAsync(CancellationToken ct)
    {
        Organizations = await _db.Organizations
            .OrderBy(o => o.LegalName)
            .ToListAsync(ct);

        // Pocas organizaciones en este listado por diseño (backoffice de plataforma,
        // no un catálogo de miles de filas) -- un chequeo por organización acá es
        // aceptable, mismo criterio que otras pantallas admin de este proyecto.
        foreach (var organization in Organizations)
        {
            var access = await _accessGate.CheckAccessAsync(organization.Id, ct);
            if (!access.IsAllowed)
            {
                OrganizationsWithoutAccess.Add(organization.Id);
            }
        }
    }
}
