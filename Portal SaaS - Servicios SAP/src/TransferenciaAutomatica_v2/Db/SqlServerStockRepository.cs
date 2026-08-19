using Microsoft.Data.SqlClient;
using Servicios.TransferenciaAutomatica_v2.Domain;

namespace Servicios.TransferenciaAutomatica_v2.Db;

/// <summary>
/// Implementación SQL Server de IStockRepository -- misma lógica de lectura que
/// HanaStockRepository, sintaxis T-SQL. SqlConnectionStringBuilder en vez de armar el
/// string a mano (evita la clase de bug que tuvimos con HANA por el ";" final).
/// TrustServerCertificate=false por default -- true solo si la compañía declara
/// explícitamente ConfiaCertificadoBaseDatos (excepción acotada, no un bypass global, ver
/// CompanyConnectionConfig.ConfiaCertificadoBaseDatos).
/// </summary>
public sealed class SqlServerStockRepository : IStockRepository
{
    public string ConnectionString(string host, int port, string schema, string userId, string password, bool encriptada, bool confiaCertificado)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{host},{port}",
            InitialCatalog = schema,
            UserID = userId,
            Password = password,
            Encrypt = encriptada,
            TrustServerCertificate = confiaCertificado
        };

        return builder.ConnectionString;
    }

    public List<HeaderDocument> ObtenerDocumentosPendientes(string connectionString, string headerQuerySource)
    {
        var resultado = new List<HeaderDocument>();

        using var conexion = new SqlConnection(connectionString);
        conexion.Open();
        using var comando = new SqlCommand(SqlServerQueries.Cabecera(headerQuerySource), conexion);
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
        using var conexion = new SqlConnection(connectionString);
        conexion.Open();
        using var comando = new SqlCommand(pickingPendingQuery, conexion);
        comando.Parameters.AddWithValue("@docEntry", docEntry);
        var cantidad = Convert.ToInt32(comando.ExecuteScalar());
        return cantidad > 0;
    }

    public List<LineaDocumento> ObtenerLineas(string connectionString, string tablaDetalle, string columnaBodegaDestino, int docEntry)
    {
        var resultado = new List<LineaDocumento>();

        using var conexion = new SqlConnection(connectionString);
        conexion.Open();
        using var comando = new SqlCommand(SqlServerQueries.Lineas(tablaDetalle, columnaBodegaDestino), conexion);
        comando.Parameters.AddWithValue("@docEntry", docEntry);

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

        using var conexion = new SqlConnection(connectionString);
        conexion.Open();
        using var comando = new SqlCommand(SqlServerQueries.PrioridadBodegas(warehousePriorityTable), conexion);
        comando.Parameters.AddWithValue("@whsCodeDestino", whsCodeDestino);

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

        var nombresParametros = whsCodes.Select((_, i) => $"whs{i}").ToList();

        using var conexion = new SqlConnection(connectionString);
        conexion.Open();
        using var comando = new SqlCommand(SqlServerQueries.Disponible(nombresParametros), conexion);
        comando.Parameters.AddWithValue("@itemCode", itemCode);
        for (var i = 0; i < whsCodes.Count; i++)
        {
            comando.Parameters.AddWithValue($"@{nombresParametros[i]}", whsCodes[i]);
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
        using var conexion = new SqlConnection(connectionString);
        conexion.Open();
        using var comando = new SqlCommand(SqlServerQueries.MarcarCompletado(tablaCabecera, completionUdfFieldName), conexion);
        comando.Parameters.AddWithValue("@docEntry", docEntry);
        comando.ExecuteNonQuery();
    }
}
