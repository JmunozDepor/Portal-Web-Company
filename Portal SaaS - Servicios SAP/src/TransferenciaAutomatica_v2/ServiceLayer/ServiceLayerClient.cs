using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Servicios.TransferenciaAutomatica_v2.ServiceLayer;

/// <summary>
/// Login + POST StockTransfers contra Service Layer B1 -- reemplaza a SBOClases del
/// servicio legado. Cambios deliberados al portar (bugs de plomería, no algoritmo):
///   - HttpClient + CookieContainer en vez de RestSharp -- misma sesión B1SESSION, sin
///     dependencia externa.
///   - Nunca se deshabilita la validación de certificado en bloque por default. El legado
///     hacía "ServicePointManager.ServerCertificateValidationCallback = delegate { return
///     true; }" de forma global y permanente para TODOS los errores -- eso es lo que
///     CLAUDE.md prohíbe. Acá se usa la validación default del framework, salvo que la
///     compañía declare explícitamente una excepción:
///       - ToleraNombreCertificadoServiceLayer=true: tolera SOLO RemoteCertificateNameMismatch,
///         la cadena/firma sigue validándose.
///       - ConfiaCertificadoServiceLayer=true: confía en el certificado completo (nombre Y
///         cadena) -- para ambientes de test con certificado autofirmado en ambos extremos.
///     Ambas son excepciones acotadas y documentadas por compañía (ver CompanyConnectionConfig),
///     no un bypass global y permanente como el legado.
///   - Los catches vacíos del legado ("catch (Exception er) { }" en Login/PostSL, que
///     tragaban cualquier error de red o de credenciales) se eliminan -- las excepciones
///     suben y las captura el try/catch por compañía de Worker.cs.
///   - Content-Type "application/json" sin "; charset=utf-8". PostAsJsonAsync de .NET
///     agrega ese charset por default y esta instancia de Service Layer lo rechaza (lee
///     el body como vacío y devuelve "Invalid login credential" en vez de un error de
///     formato) -- verificado comparando ambos headers contra el mismo endpoint. El legado
///     con RestSharp mandaba "application/json" a secas, por eso nunca lo sufrió.
/// </summary>
public sealed class ServiceLayerClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private bool _autenticado;

    public ServiceLayerClient(string baseUrl, bool toleraNombreCertificado = false, bool confiaCertificadoCompleto = false)
    {
        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true };

        if (confiaCertificadoCompleto)
        {
            // Excepción acotada a esta compañía (no global/permanente como el legado) --
            // pensada para ambientes de test con certificado autofirmado. El tráfico sigue
            // yendo cifrado, solo se salta la validación de nombre y cadena.
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        else if (toleraNombreCertificado)
        {
            handler.ServerCertificateCustomValidationCallback = (_, certificate, chain, errores) =>
            {
                // Solo se ignora el mismatch de nombre -- si además hay cadena inválida o
                // no confiable, la conexión se sigue rechazando.
                var erroresSinNombre = errores & ~SslPolicyErrors.RemoteCertificateNameMismatch;
                return erroresSinNombre == SslPolicyErrors.None
                    && certificate is not null
                    && chain is not null;
            };
        }

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"),
            // Explícito en vez de confiar en el default de HttpClient (100s): si Service
            // Layer se degrada, un login o un POST colgado no debe poder acaparar el hilo
            // único del Worker por más de esto -- cae en el aislamiento por documento de
            // Worker.cs en vez de arrastrar el resto del ciclo.
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    public async Task LoginAsync(string companyDb, string userName, string password, CancellationToken ct)
    {
        using var contenido = SerializarSinCharset(new LoginRequest(companyDb, userName, password));
        using var respuesta = await _http.PostAsync("Login", contenido, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            var detalle = await respuesta.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Login a Service Layer falló ({(int)respuesta.StatusCode} {respuesta.StatusCode}): {detalle}");
        }

        _autenticado = true;
    }

    /// <summary>Legado: SBOClases.PostSL&lt;StockTransfers&gt;.</summary>
    public async Task<StockTransferDocument> PostearStockTransferAsync(StockTransferDocument transferencia, CancellationToken ct)
    {
        if (!_autenticado)
        {
            throw new InvalidOperationException("Hay que llamar a LoginAsync antes de postear un StockTransfer.");
        }

        using var cuerpo = SerializarSinCharset(transferencia);
        using var respuesta = await _http.PostAsync("StockTransfers", cuerpo, ct);
        var contenido = await respuesta.Content.ReadAsStringAsync(ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            var error = JsonSerializer.Deserialize<ServiceLayerErrorResponse>(contenido, JsonOptions);
            var mensaje = error?.Error?.Message?.Value ?? contenido;
            throw new InvalidOperationException(
                $"POST StockTransfers falló ({(int)respuesta.StatusCode} {respuesta.StatusCode}): {mensaje}");
        }

        return JsonSerializer.Deserialize<StockTransferDocument>(contenido, JsonOptions)
            ?? throw new InvalidOperationException("Service Layer devolvió una respuesta vacía para StockTransfers.");
    }

    private static StringContent SerializarSinCharset<T>(T valor)
    {
        var json = JsonSerializer.Serialize(valor);
        var contenido = new StringContent(json, Encoding.UTF8);
        contenido.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return contenido;
    }

    public void Dispose() => _http.Dispose();
}
