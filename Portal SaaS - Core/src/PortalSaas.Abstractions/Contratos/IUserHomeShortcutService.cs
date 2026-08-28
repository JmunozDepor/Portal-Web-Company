namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Accesos directos personalizados de la pantalla de Inicio (`user_home_shortcuts`).
/// Un usuario sin ninguna fila todavía ve el fallback autogenerado por categoría (ver
/// Pages/Home/Index.cshtml.cs) -- esta interfaz solo administra las elecciones puntuales.
/// Portado de PortalSAP_v2 (IAccesoDirectoInicioService), reescrito contra
/// PortalSaasDbContext en vez de SQL crudo a HANA.
/// </summary>
public interface IUserHomeShortcutService
{
    /// <summary>Ids de Menu elegidos por el usuario, en el orden en que los agregó.</summary>
    Task<IReadOnlyList<long>> ListMenuIdsAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(Guid userId, long menuId, CancellationToken ct = default);

    Task RemoveAsync(Guid userId, long menuId, CancellationToken ct = default);
}
