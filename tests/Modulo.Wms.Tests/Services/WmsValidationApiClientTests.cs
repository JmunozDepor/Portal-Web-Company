using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsValidationApiClientTests
{
    private sealed class HttpHandlerFalso : HttpMessageHandler
    {
        public string? UltimaUrl { get; private set; }
        private readonly HttpStatusCode _statusCode;
        private readonly string _cuerpo;

        public HttpHandlerFalso(HttpStatusCode statusCode, string cuerpo)
        {
            _statusCode = statusCode;
            _cuerpo = cuerpo;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UltimaUrl = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(_statusCode) { Content = new StringContent(_cuerpo) });
        }
    }

    [Fact]
    public async Task CheckStageRecordAsync_EncuentraRegistroQueMatcheaCompania_DevuelveFoundConStatusId()
    {
        var cuerpo = """{"result_count":1,"next_page":null,"results":[{"status_id":90,"error_message":null,"company_code":"COMP01"}]}""";
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK, cuerpo);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var sut = new WmsValidationApiClient(httpClient, NullLogger<WmsValidationApiClient>.Instance);

        var resultado = await sut.CheckStageRecordAsync(
            "https://wms.example.com/lgfapi/v10/entity/", "wmsuser", "wmspass",
            "item", "item_alternate_code", "ITM001", "COMP01", filtrarPorUrl: true, CancellationToken.None);

        Assert.True(resultado.Found);
        Assert.Equal(90, resultado.StatusId);
        Assert.Contains("item_alternate_code=ITM001", handlerFalso.UltimaUrl);
        Assert.Contains("company_code=COMP01", handlerFalso.UltimaUrl);
    }

    [Fact]
    public async Task CheckStageRecordAsync_SinResultados_DevuelveNotFound()
    {
        var cuerpo = """{"result_count":0,"next_page":null,"results":[]}""";
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK, cuerpo);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var sut = new WmsValidationApiClient(httpClient, NullLogger<WmsValidationApiClient>.Instance);

        var resultado = await sut.CheckStageRecordAsync(
            "https://wms.example.com/lgfapi/v10/entity/", "wmsuser", "wmspass",
            "item", "item_alternate_code", "ITM999", "COMP01", filtrarPorUrl: true, CancellationToken.None);

        Assert.False(resultado.Found);
        Assert.Null(resultado.StatusId);
    }

    [Fact]
    public async Task CheckStageRecordAsync_ResultadoDeOtraCompania_NoMatchea_DevuelveNotFound()
    {
        var cuerpo = """{"result_count":1,"next_page":null,"results":[{"status_id":90,"error_message":null,"company_code":"OTRA"}]}""";
        var handlerFalso = new HttpHandlerFalso(HttpStatusCode.OK, cuerpo);
        var httpClient = new HttpClient(handlerFalso) { BaseAddress = new Uri("https://wms.example.com/") };
        var sut = new WmsValidationApiClient(httpClient, NullLogger<WmsValidationApiClient>.Instance);

        var resultado = await sut.CheckStageRecordAsync(
            "https://wms.example.com/lgfapi/v10/entity/", "wmsuser", "wmspass",
            "item", "item_alternate_code", "ITM001", "COMP01", filtrarPorUrl: true, CancellationToken.None);

        Assert.False(resultado.Found);
    }
}
