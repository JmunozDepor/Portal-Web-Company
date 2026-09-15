using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Extrae campos de un comprobante (foto o PDF) con Google Gemini (Google AI Studio /
/// Gemini API) -- alternativa gratuita a Azure Document Intelligence. Se le manda la
/// imagen/PDF inline (base64) y un response_schema JSON; Gemini devuelve directamente
/// {amount, taxAmount, date, documentNumber, supplierTaxId, supplierName} sin
/// necesidad de un segundo pase de regex (a diferencia del extractor de Azure, que sí
/// lo hace para RUT/folio).
///
/// Endpoint REST directo (sin SDK de Google): POST a
/// https://generativelanguage.googleapis.com/v1beta/models/{modelo}:generateContent?key=...
///
/// Credenciales 100% self-service vía IExternalServiceProviderSelector (Configuracion
/// &gt; Proveedores): puede haber más de una cuenta de Gemini por compañía; si la de
/// mayor prioridad agotó su cupo DIARIO se prueba la siguiente. El campo Endpoint
/// del proveedor es OPCIONAL para este tipo -- si viene se usa como id del modelo (ej.
/// "gemini-2.5-flash-lite"), si no se usa <see cref="DefaultModel"/>.
///
/// Límites de la capa gratuita (para "estar dentro del nivel gratis"):
/// - RPD (solicitudes por día): el cupo se cuenta acá 1 unidad por llamada contra
///   ExternalServiceProvider.MonthlyLimit, que para Gemini se interpreta "por día"
///   (QuotaPeriods.Daily) y se reinicia a medianoche hora del Pacífico -- ver
///   ExternalServiceUsageService. Valor típico gratuito: 1500/día (Flash-Lite) o
///   250-1500/día (Flash).
/// - RPM (solicitudes por minuto): <see cref="GeminiRateLimiter"/> corta ANTES de
///   mandar la solicitud cuando se supera el tope por minuto, para no comerse el
///   HTTP 429. Si igual llega un 429, se informa "reintentá en unos segundos".
/// El límite real lo sigue imponiendo Google; estos contadores son un guardrail de
/// presupuesto.
/// </summary>
public sealed class GeminiReceiptExtractorService : IReceiptExtractorService
{
    /// <summary>
    /// Modelo por defecto cuando el proveedor no define uno en Endpoint. Familia Flash
    /// (multimodal, la mayor cuota diaria gratuita para escaneo continuo de boletas).
    /// Para el tope diario plano de 1500 conviene "gemini-2.5-flash-lite" en el campo
    /// Endpoint del proveedor.
    /// </summary>
    public const string DefaultModel = "gemini-2.5-flash";

    private const string BaseUrl = "https://generativelanguage.googleapis.com/";

    /// <summary>
    /// Normaliza lo que el admin cargó en el campo <c>Endpoint</c> del proveedor a un id
    /// de modelo desnudo. Acepta el id tal cual (<c>gemini-2.5-flash</c>) o, pegado por
    /// error, la URL entera
    /// (<c>https://.../v1beta/models/gemini-2.5-flash:generateContent</c>),
    /// <c>models/gemini-2.5-flash</c> o <c>gemini-2.5-flash:generateContent</c>. Vacío
    /// =&gt; <see cref="DefaultModel"/>. La API de Gemini quiere solo el segmento que va
    /// después de <c>models/</c>, sin el sufijo <c>:generateContent</c>.
    /// </summary>
    public static string ResolveModel(string? endpoint)
    {
        var value = endpoint?.Trim();
        if (string.IsNullOrEmpty(value))
            return DefaultModel;

        // Si pegaron una URL o ".../models/<id>", quedarse con lo que sigue al último "models/".
        var marker = value.LastIndexOf("models/", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
            value = value[(marker + "models/".Length)..];

        // Cortar cualquier sufijo de método o query (":generateContent", "?key=...", "/...").
        var cut = value.IndexOfAny(new[] { ':', '?', '/' });
        if (cut >= 0)
            value = value[..cut];

        value = value.Trim();
        return value.Length == 0 ? DefaultModel : value;
    }

    private const string PromptBase =
        "Sos un extractor de datos de comprobantes de gasto chilenos (boletas y facturas electrónicas, vouchers). " +
        "Devolvé SOLO los campos que aparezcan de forma clara en el documento; para cualquier dato que no " +
        "figure o no puedas leer con seguridad, devolvé null (no inventes ni estimes). " +
        "amount = monto total a pagar con impuestos incluidos. netAmount = monto neto/afecto (facturas). " +
        "exemptAmount = monto exento. taxAmount = monto de IVA. " +
        "date = fecha de emisión en formato yyyy-MM-dd. time = hora de la transacción en formato HH:mm (24h) si aparece impresa. " +
        "documentNumber = número de boleta/factura o folio. " +
        "supplierTaxId = RUT del emisor en formato 12.345.678-9. supplierName = razón social o nombre del emisor. " +
        "documentType = uno de: factura_electronica, factura_exenta, boleta_electronica, boleta_exenta, nota_credito, nota_debito, guia_despacho, otro (según el encabezado impreso). " +
        "items = lista breve de las descripciones de los ítems/productos del comprobante (máximo 10).";

    private const string PromptCategoriaSuffix =
        " suggestedCategory = la categoría de gasto que mejor describe este comprobante, ELEGIDA EXACTAMENTE de esta lista: {0}. " +
        "Si ninguna aplica con claridad, devolvé null.";

    private static string BuildPrompt(IReadOnlyList<string> categorias) =>
        categorias.Count == 0
            ? PromptBase
            : PromptBase + string.Format(CultureInfo.InvariantCulture, PromptCategoriaSuffix, string.Join(", ", categorias));

    // Subconjunto de OpenAPI que acepta Gemini como responseSchema. Todo opcional y
    // nullable: el prompt ya obliga a usar null cuando el dato no está.
    private static readonly object ResponseSchema = new
    {
        type = "object",
        properties = new
        {
            amount = new { type = "number", nullable = true },
            netAmount = new { type = "number", nullable = true },
            exemptAmount = new { type = "number", nullable = true },
            taxAmount = new { type = "number", nullable = true },
            date = new { type = "string", nullable = true },
            time = new { type = "string", nullable = true },
            documentNumber = new { type = "string", nullable = true },
            documentType = new { type = "string", nullable = true },
            supplierTaxId = new { type = "string", nullable = true },
            supplierName = new { type = "string", nullable = true },
            items = new { type = "array", nullable = true, items = new { type = "string" } },
            suggestedCategory = new { type = "string", nullable = true },
        },
    };

    private readonly HttpClient _http;
    private readonly IExternalServiceProviderSelector _selector;
    private readonly IExternalServiceUsageService _usage;
    private readonly IExpenseTypeService _expenseTypes;
    private readonly GeminiRateLimiter _rateLimiter;
    private readonly ILogger<GeminiReceiptExtractorService> _logger;

    public GeminiReceiptExtractorService(HttpClient http, IExternalServiceProviderSelector selector,
        IExternalServiceUsageService usage, IExpenseTypeService expenseTypes, GeminiRateLimiter rateLimiter,
        ILogger<GeminiReceiptExtractorService> logger)
    {
        _http = http;
        _http.BaseAddress ??= new Uri(BaseUrl);
        _selector = selector;
        _usage = usage;
        _expenseTypes = expenseTypes;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task<ExtractedReceiptDto> ExtractAsync(Guid companyId, byte[] content, string mimeType, CancellationToken ct = default)
    {
        try
        {
            var provider = await _selector.SelectAvailableAsync(companyId, ExternalServiceType.GoogleGeminiVision, ct);
            if (provider is null)
            {
                return Fail("No hay ninguna cuenta de Google Gemini configurada, o todas alcanzaron su cupo diario gratuito -- configurá una en Configuración > Proveedores.");
            }

            // Guardrail de RPM: si ya se mandaron demasiadas solicitudes en el último
            // minuto para esta cuenta, no llamamos (evita el HTTP 429 de Google).
            if (!_rateLimiter.TryAcquire(provider.ProviderId))
            {
                return Fail("El lector de comprobantes recibió muchas solicitudes en el último minuto -- esperá unos segundos y volvé a intentar.");
            }

            var model = ResolveModel(provider.Endpoint);
            var categorias = await LoadCategoriasAsync(companyId, ct);

            var body = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = BuildPrompt(categorias) },
                            new { inlineData = new { mimeType = NormalizeMimeType(mimeType), data = Convert.ToBase64String(content) } },
                        },
                    },
                },
                generationConfig = new
                {
                    temperature = 0,
                    responseMimeType = "application/json",
                    responseSchema = ResponseSchema,
                },
            };

            // La API key va en el query string (?key=...), igual que el resto de los
            // servicios externos de este módulo -- nunca en un header propio ni logueada.
            using var response = await _http.PostAsJsonAsync(
                $"v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={provider.ApiKey}", body, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gemini devolvió {Status} al analizar un comprobante (modelo {Model}).", (int)response.StatusCode, model);

                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.TooManyRequests =>
                        Fail("Google limitó las solicitudes por el momento (cupo por minuto o por día) -- esperá unos segundos y volvé a intentar, o completá los campos a mano."),
                    _ => Fail("No se pudo leer el documento automáticamente -- completá los campos a mano."),
                };
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            var root = document.RootElement;

            if (root.TryGetProperty("promptFeedback", out var feedback)
                && feedback.TryGetProperty("blockReason", out var blockReason))
            {
                _logger.LogWarning("Gemini bloqueó el comprobante (blockReason={Reason}).", blockReason.GetString());
                return Fail("El servicio de OCR rechazó el documento -- completá los campos a mano.");
            }

            var payload = ExtractJsonPayload(root);
            if (payload is null)
                return Fail("No se pudo leer el documento automáticamente -- completá los campos a mano.");

            // La llamada llegó a buen puerto: se cuenta 1 solicitud contra el cupo del
            // mismo proveedor que se usó (mismo criterio que el extractor de Azure). El
            // balde es diario para Gemini (provider.QuotaPeriod == QuotaPeriods.Daily).
            await _usage.RecordAsync(provider.ProviderId, 1, provider.QuotaPeriod, ct);

            using var parsed = JsonDocument.Parse(payload);
            var fields = parsed.RootElement;

            return new ExtractedReceiptDto(
                Amount: ReadDecimal(fields, "amount"),
                TaxAmount: ReadDecimal(fields, "taxAmount"),
                Date: ReadDate(fields, "date"),
                DocumentNumber: ReadString(fields, "documentNumber"),
                SupplierTaxId: ReadString(fields, "supplierTaxId"),
                SupplierName: ReadString(fields, "supplierName"),
                Confidence: null,
                Error: null,
                TransactionTime: ReadTime(fields, "time"),
                NetAmount: ReadDecimal(fields, "netAmount"),
                ExemptAmount: ReadDecimal(fields, "exemptAmount"),
                SiiDocumentCode: ChileanDocumentKind.SiiCodeForSlug(ReadString(fields, "documentType")),
                Items: ReadStringArray(fields, "items"),
                SuggestedCategoryText: ReadString(fields, "suggestedCategory"),
                Source: ReceiptSource.GeminiOcr);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo extraer el comprobante con Google Gemini.");
            return Fail("No se pudo leer el documento automáticamente -- completá los campos a mano.");
        }
    }

    private async Task<IReadOnlyList<string>> LoadCategoriasAsync(Guid companyId, CancellationToken ct)
    {
        try
        {
            var tipos = await _expenseTypes.ListActiveAsync(companyId, ct);
            return tipos.Where(t => !t.IsMileage).Select(t => t.Name).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo cargar el catálogo de tipos de gasto para la sugerencia de categoría.");
            return Array.Empty<string>();
        }
    }

    private static ExtractedReceiptDto Fail(string error) =>
        new(null, null, null, null, null, null, null, Error: error);

    /// <summary>Gemini acepta image/png|jpeg|webp|heic|heif y application/pdf. Foto sin content-type fiable -&gt; jpeg.</summary>
    private static string NormalizeMimeType(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return "image/jpeg";

        var value = mimeType.Trim().ToLowerInvariant();
        return value switch
        {
            "image/jpg" => "image/jpeg",
            "application/pdf" or "image/png" or "image/jpeg" or "image/webp" or "image/heic" or "image/heif" => value,
            _ when value.StartsWith("image/") => value,
            _ => "image/jpeg",
        };
    }

    /// <summary>candidates[0].content.parts[*].text concatenado -- con responseSchema es un único string JSON.</summary>
    private static string? ExtractJsonPayload(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return null;

        if (!candidates[0].TryGetProperty("content", out var contentEl)
            || !contentEl.TryGetProperty("parts", out var parts))
            return null;

        var buffer = new System.Text.StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                buffer.Append(text.GetString());
        }

        var result = buffer.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? NullIfBlank(value.GetString())
            : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal? ReadDecimal(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var d) => d,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    private static DateTime? ReadDate(JsonElement obj, string name)
    {
        var raw = ReadString(obj, name);
        if (raw is null)
            return null;

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.Date
            : null;
    }

    /// <summary>Hora "HH:mm" (o "HH:mm:ss") -&gt; TimeOnly. Descarta valores fuera de rango.</summary>
    private static TimeOnly? ReadTime(JsonElement obj, string name)
    {
        var raw = ReadString(obj, name);
        if (raw is null)
            return null;

        foreach (var fmt in new[] { "HH:mm", "H:mm", "HH:mm:ss", "h:mm tt", "hh:mm tt" })
        {
            if (TimeOnly.TryParseExact(raw, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                return t;
        }
        return TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var any) ? any : null;
    }

    private static IReadOnlyList<string>? ReadStringArray(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && NullIfBlank(item.GetString()) is { } s)
                list.Add(s);
        }
        return list.Count == 0 ? null : list;
    }
}
