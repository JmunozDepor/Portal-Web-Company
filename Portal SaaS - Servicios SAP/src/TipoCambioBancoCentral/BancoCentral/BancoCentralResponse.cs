using System.Text.Json.Serialization;

namespace Servicios.TipoCambioBancoCentral.BancoCentral;

// Copia tal cual del servicio legado (BancoCentral.cs) -- shape de la respuesta JSON de
// SieteRestWS.ashx, no se modifica al portar.
public sealed class BancoCentralResponse
{
    [JsonPropertyName("Codigo")]
    public int Codigo { get; set; }

    [JsonPropertyName("Descripcion")]
    public string? Descripcion { get; set; }

    [JsonPropertyName("Series")]
    public Series? Series { get; set; }

    [JsonPropertyName("SeriesInfos")]
    public List<object>? SeriesInfos { get; set; }
}

public sealed class Series
{
    [JsonPropertyName("descripEsp")]
    public string? DescripEsp { get; set; }

    [JsonPropertyName("descripIng")]
    public string? DescripIng { get; set; }

    [JsonPropertyName("seriesId")]
    public string? SeriesId { get; set; }

    [JsonPropertyName("Obs")]
    public List<Obs>? Obs { get; set; }
}

public sealed class Obs
{
    [JsonPropertyName("indexDateString")]
    public string IndexDateString { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("statusCode")]
    public string? StatusCode { get; set; }
}
