using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.TiposGasto;

public sealed class IndexModel : RendicionesPageModelBase
{
    private readonly IExpenseTypeService _expenseTypes;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly IGeneralLedgerAccountCatalogService _accounts;

    public IndexModel(IExpenseTypeService expenseTypes, ICurrentCompanyAccessor currentCompany, IGeneralLedgerAccountCatalogService accounts)
    {
        _expenseTypes = expenseTypes;
        _currentCompany = currentCompany;
        _accounts = accounts;
    }

    /// <summary>Búsqueda en vivo de Cuenta Mayor contra el plan de cuentas real de SAP -- mismo
    /// patrón que Modulo.Ventas/Compras (catalog-search.js + wireCatalogSearch, minChars: 0
    /// porque el plan de cuentas es un catálogo chico, precarga completa al foco).</summary>
    public async Task<JsonResult> OnGetSearchAccountsAsync(string text, CancellationToken ct)
    {
        var accounts = await _accounts.ListAsync(text, 30, ct);
        return new JsonResult(accounts.Select(a => new { a.AccountCode, a.AccountName }));
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
