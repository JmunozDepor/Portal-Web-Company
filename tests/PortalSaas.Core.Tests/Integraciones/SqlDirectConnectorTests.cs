using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Core.Integraciones;
using Xunit;

namespace PortalSaas.Core.Tests.Integraciones;

public class SqlDirectConnectorTests
{
    private sealed class HanaServiceFalso : IHanaService
    {
        public IReadOnlyList<IReadOnlyDictionary<string, object?>>? FilasARetornar { get; set; }
        public object? ParametrosRecibidos { get; private set; }
        public string? SqlRecibido { get; private set; }

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryDynamicAsync(
            string sqlParametrizado, object? parametros = null, CancellationToken ct = default)
        {
            SqlRecibido = sqlParametrizado;
            ParametrosRecibidos = parametros;
            return Task.FromResult(FilasARetornar ?? Array.Empty<IReadOnlyDictionary<string, object?>>());
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parametros = null, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<int> ExecuteAsync(string sql, object? parametros = null, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<string> GetPlatformEngineTypeAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
    }

    [Fact]
    public async Task PullAsync_FilasConColumnasDinamicas_MapeaTodasLasClavesAlRegistro()
    {
        var hana = new HanaServiceFalso
        {
            FilasARetornar = new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["item_alternate_code"] = "ITM1", ["brand_code"] = "NIKE", ["putaway_type"] = "A" },
            },
        };
        var conector = new SqlDirectConnector(hana);

        var resultado = await conector.PullAsync("""{"Query":"SELECT * FROM OITM"}""", null, CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal("ITM1", registro["item_alternate_code"]);
        Assert.Equal("NIKE", registro["brand_code"]);
        Assert.Equal("A", registro["putaway_type"]);
    }

    [Fact]
    public async Task PullAsync_ConCursor_PasaElValorComoParametro()
    {
        var hana = new HanaServiceFalso { FilasARetornar = new List<IReadOnlyDictionary<string, object?>>() };
        var conector = new SqlDirectConnector(hana);
        var cursor = new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

        await conector.PullAsync("""{"Query":"SELECT 1 WHERE :cursor IS NULL"}""", cursor, CancellationToken.None);

        var parametros = Assert.IsType<Dictionary<string, object?>>(hana.ParametrosRecibidos);
        Assert.Equal(cursor.UtcDateTime, parametros["cursor"]);
    }

    [Fact]
    public void DescribirConsulta_SinCursor_IndicaPrimeraCorrida()
    {
        var conector = new SqlDirectConnector(new HanaServiceFalso());

        var descripcion = conector.DescribirConsulta("""{"Query":"SELECT 1"}""");

        Assert.Contains("sin cursor, primera corrida", descripcion);
    }

    [Fact]
    public async Task PushAsync_SiempreLanzaNotSupportedException()
    {
        var conector = new SqlDirectConnector(new HanaServiceFalso());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => conector.PushAsync("{}", Array.Empty<IntegrationRecord>(), CancellationToken.None));
    }
}
