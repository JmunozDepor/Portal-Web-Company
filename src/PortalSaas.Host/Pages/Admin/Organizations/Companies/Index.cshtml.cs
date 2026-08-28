using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Companies;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly ISapConnectionTestService _connectionTestService;

    public IndexModel(PortalSaasDbContext db, ISapConnectionTestService connectionTestService)
    {
        _db = db;
        _connectionTestService = connectionTestService;
    }

    public Organization Organization { get; private set; } = null!;
    public List<Company> Companies { get; private set; } = [];
    public bool TieneInstancias { get; private set; }

    public Guid? TestedCompanyId { get; private set; }
    public SapConnectionTestResult? TestResult { get; private set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;
        Companies = await _db.Companies
            .Include(c => c.Instance)
            .Where(c => c.OrganizationId == organizationId)
            .OrderBy(c => c.Code)
            .ToListAsync();
        TieneInstancias = await _db.Instances.AnyAsync(i => i.OrganizationId == organizationId);

        return Page();
    }

    /// <summary>
    /// Vuelve a cargar la lista completa (Razor Pages no conserva estado entre GET/POST)
    /// y le agrega el resultado de probar UNA compañía puntual -- se queda en la misma
    /// página en vez de redirigir para no perder el resultado en un round-trip extra.
    /// </summary>
    public async Task<IActionResult> OnPostTestConnectionAsync(Guid organizationId, Guid companyId)
    {
        var getResult = await OnGetAsync(organizationId);
        if (getResult is not PageResult)
        {
            return getResult;
        }

        TestedCompanyId = companyId;
        TestResult = await _connectionTestService.TestAsync(companyId);

        return Page();
    }

    // Bloquea el borrado si la compañía ya tiene acceso/permisos asignados a algún
    // usuario, una conexión externa de plugin configurada, o una clave de API activa
    // -- borrarla ahí dejaría esas filas huérfanas en silencio (o, en el caso de la
    // API key, un acceso M2M silencioso desconectado de cualquier compañía real).
    public async Task<IActionResult> OnPostDeleteAsync(Guid organizationId, Guid id)
    {
        var inUse = await _db.UserMenuGroups.AnyAsync(g => g.CompanyId == id)
            || await _db.UserMenuProfiles.AnyAsync(p => p.CompanyId == id)
            || await _db.ModuleExternalConnections.AnyAsync(c => c.CompanyId == id)
            || await _db.CompanyExternalConnections.AnyAsync(c => c.CompanyId == id)
            || await _db.CompanyModuleConnections.AnyAsync(b => b.CompanyId == id)
            || await _db.ApiClientCredentials.AnyAsync(c => c.CompanyId == id && c.Activo);
        if (inUse)
        {
            ErrorMessage = "No se puede eliminar: la compañía tiene usuarios con acceso asignado, conexiones de plugin configuradas o claves de API activas.";
            return RedirectToPage(new { organizationId });
        }

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == organizationId);
        if (company is not null)
        {
            _db.Companies.Remove(company);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage(new { organizationId });
    }
}
