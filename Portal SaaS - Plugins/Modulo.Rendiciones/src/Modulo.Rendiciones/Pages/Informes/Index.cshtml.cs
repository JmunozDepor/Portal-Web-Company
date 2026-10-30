using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Pages.Informes;

/// <summary>
/// "Informes: seleccioná los gastos a exportar" -- lista los informes existentes y
/// permite armar uno nuevo eligiendo entre los gastos SUELTOS del usuario (ver
/// Pages/Gastos, donde se capturan).
/// </summary>
public sealed class IndexModel : RendicionesRendidorPageModelBase
{
    private readonly IExpenseReportService _reports;
    private readonly IExpenseService _expenses;
    private readonly IExpensePolicyService _policies;
    private readonly IPolicyProfileService _policyProfiles;
    private readonly IUserCostCenterService _costCenters;
    private readonly IExpenseFundService _funds;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IExpenseReportService reports, IExpenseService expenses, IExpensePolicyService policies,
        IPolicyProfileService policyProfiles, IUserCostCenterService costCenters, IExpenseFundService funds,
        IRendicionesUserRoleService roles, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
        _reports = reports;
        _expenses = expenses;
        _policies = policies;
        _policyProfiles = policyProfiles;
        _costCenters = costCenters;
        _funds = funds;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<ExpenseReport> Reports { get; private set; } = Array.Empty<ExpenseReport>();
    public IReadOnlyList<ExpenseReportLine> LooseExpenses { get; private set; } = Array.Empty<ExpenseReportLine>();
    public IReadOnlyList<CostCenterDto> CostCenters { get; private set; } = Array.Empty<CostCenterDto>();
    public IReadOnlyList<ExpenseFund> OpenFunds { get; private set; } = Array.Empty<ExpenseFund>();

    /// <summary>I5: misma resolución real de política que ExpensePolicyService.ValidateAsync
    /// usa al guardar (no una "última política" arbitraria del tipo) -- ver
    /// Pages/Gastos/IndexModel.PolicyLabelAsync, mismo criterio.</summary>
    public async Task<string> PolicyLabelAsync(ExpenseReportLine expense, CancellationToken ct = default)
    {
        if (expense.ExpenseTypeId is not { } typeId)
            return "Sin política";

        var userProfileId = await _policyProfiles.GetAssignedProfileIdAsync(_currentCompany.CompanyId, expense.UserId, ct);
        var policy = await _policies.FindApplicablePolicyAsync(_currentCompany.CompanyId, typeId, expense.CityId,
            userProfileId, ExpensePolicyPeriod.PerDocument, ct);
        return RendicionesUi.PolicyLabel(policy);
    }

    [BindProperty]
    public List<long> SelectedExpenseIds { get; set; } = new();

    [BindProperty]
    public string? CostCenterCode { get; set; }

    [BindProperty]
    public long? ExpenseFundId { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostCrearAsync(CancellationToken ct)
    {
        if (SelectedExpenseIds.Count == 0)
        {
            ErrorMessage = "Elegí al menos un gasto para armar el informe.";
            await LoadAsync(ct);
            return Page();
        }

        try
        {
            string? costCenterName = null;
            if (!string.IsNullOrWhiteSpace(CostCenterCode))
                costCenterName = CostCenters.FirstOrDefault(c => c.Code == CostCenterCode)?.Name
                    ?? (await _costCenters.GetAvailableAsync(_currentCompany.CompanyId, _currentUser.UserId, ct))
                        .FirstOrDefault(c => c.Code == CostCenterCode)?.Name;

            var id = await _reports.CreateReportAsync(_currentCompany.CompanyId, _currentUser.UserId,
                SelectedExpenseIds, ExpenseFundId, CostCenterCode, costCenterName, ct);

            return RedirectToPage("./Detalle", new { id });
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            await LoadAsync(ct);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Reports = await _reports.ListByUserAsync(_currentCompany.CompanyId, _currentUser.UserId, ct);
        LooseExpenses = await _expenses.ListLooseAsync(_currentCompany.CompanyId, _currentUser.UserId, ct);
        CostCenters = await _costCenters.GetAvailableAsync(_currentCompany.CompanyId, _currentUser.UserId, ct);
        OpenFunds = (await _funds.ListByUserAsync(_currentCompany.CompanyId, _currentUser.UserId, ct))
            .Where(f => f.Status == "Open")
            .ToList();
    }
}
