using Servicios.Common.Contratos;

namespace Servicios.TransferenciaAutomatica_v2.Db;

/// <summary>
/// Único punto donde Worker.cs decide qué motor usar por compañía -- agregar un motor
/// nuevo es agregar un case acá, sin tocar el resto del algoritmo. Mismo patrón que
/// WarehouseTransferRepositoryFactory en v1.
/// </summary>
public static class StockRepositoryFactory
{
    public static IStockRepository Crear(MotorBaseDatos engineType) => engineType switch
    {
        MotorBaseDatos.Hana => new HanaStockRepository(),
        MotorBaseDatos.SqlServer => new SqlServerStockRepository(),
        _ => throw new NotSupportedException($"EngineType {engineType} no soportado en TransferenciaAutomatica_v2.")
    };
}
