using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Companies;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class EditModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly ISecretoCifradoService _secretoCifradoService;

    public EditModel(PortalSaasDbContext db, ISecretoCifradoService secretoCifradoService)
    {
        _db = db;
        _secretoCifradoService = secretoCifradoService;
    }

    public Organization Organization { get; private set; } = null!;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> Instances { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var company = await _db.Companies.FindAsync(id);
        if (company is null)
        {
            return NotFound();
        }

        Organization = (await _db.Organizations.FindAsync(company.OrganizationId))!;
        await CargarInstanciasAsync(company.OrganizationId);

        Input = new InputModel
        {
            Id = company.Id,
            InstanceId = company.InstanceId,
            Code = company.Code,
            Name = company.Name,
            DatabaseName = company.DatabaseName,
            ServiceLayerUrl = company.ServiceLayerUrl,
            IntegrationUsername = company.IntegrationUsername,
            Country = company.Country,
            IsActive = company.IsActive,
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var company = await _db.Companies.FindAsync(Input.Id);
        if (company is null)
        {
            return NotFound();
        }

        Organization = (await _db.Organizations.FindAsync(company.OrganizationId))!;
        await CargarInstanciasAsync(company.OrganizationId);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var instanciaValida = await _db.Instances.AnyAsync(i => i.Id == Input.InstanceId && i.OrganizationId == company.OrganizationId);
        if (!instanciaValida)
        {
            ModelState.AddModelError(nameof(Input.InstanceId), "Instancia inválida.");
            return Page();
        }

        var code = Input.Code.Trim().ToUpperInvariant();
        var codigoEnUso = await _db.Companies.AnyAsync(c => c.Code == code && c.Id != Input.Id);
        if (codigoEnUso)
        {
            ModelState.AddModelError(nameof(Input.Code), "Ya existe otra compañía con ese código.");
            return Page();
        }

        company.InstanceId = Input.InstanceId;
        company.Code = code;
        company.Name = Input.Name.Trim();
        company.DatabaseName = Input.DatabaseName.Trim();
        company.ServiceLayerUrl = Input.ServiceLayerUrl.Trim();
        company.IntegrationUsername = Input.IntegrationUsername.Trim();
        company.Country = Input.Country.Trim();
        company.IsActive = Input.IsActive;

        if (!string.IsNullOrWhiteSpace(Input.IntegrationSecretKey))
        {
            company.IntegrationSecretKey = _secretoCifradoService.Encrypt(Input.IntegrationSecretKey);
        }

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Organizations/Companies/Index", new { organizationId = company.OrganizationId });
    }

    private async Task CargarInstanciasAsync(Guid organizationId)
    {
        Instances = await _db.Instances
            .Where(i => i.OrganizationId == organizationId)
            .OrderBy(i => i.Name)
            .Select(i => new SelectListItem(i.Name, i.Id.ToString()))
            .ToListAsync();
    }

    public sealed class InputModel
    {
        public Guid Id { get; set; }

        [Required(ErrorMessage = "Selecciona la instancia.")]
        [Display(Name = "Instancia")]
        public long InstanceId { get; set; }

        [Required(ErrorMessage = "Ingresa el código SAP de la compañía.")]
        [Display(Name = "Código (ej. DEPOR)")]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa el nombre.")]
        [Display(Name = "Nombre")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa el nombre de la base de datos SAP.")]
        [Display(Name = "Base de datos")]
        public string DatabaseName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa la URL del Service Layer.")]
        [Display(Name = "URL del Service Layer")]
        public string ServiceLayerUrl { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa el usuario de integración.")]
        [Display(Name = "Usuario de integración")]
        public string IntegrationUsername { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Nueva clave de integración (dejar en blanco para no cambiarla)")]
        public string? IntegrationSecretKey { get; set; }

        [Required(ErrorMessage = "Ingresa el país.")]
        [Display(Name = "País")]
        public string Country { get; set; } = string.Empty;

        [Display(Name = "Activa")]
        public bool IsActive { get; set; } = true;
    }
}
