namespace Servicios.TransferenciaAutomatica.Sap;

/// <summary>
/// Acceso a datos del algoritmo de transferencia, independiente del motor (HANA/SQL
/// Server). Worker.cs elige la implementación vía WarehouseTransferRepositoryFactory según
/// compania.EngineType -- el resto del algoritmo (loop de documentos, mapeo de ObjType,
/// armado del StockTransfer, POST a Service Layer) es agnóstico de motor.
/// </summary>
public interface IWarehouseTransferRepository
{
    string ConnectionString(string host, int port, string schema, string userId, string password, bool encriptada);

    List<HeaderDocument> ObtenerDocumentosPendientes(string connectionString, string headerQuerySource);

    List<WarehouseAllocationLine> ObtenerAsignacionBodega(
        string connectionString, string warehouseAssignmentProcedure, int docEntry, string tablaDetalle, int interaccion);

    void MarcarDocumentoCompletado(string connectionString, string tablaCabecera, string completionUdfFieldName, int docEntry);
}
