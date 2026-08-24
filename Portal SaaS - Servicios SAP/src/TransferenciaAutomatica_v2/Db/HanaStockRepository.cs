using Sap.Data.Hana;
using Servicios.TransferenciaAutomatica_v2.Domain;

namespace Servicios.TransferenciaAutomatica_v2.Db;

/// <summary>
/// Implementación HANA de IStockRepository. Mismos aprendizajes que
/// Servicios.TransferenciaAutomatica (v1) / Sap/HanaRepository.cs: sin punto y coma final
/// en la cadena de conexión cuando la última clave es booleana (bug real del driver
/// Sap.Data.Hana.Net.v8.0), sin API async real (Begin/End APM), columnas NULL toleradas
/// con IsDBNull antes de GetString.
/// </summary>
public sealed class HanaStockRepository : IStockRepository
{
    /// <summary>
    /// Sin esto, un query que queda esperando un lock de tabla (ej. otro proceso SAP con
    /// una transacción larga sobre ORDR/OITW) bloquea indefinidamente el hilo único del
    /// Worker -- nada vuelve a correr, ni siquiera el próximo ciclo, hasta que algo externo
    /// libere el lock. El driver Sap.Data.Hana no trae un default acotado como SqlClient,
    /// así que hay que fijarlo explícito. 30s acota el peor caso a "el documento falla y se
    /// reintenta" (cae en el aislamiento por documento de Worker.cs) en vez de "el servicio
    /// queda pegado". No se toca la connection string (a diferencia de SqlServerStockRepository)
    /// para no repetir la clase de bug de plomería que este driver ya tuvo con el ";" final.
    /// </summary>
    private const int ComandoTimeoutSegundos = 30;

    // confiaCertificado no aplica a HANA (ver IStockRepository.ConnectionString) -- este
    // motor ya resuelve la validación de certificado vía "encriptada".
    public string ConnectionString(string host, int port, string schema, string userId, string password, bool encriptada, bool confiaCertificado)
    {
        var baseString = $"Server={host}:{port};UserID={userId};Password={password};Current Schema={schema}";
        return encriptada ? baseString + ";Encrypt=true;sslValidateCertificate=true" : baseString;
    }

    public List<HeaderDocument> ObtenerDocumentosPendientes(string connectionString, string headerQuerySource)
    {
        var resultado = new List<HeaderDocument>();

        using var conexion = new HanaConnection(connectionString);
        conexion.Open();
        using var comando = new HanaCommand(HanaQueries.Cabecera(headerQuerySource), conexion) { CommandTimeout = ComandoTimeoutSegundos };
        using var lector = comando.ExecuteReader();
        var ordDocEntry = lector.GetOrdinal("DocEntry");
        var ordDocNum = lector.GetOrdinal("DocNum");
        var ordObjType = lector.GetOrdinal("ObjType");
        var ordCardCode = lector.GetOrdinal("CardCode");

        while (lector.Read())
        {
            resultado.Add(new HeaderDocument(
                lector.GetInt32(ordDocEntry),
                lector.GetInt32(ordDocNum),
                lector.GetString(ordObjType),
                lector.GetString(ordCardCode)));
        }

        return resultado;
    }

    public bool TienePickingPendiente(string connectionString, string pickingPendingQuery, int docEntry)
    {
        using var conexion = new HanaConnection(connectionString);
        conexion.Open();
        using var comando = new HanaCommand(pickingPendingQuery, conexion) { CommandTimeout = ComandoTimeoutSegundos };
        comando.Parameters.Add(new HanaParameter("docEntry", docEntry));
        var cantidad = Convert.ToInt32(comando.ExecuteScalar());
        return cantidad > 0;
    }

    public List<LineaDocumento> ObtenerLineas(string connectionString, string tablaDetalle, string columnaBodegaDestino, int docEntry)
    {
        var resultado = new List<LineaDocumento>();

        using var conexion = new HanaConnection(connectionString);
        conexion.Open();
        using var comando = new HanaCommand(HanaQueries.Lineas(tablaDetalle, columnaBodegaDestino), conexion) { CommandTimeout = ComandoTimeoutSegundos };
        comando.Parameters.Add(new HanaParameter("docEntry", docEntry));

        using var lector = comando.ExecuteReader();
        var ordItemCode = lector.GetOrdinal("ItemCode");
        var ordWhsCode = lector.GetOrdinal("WhsCode");
        var ordCantidad = lector.GetOrdinal("Cantidad");

        while (lector.Read())
        {
            if (lector.IsDBNull(ordWhsCode))
            {
                continue;
            }

            resultado.Add(new LineaDocumento(
                lector.GetString(ordItemCode),
                lector.GetString(ordWhsCode),
                lector.GetDecimal(ordCantidad)));
        }

        return resultado;
    }

    public List<PrioridadBodega> ObtenerPrioridadBodegas(string connectionString, string warehousePriorityTable, string whsCodeDestino)
    {
        var resultado = new List<PrioridadBodega>();

        using var conexion = new HanaConnection(connectionString);
        conexion.Open();
        using var comando = new HanaCommand(HanaQueries.PrioridadBodegas(warehousePriorityTable), conexion) { CommandTimeout = ComandoTimeoutSegundos };
        comando.Parameters.Add(new HanaParameter("whsCodeDestino", whsCodeDestino));

        using var lector = comando.ExecuteReader();
        var ordPrioridad = lector.GetOrdinal("Prioridad");
        var ordWhsCodeOrigen = lector.GetOrdinal("WhsCodeOrigen");

        while (lector.Read())
        {
            resultado.Add(new PrioridadBodega(lector.GetInt32(ordPrioridad), lector.GetString(ordWhsCodeOrigen)));
        }

        if (resultado.Count > 5)
        {
            throw new InvalidOperationException(
                $"{warehousePriorityTable} devolvió {resultado.Count} prioridades para '{whsCodeDestino}' -- el máximo de negocio es 5.");
        }

        return resultado;
    }

    public Dictionary<string, decimal> ObtenerDisponible(string connectionString, string itemCode, IReadOnlyList<string> whsCodes)
    {
        var resultado = new Dictionary<string, decimal>();
        if (whsCodes.Count == 0)
        {
            return resultado;
        }

        using var conexion = new HanaConnection(connectionString);
        conexion.Open();
        using var comando = new HanaCommand(HanaQueries.Disponible(whsCodes.Count), conexion) { CommandTimeout = ComandoTimeoutSegundos };
        comando.Parameters.Add(new HanaParameter("itemCode", itemCode));
        foreach (var whsCode in whsCodes)
        {
            comando.Parameters.Add(new HanaParameter("whsCode", whsCode));
        }

        using var lector = comando.ExecuteReader();
        var ordWhsCode = lector.GetOrdinal("WhsCode");
        var ordDisponible = lector.GetOrdinal("Disponible");

        while (lector.Read())
        {
            resultado[lector.GetString(ordWhsCode)] = lector.GetDecimal(ordDisponible);
        }

        return resultado;
    }

    public void MarcarDocumentoCompletado(string connectionString, string tablaCabecera, string completionUdfFieldName, int docEntry)
    {
        using var conexion = new HanaConnection(connectionString);
        conexion.Open();
        using var comando = new HanaCommand(HanaQueries.MarcarCompletado(tablaCabecera, completionUdfFieldName), conexion) { CommandTimeout = ComandoTimeoutSegundos };
        comando.Parameters.Add(new HanaParameter("docEntry", docEntry));
        comando.ExecuteNonQuery();
    }
}
