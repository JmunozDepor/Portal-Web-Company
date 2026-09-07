using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PortalSaas.Core.Sap;

/// <summary>
/// Lee un entero que Service Layer puede devolver como número JSON (<c>1250000001</c>) o
/// como string (<c>"1250000001"</c>) de forma inconsistente segun el recurso/contexto.
/// Caso real: <c>StockTransferLines[].BaseType</c> de un Traslado creado por Copy-From de
/// una Solicitud de Traslado vuelve como string y rompia la deserializacion con
/// <see cref="JsonException"/> ("The JSON value could not be converted to
/// System.Nullable`1[System.Int32]"). Un valor no numerico o vacio se trata como null
/// (BaseType/BaseEntry/BaseLine solo se usan al crear, no al mostrar el documento).
/// </summary>
internal sealed class FlexibleNullableInt32Converter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                return reader.TryGetInt32(out var n) ? n : null;
            case JsonTokenType.String:
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s))
                {
                    return null;
                }

                return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            default:
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteNumberValue(value.Value);
        }
    }
}
