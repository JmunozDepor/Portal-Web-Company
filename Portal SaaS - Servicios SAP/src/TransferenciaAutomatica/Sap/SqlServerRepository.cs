using System;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Servicios.TransferenciaAutomatica.Sap;

/// <summary>
/// Equivalente SQL Server de HanaRepository.cs -- mismo algoritmo, driver
/// Microsoft.Data.SqlClient. A diferencia del connection string armado a mano para HANA
/// (que costó un bug real por un ";" final, ver HanaRepository.cs), acá se usa
/// SqlConnectionStringBuilder para evitar ese tipo de problema por construcción.
///
/// TrustServerCertificate siempre false -- nunca deshabilitar la validación de
/// certificado (ver CLAUDE.md, regla dura de la familia). Si algún cliente real necesita
/// una excepción (mismo tipo de problema que tuvimos con el certificado de Service Layer
/// de comercialdepor), se agrega entonces, acotada y documentada -- no de forma preventiva.
/// </summary>
public sealed class SqlServerRepository : IWarehouseTransferRepository
{
    public string ConnectionString(string host, int port, string schema, string userId, string password, bool encriptada)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{host},{port}",
            InitialCatalog = schema,
            UserID = userId,
            Password = password,
            Encrypt = encriptada,
            TrustServerCertificate = false
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
        var ordObjType = lector.GetOrdinal("ObjType");
        var ordCardCode = lector.GetOrdinal("CardCode");

        while (lector.Read())
        {
            resultado.Add(new HeaderDocument(
                lector.GetInt32(ordDocEntry),
                lector.GetString(ordObjType),
                lector.GetString(ordCardCode)));
        }

        return resultado;
    }

    public List<WarehouseAllocationLine> ObtenerAsignacionBodega(
        string connectionString, string warehouseAssignmentProcedure, int docEntry, string tablaDetalle, int interaccion)
    {
        var resultado = new List<WarehouseAllocationLine>();

        using var conexion = new SqlConnection(connectionString);
        conexion.Open();
        using var comando = new SqlCommand(SqlServerQueries.AsignacionBodega(warehouseAssignmentProcedure), conexion);
        comando.Parameters.AddWithValue("@docEntry", docEntry);
        comando.Parameters.AddWithValue("@tabla", tablaDetalle);
        comando.Parameters.AddWithValue("@interaccion", interaccion);

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
    /// Mismo caso que en HanaRepository.GetStringONull: cuando una bodega no aporta nada
    /// en una iteración de prioridad, el SP devuelve la fila con estas columnas en NULL.
    /// </summary>
    private static string? GetStringONull(SqlDataReader lector, int ordinal)
        => lector.IsDBNull(ordinal) ? null : lector.GetString(ordinal);

    public void MarcarDocumentoCompletado(string connectionString, string tablaCabecera, string completionUdfFieldName, int docEntry)
    {
        using var conexion = new SqlConnection(connectionString);
        conexion.Open();
        using var comando = new SqlCommand(SqlServerQueries.MarcarCompletado(tablaCabecera, completionUdfFieldName), conexion);
        comando.Parameters.AddWithValue("@docEntry", docEntry);
        comando.ExecuteNonQuery();
    }

    public bool DocumentoEstaEnPicking(string connectionString, int docEntry, string objType)
    {
        using var conexion = new SqlConnection(connectionString);
        conexion.Open();
        using var comando = new SqlCommand(SqlServerQueries.DocumentoEnPicking(), conexion);
        comando.Parameters.AddWithValue("@docEntry", docEntry);
        comando.Parameters.AddWithValue("@baseObject", int.Parse(objType, CultureInfo.InvariantCulture));
        return Convert.ToInt32(comando.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }
}
