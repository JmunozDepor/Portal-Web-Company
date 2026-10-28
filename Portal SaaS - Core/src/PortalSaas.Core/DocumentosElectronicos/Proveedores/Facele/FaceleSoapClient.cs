using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PortalSaas.Core.DocumentosElectronicos.Proveedores.Facele;

/// <summary>
/// Cliente SOAP mínimo para los servicios DoceleOL de FACELE (sin WCF). Autenticación por headers
/// HTTP facele.user / facele.pass. Cualquier falla de transporte (HTTP != 200, timeout, respuesta
/// no-SOAP) lanza DteProviderUnavailableException con solo la operación y el código: nunca con los
/// headers, el cuerpo de la respuesta ni las credenciales.
/// </summary>
public sealed class FaceleSoapClient
{
    public const string EmissionService = "DoceleOLService";
    public const string IssuedDocumentsService = "DocumentosEmitidosService";
    /// <summary>Servicio de documentos (consulta y aprobación de recibidos): vive bajo .../cl-dol, no .../cl-dol-auth.</summary>
    public const string ReceptionService = "DTEService";

    private static readonly Dictionary<string, string> Namespaces = new()
    {
        [EmissionService] = "http://www.facele.cl/DoceleOL/",
        [IssuedDocumentsService] = "http://ws.docele.cl/DocumentosEmitidos/",
        [ReceptionService] = "http://facele.cl/docele/servicios/DTE/",
    };

    private readonly HttpClient _http;

    public FaceleSoapClient(HttpClient http) => _http = http;

    /// <summary>Llama la operación y devuelve el elemento &lt;operacionResponse&gt; del Body.</summary>
    public Task<XElement> CallAsync(
        DteProviderContext ctx, string service, string operation, IReadOnlyList<(string Name, string Value)> parameters,
        CancellationToken ct) =>
        CallAsync(ctx.BaseUrl, ctx.Username, ctx.Password, service, operation,
            parameters.Select(p => new XElement(p.Name, p.Value)), ct);

    /// <summary>
    /// Variante con cuerpo arbitrario (elementos anidados, ej. aprobarDTERecibidos/aprobacion). Los elementos van sin
    /// namespace: FACELE declara sus esquemas sin elementFormDefault (hijos no calificados).
    /// </summary>
    public async Task<XElement> CallAsync(
        string baseUrl, string username, string password, string service, string operation, IEnumerable<XElement> body,
        CancellationToken ct)
    {
        var ns = Namespaces[service];
        var bodyXml = new StringBuilder();
        foreach (var element in body)
            bodyXml.Append(element.ToString(SaveOptions.DisableFormatting));
        var envelope =
            $"<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:tns=\"{ns}\">" +
            $"<soapenv:Body><tns:{operation}>{bodyXml}</tns:{operation}></soapenv:Body></soapenv:Envelope>";

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/" + service)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "text/xml"),
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", ns + operation);
        request.Headers.TryAddWithoutValidation("facele.user", username);
        request.Headers.TryAddWithoutValidation("facele.pass", password);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new DteProviderUnavailableException($"FACELE no respondió a tiempo ({operation}).");
        }
        catch (HttpRequestException ex)
        {
            throw new DteProviderUnavailableException($"No se pudo conectar con FACELE ({operation}): {ex.HttpRequestError}.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new DteProviderUnavailableException($"FACELE respondió HTTP {(int)response.StatusCode} en {operation}.");
            var text = await response.Content.ReadAsStringAsync(ct);
            try
            {
                var doc = XDocument.Parse(text);
                return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == operation + "Response")
                    ?? throw new DteProviderUnavailableException($"Respuesta de FACELE sin {operation}Response.");
            }
            catch (XmlException)
            {
                throw new DteProviderUnavailableException($"Respuesta de FACELE no es XML válido ({operation}).");
            }
        }
    }

    /// <summary>
    /// Descarga un PDF por GET. Las credenciales van solo si se pasan (el llamador decide según el servidor). Devuelve
    /// null si la respuesta no es un PDF (ej. la página de login del portal) o pasa de 20 MB.
    /// </summary>
    public async Task<byte[]?> DownloadPdfAsync(Uri url, string? username, string? password, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (username is not null) request.Headers.TryAddWithoutValidation("facele.user", username);
        if (password is not null) request.Headers.TryAddWithoutValidation("facele.pass", password);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new DteProviderUnavailableException("FACELE no respondió a tiempo (PDF del documento).");
        }
        catch (HttpRequestException ex)
        {
            throw new DteProviderUnavailableException($"No se pudo conectar con FACELE (PDF del documento): {ex.HttpRequestError}.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxPdfBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return bytes.Length is > 4 and <= MaxPdfBytes && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8) ? bytes : null;
        }
    }

    private const int MaxPdfBytes = 20 * 1024 * 1024;
}
