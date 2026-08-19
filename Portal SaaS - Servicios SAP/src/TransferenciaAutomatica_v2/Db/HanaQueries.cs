namespace Servicios.TransferenciaAutomatica_v2.Db;

/// <summary>
/// SQL HANA para las lecturas simples que reemplazan a SP_DEP_ORDER_ABS -- el algoritmo de
/// cascada en sí vive en Domain/AllocationEngine.cs, acá solo hay lecturas parametrizadas
/// (nunca concatenadas para valores; los identificadores -tabla/vista/columna- vienen de
/// configuración por compañía, no de datos de usuario).
/// </summary>
public static class HanaQueries
{
    public static string Cabecera(string headerQuerySource)
        => $"SELECT \"DocEntry\", \"DocNum\", \"ObjType\", \"CardCode\" FROM {headerQuerySource}";

    public static string Lineas(string tablaDetalle, string columnaBodegaDestino)
        => $"SELECT \"ItemCode\", \"{columnaBodegaDestino}\" AS \"WhsCode\", SUM(\"Quantity\") AS \"Cantidad\" " +
           $"FROM \"{tablaDetalle}\" WHERE \"DocEntry\" = ? GROUP BY \"ItemCode\", \"{columnaBodegaDestino}\"";

    /// <summary>
    /// warehousePriorityTable es una tabla de usuario (UDT) de SAP B1 -- convención
    /// estándar U_&lt;Campo&gt; para los UDF (ej. "@DEP_ORDEN_ASIG_STK"), no un nombre de
    /// columna genérico. Sin comillas automáticas alrededor del nombre de tabla (a
    /// diferencia de una versión anterior): el valor de configuración ya trae el formato
    /// completo que necesite (igual criterio que HeaderQuerySource). Se alias de vuelta a
    /// Prioridad/WhsCodeOrigen para no tener que tocar el resto del repositorio.
    /// </summary>
    public static string PrioridadBodegas(string warehousePriorityTable)
        => $"SELECT \"U_Prioridad\" AS \"Prioridad\", \"U_WhsCodeOrigen\" AS \"WhsCodeOrigen\" FROM {warehousePriorityTable} " +
           "WHERE \"U_WhsCodeDestino\" = ? ORDER BY \"U_Prioridad\"";

    public static string Disponible(int cantidadBodegas)
    {
        var placeholders = string.Join(", ", Enumerable.Repeat("?", cantidadBodegas));
        return "SELECT \"WhsCode\", IFNULL(\"OnHand\", 0) - IFNULL(\"IsCommited\", 0) AS \"Disponible\" " +
               $"FROM \"OITW\" WHERE \"ItemCode\" = ? AND \"WhsCode\" IN ({placeholders})";
    }

    public static string MarcarCompletado(string tablaCabecera, string completionUdfFieldName)
        => $"UPDATE \"{tablaCabecera}\" SET \"{completionUdfFieldName}\" = 'N' WHERE \"DocEntry\" = ?";
}
