namespace Servicios.TransferenciaAutomatica.Sap;

/// <summary>
/// Wrapper fino sobre HanaRepository (métodos estáticos, ya en producción para
/// comercialdepor) para que implemente IWarehouseTransferRepository -- no se toca
/// HanaRepository.cs, cero riesgo de regresión sobre lo que ya funciona.
/// </summary>
public sealed class HanaWarehouseTransferRepository : IWarehouseTransferRepository
{
    public string ConnectionString(string host, int port, string schema, string userId, string password, bool encriptada)
        => HanaRepository.ConnectionString(host, port, schema, userId, password, encriptada);

    public List<HeaderDocument> ObtenerDocumentosPendientes(string connectionString, string headerQuerySource)
        => HanaRepository.ObtenerDocumentosPendientes(connectionString, headerQuerySource);

    public List<WarehouseAllocationLine> ObtenerAsignacionBodega(
        string connectionString, string warehouseAssignmentProcedure, int docEntry, string tablaDetalle, int interaccion)
        => HanaRepository.ObtenerAsignacionBodega(connectionString, warehouseAssignmentProcedure, docEntry, tablaDetalle, interaccion);

    public void MarcarDocumentoCompletado(string connectionString, string tablaCabecera, string completionUdfFieldName, int docEntry)
        => HanaRepository.MarcarDocumentoCompletado(connectionString, tablaCabecera, completionUdfFieldName, docEntry);

    public bool DocumentoEstaEnPicking(string connectionString, int docEntry, string objType)
        => HanaRepository.DocumentoEstaEnPicking(connectionString, docEntry, objType);
}
