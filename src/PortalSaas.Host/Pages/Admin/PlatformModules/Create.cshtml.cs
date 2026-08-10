using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.PlatformModules;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class CreateModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly PluginManager _pluginManager;

    public CreateModel(PortalSaasDbContext db, PluginManager pluginManager)
    {
        _db = db;
        _pluginManager = pluginManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<Organization> Organizations { get; private set; } = [];

    /// <summary>
    /// Plugins REALMENTE cargados en este proceso (PluginManager.ModulosCargados) que
    /// todavía no tienen fila en PlatformModule -- antes Input.Code era texto libre
    /// ("debe coincidir con el OriginModule/ModuleCode real del plugin"), lo que dejaba
    /// tipear un código con un typo y crear un PlatformModule que MenuNavigationService
    /// nunca iba a poder matchear contra ningún plugin real. Filtrar los ya usados evita
    /// además crear un duplicado (mismo chequeo que ya hacía OnPostAsync, ver codeEnUso,
    /// pero ahora también oculto en el selector antes de intentarlo).
    /// </summary>
    public List<IModuloPortal> AvailablePlugins { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Organizations = await _db.Organizations.OrderBy(o => o.LegalName).ToListAsync();
        await LoadAvailablePluginsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Organizations = await _db.Organizations.OrderBy(o => o.LegalName).ToListAsync();
        await LoadAvailablePluginsAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var code = Input.Code.Trim();
        var codeEnUso = await _db.PlatformModules.AnyAsync(m => m.Code == code);
        if (codeEnUso)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Code)}", "Ya existe otro módulo con ese código.");
            return Page();
        }

        var module = new PlatformModule
        {
            Code = code,
            Name = Input.Name.Trim(),
            IsCore = Input.IsCore,
            ExclusiveOrganizationId = Input.IsCore ? null : Input.ExclusiveOrganizationId,
        };

        _db.PlatformModules.Add(module);
        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/PlatformModules/Index");
    }

    private async Task LoadAvailablePluginsAsync()
    {
        var codesEnUso = await _db.PlatformModules.Select(m => m.Code).ToListAsync();
        AvailablePlugins = _pluginManager.ModulosCargados
            .Where(p => !codesEnUso.Contains(p.ModuleCode, StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p.Name)
            .ToList();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Elegí el plugin.")]
        [Display(Name = "Código (ModuleCode del plugin realmente cargado)")]
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
