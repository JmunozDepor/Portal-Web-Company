namespace Servicios.TransferenciaAutomatica_v2.Db;

/// <summary>Equivalente T-SQL de HanaQueries.cs -- mismas lecturas, sintaxis de SQL Server.</summary>
public static class SqlServerQueries
{
    public static string Cabecera(string headerQuerySource)
        => $"SELECT DocEntry, DocNum, ObjType, CardCode FROM {headerQuerySource}";

    public static string Lineas(string tablaDetalle, string columnaBodegaDestino)
        => $"SELECT ItemCode, {columnaBodegaDestino} AS WhsCode, SUM(Quantity) AS Cantidad " +
           $"FROM {tablaDetalle} WHERE DocEntry = @docEntry GROUP BY ItemCode, {columnaBodegaDestino}";

    /// <summary>
    /// warehousePriorityTable es una tabla de usuario (UDT) de SAP B1 -- convención
    /// estándar U_&lt;Campo&gt; para los UDF y "@" en el nombre de la tabla (ej.
    /// "[@DEP_ORDEN_ASIG_STK]"), no un nombre de columna genérico. Se alias de vuelta a
    /// Prioridad/WhsCodeOrigen para no tener que tocar el resto del repositorio.
    /// </summary>
    public static string PrioridadBodegas(string warehousePriorityTable)
        => $"SELECT U_Prioridad AS Prioridad, U_WhsCodeOrigen AS WhsCodeOrigen FROM {warehousePriorityTable} " +
           "WHERE U_WhsCodeDestino = @whsCodeDestino ORDER BY U_Prioridad";

    public static string Disponible(IReadOnlyList<string> nombresParametros)
    {
        var placeholders = string.Join(", ", nombresParametros.Select(p => "@" + p));
        return "SELECT WhsCode, ISNULL(OnHand, 0) - ISNULL(IsCommited, 0) AS Disponible " +
               $"FROM OITW WHERE ItemCode = @itemCode AND WhsCode IN ({placeholders})";
    }

    public static string MarcarCompletado(string tablaCabecera, string completionUdfFieldName)
        => $"UPDATE {tablaCabecera} SET {completionUdfFieldName} = 'N' WHERE DocEntry = @docEntry";
}
