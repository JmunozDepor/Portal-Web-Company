using Servicios.Common.Contratos;

namespace Servicios.TransferenciaAutomatica.Sap;

/// <summary>
/// Único punto donde Worker.cs decide qué motor usar por compañía -- agregar un motor
/// nuevo es agregar un case acá, sin tocar el resto del algoritmo.
/// </summary>
public static class WarehouseTransferRepositoryFactory
{
    public static IWarehouseTransferRepository Crear(MotorBaseDatos engineType) => engineType switch
    {
        MotorBaseDatos.Hana => new HanaWarehouseTransferRepository(),
        MotorBaseDatos.SqlServer => new SqlServerRepository(),
        _ => throw new NotSupportedException($"EngineType {engineType} no soportado en TransferenciaAutomatica.")
    };
}
