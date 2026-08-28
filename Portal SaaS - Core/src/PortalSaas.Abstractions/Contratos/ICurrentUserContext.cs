namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Identidad y permisos del usuario del portal en el request actual. Cualquier plugin
/// pregunta acá antes de mostrar una acción (botón, link, etc.). Portado de PortalSAP_v2
/// (ICurrentUserContext), tal cual.
/// </summary>
public interface ICurrentUserContext
{
    Guid UserId { get; }
    string Username { get; }
    bool IsAdmin { get; }

    /// <summary>
    /// Organización a la que pertenece el usuario logueado (claim fijado en el login,
    /// ver Pages/Account/Login.cshtml.cs). Un plugin (solo depende de Abstractions,
    /// nunca de PortalSaasDbContext) usa esto para acotar toda consulta a su propia
    /// organización, sin confiar en un id recibido desde la UI.
    /// </summary>
    Guid OrganizationId { get; }

    /// <summary>
    /// True si el usuario tiene la acción indicada (ver Modelos.PortalActions) habilitada
    /// para el menú final indicado, según su Profile en la compañía activa. Los
    /// administradores (IsAdmin) siempre devuelven true sin consultar nada más.
    /// menuCode es el código calificado "OriginModule.Code" (Menu.Code es único DENTRO
    /// de OriginModule, no global -- mismo formato que MenuItemDefinition.ParentCode).
    /// </summary>
    Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default);
}
