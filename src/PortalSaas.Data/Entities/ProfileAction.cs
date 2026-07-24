namespace PortalSaas.Data.Entities;

/// <summary>Qué PermissionAction incluye cada Profile -- portado de `PERFIL_ACCION`.</summary>
public sealed class ProfileAction
{
    public long ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;

    public long ActionId { get; set; }
    public PermissionAction Action { get; set; } = null!;
}
