using Servicios.Common.Contratos;

namespace Servicios.Common.Configuracion;

/// <summary>
/// Shape de configuración de UNA compañía en appsettings.json, sección "Sociedades"
/// (array). Mismo espíritu que el SociedadSetting del servicio legado -- una entrada por
/// compañía, agregar una compañía nueva es agregar un objeto al array, sin tocar código.
/// Los campos *Secreto vienen cifrados con ISecretoCifradoService.
/// </summary>
public sealed class SociedadSetting
{
    public required string CompanyCode { get; init; }

    public required string EngineType { get; init; }

    public required string Host { get; init; }

    public required int Port { get; init; }

    /// <summary>Ver CompanyConnectionConfig.DatabaseEncryptada.</summary>
    public bool DatabaseEncryptada { get; init; } = true;

    public required string Schema { get; init; }

    public required string DbUserId { get; init; }

    public required string DbSecreto { get; init; }

    public required string ServiceLayerUrl { get; init; }

    public required string ServiceLayerUsername { get; init; }

    public required string ServiceLayerSecreto { get; init; }

    /// <summary>Ver CompanyConnectionConfig.ToleraNombreCertificadoServiceLayer.</summary>
    public bool ToleraNombreCertificadoServiceLayer { get; init; }

    /// <summary>Ver CompanyConnectionConfig.ConfiaCertificadoServiceLayer.</summary>
    public bool ConfiaCertificadoServiceLayer { get; init; }

    public int WarehousePriorityCount { get; init; } = 3;

    /// <summary>Ver CompanyConnectionConfig.HeaderQuerySource.</summary>
    public required string HeaderQuerySource { get; init; }

    /// <summary>Ver CompanyConnectionConfig.WarehouseAssignmentProcedure.</summary>
    public required string WarehouseAssignmentProcedure { get; init; }

    /// <summary>Ver CompanyConnectionConfig.CompletionUdfFieldName.</summary>
    public required string CompletionUdfFieldName { get; init; }

    /// <summary>Ver CompanyConnectionConfig.WarehousePriorityTable.</summary>
    public string? WarehousePriorityTable { get; init; }

    /// <summary>Ver CompanyConnectionConfig.PickingPendingQuery.</summary>
    public string? PickingPendingQuery { get; init; }

    /// <summary>Ver CompanyConnectionConfig.ConfiaCertificadoBaseDatos.</summary>
    public bool ConfiaCertificadoBaseDatos { get; init; }

    public bool IsActive { get; init; } = true;

    public MotorBaseDatos ResolverEngineType() => EngineType.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
        ? MotorBaseDatos.SqlServer
        : MotorBaseDatos.Hana;
}
