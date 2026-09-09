using System.Net;
using System.Text;
using Azure.AI.DocumentIntelligence;
using Azure.Core.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Tests.Servicios;

public class ExternalServiceHealthCheckerTests
{
    private static readonly Guid Company = Guid.NewGuid();

    [Fact]
    public async Task Gemini_ok_when_model_metadata_lists_generateContent()
    {
        const string body = """{ "name": "models/gemini-2.5-flash", "supportedGenerationMethods": ["generateContent", "countTokens"] }""";
        var handler = new StubHandler(_ => Ok(body));
        var sut = Build(handler, Provider(ExternalServiceType.GoogleGeminiVision, endpoint: null));

        var result = await sut.CheckAsync(1, Company);

        Assert.True(result.Ok);
        Assert.Contains($"/v1beta/models/{GeminiReceiptExtractorService.DefaultModel}?key=plain-key", handler.LastUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Gemini_unhealthy_when_model_does_not_support_generateContent()
    {
        var handler = new StubHandler(_ => Ok("""{ "supportedGenerationMethods": ["embedContent"] }"""));
        var sut = Build(handler, Provider(ExternalServiceType.GoogleGeminiVision, endpoint: "text-embedding-004"));

        var result = await sut.CheckAsync(1, Company);

        Assert.False(result.Ok);
        Assert.Contains("generateContent", result.Message);
    }

    [Fact]
    public async Task Gemini_check_extracts_model_id_when_endpoint_is_a_full_url()
    {
        const string body = """{ "supportedGenerationMethods": ["generateContent"] }""";
        var handler = new StubHandler(_ => Ok(body));
        var sut = Build(handler, Provider(ExternalServiceType.GoogleGeminiVision,
            endpoint: "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent"));

        var result = await sut.CheckAsync(1, Company);

        Assert.True(result.Ok);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash?key=plain-key", handler.LastUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Gemini_reports_bad_endpoint_on_400()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{ "error": { "message": "unexpected model name format" } }""", Encoding.UTF8, "application/json"),
        });
        var sut = Build(handler, Provider(ExternalServiceType.GoogleGeminiVision, endpoint: null));

        var result = await sut.CheckAsync(1, Company);

