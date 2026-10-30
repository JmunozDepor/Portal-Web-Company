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
    private readonly IPolicyProfileService _policyProfiles;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IExpenseService expenses, IExpensePolicyService policies, IPolicyProfileService policyProfiles,
        IRendicionesUserRoleService roles, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
        _expenses = expenses;
        _policies = policies;
        _policyProfiles = policyProfiles;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<ExpenseReportLine> Expenses { get; private set; } = Array.Empty<ExpenseReportLine>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Expenses = await _expenses.ListAllAsync(_currentCompany.CompanyId, _currentUser.UserId, ct);
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

    /// <summary>I5: resuelve la política REAL aplicable a esta línea (misma resolución que
    /// ExpensePolicyService.ValidateAsync usa al guardar), no una "última política" arbitraria
    /// del tipo. Todas las filas de esta página son del usuario actual (dueño de "Mis
    /// Gastos"), así que su perfil se resuelve una vez por fila -- volumen bajo, no amerita
    /// cachearlo acá.</summary>
    public async Task<string> PolicyLabelAsync(ExpenseReportLine expense, CancellationToken ct = default)
    {
        if (expense.ExpenseTypeId is not { } typeId)
            return "Sin política";

        var userProfileId = await _policyProfiles.GetAssignedProfileIdAsync(_currentCompany.CompanyId, expense.UserId, ct);
        var policy = await _policies.FindApplicablePolicyAsync(_currentCompany.CompanyId, typeId, expense.CityId,
            userProfileId, ExpensePolicyPeriod.PerDocument, ct);
        return RendicionesUi.PolicyLabel(policy);
    }

    public static string StatusToShow(ExpenseReportLine expense) =>
        expense.Status == "Loose" ? "Loose" : expense.ExpenseReport?.Status ?? "En informe";
}
