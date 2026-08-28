namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Roles explícitos ("Aprobador"/"Administrador", ver Models.RendicionesRoles) de un
/// usuario dentro de este módulo -- cierra el hueco real encontrado de que
/// /rendiciones/configuracion/* y /rendiciones/aprobaciones no tenían NINGÚN gate más
/// allá de tener el módulo habilitado a nivel plataforma.
/// </summary>
public interface IRendicionesUserRoleService
{
    /// <summary>True si el usuario tiene el rol explícito O es admin de la organización a
    /// nivel plataforma (bypass -- evita que el día que se active el gate, nadie tenga
    /// todavía una fila en la tabla nueva y quede bloqueado del todo).</summary>
    Task<bool> HasRoleAsync(Guid companyId, Guid userId, string role, bool isPlatformAdmin, CancellationToken ct = default);

    /// <summary>True si tiene AL MENOS UNO de los roles pedidos -- para pantallas compartidas
    /// entre dos audiencias (ej. Informes/Detalle, reutilizada por Rendidor y Aprobador).</summary>
    Task<bool> HasAnyRoleAsync(Guid companyId, Guid userId, IReadOnlyList<string> roles, bool isPlatformAdmin, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListRolesAsync(Guid companyId, Guid userId, CancellationToken ct = default);

    /// <summary>Todas las asignaciones de la compañía -- para la pantalla "Usuarios y Roles".</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> ListAllAsync(Guid companyId, CancellationToken ct = default);

    Task SetRoleAsync(Guid companyId, Guid userId, string role, bool granted, CancellationToken ct = default);
}
