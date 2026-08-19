using Sap.Data.Hana;

namespace Servicios.TransferenciaAutomatica.Sap;

/// <summary>
/// Acceso a HANA para el algoritmo de transferencia -- reemplaza a ConexionBaseDatos +
/// las llamadas directas a Queries.cs del servicio legado. El driver Sap.Data.Hana no
/// expone una API async real (solo el patrón APM Begin/End), así que estos métodos son
/// sincrónicos a propósito -- igual que el legado, y aceptable acá porque el worker corre
/// cada 5 minutos, no en un hot path.
///
/// TLS: Encrypt=true;sslValidateCertificate=true en la cadena de conexión -- el legado no
/// fijaba estas opciones (quedaba a lo que trajera por default el driver v2.1). Nunca se
/// deshabilita la validación de certificado (ver CLAUDE.md, regla dura de la familia).
///
/// Sin punto y coma final: el driver Sap.Data.Hana.Net.v8.0 tira "Invalid connection
/// string" cuando la última clave es booleana (Encrypt/sslValidateCertificate) y el string
/// termina en ";" -- verificado por prueba directa contra el driver, mismo string sin el
/// ";" final conecta bien. No es un bug de las credenciales ni de Current Schema.
/// </summary>
public static class HanaRepository
{
    public static string ConnectionString(string host, int port, string schema, string userId, string password, bool encriptada)
    {
        var baseString = $"Server={host}:{port};UserID={userId};Password={password};Current Schema={schema}";
        return encriptada ? baseString + ";Encrypt=true;sslValidateCertificate=true" : baseString;
    }

    public static List<HeaderDocument> ObtenerDocumentosPendientes(string connectionString, string headerQuerySource)
    {
        var resultado = new List<HeaderDocument>();

        using var conexion = new HanaConnection(connectionString);
        conexion.Open();
        using var comando = new HanaCommand(HanaQueries.Cabecera(headerQuerySource), conexion);
        using var lector = comando.ExecuteReader();
        while (lector.Read())
        {
            resultado.Add(new HeaderDocument(
                lector.GetInt32(lector.GetOrdinal("DocEntry")),
                lector.GetString(lector.GetOrdinal("ObjType")),
                lector.GetString(lector.GetOrdinal("CardCode"))));
        }

        return resultado;
    }

    public static List<WarehouseAllocationLine> ObtenerAsignacionBodega(
        string connectionString, string warehouseAssignmentProcedure, int docEntry, string tablaDetalle, int interaccion)
    {
        var resultado = new List<WarehouseAllocationLine>();

        using var conexion = new HanaConnection(connectionString);
        conexion.Open();
        using var comando = new HanaCommand(HanaQueries.AsignacionBodega(warehouseAssignmentProcedure), conexion);
        comando.Parameters.Add(new HanaParameter("docEntry", docEntry));
        comando.Parameters.Add(new HanaParameter("tabla", tablaDetalle));
        comando.Parameters.Add(new HanaParameter("interaccion", interaccion));

        using var lector = comando.ExecuteReader();
        var ordItemCode = lector.GetOrdinal("ItemCode");
        var ordWhsCode = lector.GetOrdinal("WhsCode");
        var ordWhsCodeDesde = lector.GetOrdinal("WhsCodeDesde");
        var ordTransferencia = lector.GetOrdinal("Transferencia");

        while (lector.Read())
        {
            resultado.Add(new WarehouseAllocationLine(
                GetStringONull(lector, ordItemCode),
                lector.IsDBNull(ordTransferencia) ? null : lector.GetInt32(ordTransferencia),
                GetStringONull(lector, ordWhsCode),
                GetStringONull(lector, ordWhsCodeDesde)));
        }

        return resultado;
    }

    /// <summary>
    /// Cuando una bodega no aporta nada en una iteración de prioridad, SP_DEP_ORDER_ABS
    /// devuelve la fila igual pero con estas columnas en NULL (no solo Transferencia) --
    /// GetString del driver no tolera NULL como sí lo hacía el mapeo por reflexión del
    /// legado (ConexionBaseDatos.GetItem, que asignaba null directo al pasar por
    /// DataTable). Estas filas terminan igual descartadas por el filtro
    /// "Transferencia is &gt; 0" en Worker.cs.
    /// </summary>
    private static string? GetStringONull(HanaDataReader lector, int ordinal)
        => lector.IsDBNull(ordinal) ? null : lector.GetString(ordinal);

    public static void MarcarDocumentoCompletado(
        string connectionString, string tablaCabecera, string completionUdfFieldName, int docEntry)
    {
        using var conexion = new HanaConnection(connectionString);
        conexion.Open();
        using var comando = new HanaCommand(HanaQueries.MarcarCompletado(tablaCabecera, completionUdfFieldName), conexion);
        comando.Parameters.Add(new HanaParameter("docEntry", docEntry));
        comando.ExecuteNonQuery();
    }
}
