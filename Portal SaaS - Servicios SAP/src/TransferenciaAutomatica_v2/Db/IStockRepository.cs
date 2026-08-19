using Servicios.TransferenciaAutomatica_v2.Domain;

namespace Servicios.TransferenciaAutomatica_v2.Db;

/// <summary>
/// Acceso a datos del algoritmo de transferencia, independiente del motor (HANA/SQL
/// Server). Worker.cs elige la implementación vía StockRepositoryFactory según
/// compania.EngineType -- el algoritmo de asignación en sí (AllocationEngine) no depende
/// de esta interfaz, solo la orquestación en Worker.cs.
/// </summary>
public interface IStockRepository
{
    /// <summary>
    /// confiaCertificado: solo lo usa SqlServerStockRepository (TrustServerCertificate) --
    /// HanaStockRepository lo ignora, ya maneja la validación de certificado vía
    /// "encriptada" (sslValidateCertificate). Ver CompanyConnectionConfig.ConfiaCertificadoBaseDatos.
    /// </summary>
    string ConnectionString(string host, int port, string schema, string userId, string password, bool encriptada, bool confiaCertificado);

    /// <summary>
    /// headerQuerySource ya debe traer, en su propio texto (responsabilidad de la
    /// compañía), el filtro de "sin picking pendiente" y el ORDER BY de prioridad de
    /// proceso (nuevos primero) -- ver convención documentada en el CLAUDE.md de este
    /// proyecto. Acá solo se hace SELECT DocEntry, DocNum, ObjType, CardCode FROM (...).
    /// </summary>
    List<HeaderDocument> ObtenerDocumentosPendientes(string connectionString, string headerQuerySource);

    /// <summary>
    /// Reconsulta puntual (no la de selección) -- se llama antes de crear cada
    /// transferencia para cerrar la ventana de carrera de un documento que entró a picking
    /// a mitad del ciclo. pickingPendingQuery viene de compania.PickingPendingQuery, con
    /// "{TablaDetalle}" ya reemplazado por el llamador.
    /// </summary>
    bool TienePickingPendiente(string connectionString, string pickingPendingQuery, int docEntry);

    List<LineaDocumento> ObtenerLineas(string connectionString, string tablaDetalle, string columnaBodegaDestino, int docEntry);

    /// <summary>Máximo 5 filas -- tope de negocio, ver CompanyConnectionConfig.WarehousePriorityTable.</summary>
    List<PrioridadBodega> ObtenerPrioridadBodegas(string connectionString, string warehousePriorityTable, string whsCodeDestino);

    /// <summary>OnHand - IsCommitted (tabla OITW estándar de SAP B1) para el item, por cada bodega candidata.</summary>
    Dictionary<string, decimal> ObtenerDisponible(string connectionString, string itemCode, IReadOnlyList<string> whsCodes);

    void MarcarDocumentoCompletado(string connectionString, string tablaCabecera, string completionUdfFieldName, int docEntry);
}
