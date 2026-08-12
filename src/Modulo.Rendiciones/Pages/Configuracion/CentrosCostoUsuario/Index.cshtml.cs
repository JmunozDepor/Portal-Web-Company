using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Pages.Configuracion.CentrosCostoUsuario;

/// <summary>
/// Qué centros de costo (dimensión 1 de SAP) puede usar cada colaborador. Sin ninguna
/// asignación acá, el colaborador ve el catálogo completo de SAP como fallback
/// (IUserCostCenterService.GetAvailableAsync) -- esta pantalla es para acotarlo cuando
/// Administración lo necesite, no un requisito para que el resto del módulo funcione.
/// </summary>
public sealed class IndexModel : RendicionesPageModelBase
{
    private readonly IUserCostCenterService _userCostCenters;
    private readonly ITenantUserAdminService _users;
    private readonly ICostCenterCatalogService _costCenters;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(IUserCostCenterService userCostCenters, ITenantUserAdminService users,
        ICostCenterCatalogService costCenters, ICurrentCompanyAccessor currentCompany, ILogger<IndexModel> logger)
    {
        _userCostCenters = userCostCenters;
        _users = users;
        _costCenters = costCenters;
        _currentCompany = currentCompany;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? UserId { get; set; }

    [BindProperty]
    public string? SelectedCostCenterCode { get; set; }

    public List<SelectListItem> AvailableUsers { get; set; } = new();
    public string? SelectedUserName { get; private set; }
    public IReadOnlyList<Models.UserCostCenter> Assigned { get; private set; } = Array.Empty<Models.UserCostCenter>();
    public IReadOnlyList<CostCenterDto> SapCostCenters { get; private set; } = Array.Empty<CostCenterDto>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var users = await _users.ListAsync(ct);
        AvailableUsers = users.Select(u => new SelectListItem(u.Username, u.Id.ToString())).ToList();

        if (string.IsNullOrEmpty(UserId) || !Guid.TryParse(UserId, out var userGuid))
            return;

        SelectedUserName = users.FirstOrDefault(u => u.Id.ToString() == UserId)?.Username;
        Assigned = await _userCostCenters.ListAssignedAsync(_currentCompany.CompanyId, userGuid, ct);
        SapCostCenters = await LoadCatalogSafeAsync(() => _costCenters.ListAsync(ct: ct), _logger, "Centro de Costos");
    }

    public async Task<IActionResult> OnPostAsignarAsync(CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(UserId) || !Guid.TryParse(UserId, out var userGuid))
                throw new InvalidOperationException("Elegí un colaborador.");
            if (string.IsNullOrWhiteSpace(SelectedCostCenterCode))
                throw new InvalidOperationException("Elegí un centro de costo.");

            var costCenters = await LoadCatalogSafeAsync(() => _costCenters.ListAsync(ct: ct), _logger, "Centro de Costos");
            var name = costCenters.FirstOrDefault(c => c.Code == SelectedCostCenterCode)?.Name;

            await _userCostCenters.AssignAsync(_currentCompany.CompanyId, userGuid, SelectedCostCenterCode, name, ct);
            SuccessMessage = "Centro de costo asignado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { UserId });
    }

    public async Task<IActionResult> OnPostQuitarAsync(long id, CancellationToken ct)
    {
        try
        {
            await _userCostCenters.RemoveAsync(id, _currentCompany.CompanyId, ct);
            SuccessMessage = "Asignación quitada.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage(new { UserId });
    }
}
