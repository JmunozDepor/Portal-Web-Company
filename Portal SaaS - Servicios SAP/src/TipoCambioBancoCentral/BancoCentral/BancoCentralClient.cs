using System.Globalization;
using Flurl.Http;
using Microsoft.Extensions.Logging;

namespace Servicios.TipoCambioBancoCentral.BancoCentral;

/// <summary>
/// Consulta a la API REST del Banco Central de Chile. Lógica de negocio portada tal cual
/// del servicio legado (Service1.ConsultarBancoCentralRango) -- mismo endpoint, misma
/// serie "Dólar Observado" (F073.TCO.PRE.Z.D), mismo formato de fechas. Lo único que
/// cambia al portar es de dónde vienen la URL/usuario/clave (antes ConfigurationManager.
/// AppSettings en texto plano, ahora BancoCentralOptions + ISecretoCifradoService) y que
/// usa ILogger en vez del Logger a archivo del legado.
/// </summary>
public sealed class BancoCentralClient
{
    private const string SerieDolarObservado = "F073.TCO.PRE.Z.D";

    private readonly ILogger<BancoCentralClient> _logger;

    public BancoCentralClient(ILogger<BancoCentralClient> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Trae el histórico de "Dólar Observado" en el rango [desde, hasta].
    /// </summary>
    /// <returns>Diccionario indexado por fecha ("dd-MM-yyyy") con su respectivo valor de TC.</returns>
    public async Task<Dictionary<string, string>?> ConsultarRangoAsync(
        string url, string usuario, string clave, DateTime desde, DateTime hasta, CancellationToken ct)
    {
        string fDesde = desde.ToString("yyyy-MM-dd");
        string fHasta = hasta.ToString("yyyy-MM-dd");
        var peticion = $"{url}?user={usuario}&pass={clave}&function=GetSeries&timeseries={SerieDolarObservado}&firstdate={fDesde}&lastdate={fHasta}";

        try
        {
            string responseString = await new FlurlClient().Request(peticion).GetStringAsync(cancellationToken: ct);

            if (string.IsNullOrWhiteSpace(responseString) || !responseString.Trim().StartsWith("{"))
            {
                return null;
            }

            var response = System.Text.Json.JsonSerializer.Deserialize<BancoCentralResponse>(responseString);
            var diccionarioResultados = new Dictionary<string, string>();

            if (response?.Series?.Obs != null)
            {
                foreach (var obs in response.Series.Obs)
                {
                    diccionarioResultados.TryAdd(obs.IndexDateString, obs.Value);
                }
            }

            return diccionarioResultados;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ConsultarRangoAsync");
            return null;
        }
    }

    public static bool TryParseValorOficial(string valorOficialString, out decimal valorOficialDecimal) =>
        decimal.TryParse(valorOficialString, NumberStyles.Any, CultureInfo.InvariantCulture, out valorOficialDecimal);
}
