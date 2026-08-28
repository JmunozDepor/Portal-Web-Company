using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Pages.FondosPorRendir;

/// <summary>Listar y crear fondos por rendir del usuario logueado.</summary>
public sealed class IndexModel : RendicionesRendidorPageModelBase
{
    private readonly IExpenseFundService _funds;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly IUserCostCenterService _costCenters;

    public IndexModel(IExpenseFundService funds, ICurrentUserContext currentUser,
        ICurrentCompanyAccessor currentCompany, IUserCostCenterService costCenters, IRendicionesUserRoleService roles)
        : base(roles, currentUser, currentCompany)
    {
        _funds = funds;
        _currentUser = currentUser;
        _currentCompany = currentCompany;
        _costCenters = costCenters;
    }

    public IReadOnlyList<ExpenseFund> Funds { get; private set; } = Array.Empty<ExpenseFund>();

    public IReadOnlyList<CostCenterDto> CostCenters { get; private set; } = Array.Empty<CostCenterDto>();

    [BindProperty]
    public NewFundInput New { get; set; } = new();

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
            string? costCenterName = null;
            if (!string.IsNullOrWhiteSpace(New.CostCenterCode))
            {
                var centers = await _costCenters.GetAvailableAsync(_currentCompany.CompanyId, _currentUser.UserId, ct);
                costCenterName = centers.FirstOrDefault(c => c.Code == New.CostCenterCode)?.Name;
            }

            await _funds.CreateAsync(new ExpenseFund
            {
                CompanyId = _currentCompany.CompanyId,
                UserId = _currentUser.UserId,
                Currency = New.Currency,
                Amount = New.Amount,
                DeliveredAt = New.DeliveredAt,
                SettlementDueAt = New.SettlementDueAt,
                CostCenterCode = New.CostCenterCode,
                CostCenterName = costCenterName,
            }, ct);

            SuccessMessage = "Fondo por rendir registrado.";
            return RedirectToPage();
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
        Funds = await _funds.ListByUserAsync(_currentCompany.CompanyId, _currentUser.UserId, ct);
        CostCenters = await _costCenters.GetAvailableAsync(_currentCompany.CompanyId, _currentUser.UserId, ct);
    }

    public sealed class NewFundInput
    {
        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a 0.")]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(3, MinimumLength = 3, ErrorMessage = "Usar el código de moneda de 3 letras (ej. CLP, USD).")]
        public string Currency { get; set; } = "CLP";

        [Required]
        [DataType(DataType.Date)]
        public DateTimeOffset DeliveredAt { get; set; } = DateTimeOffset.UtcNow.Date;

        [DataType(DataType.Date)]
        public DateTimeOffset? SettlementDueAt { get; set; }

        public string? CostCenterCode { get; set; }
    }
}
