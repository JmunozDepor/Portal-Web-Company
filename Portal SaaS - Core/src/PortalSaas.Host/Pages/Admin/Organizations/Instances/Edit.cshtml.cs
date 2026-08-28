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

    public IEnumerable<string> EngineTypes => InstanceEngineType.All;

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var instance = await _db.Instances.FindAsync(id);
        if (instance is null)
        {
            return NotFound();
        }

        Organization = (await _db.Organizations.FindAsync(instance.OrganizationId))!;

        Input = new InputModel
        {
            Id = instance.Id,
            Name = instance.Name,
            Host = instance.Host,
            Port = instance.Port,
            EngineType = instance.EngineType,
            TechnicalUsername = instance.TechnicalUsername,
            IsActive = instance.IsActive,
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var instance = await _db.Instances.FindAsync(Input.Id);
        if (instance is null)
        {
            return NotFound();
        }

        Organization = (await _db.Organizations.FindAsync(instance.OrganizationId))!;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var name = Input.Name.Trim();
        var nombreEnUso = await _db.Instances.AnyAsync(i => i.OrganizationId == instance.OrganizationId && i.Name == name && i.Id != Input.Id);
        if (nombreEnUso)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Name)}", "Ya existe otra instancia con ese nombre en esta organización.");
            return Page();
        }

        instance.Name = name;
        instance.Host = Input.Host.Trim();
        instance.Port = Input.Port;
        instance.EngineType = Input.EngineType;
        instance.TechnicalUsername = Input.TechnicalUsername.Trim();
        instance.IsActive = Input.IsActive;

        if (!string.IsNullOrWhiteSpace(Input.TechnicalSecretKey))
        {
            instance.TechnicalSecretKey = _secretoCifradoService.Encrypt(Input.TechnicalSecretKey);
        }

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Organizations/Instances/Index", new { organizationId = instance.OrganizationId });
    }

    public sealed class InputModel
    {
        public long Id { get; set; }

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

        [DataType(DataType.Password)]
        [Display(Name = "Nueva clave técnica (dejar en blanco para no cambiarla)")]
        public string? TechnicalSecretKey { get; set; }

        [Display(Name = "Activa")]
        public bool IsActive { get; set; } = true;
    }
}
