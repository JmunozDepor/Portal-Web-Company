using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Gastos;

/// <summary>"Mis Gastos": todos los gastos del usuario, sueltos o ya en un informe.</summary>
public sealed class IndexModel : RendicionesRendidorPageModelBase
{
    private readonly IExpenseService _expenses;
    private readonly IExpensePolicyService _policies;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IExpenseService expenses, IExpensePolicyService policies, IRendicionesUserRoleService roles,
        ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
        _expenses = expenses;
        _policies = policies;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<ExpenseReportLine> Expenses { get; private set; } = Array.Empty<ExpenseReportLine>();

    /// <summary>Última política activa por tipo de gasto -- para mostrar la columna "Política" sin
    /// disparar una consulta por fila (se carga una vez, ver OnGetAsync).</summary>
    private Dictionary<long, ExpensePolicy> PolicyByExpenseType { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Expenses = await _expenses.ListAllAsync(_currentCompany.CompanyId, _currentUser.UserId, ct);
        var policies = await _policies.ListAsync(_currentCompany.CompanyId, ct);
        PolicyByExpenseType = policies
            .Where(p => p.IsActive)
            .GroupBy(p => p.ExpenseTypeId)
            .ToDictionary(g => g.Key, g => g.Last());
    }

    /// <summary>Duplica un gasto SUELTO como base para uno nuevo -- mismo criterio que "Nuevo gasto"
    /// pero con los datos ya cargados, para no volver a tipear un gasto repetido (ej. el mismo taxi
    /// de todos los días). El original nunca se toca. Solo gastos propios y sueltos -- uno ya
    /// vinculado a un informe no tiene sentido "copiar" desde acá (se edita/copia desde el informe).</summary>
    public async Task<IActionResult> OnPostCopyAsync(long id, CancellationToken ct)
    {
        var source = await _expenses.GetAsync(id, _currentCompany.CompanyId, ct);
        if (source is null || source.UserId != _currentUser.UserId)
        {
            return NotFound();
        }

        var copy = new ExpenseReportLine
        {
            CompanyId = source.CompanyId,
            UserId = source.UserId,
            Status = "Loose",
            ExpenseTypeId = source.ExpenseTypeId,
            DocumentTypeId = source.DocumentTypeId,
            Date = DateTimeOffset.UtcNow,
            Amount = source.Amount,
            TaxAmount = source.TaxAmount,
            Currency = source.Currency,
            SupplierName = source.SupplierName,
            SupplierTaxId = source.SupplierTaxId,
        };

        await _expenses.CreateAsync(copy, ct);
        SuccessMessage = "Gasto copiado -- revisá la fecha y el comprobante antes de usarlo.";
        return RedirectToPage();
    }

    public string PolicyLabel(ExpenseReportLine expense)
    {
        if (expense.ExpenseTypeId is not { } typeId || !PolicyByExpenseType.TryGetValue(typeId, out var policy))
        {
            return "Sin política";
        }

        return policy.MaxAmount is { } max ? $"Tope {max:N0}" : "Sin tope";
    }

    public static string StatusToShow(ExpenseReportLine expense) =>
        expense.Status == "Loose" ? "Loose" : expense.ExpenseReport?.Status ?? "En informe";
}
