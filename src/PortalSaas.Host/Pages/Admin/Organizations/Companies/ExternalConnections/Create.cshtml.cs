using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Companies.ExternalConnections;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class CreateModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly ICompanyExternalConnectionService _svc;

    public CreateModel(PortalSaasDbContext db, ICompanyExternalConnectionService svc)
    {
        _db = db;
        _svc = svc;
    }

    public Company Company { get; private set; } = null!;

    public Organization Organization { get; private set; } = null!;

    [BindProperty]
    public ExternalConnectionEditModel Input { get; set; } = new();

    public IReadOnlyCollection<string> Tipos => ExternalConnectionType.All;

    public async Task<IActionResult> OnGetAsync(Guid companyId)
    {
        if (!await LoadHeaderAsync(companyId))
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid companyId)
    {
        if (!await LoadHeaderAsync(companyId))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await _svc.CreateAsync(Company.OrganizationId, companyId, Input);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        return RedirectToPage("Index", new { companyId });
    }

    private async Task<bool> LoadHeaderAsync(Guid companyId)
    {
        var company = await _db.Companies.FindAsync(companyId);
        if (company is null)
        {
            return false;
        }

        Company = company;
        Organization = (await _db.Organizations.FindAsync(company.OrganizationId))!;
        return true;
    }
}
