namespace Modulo.Rendiciones.Models;

/// <summary>Los 3 perfiles grandes de este módulo -- ver RendicionesUserRole.</summary>
public static class RendicionesRoles
{
    public const string Rendidor = "Rendidor";
    public const string Aprobador = "Aprobador";
    public const string Administrador = "Administrador";

    /// <summary>Los 3 son explícitos -- Rendidor DEJÓ de ser implícito (hallazgo del dueño del
    /// proyecto: no todos los usuarios deben poder declarar gastos solo por tener el módulo
    /// habilitado). Ver la migración AddRendicionesUserRolesRendidorBackfill para el backfill
    /// que evita que los usuarios que ya venían usando el módulo queden bloqueados de golpe.</summary>
    public static readonly IReadOnlyList<string> Assignable = new[] { Rendidor, Aprobador, Administrador };
}
