using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Companies.ApiKeys;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public IndexModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public Company Company { get; private set; } = null!;
    public List<ApiClientCredential> Credenciales { get; private set; } = [];

    [TempData]
    public string? RawKeyGenerada { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid companyId, CancellationToken ct)
    {
        var company = await _db.Companies.FindAsync([companyId], ct);
        if (company is null)
        {
            return NotFound();
        }

        Company = company;
        Credenciales = await _db.ApiClientCredentials
            .Where(c => c.CompanyId == companyId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(Guid companyId, string nombre, CancellationToken ct)
    {
        var company = await _db.Companies.FindAsync([companyId], ct);
        if (company is null)
        {
            return NotFound();
        }

        var rawKey = ApiKeyGenerator.GenerateRawKey();
        _db.ApiClientCredentials.Add(new ApiClientCredential
        {
            CompanyId = companyId,
            Nombre = nombre,
            ApiKeyHash = ApiKeyGenerator.Hash(rawKey),
            Activo = true,
        });
        await _db.SaveChangesAsync(ct);

        RawKeyGenerada = rawKey;
        return RedirectToPage(new { companyId });
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid companyId, Guid id, CancellationToken ct)
    {
        var credencial = await _db.ApiClientCredentials
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId, ct);
        if (credencial is null)
        {
            return NotFound();
        }

        credencial.Activo = false;
        await _db.SaveChangesAsync(ct);

        return RedirectToPage(new { companyId });
    }
}
