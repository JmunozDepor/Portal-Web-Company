using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Self-service de usuarios de la PROPIA organización del usuario logueado (ver
/// ICurrentUserContext.OrganizationId) -- lo consume Modulo.Administracion (plugin).
/// A diferencia del backoffice de operador de plataforma (/Admin/*, PortalSaasDbContext
/// directo), esto nunca recibe un organizationId de la UI: siempre se resuelve del
/// usuario autenticado en la sesión actual.
///
/// Alcance deliberadamente reducido (decisión confirmada con el dueño del proyecto):
/// solo Usuarios. MenuGroup/Profile son catálogos GLOBALES de la plataforma (docs/03
/// §3) -- este servicio los LEE para poder asignarlos a un usuario por compañía, pero
/// nunca los crea/edita (eso sigue siendo exclusivo de /Admin/MenuGroups, /Admin/Profiles).
/// Instance/Company (credenciales técnicas SAP) tampoco se tocan acá.
/// </summary>
public interface ITenantUserAdminService
{
    Task<IReadOnlyList<TenantUserDto>> ListAsync(CancellationToken ct = default);

    Task<TenantUserDetailDto?> GetAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Verifica el límite de plan (IContractLimitService) antes de crear -- falla hacia lo
    /// más estricto. Sin parámetro de contraseña a propósito: el usuario nuevo la elige él
    /// mismo vía el correo de invitación que el llamador envía después de un resultado
    /// exitoso (mismo token de un solo uso que IPasswordResetService, ver
    /// ForgotPassword.cshtml.cs del Host para el patrón de referencia).
    /// </summary>
    Task<TenantUserOperationResult> CreateAsync(string username, string email, bool isAdmin, CancellationToken ct = default);

    /// <summary>Rechaza si userId es el propio usuario logueado y la operación le quitaría IsAdmin o IsActive (auto-bloqueo).</summary>
    Task<TenantUserOperationResult> UpdateAsync(Guid userId, string email, bool isAdmin, bool isActive, bool isLocked, CancellationToken ct = default);

    /// <summary>Rechaza si userId es el propio usuario logueado (no puede eliminarse a sí mismo).</summary>
    Task<TenantUserOperationResult> DeleteAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<CompanyOptionDto>> ListCompaniesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<MenuGroupOptionDto>> ListMenuGroupsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ProfileOptionDto>> ListProfilesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<LeafMenuDto>> ListLeafMenusAsync(CancellationToken ct = default);

    /// <summary>userId y companyId deben pertenecer a la organización actual -- si no, se trata como no encontrado.</summary>
    Task<UserPermissionsDto?> GetPermissionsAsync(Guid userId, Guid companyId, CancellationToken ct = default);

    Task<TenantUserOperationResult> SavePermissionsAsync(Guid userId, Guid companyId, IReadOnlyList<long> menuGroupIds, IReadOnlyDictionary<long, long?> profileByMenu, CancellationToken ct = default);

    /// <summary>
    /// Fija la compañía con la que este usuario entra automáticamente (SelectCompany
    /// se salta el paso de elegir) -- mismo campo que ya llena el propio usuario desde
    /// "Recordar esta compañía" en el login (ver Pages/Account/SelectCompany.cshtml.cs
    /// del Host), ahora también asignable por un admin de la organización. companyId
    /// null limpia el default (el usuario vuelve a elegir en cada login).
    /// </summary>
    Task<TenantUserOperationResult> SetDefaultCompanyAsync(Guid userId, Guid? companyId, CancellationToken ct = default);
}
