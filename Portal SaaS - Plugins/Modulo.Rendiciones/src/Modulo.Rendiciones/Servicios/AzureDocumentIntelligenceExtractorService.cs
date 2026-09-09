using System.Text.RegularExpressions;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Logging;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Extrae campos de un comprobante con el modelo prebuilt-invoice de Azure Document
/// Intelligence (Amount/Date/Supplier/Tax). El identificador tributario del proveedor
/// (ej. RUT chileno) y el número de documento no son campos nativos de ese modelo -- se
/// buscan con regex sobre el texto completo que la misma respuesta ya trae
/// (result.Content), no son un segundo llamado a la API. El regex de RUT es específico
/// de Chile por ahora (heredado del original, ver PENDIENTE.md sobre
/// SupplierTaxId/multi-país) -- generalizar cuando el plugin soporte otro país.
///
/// Credenciales resueltas 100% self-service vía IExternalServiceProviderSelector
/// (Configuracion &gt; Proveedores) -- puede haber más de un recurso de Document
/// Intelligence configurado por compañía; si el de mayor prioridad ya agotó su cuota
/// mensual, se prueba automáticamente el siguiente. El costo real (páginas que trajo
/// el documento) recién se conoce DESPUÉS de la llamada -- por eso acá se usa
/// SelectAvailableAsync (chequeo previo "¿está bajo el límite?", sin reservar nada) y
/// recién después de la respuesta se registra el consumo real con
/// AnalyzeResult.Pages.Count sobre ese mismo proveedor.
/// </summary>
public sealed class AzureDocumentIntelligenceExtractorService : IReceiptExtractorService
{
    private static readonly Regex ChileanTaxIdRegex = new(@"\b\d{1,2}\.\d{3}\.\d{3}-[\dkK]\b", RegexOptions.Compiled);
    private static readonly Regex DocumentNumberRegex = new(@"(?:folio|n[°º]?\s*(?:de\s+)?(?:boleta|factura)?)\s*[:\-#]?\s*(\d{3,10})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IExternalServiceProviderSelector _selector;
    private readonly IExternalServiceUsageService _usage;
    private readonly ILogger<AzureDocumentIntelligenceExtractorService> _logger;

    public AzureDocumentIntelligenceExtractorService(IExternalServiceProviderSelector selector, IExternalServiceUsageService usage,
        ILogger<AzureDocumentIntelligenceExtractorService> logger)
    {
        _selector = selector;
        _usage = usage;
        _logger = logger;
    }

    public async Task<ExtractedReceiptDto> ExtractAsync(Guid companyId, byte[] content, string mimeType, CancellationToken ct = default)
    {
        try
        {
            var provider = await _selector.SelectAvailableAsync(companyId, ExternalServiceType.AzureDocumentIntelligence, ct);
            if (provider is null)
            {
                return new ExtractedReceiptDto(null, null, null, null, null, null, null,
                    Error: "No hay ninguna cuenta de Azure Document Intelligence configurada, o todas alcanzaron su límite mensual gratuito -- configurá una en Configuración > Proveedores.");
            }

            if (string.IsNullOrWhiteSpace(provider.Endpoint))
            {
                return new ExtractedReceiptDto(null, null, null, null, null, null, null,
                    Error: "El proveedor de Azure Document Intelligence no tiene endpoint configurado.");
            }

            var client = new DocumentIntelligenceClient(new Uri(provider.Endpoint), new AzureKeyCredential(provider.ApiKey));

            Operation<AnalyzeResult> operation = await client.AnalyzeDocumentAsync(
                WaitUntil.Completed, "prebuilt-invoice", BinaryData.FromBytes(content), cancellationToken: ct);

            var result = operation.Value;

            // Recién acá se sabe el costo real (páginas que trajo el documento) -- se
            // registra siempre que la llamada haya llegado a buen puerto, sobre el
            // MISMO proveedor que se usó para la llamada.
            if (result.Pages.Count > 0)
            {
                await _usage.RecordAsync(provider.ProviderId, result.Pages.Count, provider.QuotaPeriod, ct);
            }

            var document = result.Documents.Count > 0 ? result.Documents[0] : null;

            decimal? amount = null;
            decimal? taxAmount = null;
            DateTime? date = null;
            string? supplierName = null;
            float? confidence = document?.Confidence;

            if (document is not null)
            {
                if (document.Fields.TryGetValue("InvoiceTotal", out var totalField) && totalField.ValueCurrency is { } total)
                    amount = (decimal)total.Amount;

                if (document.Fields.TryGetValue("TotalTax", out var taxField) && taxField.ValueCurrency is { } tax)
                    taxAmount = (decimal)tax.Amount;

                if (document.Fields.TryGetValue("InvoiceDate", out var dateField) && dateField.ValueDate is { } d)
                    date = d.DateTime;

                if (document.Fields.TryGetValue("VendorName", out var vendorField))
                    supplierName = vendorField.ValueString ?? vendorField.Content;
            }

            var text = result.Content ?? string.Empty;
            var taxId = ChileanTaxIdRegex.Match(text) is { Success: true } mTaxId ? mTaxId.Value : null;
            var documentNumber = DocumentNumberRegex.Match(text) is { Success: true } mDoc ? mDoc.Groups[1].Value : null;

            return new ExtractedReceiptDto(amount, taxAmount, date, documentNumber, taxId, supplierName, confidence);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo extraer el comprobante con Azure Document Intelligence.");
            return new ExtractedReceiptDto(null, null, null, null, null, null, null,
                Error: "No se pudo leer el documento automáticamente -- completá los campos a mano.");
        }
    }
}
