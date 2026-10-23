using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Core.DocumentosElectronicos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Companies.Dte;

/// <summary>
/// Configuración de documentos tributarios electrónicos de la compañía: proveedor (FACELE, BES...),
/// ambiente, credenciales (contraseña cifrada, write-only) y datos del emisor. Una por compañía.
/// </summary>
[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly DteSettingsService _svc;

    public IndexModel(PortalSaasDbContext db, DteSettingsService svc)
    {
        _db = db;
        _svc = svc;
    }

    public Company Company { get; private set; } = null!;
    public Organization Organization { get; private set; } = null!;
    public DteSettingsDto? Current { get; private set; }
    public IReadOnlyList<string> ProviderCodes => _svc.ProviderCodes;

    [BindProperty]
    public DteSettingsInput Input { get; set; } = new();

    [TempData]
    public string? ErrorMessage { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid companyId)
    {
        if (!await CargarAsync(companyId)) return NotFound();
        if (Current is { } c)
        {
            Input = new DteSettingsInput
            {
                ProviderCode = c.ProviderCode, Environment = c.Environment, BaseUrl = c.BaseUrl, Username = c.Username,
                IsEnabled = c.IsEnabled, IssuerTaxId = c.IssuerTaxId, IssuerName = c.IssuerName,
                IssuerActivity = c.IssuerActivity, IssuerActivityCode = c.IssuerActivityCode, IssuerAddress = c.IssuerAddress,
                IssuerCommune = c.IssuerCommune, IssuerCity = c.IssuerCity, IssuerSiiBranchCode = c.IssuerSiiBranchCode,
            };
        }
        else
        {
            Input = new DteSettingsInput { ProviderCode = ProviderCodes.FirstOrDefault() ?? "", IssuerName = Company.Name };
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid companyId)
    {
        if (!await CargarAsync(companyId)) return NotFound();
        try
        {
            await _svc.SaveAsync(companyId, Input);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
        SuccessMessage = "Configuración guardada.";
        return RedirectToPage(new { companyId });
    }

    public async Task<IActionResult> OnPostTestAsync(Guid companyId)
    {
        if (!await CargarAsync(companyId)) return NotFound();
        try
        {
            await _svc.TestAsync(companyId);
            SuccessMessage = $"Conexión correcta con {Current?.ProviderCode}.";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        return RedirectToPage(new { companyId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid companyId)
    {
        if (!await CargarAsync(companyId)) return NotFound();
        try
        {
            await _svc.DeleteAsync(companyId);
            SuccessMessage = "Configuración eliminada.";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        return RedirectToPage(new { companyId });
    }

    private async Task<bool> CargarAsync(Guid companyId)
    {
        var company = await _db.Companies.FindAsync(companyId);
        if (company is null) return false;
        Company = company;
        Organization = (await _db.Organizations.FindAsync(company.OrganizationId))!;
        Current = await _svc.GetAsync(companyId);
        return true;
    }
}
