namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Catálogo FIJO y global de acciones posibles sobre un menú final (tabla `actions`).
/// No lo extiende cada módulo -- es deliberadamente cerrado para que cualquier módulo
/// nuevo reutilice el mismo motor de autorización sin configuración adicional. Portado
/// de PortalSAP_v2 (`Acciones`), mismos 6 valores, ahora en inglés por la convención
/// de este proyecto (ver docs/01-CONVENCION-NOMBRES-BD.md).
/// </summary>
public static class PortalActions
{
    public const string View = "VIEW";
    public const string Create = "CREATE";
    public const string Edit = "EDIT";
    public const string Delete = "DELETE";
    public const string Approve = "APPROVE";
    public const string Export = "EXPORT";

    public static readonly IReadOnlyCollection<string> All = [View, Create, Edit, Delete, Approve, Export];
}
