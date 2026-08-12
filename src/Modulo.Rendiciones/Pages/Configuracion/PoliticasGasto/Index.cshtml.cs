using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.PoliticasGasto;

/// <summary>
/// Tope de monto por tipo de gasto (bloqueante o solo advertencia). La detección de
/// duplicados por número de documento no se configura acá -- corre siempre, no es una
/// política que se pueda activar/desactivar por tipo.
/// </summary>
public sealed class IndexModel : RendicionesPageModelBase
{
    private readonly IExpensePolicyService _policies;
    private readonly IExpenseTypeService _expenseTypes;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IExpensePolicyService policies, IExpenseTypeService expenseTypes, ICurrentCompanyAccessor currentCompany)
    {
        _policies = policies;
        _expenseTypes = expenseTypes;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<ExpensePolicy> Policies { get; private set; } = Array.Empty<ExpensePolicy>();
    public IReadOnlyList<ExpenseType> ExpenseTypesWithoutPolicy { get; private set; } = Array.Empty<ExpenseType>();

    [BindProperty]
    public NewPolicyInput New { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostCrearAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(ct);
            return Page();
        }

        try
        {
            await _policies.CreateAsync(_currentCompany.CompanyId, New.ExpenseTypeId, New.MaxAmount, New.IsBlocking, ct);
            SuccessMessage = "Política creada.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGuardarAsync(long id, decimal? montoMaximo, bool esBloqueante, bool activo, CancellationToken ct)
    {
        try
        {
            await _policies.UpdateAsync(id, _currentCompany.CompanyId, montoMaximo, esBloqueante, activo, ct);
            SuccessMessage = "Política actualizada.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEliminarAsync(long id, CancellationToken ct)
    {
        await _policies.DeleteAsync(id, _currentCompany.CompanyId, ct);
        SuccessMessage = "Política eliminada.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Policies = await _policies.ListAsync(_currentCompany.CompanyId, ct);
        var typesWithPolicy = Policies.Select(p => p.ExpenseTypeId).ToHashSet();
        ExpenseTypesWithoutPolicy = (await _expenseTypes.ListActiveAsync(_currentCompany.CompanyId, ct))
            .Where(t => !typesWithPolicy.Contains(t.Id))
            .ToList();
    }

    public sealed class NewPolicyInput
    {
        [Required(ErrorMessage = "Elegí un tipo de gasto.")]
        public long ExpenseTypeId { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? MaxAmount { get; set; }

        public bool IsBlocking { get; set; }
    }
}
