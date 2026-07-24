namespace PortalSaas.Data.Entities;

/// <summary>
/// Catálogo FIJO y global de acciones posibles sobre un menú final -- tabla `actions`,
/// portado de PortalSAP_v2 (`ACCION`). Se llama `PermissionAction`, no `Action`, para no
/// chocar con el delegado `System.Action` (siempre en scope por ImplicitUsings). Los
/// códigos válidos son los de `PortalSaas.Abstractions.Modelos.PortalActions`
/// (`PortalSaas.Data` no referencia `Abstractions` a propósito, se mantiene sin
/// dependencias -- ver su .csproj) -- si se agrega una acción nueva, actualizar los dos
/// a la vez (mismo criterio que exigía PortalSAP_v2 entre `Acciones` y
/// `003_seed_acciones.sql`), y el seed de `PortalSaasDbContext.OnModelCreating`.
/// </summary>
public sealed class PermissionAction
{
    public long Id { get; set; }

    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;

    public ICollection<ProfileAction> ProfileActions { get; set; } = new List<ProfileAction>();
}
