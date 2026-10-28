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
    private readonly ITenantUserAdminService _users;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public DetalleModel(IExpenseReportService reports, IExpenseFundService funds, IExpenseService expenses,
        IUserCostCenterService costCenters, IExpenseApprovalGroupService groups, ITenantUserAdminService users,
        IRendicionesUserRoleService roles, ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
        _reports = reports;
        _funds = funds;
        _expenses = expenses;
        _costCenters = costCenters;
        _groups = groups;
        _users = users;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
    }

    public ExpenseReport Report { get; private set; } = null!;

    public CostCenterDto[] CostCenters { get; private set; } = Array.Empty<CostCenterDto>();
    public IReadOnlyList<ExpenseFund> OpenFunds { get; private set; } = Array.Empty<ExpenseFund>();
    public IReadOnlyList<ExpenseReportLine> LooseExpensesToAdd { get; private set; } = Array.Empty<ExpenseReportLine>();
    public IReadOnlyList<ExpenseReportAction> History { get; private set; } = Array.Empty<ExpenseReportAction>();

    private IReadOnlyDictionary<Guid, string> NameByUserId { get; set; } = new Dictionary<Guid, string>();

    /// <summary>"Juan Pérez (jmunoz)" -- quién envió el informe o actuó en una fila del
    /// historial. Pedido explícito, 2026-10-26: identificar sin ambigüedad cuando hay
    /// varios usuarios. Cae al Id en bruto si por algo no se pudo resolver (usuario
    /// eliminado, por ejemplo).</summary>
    public string UserDisplayName(Guid userId) => NameByUserId.GetValueOrDefault(userId, userId.ToString());

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
            Purpose = Report.Purpose,
            InternalNumber = Report.InternalNumber,
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
                Header.CostCenterCode, costCenterName, Header.Purpose, Header.InternalNumber, ct);
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

        var history = await _reports.ListHistoryAsync(id, ct);
        // Un aprobador que YA actuó (aprobó/rechazó un nivel) debe poder seguir viendo el
        // informe después -- "isCurrentApprover" solo es true mientras el informe sigue
        // Pending EN SU nivel, así que al aprobar el último nivel (o cualquier nivel que no
        // sea el final) el estado avanza/cambia y el aprobador que recién actuó quedaba
        // bloqueado por este mismo chequeo -- el síntoma reportado era un 404 justo al
        // redirigir después de aprobar (bug real, 2026-10-26).
        var wasApprover = history.Any(a => a.UserId == _currentUser.UserId);

        // Solo el dueño, un administrador del portal, el aprobador del nivel actual, o
        // alguien que ya aprobó/rechazó algún nivel de este informe pueden ver/editar.
        if (report.UserId != _currentUser.UserId && !_currentUser.IsAdmin && !isCurrentApprover && !wasApprover)
            return false;

        Report = report;
        IsCurrentApprover = isCurrentApprover;
        CostCenters = (await _costCenters.GetAvailableAsync(_currentCompany.CompanyId, report.UserId, ct)).ToArray();
        OpenFunds = (await _funds.ListByUserAsync(_currentCompany.CompanyId, report.UserId, ct))
            .Where(f => f.Status == "Open")
            .ToList();
        LooseExpensesToAdd = await _expenses.ListLooseAsync(_currentCompany.CompanyId, report.UserId, ct);
        History = history;

        // ITenantUserAdminService.ListAsync() ya está acotado a la organización actual
        // (mismo patrón que Pages/Aprobaciones/Index y Servicios/ClosingReportService).
        var users = await _users.ListAsync(ct);
        NameByUserId = users.DistinctBy(u => u.Id)
            .ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.Username : $"{u.FullName} ({u.Username})");

        return true;
    }

    public sealed class HeaderInput
    {
        public string? CostCenterCode { get; set; }
        public long? ExpenseFundId { get; set; }

        [System.ComponentModel.DataAnnotations.StringLength(500)]
        public string? Purpose { get; set; }

        [System.ComponentModel.DataAnnotations.StringLength(50)]
        public string? InternalNumber { get; set; }
    }
}
