using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Administracion;

/// <summary>
/// Implementación real de ITenantUserAdminService -- ver su doc-comment para el
/// alcance (solo Usuarios, acotado a ICurrentUserContext.OrganizationId). Reusa contra
/// PortalSaasDbContext la misma lógica ya probada en
/// Pages/Admin/Organizations/Users/Create.cshtml.cs y Permissions.cshtml.cs del
/// backoffice de operador de plataforma, con dos diferencias: (1) la organización
/// nunca viene de un parámetro, siempre de la sesión actual; (2) auto-bloqueo -- un
/// admin de organización no puede quitarse a sí mismo el acceso ni eliminarse.
/// </summary>
public sealed class TenantUserAdminService : ITenantUserAdminService
{
    private readonly PortalSaasDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IContractLimitService _contractLimitService;

    public TenantUserAdminService(PortalSaasDbContext db, ICurrentUserContext currentUser, IContractLimitService contractLimitService)
    {
        _db = db;
        _currentUser = currentUser;
        _contractLimitService = contractLimitService;
    }

    public async Task<IReadOnlyList<TenantUserDto>> ListAsync(CancellationToken ct = default)
    {
        return await _db.Users
            .Where(u => u.OrganizationId == _currentUser.OrganizationId)
            .OrderBy(u => u.Username)
            .Select(u => new TenantUserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                IsAdmin = u.IsAdmin,
                IsActive = u.IsActive,
                IsLocked = u.IsLocked,
                LastLoginAt = u.LastLoginAt,
            })
            .ToListAsync(ct);
    }

    public async Task<TenantUserDetailDto?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await FindOwnUserAsync(userId, ct);
        return user is null
            ? null
            : new TenantUserDetailDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                IsAdmin = user.IsAdmin,
                IsActive = user.IsActive,
                IsLocked = user.IsLocked,
            };
    }

    public async Task<TenantUserOperationResult> CreateAsync(string username, string email, string password, bool isAdmin, CancellationToken ct = default)
    {
        var organizationId = _currentUser.OrganizationId;

        // Regla dura del proyecto: los límites de plan se hacen cumplir en código, no
        // solo se documentan -- si no se puede verificar, se bloquea (ver IContractLimitService).
        var limitCheck = await _contractLimitService.CheckUserLimitAsync(organizationId, ct);
        if (!limitCheck.IsAllowed)
        {
            return TenantUserOperationResult.Failure(limitCheck.Reason!);
        }

        var normalizedUsername = username.Trim();
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var usernameEnUso = await _db.Users.AnyAsync(u => u.OrganizationId == organizationId && u.Username.ToLower() == normalizedUsername.ToLower(), ct);
        if (usernameEnUso)
        {
            return TenantUserOperationResult.Failure("Ya existe un usuario con ese nombre en tu organización.");
        }

        var emailEnUso = await _db.Users.AnyAsync(u => u.OrganizationId == organizationId && u.Email.ToLower() == normalizedEmail, ct);
        if (emailEnUso)
        {
            return TenantUserOperationResult.Failure("Ya existe un usuario con ese correo en tu organización.");
        }

        var (hash, salt) = PasswordHasher.Hash(password);
        var user = new User
        {
            OrganizationId = organizationId,
            Username = normalizedUsername,
            Email = normalizedEmail,
            PasswordHash = hash,
            PasswordSalt = salt,
            IsAdmin = isAdmin,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return TenantUserOperationResult.Success(user.Id);
    }

    public async Task<TenantUserOperationResult> UpdateAsync(Guid userId, string email, bool isAdmin, bool isActive, bool isLocked, CancellationToken ct = default)
    {
        var user = await FindOwnUserAsync(userId, ct);
        if (user is null)
        {
            return TenantUserOperationResult.Failure("Usuario no encontrado.");
        }

        if (userId == _currentUser.UserId && (!isAdmin || !isActive))
        {
            return TenantUserOperationResult.Failure("No podés quitarte a vos mismo el acceso de administrador ni desactivarte.");
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var emailEnUso = await _db.Users.AnyAsync(u => u.OrganizationId == _currentUser.OrganizationId && u.Id != userId && u.Email.ToLower() == normalizedEmail, ct);
        if (emailEnUso)
        {
            return TenantUserOperationResult.Failure("Ya existe un usuario con ese correo en tu organización.");
        }

        user.Email = normalizedEmail;
        user.IsAdmin = isAdmin;
        user.IsActive = isActive;
        user.IsLocked = isLocked;

        await _db.SaveChangesAsync(ct);

        return TenantUserOperationResult.Success(user.Id);
    }

    public async Task<TenantUserOperationResult> DeleteAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == _currentUser.UserId)
        {
            return TenantUserOperationResult.Failure("No podés eliminarte a vos mismo.");
        }

        var user = await FindOwnUserAsync(userId, ct);
        if (user is null)
        {
            return TenantUserOperationResult.Failure("Usuario no encontrado.");
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);

        return TenantUserOperationResult.Success();
    }

    public async Task<IReadOnlyList<CompanyOptionDto>> ListCompaniesAsync(CancellationToken ct = default)
    {
        return await _db.Companies
            .Where(c => c.OrganizationId == _currentUser.OrganizationId)
            .OrderBy(c => c.Code)
            .Select(c => new CompanyOptionDto { Id = c.Id, Code = c.Code, Name = c.Name })
            .ToListAsync(ct);
    }

    // MenuGroup/Profile son catálogos GLOBALES de la plataforma (docs/03 §3) -- se leen
    // tal cual, nunca se filtran por organización (no hay columna para hacerlo) ni se
    // escriben desde este servicio.
    public async Task<IReadOnlyList<MenuGroupOptionDto>> ListMenuGroupsAsync(CancellationToken ct = default)
    {
        return await _db.MenuGroups
            .OrderBy(g => g.Name)
            .Select(g => new MenuGroupOptionDto { Id = g.Id, Name = g.Name })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProfileOptionDto>> ListProfilesAsync(CancellationToken ct = default)
    {
        return await _db.Profiles
            .OrderBy(p => p.Name)
            .Select(p => new ProfileOptionDto { Id = p.Id, Name = p.Name })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LeafMenuDto>> ListLeafMenusAsync(CancellationToken ct = default)
    {
        return await _db.Menus
            .Where(m => m.PagePath != null)
            .OrderBy(m => m.OriginModule).ThenBy(m => m.Order)
            .Select(m => new LeafMenuDto { Id = m.Id, OriginModule = m.OriginModule, Code = m.Code, Name = m.Name })
            .ToListAsync(ct);
    }

    public async Task<UserPermissionsDto?> GetPermissionsAsync(Guid userId, Guid companyId, CancellationToken ct = default)
    {
        if (!await IsOwnUserAsync(userId, ct) || !await IsOwnCompanyAsync(companyId, ct))
        {
            return null;
        }

        var menuGroupIds = await _db.UserMenuGroups
            .Where(g => g.UserId == userId && g.CompanyId == companyId)
            .Select(g => g.MenuGroupId)
            .ToListAsync(ct);

        var profileByMenu = await _db.UserMenuProfiles
            .Where(p => p.UserId == userId && p.CompanyId == companyId)
            .ToDictionaryAsync(p => p.MenuId, p => (long?)p.ProfileId, ct);

        return new UserPermissionsDto { MenuGroupIds = menuGroupIds, ProfileByMenu = profileByMenu };
    }

    public async Task<TenantUserOperationResult> SavePermissionsAsync(Guid userId, Guid companyId, IReadOnlyList<long> menuGroupIds, IReadOnlyDictionary<long, long?> profileByMenu, CancellationToken ct = default)
    {
        if (!await IsOwnUserAsync(userId, ct) || !await IsOwnCompanyAsync(companyId, ct))
        {
            return TenantUserOperationResult.Failure("Usuario o compañía no encontrados.");
        }

        // --- MenuGroups ---
        var gruposActuales = await _db.UserMenuGroups
            .Where(g => g.UserId == userId && g.CompanyId == companyId)
            .ToListAsync(ct);

        var gruposSeleccionados = menuGroupIds.ToHashSet();

        _db.UserMenuGroups.RemoveRange(gruposActuales.Where(g => !gruposSeleccionados.Contains(g.MenuGroupId)));

        var gruposYaAsignados = gruposActuales.Select(g => g.MenuGroupId).ToHashSet();
        foreach (var menuGroupId in gruposSeleccionados.Where(id => !gruposYaAsignados.Contains(id)))
        {
            _db.UserMenuGroups.Add(new UserMenuGroup { UserId = userId, CompanyId = companyId, MenuGroupId = menuGroupId });
        }

        // --- Profile por nodo de menú final ---
        var perfilesActuales = await _db.UserMenuProfiles
            .Where(p => p.UserId == userId && p.CompanyId == companyId)
            .ToListAsync(ct);
        var perfilesActualesPorMenu = perfilesActuales.ToDictionary(p => p.MenuId);

        foreach (var (menuId, profileId) in profileByMenu)
        {
            var existente = perfilesActualesPorMenu.GetValueOrDefault(menuId);

            if (profileId is null or 0)
            {
                if (existente is not null)
                {
                    _db.UserMenuProfiles.Remove(existente);
                }

                continue;
            }

            if (existente is null)
            {
                _db.UserMenuProfiles.Add(new UserMenuProfile { UserId = userId, CompanyId = companyId, MenuId = menuId, ProfileId = profileId.Value });
            }
            else if (existente.ProfileId != profileId.Value)
            {
                existente.ProfileId = profileId.Value;
            }
        }

        await _db.SaveChangesAsync(ct);

        return TenantUserOperationResult.Success();
    }

    private Task<User?> FindOwnUserAsync(Guid userId, CancellationToken ct) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.OrganizationId == _currentUser.OrganizationId, ct);

    private Task<bool> IsOwnUserAsync(Guid userId, CancellationToken ct) =>
        _db.Users.AnyAsync(u => u.Id == userId && u.OrganizationId == _currentUser.OrganizationId, ct);

    private Task<bool> IsOwnCompanyAsync(Guid companyId, CancellationToken ct) =>
        _db.Companies.AnyAsync(c => c.Id == companyId && c.OrganizationId == _currentUser.OrganizationId, ct);
}
