using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Pages.Informes;

public sealed class DetalleModel : RendicionesRendidorOAprobadorPageModelBase
{
    private readonly IExpenseReportService _reports;
    private readonly IExpenseFundService _funds;
    private readonly IExpenseService _expenses;
    private readonly IUserCostCenterService _costCenters;
    private readonly IExpenseApprovalGroupService _groups;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public DetalleModel(IExpenseReportService reports, IExpenseFundService funds, IExpenseService expenses,
        IUserCostCenterService costCenters, IExpenseApprovalGroupService groups, IRendicionesUserRoleService roles,
        ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
        _reports = reports;
        _funds = funds;
        _expenses = expenses;
        _costCenters = costCenters;
        _groups = groups;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    public ExpenseReport Report { get; private set; } = null!;

    public CostCenterDto[] CostCenters { get; private set; } = Array.Empty<CostCenterDto>();
    public IReadOnlyList<ExpenseFund> OpenFunds { get; private set; } = Array.Empty<ExpenseFund>();
    public IReadOnlyList<ExpenseReportLine> LooseExpensesToAdd { get; private set; } = Array.Empty<ExpenseReportLine>();
    public IReadOnlyList<ExpenseReportAction> History { get; private set; } = Array.Empty<ExpenseReportAction>();

    public bool IsOwner => Report.UserId == _currentUser.UserId;
    public bool IsEditable => Report.Status == "Draft";
    public bool CanReopen => Report.Status == "Rejected" && IsOwner;
    public bool IsCurrentApprover { get; private set; }

    [BindProperty]
    public HeaderInput Header { get; set; } = new();

    [BindProperty]
    public List<long> ExpenseIdsToAdd { get; set; } = new();

    [BindProperty]
    public string? Comment { get; set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct)
    {
        if (!await LoadAsync(id, ct))
            return NotFound();

        Header = new HeaderInput
        {
            CostCenterCode = Report.CostCenterCode,
            ExpenseFundId = Report.ExpenseFundId,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostGuardarCabeceraAsync(long id, CancellationToken ct)
    {
        if (!await LoadAsync(id, ct))
            return NotFound();

        try
        {
            var costCenterName = CostCenters.FirstOrDefault(c => c.Code == Header.CostCenterCode)?.Name;

            await _reports.UpdateHeaderAsync(id, _currentCompany.CompanyId, Header.ExpenseFundId,
                Header.CostCenterCode, costCenterName, ct);
            SuccessMessage = "Cabecera actualizada.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAgregarGastosAsync(long id, CancellationToken ct)
    {
        try
        {
            await _reports.AttachExpensesAsync(id, _currentCompany.CompanyId, ExpenseIdsToAdd, ct);
            SuccessMessage = "Gastos agregados al informe.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDesvincularGastoAsync(long id, long lineId, CancellationToken ct)
    {
        try
        {
            await _reports.DetachExpenseAsync(id, lineId, _currentCompany.CompanyId, ct);
            SuccessMessage = "Gasto quitado del informe -- volvió a \"Mis Gastos\" como suelto.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostQuitarComprobanteAsync(long id, long lineId, CancellationToken ct)
    {
        try
        {
            await _reports.RemoveReceiptAsync(id, lineId, _currentCompany.CompanyId, ct);
            SuccessMessage = "Documento quitado del gasto.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostEliminarAsync(long id, CancellationToken ct)
    {
        try
        {
            await _reports.DeleteReportAsync(id, _currentCompany.CompanyId, _currentUser.UserId, ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            return RedirectToPage(new { id });
        }

        return RedirectToPage("./Index");
    }

    public async Task<IActionResult> OnPostEnviarAsync(long id, CancellationToken ct)
    {
        try
        {
            await _reports.SubmitAsync(id, _currentCompany.CompanyId, ct);
            SuccessMessage = "Informe enviado a aprobación.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAprobarAsync(long id, CancellationToken ct)
    {
        try
        {
            await _reports.ApproveAsync(id, _currentCompany.CompanyId, _currentUser.UserId, Comment, ct);
            SuccessMessage = "Informe aprobado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRechazarAsync(long id, CancellationToken ct)
    {
        try
        {
            await _reports.RejectAsync(id, _currentCompany.CompanyId, _currentUser.UserId, Comment, ct);
            SuccessMessage = "Informe rechazado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostReabrirAsync(long id, CancellationToken ct)
    {
        try
        {
            await _reports.ReopenAsync(id, _currentCompany.CompanyId, _currentUser.UserId, ct);
            SuccessMessage = "Informe reabierto -- podés corregirlo y reenviarlo.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(long id, CancellationToken ct)
    {
        var report = await _reports.GetAsync(id, _currentCompany.CompanyId, ct);
        if (report is null)
            return false;

        var isCurrentApprover = false;
        if (report.Status == "Pending" && report.ExpenseApprovalGroupId is { } groupId && report.CurrentLevel is { } level)
        {
            var levels = await _groups.GetLevelsAsync(groupId, ct);
            isCurrentApprover = levels.TryGetValue(level, out var approverId) && approverId == _currentUser.UserId;
        }

        // Solo el dueño, un administrador del portal, o el aprobador del nivel actual
        // pueden ver/editar el informe.
        if (report.UserId != _currentUser.UserId && !_currentUser.IsAdmin && !isCurrentApprover)
            return false;

        Report = report;
        IsCurrentApprover = isCurrentApprover;
        CostCenters = (await _costCenters.GetAvailableAsync(_currentCompany.CompanyId, report.UserId, ct)).ToArray();
        OpenFunds = (await _funds.ListByUserAsync(_currentCompany.CompanyId, report.UserId, ct))
            .Where(f => f.Status == "Open")
            .ToList();
        LooseExpensesToAdd = await _expenses.ListLooseAsync(_currentCompany.CompanyId, report.UserId, ct);
        History = await _reports.ListHistoryAsync(id, ct);
        return true;
    }

    public sealed class HeaderInput
    {
        public string? CostCenterCode { get; set; }
        public long? ExpenseFundId { get; set; }
    }
}
