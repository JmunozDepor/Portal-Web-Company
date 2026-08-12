using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Modules;

/// <summary>
/// Add-ons de módulos por organización (OrganizationModule) -- módulos que la
/// organización tiene contratados MÁS ALLÁ de lo que ya incluye su plan (ver
/// IModuleAccessService, MenuNavigationService). Antes de esta pantalla la tabla
/// existía sin ningún consumidor de escritura real, solo el de lectura
/// (ModuleAccessService).
/// </summary>
[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly IModuleAccessService _moduleAccess;
    private readonly ICacheService _cache;

    public IndexModel(PortalSaasDbContext db, IModuleAccessService moduleAccess, ICacheService cache)
    {
        _db = db;
        _moduleAccess = moduleAccess;
        _cache = cache;
    }

    public Organization Organization { get; private set; } = null!;
    public List<PlatformModule> AllModules { get; private set; } = [];
    public HashSet<long> SelectedAddonModuleIds { get; private set; } = [];

    /// <summary>Códigos ya incluidos vía core/plan (no vía add-on) -- se muestran informativos, sin checkbox.</summary>
    public HashSet<string> AlreadyIncludedCodes { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;
        await LoadAsync(organizationId);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid organizationId, List<long>? selectedAddonModuleIds)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        var existentes = await _db.OrganizationModules
            .Where(om => om.OrganizationId == organizationId)
            .ToListAsync();

        var seleccionados = (selectedAddonModuleIds ?? []).ToHashSet();

        _db.OrganizationModules.RemoveRange(existentes.Where(om => !seleccionados.Contains(om.ModuleId)));

        var yaAsignados = existentes.Select(om => om.ModuleId).ToHashSet();
        foreach (var moduleId in seleccionados.Where(moduleId => !yaAsignados.Contains(moduleId)))
        {
            _db.OrganizationModules.Add(new OrganizationModule { OrganizationId = organizationId, ModuleId = moduleId });
        }

        await _db.SaveChangesAsync();

        // Solo esta organización cambió su set de add-ons -- invalidación puntual, no
        // hace falta tocar el caché de las demás. Ver ICacheService/Task 6.
        _cache.RemoveByPrefix($"modulos-contratados:{organizationId}");

        Organization = organization;
        await LoadAsync(organizationId);

        TempData["Mensaje"] = "Módulos contratados actualizados.";
        return RedirectToPage(new { organizationId });
    }

    private async Task LoadAsync(Guid organizationId)
    {
        // Un módulo exclusivo de otra organización (personalización puntual, ej.
        // SellOut/GestionDistribucionGastos) nunca aparece acá como opción -- ni
        // siquiera para desmarcarlo por error, ver PlatformModule.ExclusiveOrganizationId.
        AllModules = await _db.PlatformModules
            .Where(m => m.ExclusiveOrganizationId == null || m.ExclusiveOrganizationId == organizationId)
            .OrderBy(m => m.Code)
            .ToListAsync();

        var addonModuleIds = await _db.OrganizationModules
            .Where(om => om.OrganizationId == organizationId)
            .Select(om => om.ModuleId)
            .ToListAsync();
        SelectedAddonModuleIds = addonModuleIds.ToHashSet();

        var contractedCodes = await _moduleAccess.GetContractedModuleCodesAsync(organizationId);
        var addonCodes = AllModules.Where(m => SelectedAddonModuleIds.Contains(m.Id)).Select(m => m.Code).ToHashSet();
        AlreadyIncludedCodes = contractedCodes.Where(code => !addonCodes.Contains(code)).ToHashSet();
    }
}
