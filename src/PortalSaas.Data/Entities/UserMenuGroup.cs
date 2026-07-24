namespace PortalSaas.Data.Entities;

/// <summary>
/// A qué MenuGroup pertenece un usuario, POR COMPAÑÍA -- portado de
/// `USUARIO_GRUPO_MENU`. Un usuario puede pertenecer a más de un grupo, y el acceso
/// puede variar por compañía (equivalente a EMPRESA_CODIGO en PortalSAP_v2).
/// </summary>
public sealed class UserMenuGroup
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public long MenuGroupId { get; set; }
    public MenuGroup MenuGroup { get; set; } = null!;

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
}
