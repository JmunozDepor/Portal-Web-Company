using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Companies.ExternalConnections;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class EditModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly ICompanyExternalConnectionService _svc;

    public EditModel(PortalSaasDbContext db, ICompanyExternalConnectionService svc)
    {
        _db = db;
        _svc = svc;
    }

    public Company Company { get; private set; } = null!;
    public Organization Organization { get; private set; } = null!;
    public ModuleConnectionBindingDto Binding { get; private set; } = null!;
    public IReadOnlyList<DatabaseInstanceOptionDto> Instances { get; private set; } = [];

    [BindProperty]
    public ModuleDatabaseBindingInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid companyId, string moduleCode)
    {
        if (!await CargarAsync(companyId, moduleCode))
        {
            return NotFound();
        }

        Input = new ModuleDatabaseBindingInput
        {
            InstanceId = Binding.InstanceId ?? 0,
            DatabaseName = Binding.DatabaseName ?? string.Empty,
            UseOwnCredentials = Binding.HasOwnCredentials,
            TechnicalUsername = Binding.OwnTechnicalUsername,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid companyId, string moduleCode)
    {
        if (!await CargarAsync(companyId, moduleCode))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await _svc.SetBindingAsync(Company.OrganizationId, companyId, moduleCode, Input);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        return RedirectToPage("./Index", new { companyId });
    }

    private async Task<bool> CargarAsync(Guid companyId, string moduleCode)
    {
        var company = await _db.Companies.FindAsync(companyId);
        if (company is null)
        {
            return false;
        }

        var binding = await _svc.GetBindingAsync(company.OrganizationId, companyId, moduleCode);
        if (binding is null)
        {
            return false;
        }

        Company = company;
        Organization = (await _db.Organizations.FindAsync(company.OrganizationId))!;
        Binding = binding;
        Instances = await _svc.ListInstancesAsync(company.OrganizationId, companyId);
        return true;
    }
}
