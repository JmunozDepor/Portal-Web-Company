using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;

namespace PortalSaas.Host.Pages.Admin.Plans;

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

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var plan = await _db.Plans.FindAsync(id);
        if (plan is null)
        {
            return NotFound();
        }

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
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var plan = await _db.Plans.FindAsync(Input.Id);
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

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Plans/Index");
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
    }
}
