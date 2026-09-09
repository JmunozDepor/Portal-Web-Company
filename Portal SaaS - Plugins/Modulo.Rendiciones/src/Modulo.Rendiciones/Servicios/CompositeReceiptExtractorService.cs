using Microsoft.Extensions.Logging;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Es lo que consume Pages/Gastos/Importar -- decide qué motor de OCR usar según lo
/// que la compañía tenga configurado en Configuracion &gt; Proveedores, con el mismo
/// criterio de fallback por prioridad que ya existe DENTRO de cada servicio (cuando
/// una cuenta agota su cuota se pasa a la siguiente), pero acá ENTRE servicios:
///
///   1. Azure Document Intelligence, si tiene alguna cuenta activa con cupo.
///   2. Google Gemini, si no.
///
/// Así, configurar sólo Gemini "lo prende"; configurar los dos deja a Gemini de
/// respaldo para cuando Azure agote su cupo mensual. Para usar Gemini como principal
/// teniendo Azure configurado, desactivá las cuentas de Azure (IsActive=false).
/// </summary>
public sealed class CompositeReceiptExtractorService : IReceiptExtractorService
{
    private readonly IExternalServiceProviderSelector _selector;
    private readonly AzureDocumentIntelligenceExtractorService _azure;
    private readonly GeminiReceiptExtractorService _gemini;
    private readonly ILogger<CompositeReceiptExtractorService> _logger;

    public CompositeReceiptExtractorService(IExternalServiceProviderSelector selector,
        AzureDocumentIntelligenceExtractorService azure, GeminiReceiptExtractorService gemini,
        ILogger<CompositeReceiptExtractorService> logger)
    {
        _selector = selector;
        _azure = azure;
        _gemini = gemini;
        _logger = logger;
    }

    public async Task<ExtractedReceiptDto> ExtractAsync(Guid companyId, byte[] content, string mimeType, CancellationToken ct = default)
    {
        (string ServiceType, IReceiptExtractorService Extractor)[] chain =
        {
            (ExternalServiceType.AzureDocumentIntelligence, _azure),
            (ExternalServiceType.GoogleGeminiVision, _gemini),
        };

        string? lastError = null;

        foreach (var (serviceType, extractor) in chain)
        {
            try
            {
                // SelectAvailableAsync no reserva nada ni llama a la API externa -- solo
                // mira si hay una cuenta activa de ese tipo todavía bajo su límite mensual.
                var available = await _selector.SelectAvailableAsync(companyId, serviceType, ct);
                if (available is null)
                    continue;

                _logger.LogDebug("OCR de comprobante: usando {ServiceType} para la compañía {CompanyId}.", serviceType, companyId);
                return await extractor.ExtractAsync(companyId, content, mimeType, ct);
            }
            catch (Exception ex)
            {
                // Un fallo de infraestructura al resolver el proveedor (BD del módulo sin
                // migrar, secreto que no descifra, etc.) NO debe tumbar la pantalla de
                // importación con un 500 -- se registra, se intenta el siguiente motor y,
                // si no queda ninguno, se devuelve un DTO con el motivo real.
                _logger.LogError(ex, "El motor de OCR {ServiceType} falló al resolver/ejecutar para la compañía {CompanyId}.", serviceType, companyId);
                lastError = ex.Message;
            }
        }

        return new ExtractedReceiptDto(null, null, null, null, null, null, null,
            Error: lastError is null
                ? "No hay ningún servicio de OCR configurado con cupo disponible (Azure Document Intelligence o Google Gemini) -- configurá uno en Configuración > Proveedores."
                : $"El servicio de OCR no pudo procesar el comprobante ({lastError}). Podés completar los campos a mano y guardar igual.");
    }
}
