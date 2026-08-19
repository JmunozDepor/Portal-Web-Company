namespace Servicios.TransferenciaAutomatica.Sap;

/// <summary>
/// Las mismas 3 consultas de Queries.cs del servicio legado (más el UPDATE de cierre),
/// mismo texto SQL/nombre de procedure -- lo único que cambia al portar es que los
/// valores (docEntry, tabla, interacción) van parametrizados con HanaParameter en vez de
/// concatenados con string.Format, que era la vulnerabilidad de SQL injection documentada
/// en docs/00-VINCULO-CON-PORTAL-SAAS.md. Los identificadores (nombre de vista, de tabla,
/// de columna UDF) vienen de configuración por compañía -- no de datos de usuario -- así
/// que HANA no permite parametrizarlos (ADO.NET no parametriza identificadores) y se
/// interpolan igual que en el legado.
/// </summary>
public static class HanaQueries
{
    /// <summary>Legado: Queries.QueryBuscarVistaCalculadaStock().</summary>
    public static string Cabecera(string headerQuerySource)
        => $"SELECT \"DocEntry\", \"ObjType\", \"CardCode\" FROM {headerQuerySource} " +
           "GROUP BY \"DocEntry\", \"ObjType\", \"CardCode\"";

    /// <summary>
    /// Legado: Queries.QueryBuscarVistaCalculadaStockLineas -- mismo stored procedure,
    /// ahora con parámetros posicionales ("?") en vez de literales concatenados.
    /// </summary>
    public static string AsignacionBodega(string warehouseAssignmentProcedure)
        => $"call {warehouseAssignmentProcedure} (?, ?, ?)";

    /// <summary>Legado: Queries.UpdateDocumentoSap -- DocEntry ahora parametrizado.</summary>
    public static string MarcarCompletado(string tablaCabecera, string completionUdfFieldName)
        => $"UPDATE \"{tablaCabecera}\" SET \"{completionUdfFieldName}\" = 'N' WHERE \"DocEntry\" = ?";
}