        Assert.False(result.Ok);
        Assert.Contains("Endpoint", result.Message);
    }

    [Fact]
    public async Task Gemini_reports_rejected_key_on_403()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{ "error": { "message": "API key not valid" } }""", Encoding.UTF8, "application/json"),
        });
        var sut = Build(handler, Provider(ExternalServiceType.GoogleGeminiVision, endpoint: null));

        var result = await sut.CheckAsync(1, Company);

        Assert.False(result.Ok);
        Assert.Contains("rechazó la clave", result.Message);
    }

    [Fact]
    public async Task Gemini_reports_unknown_model_on_404()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = Build(handler, Provider(ExternalServiceType.GoogleGeminiVision, endpoint: "gemini-9-turbo"));

        var result = await sut.CheckAsync(1, Company);

        Assert.False(result.Ok);
        Assert.Contains("gemini-9-turbo", result.Message);
    }

    [Fact]
    public async Task AzureDocIntel_ok_when_sdk_metadata_call_succeeds()
    {
        // El SDK (mismo que el OCR real) pega a GET .../info -- respuesta 200 con el shape
        // de DocumentIntelligenceResourceDetails.
        const string body = """
        {
          "customDocumentModels": { "count": 0, "limit": 20000 },
          "customNeuralDocumentModelBuilds": { "used": 0, "quota": 20, "quotaResetDateTime": "2025-01-01T00:00:00Z" }
        }
        """;
        var handler = new StubHandler(_ => Ok(body));
        var sut = BuildAzure(handler, Provider(ExternalServiceType.AzureDocumentIntelligence, endpoint: "https://di.example.com/"));

        var result = await sut.CheckAsync(1, Company);

        Assert.True(result.Ok);
        Assert.Contains("mismo SDK", result.Message);
    }

    [Fact]
    public async Task AzureDocIntel_reports_bad_key_on_401()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var sut = BuildAzure(handler, Provider(ExternalServiceType.AzureDocumentIntelligence, endpoint: "https://di.example.com"));

        var result = await sut.CheckAsync(1, Company);

        Assert.False(result.Ok);
        Assert.Contains("rechazó la clave", result.Message);
    }

    [Fact]
    public async Task AzureDocIntel_without_endpoint_is_unhealthy()
    {
        var sut = Build(new StubHandler(_ => throw new InvalidOperationException("should not be called")),
            Provider(ExternalServiceType.AzureDocumentIntelligence, endpoint: null));

        var result = await sut.CheckAsync(1, Company);

        Assert.False(result.Ok);
        Assert.Contains("no tiene endpoint", result.Message);
    }

    [Fact]
    public async Task AzureDocIntel_with_garbage_endpoint_is_unhealthy()
    {
        var sut = Build(new StubHandler(_ => throw new InvalidOperationException("should not be called")),
            Provider(ExternalServiceType.AzureDocumentIntelligence, endpoint: "no-es-una-url"));

        var result = await sut.CheckAsync(1, Company);

        Assert.False(result.Ok);
        Assert.Contains("URL válida", result.Message);
    }

    [Fact]
    public async Task Unknown_provider_is_unhealthy_without_http()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("should not be called"));
        var sut = Build(handler, provider: null);

        var result = await sut.CheckAsync(99, Company);

        Assert.False(result.Ok);
        Assert.Null(handler.LastUri);
    }

    [Fact]
    public async Task Network_failure_is_reported_not_thrown()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("name resolution failed"));
        var sut = Build(handler, Provider(ExternalServiceType.GoogleGeminiVision, endpoint: null));

        var result = await sut.CheckAsync(1, Company);

        Assert.False(result.Ok);
        Assert.Contains("No se pudo contactar", result.Message);
    }

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static ExternalServiceProvider Provider(string serviceType, string? endpoint) => new()
    {
        Id = 1,
        CompanyId = Company,
        ServiceType = serviceType,
        Name = "Cuenta de prueba",
        Endpoint = endpoint,
        ApiKeyEncrypted = "enc:plain-key",
        MonthlyLimit = 1500,
    };

    private static ExternalServiceHealthChecker Build(StubHandler handler, ExternalServiceProvider? provider) =>
        new(new StubProviders(provider), new PassthroughSecrets(),
            new HttpClient(handler), NullLogger<ExternalServiceHealthChecker>.Instance);

    /// <summary>Como <see cref="Build"/> pero enruta también el SDK de Azure DI por el stub handler.</summary>
    private static ExternalServiceHealthChecker BuildAzure(StubHandler handler, ExternalServiceProvider? provider)
    {
        var options = new DocumentIntelligenceClientOptions { Transport = new HttpClientTransport(new HttpClient(handler)) };
        options.Retry.MaxRetries = 0;
        return new(new StubProviders(provider), new PassthroughSecrets(),
            new HttpClient(handler), NullLogger<ExternalServiceHealthChecker>.Instance, options);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public Uri? LastUri { get; private set; }
        public System.Net.Http.Headers.HttpRequestHeaders? LastHeaders { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastHeaders = request.Headers;
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class StubProviders : IExternalServiceProviderService
    {
        private readonly ExternalServiceProvider? _provider;

        public StubProviders(ExternalServiceProvider? provider) => _provider = provider;

        public Task<ExternalServiceProvider?> GetAsync(long id, Guid companyId, CancellationToken ct = default) =>
            Task.FromResult(_provider);

        public Task<IReadOnlyList<ExternalServiceProvider>> ListAsync(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExternalServiceProvider>>(Array.Empty<ExternalServiceProvider>());

        public Task<IReadOnlyList<ExternalServiceProvider>> ListByServiceAsync(Guid companyId, string serviceType, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExternalServiceProvider>>(Array.Empty<ExternalServiceProvider>());

        public Task<long> CreateAsync(Guid companyId, string serviceType, string name, string? endpoint, string apiKey, int monthlyLimit, int priority, CancellationToken ct = default) =>
            Task.FromResult(1L);

        public Task UpdateAsync(long id, Guid companyId, string name, string? endpoint, string? apiKey, int monthlyLimit, int priority, bool isActive, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>El "cifrado" de prueba solo antepone "enc:" -- Decrypt lo saca.</summary>
    private sealed class PassthroughSecrets : ISecretoCifradoService
    {
        public string Encrypt(string plainText) => "enc:" + plainText;
        public string Decrypt(string cipherText) => cipherText.StartsWith("enc:") ? cipherText[4..] : cipherText;
    }
}
