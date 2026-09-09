using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.UsuariosRoles;

/// <summary>
/// Quiénes forman parte del proceso de Rendiciones de la compañía activa y con qué
/// rol ("Rendidor"/"Aprobador"/"Administrador", ver Models.RendicionesRoles).
///
/// NO lista a todos los usuarios de la organización: un usuario "pertenece al proceso"
/// solo si tiene al menos un rol asignado (fila en rendiciones_user_roles). Para sumar
/// a alguien está el drawer "Agregar usuario al proceso" (OnPostAgregarAsync); quitarle
/// todos los roles y guardar lo saca del proceso. Un usuario que no está en la lista
/// recibe la página de restricción al entrar a cualquier pantalla del módulo (ver
/// RendicionesRolePageModelBase -> Pages/SinAcceso.cshtml).
///
/// La matriz no se auto-envía por checkbox: se marca/desmarca todo y recién "Guardar
/// cambios" calcula el diff contra la base y lo persiste en una sola transacción
/// (OnPostGuardarAsync + IRendicionesUserRoleService.ApplyRoleChangesAsync). Además,
/// un buscador client-side y un botón por fila para activar/desactivar la cuenta
/// (ITenantUserAdminService.UpdateAsync).
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

    /// <summary>Usuarios que YA forman parte del proceso (tienen al menos un rol).</summary>
    public IReadOnlyList<UserRoleRow> Rows { get; private set; } = Array.Empty<UserRoleRow>();

    /// <summary>Usuarios de la empresa que todavía no están en el proceso -- para el drawer de alta.</summary>
    public IReadOnlyList<UserOption> UsuariosDisponibles { get; private set; } = Array.Empty<UserOption>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        (Rows, UsuariosDisponibles) = await LoadAsync(ct);
    }

    /// <summary>
    /// Guardado por lotes de la matriz: recibe las casillas marcadas ("{userId}|{rol}")
    /// y aplica solo el diff contra lo que hay en la base. Recorre únicamente a los
    /// usuarios que YA son parte del proceso (los que tienen fila hoy) -- sumar a
    /// alguien nuevo es responsabilidad de OnPostAgregarAsync, no de acá. Si una fila
    /// queda con los 3 roles desmarcados, ese usuario sale del proceso.
    /// </summary>
    public async Task<IActionResult> OnPostGuardarAsync(List<string>? seleccion, CancellationToken ct)
    {
        var marcados = ParseSeleccion(seleccion);
        var rolesByUser = await _roles.ListAllAsync(_currentCompany.CompanyId, ct);

        var cambios = new List<(Guid UserId, string Role, bool Granted)>();
        foreach (var (userId, asignados) in rolesByUser)
        {
            foreach (var role in RendicionesRoles.Assignable)
            {
                var desired = marcados.Contains((userId, role));
                var current = asignados.Contains(role);
                if (desired != current)
                    cambios.Add((userId, role, desired));
            }
        }

        if (cambios.Count == 0)
        {
            SuccessMessage = "No había cambios que guardar.";
            return RedirectToPage();
        }

        await _roles.ApplyRoleChangesAsync(_currentCompany.CompanyId, cambios, ct);

        var sacados = rolesByUser.Keys.Count(uid => RendicionesRoles.Assignable.All(r => !marcados.Contains((uid, r))));
        SuccessMessage = sacados > 0
            ? $"Cambios guardados. {sacados} usuario(s) quedaron fuera del proceso."
            : $"Cambios guardados ({cambios.Count} ajuste{(cambios.Count == 1 ? "" : "s")} de rol).";
        return RedirectToPage();
    }

    /// <summary>Suma un usuario de la empresa al proceso con uno o más roles iniciales.</summary>
    public async Task<IActionResult> OnPostAgregarAsync(Guid nuevoUsuarioId, List<string>? rolesIniciales, CancellationToken ct)
    {
        if (nuevoUsuarioId == Guid.Empty)
        {
            ErrorMessage = "Elegí un usuario para agregar.";
            return RedirectToPage();
        }

        var user = await _users.GetAsync(nuevoUsuarioId, ct);
        if (user is null)
        {
            ErrorMessage = "Usuario no encontrado en esta organización.";
            return RedirectToPage();
        }

        if (user.IsAdmin)
        {
            ErrorMessage = $"\"{user.Username}\" es administrador de la organización -- ya tiene acceso a todo el módulo, no hace falta agregarlo.";
            return RedirectToPage();
        }

        var yaAsignados = await _roles.ListRolesAsync(_currentCompany.CompanyId, nuevoUsuarioId, ct);
        if (yaAsignados.Count > 0)
        {
            ErrorMessage = $"\"{user.Username}\" ya forma parte del proceso.";
            return RedirectToPage();
        }

        var roles = (rolesIniciales ?? new List<string>())
            .Where(RendicionesRoles.Assignable.Contains)
            .Distinct()
            .ToList();

        if (roles.Count == 0)
        {
            ErrorMessage = "Elegí al menos un rol para el usuario (Rendidor, Aprobador o Administrador).";
            return RedirectToPage();
        }

        await _roles.ApplyRoleChangesAsync(
            _currentCompany.CompanyId,
            roles.Select(r => (nuevoUsuarioId, r, true)).ToList(),
            ct);

        SuccessMessage = $"\"{user.Username}\" agregado al proceso de Rendiciones.";
        return RedirectToPage();
    }

    /// <summary>Activa o desactiva la cuenta del usuario (conserva el resto de sus datos).</summary>
    public async Task<IActionResult> OnPostToggleActivoAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.GetAsync(userId, ct);
        if (user is null)
        {
            ErrorMessage = "Usuario no encontrado en esta organización.";
            return RedirectToPage();
        }

        var result = await _users.UpdateAsync(userId, user.Email, user.IsAdmin, !user.IsActive, user.IsLocked, ct);
        if (result.IsSuccess)
            SuccessMessage = user.IsActive ? $"Usuario \"{user.Username}\" desactivado." : $"Usuario \"{user.Username}\" activado.";
        else
            ErrorMessage = result.Reason ?? "No se pudo actualizar el usuario.";

        return RedirectToPage();
    }

    private async Task<(IReadOnlyList<UserRoleRow> Rows, IReadOnlyList<UserOption> Disponibles)> LoadAsync(CancellationToken ct)
    {
        var users = await _users.ListAsync(ct);
        var rolesByUser = await _roles.ListAllAsync(_currentCompany.CompanyId, ct);

        var rows = users
            .Where(u => rolesByUser.ContainsKey(u.Id))
            .OrderBy(u => u.Username)
            .Select(u =>
            {
                var asignados = rolesByUser[u.Id];
                return new UserRoleRow(
                    u.Id,
                    u.Username,
                    u.Email,
                    u.IsAdmin,
                    u.IsActive,
                    u.IsLocked,
                    asignados.Contains(RendicionesRoles.Rendidor),
                    asignados.Contains(RendicionesRoles.Aprobador),
                    asignados.Contains(RendicionesRoles.Administrador));
            })
            .ToList();

        var disponibles = users
            .Where(u => !u.IsAdmin && !rolesByUser.ContainsKey(u.Id))
            .OrderBy(u => u.Username)
            .Select(u => new UserOption(u.Id, u.Username, u.Email))
            .ToList();

        return (rows, disponibles);
    }

    private static HashSet<(Guid, string)> ParseSeleccion(List<string>? seleccion)
    {
        var set = new HashSet<(Guid, string)>();
        if (seleccion is null)
            return set;

        foreach (var item in seleccion)
        {
            var parts = item.Split('|', 2);
            if (parts.Length == 2 && Guid.TryParse(parts[0], out var id) && RendicionesRoles.Assignable.Contains(parts[1]))
                set.Add((id, parts[1]));
        }

        return set;
    }

    public sealed record UserRoleRow(Guid Id, string Username, string Email, bool IsPlatformAdmin, bool IsActive, bool IsLocked, bool IsRendidor, bool IsAprobador, bool IsAdministrador);

    public sealed record UserOption(Guid Id, string Username, string Email);
}
