using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Plans;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class CreateModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public CreateModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var code = Input.Code.Trim().ToLowerInvariant();
        var codeEnUso = await _db.Plans.AnyAsync(p => p.Code == code);
        if (codeEnUso)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Code)}", "Ya existe un plan con ese código.");
            return Page();
        }

        _db.Plans.Add(new Plan
        {
            Code = code,
            Name = Input.Name.Trim(),
            UserLimit = Input.UserLimit,
            CompanyLimit = Input.CompanyLimit,
            MonthlyTransactionLimit = Input.MonthlyTransactionLimit,
            MonthlyPrice = Input.MonthlyPrice,
            Currency = Input.Currency.Trim().ToUpperInvariant(),
            IsActive = Input.IsActive,
        });

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Plans/Index");
    }

    public sealed class InputModel
    {
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
