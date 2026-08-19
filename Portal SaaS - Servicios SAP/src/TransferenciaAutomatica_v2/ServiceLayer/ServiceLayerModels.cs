using System.Text.Json.Serialization;

namespace Servicios.TransferenciaAutomatica_v2.ServiceLayer;

/// <summary>Body de POST Login -- legado: Clases.Models.Login.</summary>
public sealed record LoginRequest(string CompanyDB, string UserName, string Password);

/// <summary>
/// Body de POST StockTransfers -- legado: Clases.Models.StockTransfers, que traía ~100
/// campos U_GSP_*/U_NX_* sin usar (copiados del DTO genérico de otro documento). Acá solo
/// los campos que el algoritmo realmente lee o escribe -- no es reescribir el algoritmo,
/// es no cargar una DTO con basura de otro módulo (ver CLAUDE.md: bugs de plomería sí se
/// corrigen al portar).
/// </summary>
public sealed class StockTransferDocument
{
    public string? CardCode { get; set; }
    public List<StockTransferLine> StockTransferLines { get; set; } = new();
    public List<StockTransferDocumentReference> DocumentReferences { get; set; } = new();

    /// <summary>Solo se puebla al deserializar la respuesta de Service Layer.</summary>
    public int? DocEntry { get; set; }

    /// <summary>
    /// Número de documento visible para el usuario en SAP (distinto de DocEntry, que es la
    /// clave interna). Solo se puebla al deserializar la respuesta de Service Layer.
    /// </summary>
    public int? DocNum { get; set; }
}

public sealed class StockTransferLine
{
    public required string ItemCode { get; set; }

    /// <summary>
    /// decimal, no int -- Service Layer devuelve Quantity con decimales (ej. "5.000000")
    /// incluso para transferencias de unidades enteras. Legado: Stocktransferline.Quantity
    /// también era decimal? (Clases/Models/StockTransfers.cs).
    /// </summary>
    public required decimal Quantity { get; set; }

    public required string WarehouseCode { get; set; }
    public required string FromWarehouseCode { get; set; }
}

public sealed class StockTransferDocumentReference
{
    public required int RefDocEntr { get; set; }
    public required string RefObjType { get; set; }
}

/// <summary>Forma de error estándar de Service Layer -- legado: MensajeAPI/MensajeError.cs.</summary>
public sealed class ServiceLayerErrorResponse
{
    [JsonPropertyName("error")]
    public ServiceLayerError? Error { get; set; }
}

public sealed class ServiceLayerError
{
    public int Code { get; set; }
    public ServiceLayerErrorMessage? Message { get; set; }
}

public sealed class ServiceLayerErrorMessage
{
    public string? Lang { get; set; }
    public string? Value { get; set; }
}
