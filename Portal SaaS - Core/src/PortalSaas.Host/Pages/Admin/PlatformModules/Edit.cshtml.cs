using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.PlatformModules;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class EditModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public EditModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<Organization> Organizations { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var module = await _db.PlatformModules.FindAsync(id);
        if (module is null)
        {
            return NotFound();
        }

        Organizations = await _db.Organizations.OrderBy(o => o.LegalName).ToListAsync();

        Input = new InputModel
        {
            Id = module.Id,
            Code = module.Code,
            Name = module.Name,
            IsCore = module.IsCore,
            ExclusiveOrganizationId = module.ExclusiveOrganizationId,
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Organizations = await _db.Organizations.OrderBy(o => o.LegalName).ToListAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var module = await _db.PlatformModules.FindAsync(Input.Id);
        if (module is null)
        {
            return NotFound();
        }

        var code = Input.Code.Trim();
        var codeEnUso = await _db.PlatformModules.AnyAsync(m => m.Code == code && m.Id != Input.Id);
        if (codeEnUso)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Code)}", "Ya existe otro módulo con ese código.");
            return Page();
        }

        module.Code = code;
        module.Name = Input.Name.Trim();
        module.IsCore = Input.IsCore;
        module.ExclusiveOrganizationId = Input.IsCore ? null : Input.ExclusiveOrganizationId;

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/PlatformModules/Index");
    }

    public sealed class InputModel
    {
        public long Id { get; set; }

        [Required(ErrorMessage = "Ingresa el código.")]
        [Display(Name = "Código (debe coincidir con el OriginModule/ModuleCode real del plugin)")]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa el nombre.")]
        [Display(Name = "Nombre")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Core (incluido en todo plan, no se vende suelto)")]
        public bool IsCore { get; set; }

        [Display(Name = "Exclusivo de una organización (personalización puntual, nunca se vende a otra)")]
        public Guid? ExclusiveOrganizationId { get; set; }
    }
}
