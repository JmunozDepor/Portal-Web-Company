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
}
