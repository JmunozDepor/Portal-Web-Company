using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Gastos;

/// <summary>
/// Autorización compartida entre el visor (Comprobante) y el endpoint de archivo
/// crudo (ComprobanteArchivo) -- dueño del gasto, administrador del portal, o
/// cualquier aprobador (de cualquier nivel) del grupo del informe al que pertenezca,
/// para poder revisar la boleta real antes de decidir y seguir viéndola después de
/// resuelto el caso. No deriva de RendicionesPageModelBase (esas dos páginas no usan
/// TempData/mensajes), así que [Authorize] se declara acá aparte -- mismo motivo
/// documentado en RendicionesPageModelBase.
/// </summary>
[Authorize]
public abstract class ComprobanteAccesoBase : PageModel
{
    private readonly IExpenseService _expenses;
    private readonly IExpenseApprovalGroupService _groups;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;

    protected ComprobanteAccesoBase(IExpenseService expenses, IExpenseApprovalGroupService groups,
        ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
    {
        _expenses = expenses;
        _groups = groups;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    protected async Task<ExpenseReceipt?> GetAuthorizedReceiptAsync(long expenseId, CancellationToken ct)
    {
        var expense = await _expenses.GetAsync(expenseId, _currentCompany.CompanyId, ct);
        if (expense?.ExpenseReceipt is null)
            return null;

        var authorized = expense.UserId == _currentUser.UserId || _currentUser.IsAdmin;
        if (!authorized && expense.ExpenseReport?.ExpenseApprovalGroupId is { } groupId)
        {
            var levels = await _groups.GetLevelsAsync(groupId, ct);
            authorized = levels.Values.Contains(_currentUser.UserId);
        }

        return authorized ? expense.ExpenseReceipt : null;
    }
}
