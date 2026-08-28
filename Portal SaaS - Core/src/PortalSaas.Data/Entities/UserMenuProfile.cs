namespace PortalSaas.Data.Entities;

/// <summary>
/// Qué Profile tiene un usuario sobre un menú final (hoja, PagePath != null), POR
/// COMPAÑÍA -- portado de `USUARIO_MENU_PERFIL`. La clave primaria es
/// (UserId, MenuId, CompanyId) -- un usuario tiene EXACTAMENTE un perfil por menú por
/// compañía, ProfileId no es parte de la clave (mismo diseño que PortalSAP_v2).
/// </summary>
public sealed class UserMenuProfile
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public long MenuId { get; set; }
    public Menu Menu { get; set; } = null!;

    public long ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
}
