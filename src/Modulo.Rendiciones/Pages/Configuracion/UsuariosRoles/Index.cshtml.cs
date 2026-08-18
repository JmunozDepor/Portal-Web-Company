using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Pages.Configuracion.UsuariosRoles;

/// <summary>
/// Asigna los 2 roles explícitos de este módulo ("Aprobador"/"Administrador", ver
/// Models.RendicionesRoles) a los usuarios de la organización -- cierra el hueco real
/// de que /rendiciones/configuracion/* y /rendiciones/aprobaciones no tenían ningún
/// gate hasta esta entrega (ver RendicionesAdminPageModelBase/
/// RendicionesAprobadorPageModelBase). "Rendidor" no aparece acá porque es implícito
/// para cualquiera con el módulo habilitado -- no hay nada que asignar.
/// </summary>
public sealed class IndexModel : RendicionesAdminPageModelBase
{
    private readonly ITenantUserAdminService _users;
    private readonly IRendicionesUserRoleService _roles;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(ITenantUserAdminService users, IRendicionesUserRoleService roles,
        ICurrentUserContext currentUser, ICurrentCompanyAccessor currentCompany)
        : base(roles, currentUser, currentCompany)
    {
        _users = users;
        _roles = roles;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<UserRoleRow> Rows { get; private set; } = Array.Empty<UserRoleRow>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var users = await _users.ListAsync(ct);
        var rolesByUser = await _roles.ListAllAsync(_currentCompany.CompanyId, ct);

        Rows = users
            .OrderBy(u => u.Username)
            .Select(u =>
            {
                rolesByUser.TryGetValue(u.Id, out var assigned);
                assigned ??= Array.Empty<string>();
                return new UserRoleRow(
                    u.Id,
                    u.Username,
                    u.Email,
                    u.IsAdmin,
                    assigned.Contains(RendicionesRoles.Rendidor),
                    assigned.Contains(RendicionesRoles.Aprobador),
                    assigned.Contains(RendicionesRoles.Administrador));
            })
            .ToList();
    }

    public async Task<IActionResult> OnPostAsync(Guid userId, string role, bool granted, CancellationToken ct)
    {
        if (!RendicionesRoles.Assignable.Contains(role))
            return BadRequest();

        await _roles.SetRoleAsync(_currentCompany.CompanyId, userId, role, granted, ct);
        SuccessMessage = "Roles actualizados.";
        return RedirectToPage();
    }

    public sealed record UserRoleRow(Guid Id, string Username, string Email, bool IsPlatformAdmin, bool IsRendidor, bool IsAprobador, bool IsAdministrador);
}
