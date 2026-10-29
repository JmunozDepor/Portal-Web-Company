using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.Ciudades;

public sealed class IndexModel : RendicionesAdminPageModelBase
{
    private readonly ICityService _cities;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(ICityService cities, IRendicionesUserRoleService roles,
        ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
        _cities = cities;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<City> Cities { get; private set; } = Array.Empty<City>();

    [BindProperty]
    public NewCityInput New { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Cities = await _cities.ListAllAsync(_currentCompany.CompanyId, ct);
    }

    public async Task<IActionResult> OnPostCrearAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Cities = await _cities.ListAllAsync(_currentCompany.CompanyId, ct);
            return Page();
        }

        try
        {
            await _cities.CreateAsync(_currentCompany.CompanyId, New.Name, ct);
            SuccessMessage = "Ciudad creada.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGuardarAsync(long id, string nombre, bool activo, CancellationToken ct)
    {
        try
        {
            await _cities.UpdateAsync(id, _currentCompany.CompanyId, nombre, activo, ct);
            SuccessMessage = "Ciudad actualizada.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDesactivarAsync(long id, string nombre, CancellationToken ct) =>
        await OnPostGuardarAsync(id, nombre, activo: false, ct);

    public async Task<IActionResult> OnPostActivarAsync(long id, string nombre, CancellationToken ct) =>
        await OnPostGuardarAsync(id, nombre, activo: true, ct);

    public sealed class NewCityInput
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;
    }
}
