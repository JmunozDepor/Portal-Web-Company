using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;

namespace PortalSaas.Core.Seguridad;

/// <summary>
/// Implementación scoped de ICurrentUserContext. Lee la identidad desde los claims de la
/// cookie de autenticación y resuelve HasActionAsync contra UserMenuProfile+ProfileAction
/// (ver Entities/). Portado de PortalSAP_v2 (CurrentUserContext) -- la consulta de
/// permisos se reescribe contra PortalSaasDbContext en vez de SQL crudo a HANA: la
/// autorización del portal no debería depender de que el SAP del cliente esté disponible.
/// </summary>
public sealed class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly PortalSaasDbContext _db;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor, ICurrentCompanyAccessor currentCompany, PortalSaasDbContext db)
    {
        _httpContextAccessor = httpContextAccessor;
        _currentCompany = currentCompany;
        _db = db;
    }

    public Guid UserId => Guid.Parse(GetClaim(ClaimTypes.NameIdentifier));

    public string Username => GetClaim(ClaimTypes.Name);

    public bool IsAdmin => bool.Parse(GetClaim("IsAdmin"));

    public Guid OrganizationId => Guid.Parse(GetClaim("OrganizationId"));

    public async Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default)
    {
        // Regla: si IsAdmin, devolver true de inmediato sin consultar nada más -- mismo
        // criterio que PortalSAP_v2 (ES_ADMINISTRADOR tiene acceso total).
        if (IsAdmin)
        {
            return true;
        }

        var (originModule, code) = ParseMenuCode(menuCode);
        var companyId = _currentCompany.CompanyId;
        var userId = UserId;

        // Asignación MANUAL (UserMenuProfile) -- si existe una fila para este nodo,
        // GANA por sobre lo heredado del grupo (nunca se mezclan): define el acceso
        // real ella sola. "Resetear" una asignación manual (borrar esa fila desde
        // /Admin/Organizations/Users/Permissions) hace que el usuario vuelva a
        // heredar el default de sus MenuGroups, ver más abajo.
        var overrideProfileId = await _db.UserMenuProfiles
            .Where(ump => ump.UserId == userId && ump.CompanyId == companyId
                && ump.Menu.OriginModule == originModule && ump.Menu.Code == code)
            .Select(ump => (long?)ump.ProfileId)
            .FirstOrDefaultAsync(ct);

        if (overrideProfileId is { } profileId)
        {
            return await _db.ProfileActions.AnyAsync(pa => pa.ProfileId == profileId && pa.Action.Code == actionCode, ct);
        }

        // Sin asignación manual -- hereda de cualquier MenuGroup del usuario (en esta
        // compañía) que incluya este nodo con un Profile por defecto (ver
        // MenuGroupItem.DefaultProfileId). Un usuario puede pertenecer a varios
        // grupos: alcanza con que UNO otorgue la acción pedida.
        return await _db.UserMenuGroups
            .Where(ug => ug.UserId == userId && ug.CompanyId == companyId)
            .SelectMany(ug => ug.MenuGroup.MenuGroupItems)
            .Where(item => item.Menu.OriginModule == originModule && item.Menu.Code == code && item.DefaultProfileId != null)
            .AnyAsync(item => item.DefaultProfile!.ProfileActions.Any(pa => pa.Action.Code == actionCode), ct);
    }

    /// <summary>menuCode es "OriginModule.Code" -- Menu.Code es único solo dentro de OriginModule.</summary>
    private static (string OriginModule, string Code) ParseMenuCode(string menuCode)
    {
        var dotIndex = menuCode.IndexOf('.');
        if (dotIndex < 0)
        {
            throw new ArgumentException($"menuCode '{menuCode}' inválido -- se espera el formato calificado 'OriginModule.Code'.", nameof(menuCode));
        }

        return (menuCode[..dotIndex], menuCode[(dotIndex + 1)..]);
    }

    private string GetClaim(string type)
    {
        var value = _httpContextAccessor.HttpContext?.User.FindFirst(type)?.Value;
        return value ?? throw new InvalidOperationException($"No hay claim '{type}' en la sesión actual.");
    }
}
