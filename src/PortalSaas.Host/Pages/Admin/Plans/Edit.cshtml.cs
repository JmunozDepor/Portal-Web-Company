using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Plans;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class EditModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly ICacheService _cache;

    public EditModel(PortalSaasDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<PlatformModule> AllModules { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var plan = await _db.Plans
            .Include(p => p.PlanModules)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (plan is null)
        {
            return NotFound();
        }

        await LoadModulesAsync();

        Input = new InputModel
        {
            Id = plan.Id,
            Code = plan.Code,
            Name = plan.Name,
            UserLimit = plan.UserLimit,
            CompanyLimit = plan.CompanyLimit,
            MonthlyTransactionLimit = plan.MonthlyTransactionLimit,
            MonthlyPrice = plan.MonthlyPrice,
            Currency = plan.Currency,
            IsActive = plan.IsActive,
            SelectedModuleIds = plan.PlanModules.Select(pm => pm.ModuleId).ToList(),
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadModulesAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var plan = await _db.Plans
            .Include(p => p.PlanModules)
            .FirstOrDefaultAsync(p => p.Id == Input.Id);
        if (plan is null)
        {
            return NotFound();
        }

        var code = Input.Code.Trim().ToLowerInvariant();
        var codeEnUso = await _db.Plans.AnyAsync(p => p.Code == code && p.Id != Input.Id);
        if (codeEnUso)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Code)}", "Ya existe otro plan con ese código.");
            return Page();
        }

        plan.Code = code;
        plan.Name = Input.Name.Trim();
        plan.UserLimit = Input.UserLimit;
        plan.CompanyLimit = Input.CompanyLimit;
        plan.MonthlyTransactionLimit = Input.MonthlyTransactionLimit;
        plan.MonthlyPrice = Input.MonthlyPrice;
        plan.Currency = Input.Currency.Trim().ToUpperInvariant();
        plan.IsActive = Input.IsActive;

        var seleccionados = (Input.SelectedModuleIds ?? []).ToHashSet();

        foreach (var pm in plan.PlanModules.Where(pm => !seleccionados.Contains(pm.ModuleId)).ToList())
        {
            plan.PlanModules.Remove(pm);
        }

        var yaAsignados = plan.PlanModules.Select(pm => pm.ModuleId).ToHashSet();
        foreach (var moduleId in seleccionados.Where(moduleId => !yaAsignados.Contains(moduleId)))
        {
            plan.PlanModules.Add(new PlanModule { PlanId = plan.Id, ModuleId = moduleId });
        }

        await _db.SaveChangesAsync();

        // Un plan afecta a TODAS las organizaciones suscritas/con licencia sobre él --
        // no se conoce acá la lista completa sin otra consulta, así que se invalida el
        // caché de módulos contratados de todas las organizaciones (más caro, pero
        // correcto). Ver ICacheService/Task 6, docs/superpowers/plans.
        _cache.RemoveByPrefix("modulos-contratados:");

        return RedirectToPage("/Admin/Plans/Index");
    }

    private async Task LoadModulesAsync()
    {
        AllModules = await _db.PlatformModules.OrderBy(m => m.Code).ToListAsync();
    }

    public sealed class InputModel
    {
        public long Id { get; set; }

        [Required(ErrorMessage = "Ingresa el código.")]
        [RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Solo minúsculas, dígitos y guiones.")]
        [Display(Name = "Código")]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresa el nombre.")]
        [Display(Name = "Nombre")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Límite de usuarios (vacío = ilimitado)")]
        public int? UserLimit { get; set; }

        [Display(Name = "Límite de compañías (vacío = ilimitado)")]
        public int? CompanyLimit { get; set; }

        [Display(Name = "Límite de transacciones/mes (vacío = ilimitado)")]
        public int? MonthlyTransactionLimit { get; set; }

        [Display(Name = "Precio mensual")]
        public decimal? MonthlyPrice { get; set; }

        [Required]
        [Display(Name = "Moneda")]
        public string Currency { get; set; } = "CLP";

        [Display(Name = "Activo")]
        public bool IsActive { get; set; } = true;

        public List<long> SelectedModuleIds { get; set; } = [];
    }
}
