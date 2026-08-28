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
public sealed class IndexModel : RendicionesAdminPageModelBase
{
    private readonly IUserCostCenterService _userCostCenters;
    private readonly ITenantUserAdminService _users;
    private readonly ICostCenterCatalogService _costCenters;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(IUserCostCenterService userCostCenters, ITenantUserAdminService users,
        ICostCenterCatalogService costCenters, IRendicionesUserRoleService roles, ICurrentUserContext currentUser,
        ICurrentCompanyAccessor currentCompany, ILogger<IndexModel> logger)
        : base(roles, currentUser, currentCompany)
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

    /// <summary>Listado completo -- fila por usuario de la organización, con conteo de centros asignados, para la tabla "Usuario / Correo / Centros asignados / Editar".</summary>
    public IReadOnlyList<TenantUserDto> Users { get; private set; } = Array.Empty<TenantUserDto>();
    public IReadOnlyDictionary<Guid, int> AssignedCountByUserId { get; private set; } = new Dictionary<Guid, int>();

    public string? SelectedUserName { get; private set; }
    public IReadOnlyList<Models.UserCostCenter> Assigned { get; private set; } = Array.Empty<Models.UserCostCenter>();
    public IReadOnlyList<CostCenterDto> SapCostCenters { get; private set; } = Array.Empty<CostCenterDto>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var users = await _users.ListAsync(ct);
        AvailableUsers = users.Select(u => new SelectListItem(u.Username, u.Id.ToString())).ToList();
        Users = users;

        // Una sola consulta agrupada para TODA la compañía -- la versión anterior hacía
        // una consulta por usuario (N round-trips contra la base externa del plugin),
        // lento de verdad cuando esa base vive en un servidor remoto real, no local.
        AssignedCountByUserId = await _userCostCenters.CountAssignedByUserAsync(_currentCompany.CompanyId, ct);

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
