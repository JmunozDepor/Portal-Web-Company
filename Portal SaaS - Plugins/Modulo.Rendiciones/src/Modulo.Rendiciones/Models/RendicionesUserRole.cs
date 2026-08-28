namespace Modulo.Rendiciones.Models;

/// <summary>
/// Rol explícito de un usuario DENTRO de este módulo -- "Aprobador"/"Administrador"
/// (ver RendicionesRoles). "Rendidor" NUNCA tiene fila acá -- es implícito para
/// cualquier usuario con el módulo habilitado a nivel plataforma, mismo comportamiento
/// que ya tenía el módulo antes de este cambio (crear/ver sus propios gastos no exigía
/// nada más). Solo se explicitan los dos roles que hasta ahora no tenían NINGÚN gate:
/// cualquiera podía entrar a /rendiciones/configuracion/* o /rendiciones/aprobaciones
/// con solo tener el módulo habilitado (hallazgo real, ver RendicionesAdminPageModelBase/
/// RendicionesAprobadorPageModelBase).
/// </summary>
public class RendicionesUserRole
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required Guid UserId { get; set; }

    /// <summary>"Aprobador" | "Administrador" -- ver RendicionesRoles.</summary>
    public required string Role { get; set; }
}
