using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Companies.ExternalConnections;

/// <summary>
/// Base de datos externa de cada módulo de la compañía: Instancia + Base (+ credencial
/// propia opcional). Una fila por módulo con RequiresExternalDatabase. Los servidores se
/// administran en Instancias (/Admin/Organizations/Instances).
/// </summary>
[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly ICompanyExternalConnectionService _svc;

    public IndexModel(PortalSaasDbContext db, ICompanyExternalConnectionService svc)
    {
        _db = db;
        _svc = svc;
    }

    public Company Company { get; private set; } = null!;
    public IReadOnlyList<ModuleConnectionBindingDto> Bindings { get; private set; } = [];

    public string? TestedModuleCode { get; private set; }
    public bool? TestSuccess { get; private set; }
    public string? TestError { get; private set; }
    public long? TestElapsedMs { get; private set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid companyId)
    {
        var company = await _db.Companies.FindAsync(companyId);
        if (company is null)
        {
            return NotFound();
        }

        Company = company;
        Bindings = await _svc.ListBindingsAsync(company.OrganizationId, companyId);
        return Page();
    }

    public async Task<IActionResult> OnPostTestAsync(Guid companyId, string moduleCode)
    {
        var getResult = await OnGetAsync(companyId);
        if (getResult is not PageResult)
        {
            return getResult;
        }

        TestedModuleCode = moduleCode;
        try
        {
            var r = await _svc.TestBindingAsync(Company.OrganizationId, companyId, moduleCode);
            TestSuccess = r.Ok;
            TestError = r.Error;
            TestElapsedMs = r.ElapsedMs;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostClearAsync(Guid companyId, string moduleCode)
    {
        var company = await _db.Companies.FindAsync(companyId);
        if (company is null)
        {
            return NotFound();
        }

        try
        {
            await _svc.ClearBindingAsync(company.OrganizationId, companyId, moduleCode);
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(new { companyId });
    }
}
