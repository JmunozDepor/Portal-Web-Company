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
public class CreateModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly ISecretoCifradoService _secretoCifradoService;

    public CreateModel(PortalSaasDbContext db, ISecretoCifradoService secretoCifradoService)
    {
        _db = db;
        _secretoCifradoService = secretoCifradoService;
    }

    public Organization Organization { get; private set; } = null!;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> Instances { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;
        await CargarInstanciasAsync(organizationId);

        if (Instances.Count == 0)
        {
            return RedirectToPage("/Admin/Organizations/Companies/Index", new { organizationId });
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;
        await CargarInstanciasAsync(organizationId);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var instanciaValida = await _db.Instances.AnyAsync(i => i.Id == Input.InstanceId && i.OrganizationId == organizationId);
        if (!instanciaValida)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.InstanceId)}", "Instancia inválida.");
            return Page();
        }

        var code = Input.Code.Trim().ToUpperInvariant();
        var codigoEnUso = await _db.Companies.AnyAsync(c => c.Code == code);
        if (codigoEnUso)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Code)}", "Ya existe una compañía con ese código.");
            return Page();
        }

        _db.Companies.Add(new Company
        {
            OrganizationId = organizationId,
            InstanceId = Input.InstanceId,
            Code = code,
            Name = Input.Name.Trim(),
            DatabaseName = Input.DatabaseName.Trim(),
            ServiceLayerUrl = Input.ServiceLayerUrl.Trim(),
            IntegrationUsername = Input.IntegrationUsername.Trim(),
            IntegrationSecretKey = _secretoCifradoService.Encrypt(Input.IntegrationSecretKey),
            Country = Input.Country.Trim(),
            TraceabilityUserUdfName = string.IsNullOrWhiteSpace(Input.TraceabilityUserUdfName)
                ? null
                : Input.TraceabilityUserUdfName.Trim(),
        });

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Organizations/Companies/Index", new { organizationId });
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

        [Required(ErrorMessage = "Ingresa la clave de integración.")]
        [DataType(DataType.Password)]
        [Display(Name = "Clave de integración")]
        public string IntegrationSecretKey { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa el país.")]
        [Display(Name = "País")]
        public string Country { get; set; } = string.Empty;

        [Display(Name = "UDF de trazabilidad (dejar en blanco = U_PortalUser)")]
        [StringLength(50)]
        public string? TraceabilityUserUdfName { get; set; }
    }
}
