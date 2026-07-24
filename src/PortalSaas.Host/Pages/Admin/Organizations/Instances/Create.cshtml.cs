using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Instances;

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

    public IEnumerable<string> EngineTypes => InstanceEngineType.All;

    public async Task<IActionResult> OnGetAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;
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

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var name = Input.Name.Trim();
        var nombreEnUso = await _db.Instances.AnyAsync(i => i.OrganizationId == organizationId && i.Name == name);
        if (nombreEnUso)
        {
            ModelState.AddModelError(nameof(Input.Name), "Ya existe una instancia con ese nombre en esta organización.");
            return Page();
        }

        _db.Instances.Add(new Instance
        {
            OrganizationId = organizationId,
            Name = name,
            Host = Input.Host.Trim(),
            Port = Input.Port,
            EngineType = Input.EngineType,
            TechnicalUsername = Input.TechnicalUsername.Trim(),
            TechnicalSecretKey = _secretoCifradoService.Encrypt(Input.TechnicalSecretKey),
        });

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Organizations/Instances/Index", new { organizationId });
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Ingresa el nombre de la instancia.")]
        [Display(Name = "Nombre")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa el host.")]
        [Display(Name = "Host")]
        public string Host { get; set; } = string.Empty;

        [Required]
        [Range(1, 65535, ErrorMessage = "Puerto inválido.")]
        [Display(Name = "Puerto")]
        public int Port { get; set; } = 30015;

        [Required]
        [Display(Name = "Motor")]
        public string EngineType { get; set; } = InstanceEngineType.Hana;

        [Required(ErrorMessage = "Ingresa el usuario técnico.")]
        [Display(Name = "Usuario técnico")]
        public string TechnicalUsername { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa la clave técnica.")]
        [DataType(DataType.Password)]
        [Display(Name = "Clave técnica")]
        public string TechnicalSecretKey { get; set; } = string.Empty;
    }
}
