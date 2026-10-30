using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.PerfilesPolitica;

/// <summary>
/// Catálogo de Perfiles de Política de gasto (ej. "Supervisor en terreno"). Esta
/// pantalla NO asigna el perfil a ningún usuario -- eso se hace desde
/// Configuracion/UsuariosRoles, separado a propósito para no confundir este catálogo
/// con los 3 roles de acceso del módulo (ver Models/RendicionesRoles.cs).
/// </summary>
public sealed class IndexModel : RendicionesAdminPageModelBase
{
    private readonly IPolicyProfileService _profiles;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IPolicyProfileService profiles, IRendicionesUserRoleService roles,
        ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
        _profiles = profiles;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<PolicyProfile> Profiles { get; private set; } = Array.Empty<PolicyProfile>();

    [BindProperty]
    public NewPolicyProfileInput New { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Profiles = await _profiles.ListAllAsync(_currentCompany.CompanyId, ct);
    }

    public async Task<IActionResult> OnPostCrearAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Profiles = await _profiles.ListAllAsync(_currentCompany.CompanyId, ct);
            return Page();
        }

        try
        {
            await _profiles.CreateAsync(_currentCompany.CompanyId, New.Name, ct);
            SuccessMessage = "Perfil de política creado.";
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
            await _profiles.UpdateAsync(id, _currentCompany.CompanyId, nombre, activo, ct);
            SuccessMessage = "Perfil de política actualizado.";
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

    public sealed class NewPolicyProfileInput
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;
    }
}
