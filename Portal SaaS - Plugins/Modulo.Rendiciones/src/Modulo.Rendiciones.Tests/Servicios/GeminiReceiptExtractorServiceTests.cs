using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

public class GeminiReceiptExtractorServiceTests
{
    private static readonly byte[] FakeImage = Encoding.UTF8.GetBytes("not-a-real-jpeg");

    [Fact]
    public async Task Maps_structured_response_and_records_one_request()
    {
        const string geminiJson = """
        {
          "candidates": [
            { "content": { "parts": [ { "text": "{\"amount\": 11900, \"taxAmount\": 1900, \"date\": \"2026-02-14\", \"documentNumber\": \"000123\", \"supplierTaxId\": \"76.543.210-K\", \"supplierName\": \"Comercial Ejemplo SpA\"}" } ] } }
          ]
        }
        """;
        var handler = new StubHandler(_ => Ok(geminiJson));
        var usage = new StubUsage();
        var sut = Build(handler, usage, new SelectedProvider(42, Endpoint: null, ApiKey: "k"));

        var result = await sut.ExtractAsync(Guid.NewGuid(), FakeImage, "image/jpeg");

        Assert.Null(result.Error);
        Assert.Equal(11900m, result.Amount);
        Assert.Equal(1900m, result.TaxAmount);
        Assert.Equal(new DateTime(2026, 2, 14), result.Date);
        Assert.Equal("000123", result.DocumentNumber);
        Assert.Equal("76.543.210-K", result.SupplierTaxId);
        Assert.Equal("Comercial Ejemplo SpA", result.SupplierName);
        Assert.Equal((42L, 1), Assert.Single(usage.Recorded));
    }

    [Fact]
    public async Task Uses_default_model_when_provider_has_no_endpoint()
    {
        var handler = new StubHandler(_ => Ok(EmptyFields));
        var sut = Build(handler, new StubUsage(), new SelectedProvider(1, Endpoint: null, ApiKey: "secret-key"));

        await sut.ExtractAsync(Guid.NewGuid(), FakeImage, "image/jpeg");

        Assert.Contains($"/models/{GeminiReceiptExtractorService.DefaultModel}:generateContent", handler.LastUri!.AbsoluteUri);
        Assert.Contains("key=secret-key", handler.LastUri!.Query);
        Assert.Contains("inlineData", handler.LastBody);
    }

    [Fact]
    public async Task Uses_provider_endpoint_as_model_id_when_present()
    {
        var handler = new StubHandler(_ => Ok(EmptyFields));
        var sut = Build(handler, new StubUsage(), new SelectedProvider(1, Endpoint: "gemini-2.5-flash-lite", ApiKey: "k"));

        await sut.ExtractAsync(Guid.NewGuid(), FakeImage, "image/jpeg");

        Assert.Contains("/models/gemini-2.5-flash-lite:generateContent", handler.LastUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Returns_error_and_skips_http_when_no_provider_available()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("should not be called"));
        var usage = new StubUsage();
        var sut = Build(handler, usage, available: null);

        var result = await sut.ExtractAsync(Guid.NewGuid(), FakeImage, "image/jpeg");

        Assert.NotNull(result.Error);
        Assert.Null(handler.LastUri);
        Assert.Empty(usage.Recorded);
    }

    [Fact]
    public async Task Non_success_status_returns_error_without_recording_usage()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var usage = new StubUsage();
        var sut = Build(handler, usage, new SelectedProvider(1, null, "k"));

        var result = await sut.ExtractAsync(Guid.NewGuid(), FakeImage, "image/jpeg");

