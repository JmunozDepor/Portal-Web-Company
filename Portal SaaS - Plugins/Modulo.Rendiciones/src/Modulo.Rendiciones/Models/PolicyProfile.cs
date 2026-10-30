namespace Modulo.Rendiciones.Models;

/// <summary>
/// Perfil de política de gasto (p.ej. "Supervisor en terreno"), usado para agrupar
/// usuarios a los que se les aplican topes de política diferenciados (ver Tasks 6/7/8
/// del plan de auditoría por ciudad/perfil). CONCEPTO DISTINTO de los 3 roles de
/// acceso del módulo (Rendidor/Aprobador/Administrador, ver RendicionesRoles) -- no
/// reemplaza ni se mezcla con esa matriz. La asignación por usuario vive en
/// UserPolicyProfile (a lo más un perfil activo por usuario) y se gestiona desde
/// Configuracion/UsuariosRoles, separada del catálogo que se mantiene acá.
/// </summary>
public class PolicyProfile
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;
}
