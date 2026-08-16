using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.TiposGasto;

public sealed class IndexModel : RendicionesPageModelBase
{
    private readonly IExpenseTypeService _expenseTypes;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly RendicionesDbContext _db;

    public IndexModel(IExpenseTypeService expenseTypes, ICurrentCompanyAccessor currentCompany, RendicionesDbContext db)
    {
        _expenseTypes = expenseTypes;
        _currentCompany = currentCompany;
        _db = db;
    }

    /// <summary>Búsqueda en vivo de Cuenta Mayor contra el catálogo local (rendiciones_gl_accounts),
    /// mantenido sincronizado desde SAP por un job desatendido -- mismo patrón de UI que
    /// Modulo.Ventas/Compras (catalog-search.js + wireCatalogSearch, minChars: 0 porque el plan de
    /// cuentas es un catálogo chico, precarga completa al foco).</summary>
    public async Task<JsonResult> OnGetSearchAccountsAsync(string text, CancellationToken ct)
    {
        var query = _db.GlAccounts.Where(a => a.CompanyId == _currentCompany.CompanyId && a.IsActive);
        if (!string.IsNullOrWhiteSpace(text))
            query = query.Where(a => a.Code.Contains(text) || a.Name.Contains(text));

        var accounts = await query.OrderBy(a => a.Name).Take(30).ToListAsync(ct);
        return new JsonResult(accounts.Select(a => new { AccountCode = a.Code, AccountName = a.Name }));
    }

    public IReadOnlyList<ExpenseType> Types { get; private set; } = Array.Empty<ExpenseType>();

    [BindProperty]
    public NewExpenseTypeInput New { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Types = await _expenseTypes.ListAllAsync(_currentCompany.CompanyId, ct);
    }

    public async Task<IActionResult> OnPostCrearAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Types = await _expenseTypes.ListAllAsync(_currentCompany.CompanyId, ct);
            return Page();
        }

        await _expenseTypes.CreateAsync(_currentCompany.CompanyId, New.Name, New.SapGlAccount, New.IsMileage, New.RatePerKm, ct);
        SuccessMessage = "Tipo de gasto creado.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGuardarAsync(long id, string nombre, string? cuentaContableSap, bool activo, bool esKilometraje, decimal? tarifaPorKm, CancellationToken ct) =>
        await UpdateAsync(id, nombre, cuentaContableSap, activo, esKilometraje, tarifaPorKm, ct);

    public async Task<IActionResult> OnPostDesactivarAsync(long id, string nombre, string? cuentaContableSap, bool esKilometraje, decimal? tarifaPorKm, CancellationToken ct) =>
        await UpdateAsync(id, nombre, cuentaContableSap, activo: false, esKilometraje, tarifaPorKm, ct);

    public async Task<IActionResult> OnPostActivarAsync(long id, string nombre, string? cuentaContableSap, bool esKilometraje, decimal? tarifaPorKm, CancellationToken ct) =>
        await UpdateAsync(id, nombre, cuentaContableSap, activo: true, esKilometraje, tarifaPorKm, ct);

    private async Task<IActionResult> UpdateAsync(long id, string nombre, string? cuentaContableSap, bool activo, bool esKilometraje, decimal? tarifaPorKm, CancellationToken ct)
    {
        try
        {
            await _expenseTypes.UpdateAsync(id, _currentCompany.CompanyId, nombre, cuentaContableSap, activo, esKilometraje, tarifaPorKm, ct);
            SuccessMessage = "Tipo de gasto actualizado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public sealed class NewExpenseTypeInput
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(30)]
        public string? SapGlAccount { get; set; }

        public bool IsMileage { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? RatePerKm { get; set; }
    }
}