        Assert.NotNull(result.Error);
        Assert.Empty(usage.Recorded);
    }

    [Fact]
    public async Task Safety_block_returns_error_without_recording_usage()
    {
        var handler = new StubHandler(_ => Ok("""{ "promptFeedback": { "blockReason": "SAFETY" } }"""));
        var usage = new StubUsage();
        var sut = Build(handler, usage, new SelectedProvider(1, null, "k"));

        var result = await sut.ExtractAsync(Guid.NewGuid(), FakeImage, "image/jpeg");

        Assert.NotNull(result.Error);
        Assert.Empty(usage.Recorded);
    }

    [Fact]
    public async Task Records_usage_against_the_provider_quota_period()
    {
        var handler = new StubHandler(_ => Ok(EmptyFields));
        var usage = new StubUsage();
        var sut = Build(handler, usage, new SelectedProvider(7, null, "k", QuotaPeriods.Daily));

        await sut.ExtractAsync(Guid.NewGuid(), FakeImage, "image/jpeg");

        Assert.Equal((7L, 1), Assert.Single(usage.Recorded));
        Assert.Equal(QuotaPeriods.Daily, usage.LastQuotaPeriod);
    }

    [Fact]
    public async Task Rpm_guardrail_blocks_the_next_call_without_hitting_http()
    {
        var handler = new StubHandler(_ => Ok(EmptyFields));
        var usage = new StubUsage();
        var limiter = new GeminiRateLimiter();
        var sut = Build(handler, usage, new SelectedProvider(1, null, "k"), limiter);

        for (var i = 0; i < GeminiRateLimiter.MaxRequestsPerMinute; i++)
        {
            await sut.ExtractAsync(Guid.NewGuid(), FakeImage, "image/jpeg");
        }

        var callsBefore = usage.Recorded.Count;
        var result = await sut.ExtractAsync(Guid.NewGuid(), FakeImage, "image/jpeg");

        Assert.NotNull(result.Error);
        Assert.Equal(callsBefore, usage.Recorded.Count); // no se registró consumo: no se llamó a Gemini
    }

    [Theory]
    [InlineData(null, "gemini-2.5-flash")]
    [InlineData("", "gemini-2.5-flash")]
    [InlineData("   ", "gemini-2.5-flash")]
    [InlineData("gemini-2.5-flash-lite", "gemini-2.5-flash-lite")]
    [InlineData("models/gemini-2.5-flash", "gemini-2.5-flash")]
    [InlineData("gemini-2.5-flash:generateContent", "gemini-2.5-flash")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent", "gemini-2.5-flash")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:generateContent?key=abc", "gemini-2.5-flash-lite")]
    public void ResolveModel_normalizes_the_provider_endpoint_field(string? endpoint, string expected)
    {
        Assert.Equal(expected, GeminiReceiptExtractorService.ResolveModel(endpoint));
    }

    [Fact]
    public async Task Extracts_model_id_when_provider_endpoint_is_a_full_url()
    {
        var handler = new StubHandler(_ => Ok(EmptyFields));
        var sut = Build(handler, new StubUsage(), new SelectedProvider(1,
            Endpoint: "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent", ApiKey: "k"));

        await sut.ExtractAsync(Guid.NewGuid(), FakeImage, "image/jpeg");

        Assert.Contains("/models/gemini-2.5-flash:generateContent?key=k", handler.LastUri!.AbsoluteUri);
        Assert.DoesNotContain("https%3A", handler.LastUri!.AbsoluteUri);
    }

    private const string EmptyFields =
        """{ "candidates": [ { "content": { "parts": [ { "text": "{}" } ] } } ] }""";

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static GeminiReceiptExtractorService Build(StubHandler handler, StubUsage usage, SelectedProvider? available, GeminiRateLimiter? rateLimiter = null) =>
        new(new HttpClient(handler), new StubSelector(available), usage, rateLimiter ?? new GeminiRateLimiter(), NullLogger<GeminiReceiptExtractorService>.Instance);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public Uri? LastUri { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return _responder(request);
        }
    }

    private sealed class StubSelector : IExternalServiceProviderSelector
    {
        private readonly SelectedProvider? _available;

        public StubSelector(SelectedProvider? available) => _available = available;

        public Task<SelectedProvider?> SelectForReservationAsync(Guid companyId, string serviceType, int quantity, CancellationToken ct = default) =>
            Task.FromResult<SelectedProvider?>(null);

        public Task<SelectedProvider?> SelectAvailableAsync(Guid companyId, string serviceType, CancellationToken ct = default)
        {
            Assert.Equal(ExternalServiceType.GoogleGeminiVision, serviceType);
            return Task.FromResult(_available);
        }
    }

    private sealed class StubUsage : IExternalServiceUsageService
    {
        public List<(long ProviderId, int Quantity)> Recorded { get; } = new();
        public string? LastQuotaPeriod { get; private set; }

        public Task<bool> TryReserveAsync(long providerId, int quantity, int periodLimit, string quotaPeriod, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task RecordAsync(long providerId, int quantity, string quotaPeriod, CancellationToken ct = default)
        {
            Recorded.Add((providerId, quantity));
            LastQuotaPeriod = quotaPeriod;
            return Task.CompletedTask;
        }

        public Task<int> GetCurrentUsageAsync(long providerId, string quotaPeriod, CancellationToken ct = default) =>
            Task.FromResult(0);
    }
}
