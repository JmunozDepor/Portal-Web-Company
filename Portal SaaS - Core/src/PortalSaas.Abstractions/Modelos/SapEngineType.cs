namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// "hana" | "sqlserver" -- espejo intencional de PortalSaas.Data.Entities.InstanceEngineType
/// (Abstractions no puede referenciar Data a propósito, ver la dirección de dependencias
/// en CLAUDE.md). Si se agrega un motor nuevo, actualizar los dos a la vez -- mismo
/// criterio ya documentado para PermissionAction/PortalActions.
/// </summary>
public static class SapEngineType
{
    public const string Hana = "hana";
    public const string SqlServer = "sqlserver";
}
