using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Servicios.TransferenciaAutomatica.ServiceLayer;

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
    /// <summary>
    /// string, no int -- Service Layer es inconsistente: los errores originados en la DI API
    /// mandan "code" como número JSON, pero los que vienen de HANA/SQL Server lo mandan como
    /// string entre comillas (ej. "-2028"). Tipado como int fijo, esos últimos hacían fallar
    /// la deserialización con JsonException y tapaban el error real de SAP.
    /// </summary>
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Code { get; set; }

    [JsonConverter(typeof(ServiceLayerErrorMessageConverter))]
    public ServiceLayerErrorMessage? Message { get; set; }
}

/// <summary>
/// Service Layer devolvió un status de error (4xx/5xx) para el POST de StockTransfers. Lleva
/// el detalle del cuerpo de error de SAP (code + message) para que Worker.cs decida qué
/// hacer con ese documento -- a diferencia de un fallo de login o de red, que sí abortan el
/// ciclo de la compañía. Worker.cs la distingue por tipo para no confundir un rechazo de
/// negocio de SAP (ej. -10 "Quantity falls into negative inventory") con un error de plomería.
/// </summary>
public sealed class ServiceLayerPostException : Exception
{
    public ServiceLayerPostException(int httpStatus, string? sapErrorCode, string? sapMessage, string message)
        : base(message)
    {
        HttpStatus = httpStatus;
        SapErrorCode = sapErrorCode;
        SapMessage = sapMessage;
    }

    public int HttpStatus { get; }
    public string? SapErrorCode { get; }
    public string? SapMessage { get; }
}

/// <summary>Lee un campo JSON que puede venir como string o como número y lo entrega como string.</summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var entero)
                ? entero.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : reader.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"No se esperaba el token {reader.TokenType} para 'error.code'.")
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

public sealed class ServiceLayerErrorMessage
{
    public string? Lang { get; set; }
    public string? Value { get; set; }
}

/// <summary>
/// Service Layer manda "message" como objeto {lang,value} cuando el error viene de la DI
/// API, pero como string plano ("Quantity falls into negative inventory ...") cuando viene
/// de HANA/SQL Server. Este converter acepta las dos formas y siempre expone el texto en
/// Value.
/// </summary>
public sealed class ServiceLayerErrorMessageConverter : JsonConverter<ServiceLayerErrorMessage?>
{
    public override ServiceLayerErrorMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return new ServiceLayerErrorMessage { Value = reader.GetString() };
            case JsonTokenType.StartObject:
                using (var doc = JsonDocument.ParseValue(ref reader))
                {
                    var raiz = doc.RootElement;
                    return new ServiceLayerErrorMessage
                    {
                        Lang = LeerPropiedad(raiz, "lang"),
                        Value = LeerPropiedad(raiz, "value")
                    };
                }
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, ServiceLayerErrorMessage? value, JsonSerializerOptions options)
        => throw new NotSupportedException("ServiceLayerErrorMessage solo se deserializa.");

    private static string? LeerPropiedad(JsonElement elemento, string nombre)
    {
        foreach (var propiedad in elemento.EnumerateObject())
        {
            if (string.Equals(propiedad.Name, nombre, StringComparison.OrdinalIgnoreCase))
            {
                return propiedad.Value.ValueKind == JsonValueKind.Null ? null : propiedad.Value.GetString();
            }
        }

        return null;
    }
}
