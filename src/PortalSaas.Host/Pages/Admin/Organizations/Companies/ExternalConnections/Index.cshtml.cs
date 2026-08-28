using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Companies.ExternalConnections;

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

    public IReadOnlyList<ExternalConnectionDto> Connections { get; private set; } = [];
    public IReadOnlyList<ModuleConnectionBindingDto> Bindings { get; private set; } = [];

    public long? TestedConnectionId { get; private set; }
    public bool? TestSuccess { get; private set; }
    public string? TestError { get; private set; }

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
        Connections = await _svc.ListAsync(company.OrganizationId, companyId);
        Bindings = await _svc.ListBindingsAsync(company.OrganizationId, companyId);

        return Page();
    }

    public async Task<IActionResult> OnPostTestConnectionAsync(Guid companyId, long id)
    {
        var getResult = await OnGetAsync(companyId);
        if (getResult is not PageResult)
        {
            return getResult;
        }

        TestedConnectionId = id;
        try
        {
            var r = await _svc.TestAsync(Company.OrganizationId, companyId, id);
            TestSuccess = r.Ok;
            TestError = r.Error;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid companyId, long id)
    {
        var company = await _db.Companies.FindAsync(companyId);
        if (company is null)
        {
            return NotFound();
        }

        try
        {
            await _svc.DeleteAsync(company.OrganizationId, companyId, id);
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(new { companyId });
    }

    /// <summary>
    /// Grilla "Asignación por módulo": connectionId nulo o 0 ⇒ limpiar el binding, si no
    /// asignarlo. El servicio valida que la conexión pertenezca a la compañía.
    /// </summary>
    public async Task<IActionResult> OnPostSetBindingAsync(Guid companyId, string moduleCode, string purpose, long? connectionId)
    {
        var company = await _db.Companies.FindAsync(companyId);
        if (company is null)
        {
            return NotFound();
        }

        try
        {
            if (connectionId is null or 0)
            {
                await _svc.ClearBindingAsync(company.OrganizationId, companyId, moduleCode, purpose);
            }
            else
            {
                await _svc.SetBindingAsync(company.OrganizationId, companyId, moduleCode, purpose, connectionId.Value);
            }
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(new { companyId });
    }
}
