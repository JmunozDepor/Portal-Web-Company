using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Gastos;

/// <summary>"Mis Gastos": todos los gastos del usuario, sueltos o ya en un informe.</summary>
public sealed class IndexModel : RendicionesPageModelBase
{
    private readonly IExpenseService _expenses;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IExpenseService expenses, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
    {
        _expenses = expenses;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<ExpenseReportLine> Expenses { get; private set; } = Array.Empty<ExpenseReportLine>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Expenses = await _expenses.ListAllAsync(_currentCompany.CompanyId, _currentUser.UserId, ct);
    }

    public static string StatusToShow(ExpenseReportLine expense) =>
        expense.Status == "Loose" ? "Loose" : expense.ExpenseReport?.Status ?? "En informe";
}
