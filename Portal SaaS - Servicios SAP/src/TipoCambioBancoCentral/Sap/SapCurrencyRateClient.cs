using System.Globalization;
using System.Net.Http;
using B1SLayer;
using Microsoft.Extensions.Logging;

namespace Servicios.TipoCambioBancoCentral.Sap;

/// <summary>
/// Consulta/actualiza el Tipo de Cambio USD en SAP Business One vía Service Layer. Lógica
/// de negocio portada tal cual del servicio legado (Service1.ConsultarSAP/InsertarSAP) --
/// mismos endpoints internos "SBOBobService_GetCurrencyRate"/"SBOBobService_SetCurrencyRate"
/// (no son objetos configurables por compañía, son operaciones estándar de B1SLayer/Service
/// Layer, a diferencia de HeaderQuerySource/WarehouseAssignmentProcedure de
/// TransferenciaAutomatica que sí son objetos propios de cada instalación SAP). Lo único
/// que cambia al portar es recibir la SLConnection ya armada (antes se creaba una nueva en
/// cada llamado a ConsultarSAP, quedaba fija después) y usar ILogger en vez de Logger a
/// archivo.
/// </summary>
public sealed class SapCurrencyRateClient
{
    private readonly SLConnection _serviceLayer;
    private readonly ILogger _logger;

    public SapCurrencyRateClient(SLConnection serviceLayer, ILogger logger)
    {
        _serviceLayer = serviceLayer;
        _logger = logger;
    }

    /// <param name="fecha">Fecha en formato "yyyyMMdd".</param>
    /// <returns>El valor numérico del Tipo de Cambio si existe; de lo contrario 0; null si falló la consulta.</returns>
    public async Task<decimal?> ConsultarAsync(string fecha)
    {
        decimal respuesta = 0;
        try
        {
            var jsonBody = new { Currency = "USD", Date = fecha };

            var batchRequest = new SLBatchRequest(HttpMethod.Post, "SBOBobService_GetCurrencyRate", jsonBody, null);
            HttpResponseMessage[] responses = await _serviceLayer.PostBatchAsync(batchRequest);
            HttpResponseMessage? response = responses.FirstOrDefault();

            if (response != null && response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                if (decimal.TryParse(content, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rate))
                {
                    respuesta = rate;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConsultarAsync (SAP)");
            return null;
        }
        return respuesta;
    }

    /// <summary>
    /// Registra o actualiza (upsert) un Tipo de Cambio en la tabla ORTT de SAP.
    /// </summary>
    /// <param name="fecha">Fecha en formato "dd-MM-yyyy" (se convierte internamente a "yyyyMMdd").</param>
    /// <param name="valor">Valor numérico de la tasa en formato string.</param>
    public async Task InsertarAsync(string fecha, string valor)
    {
        try
        {
            if (!DateTime.TryParseExact(fecha, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
            {
                _logger.LogError("InsertarAsync: Error al convertir la fecha '{Fecha}' al formato yyyyMMdd.", fecha);
                return;
            }

            var jsonBody = new { Currency = "USD", Rate = valor, RateDate = parsedDate.ToString("yyyyMMdd") };

            var batchRequest = new SLBatchRequest(HttpMethod.Post, "SBOBobService_SetCurrencyRate", jsonBody, null);
            await _serviceLayer.PostBatchAsync(batchRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InsertarAsync (SAP)");
        }
    }
}
