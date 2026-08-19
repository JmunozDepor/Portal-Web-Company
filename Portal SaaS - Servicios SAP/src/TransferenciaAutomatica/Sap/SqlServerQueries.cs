namespace Servicios.TransferenciaAutomatica.Sap;

/// <summary>
/// Equivalente T-SQL de HanaQueries.cs -- mismo algoritmo/mismos 4 pasos, sintaxis de SQL
/// Server. Los valores van parametrizados con SqlParameter (nunca concatenados); los
/// identificadores (vista/tabla/columna UDF) vienen de configuración por compañía, no de
/// datos de usuario, así que se interpolan igual que en HanaQueries -- ADO.NET no permite
/// parametrizar identificadores.
/// </summary>
public static class SqlServerQueries
{
    public static string Cabecera(string headerQuerySource)
        => $"SELECT [DocEntry], [ObjType], [CardCode] FROM {headerQuerySource} " +
           "GROUP BY [DocEntry], [ObjType], [CardCode]";

    /// <summary>
    /// EXEC posicional -- igual criterio que el "call proc(?,?,?)" de HANA: no asume los
    /// nombres de parámetro reales del stored procedure del cliente, solo el orden
    /// (docEntry, tabla, interacción).
    /// </summary>
    public static string AsignacionBodega(string warehouseAssignmentProcedure)
        => $"EXEC {warehouseAssignmentProcedure} @docEntry, @tabla, @interaccion";

    public static string MarcarCompletado(string tablaCabecera, string completionUdfFieldName)
        => $"UPDATE [{tablaCabecera}] SET [{completionUdfFieldName}] = 'N' WHERE [DocEntry] = @docEntry";
}
