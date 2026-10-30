namespace Modulo.Rendiciones.Models;

/// <summary>
/// Asignación del PolicyProfile de un usuario -- a lo más una fila por
/// (CompanyId, UserId), mismo criterio de "una activa por usuario" que
/// ExpenseApprovalGroupMember. Se administra desde la columna "Perfil de política"
/// de Configuracion/UsuariosRoles, a propósito en un formulario propio, separado de
/// la matriz de roles de acceso (Rendidor/Aprobador/Administrador) para que ambos
/// conceptos no se confundan en pantalla.
/// </summary>
public class UserPolicyProfile
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required Guid UserId { get; set; }

    public required long PolicyProfileId { get; set; }

    public PolicyProfile? PolicyProfile { get; set; }
}
