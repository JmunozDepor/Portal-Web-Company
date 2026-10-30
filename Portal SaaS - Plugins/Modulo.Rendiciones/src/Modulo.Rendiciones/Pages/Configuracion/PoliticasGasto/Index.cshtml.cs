using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.PoliticasGasto;

/// <summary>
/// Tope de monto por tipo de gasto, opcionalmente acotado a ciudad y/o perfil de política
/// (Task 5) -- ya NO es bloqueante: superar el tope siempre es solo una advertencia. Puede
/// haber varias políticas por tipo de gasto (una "default" sin ciudad/perfil y otras más
/// específicas); cuál aplica a un gasto concreto lo resuelve un motor aparte (fuera de
/// alcance acá). La detección de duplicados por número de documento no se configura acá --
/// corre siempre, no es una política que se pueda activar/desactivar por tipo.
/// </summary>
public sealed class IndexModel : RendicionesAdminPageModelBase
{
    private readonly IExpensePolicyService _policies;
    private readonly IExpenseTypeService _expenseTypes;
    private readonly ICityService _cities;
    private readonly IPolicyProfileService _policyProfiles;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IExpensePolicyService policies, IExpenseTypeService expenseTypes, ICityService cities,
        IPolicyProfileService policyProfiles, IRendicionesUserRoleService roles, ICurrentUserContext currentUser,
        ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
        _policies = policies;
        _expenseTypes = expenseTypes;
        _cities = cities;
        _policyProfiles = policyProfiles;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<ExpensePolicy> Policies { get; private set; } = Array.Empty<ExpensePolicy>();
    public IReadOnlyList<ExpenseType> ExpenseTypes { get; private set; } = Array.Empty<ExpenseType>();
    public IReadOnlyList<City> Cities { get; private set; } = Array.Empty<City>();
    public IReadOnlyList<PolicyProfile> PolicyProfiles { get; private set; } = Array.Empty<PolicyProfile>();

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
            await _policies.CreateAsync(_currentCompany.CompanyId, New.ExpenseTypeId, New.CityId, New.PolicyProfileId,
                New.Period, New.MaxAmount, ct);
            SuccessMessage = "Política creada.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGuardarAsync(long id, [Range(0, double.MaxValue, ErrorMessage = "El monto máximo no puede ser negativo.")] decimal? montoMaximo,
        bool activo, CancellationToken ct)
    {
        // I1b: un MaxAmount negativo produciría un tope negativo -> ReimbursementAmount
        // negativo en ExpensePolicyService.ValidateAsync (el clamp final ahí lo evita, pero
        // esta es la primera barrera, en el formulario de administración). [Range] sobre un
        // parámetro de handler SÍ participa en ModelState -- hay que chequearlo a mano, a
        // diferencia de un BindProperty donde algunos frameworks ya lo hacen por vos.
        if (!ModelState.IsValid)
        {
            ErrorMessage = "El monto máximo no puede ser negativo.";
            return RedirectToPage();
        }

        try
        {
            await _policies.UpdateAsync(id, _currentCompany.CompanyId, montoMaximo, activo, ct);
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
        ExpenseTypes = await _expenseTypes.ListActiveAsync(_currentCompany.CompanyId, ct);
        Cities = await _cities.ListActiveAsync(_currentCompany.CompanyId, ct);
        PolicyProfiles = await _policyProfiles.ListActiveAsync(_currentCompany.CompanyId, ct);
    }

    public sealed class NewPolicyInput
    {
        [Required(ErrorMessage = "Elegí un tipo de gasto.")]
        public long ExpenseTypeId { get; set; }

        /// <summary>Null = cualquier ciudad.</summary>
        public long? CityId { get; set; }

        /// <summary>Null = cualquier perfil.</summary>
        public long? PolicyProfileId { get; set; }

        [Required]
        public string Period { get; set; } = ExpensePolicyPeriod.PerDocument;

        [Range(0, double.MaxValue)]
        public decimal? MaxAmount { get; set; }
    }
}
