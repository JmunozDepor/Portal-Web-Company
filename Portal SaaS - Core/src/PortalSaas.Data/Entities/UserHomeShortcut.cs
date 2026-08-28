namespace PortalSaas.Data.Entities;

/// <summary>
/// Accesos directos personalizados de la pantalla de Inicio -- el usuario elige páginas
/// puntuales del árbol de `menus` (portado de PortalSAP_v2, `USUARIO_ACCESO_DIRECTO`).
/// Sin ninguna fila para el usuario, Inicio muestra el fallback autogenerado por
/// categoría (ver Pages/Home/Index.cshtml.cs) -- Inicio nunca queda vacío.
/// </summary>
public sealed class UserHomeShortcut
{
    public long Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public long MenuId { get; set; }
    public Menu Menu { get; set; } = null!;

    public int Order { get; set; }
}
