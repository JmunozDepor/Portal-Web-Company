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
    private readonly IUserCostCenterService _costCenters;
    private readonly IExpenseFundService _funds;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IExpenseReportService reports, IExpenseService expenses, IUserCostCenterService costCenters,
        IExpenseFundService funds, IRendicionesUserRoleService roles, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
        _reports = reports;
        _expenses = expenses;
        _costCenters = costCenters;
        _funds = funds;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<ExpenseReport> Reports { get; private set; } = Array.Empty<ExpenseReport>();
    public IReadOnlyList<ExpenseReportLine> LooseExpenses { get; private set; } = Array.Empty<ExpenseReportLine>();
    public IReadOnlyList<CostCenterDto> CostCenters { get; private set; } = Array.Empty<CostCenterDto>();
    public IReadOnlyList<ExpenseFund> OpenFunds { get; private set; } = Array.Empty<ExpenseFund>();

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
