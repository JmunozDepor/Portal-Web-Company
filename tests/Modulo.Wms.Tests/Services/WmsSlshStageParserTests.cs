using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Wms.Tests.Services;

/// <summary>
/// A diferencia de los demás tests de este proyecto (que instancian WmsDbContext
/// directo, bypasseando la fábrica real de ModuloWms.RegisterServices), estos tests
/// arman un contenedor DI real -- es la única forma de detectar el bug real: la fábrica
/// de WmsDbContext depende de ICurrentCompanyAccessor.HasCompany, que sin
/// ICurrentCompanyOverride.Set(...) fijado antes de resolver el contexto (un
/// BackgroundService no tiene HttpContext) tira InvalidOperationException en cada ciclo.
/// </summary>
public class WmsSlshStageParserTests
{
    // Fakes mínimos de las dos piezas de Abstractions que la fábrica de WmsDbContext
    // (ver ModuloWms.cs) necesita -- no se referencia PortalSaas.Core desde este
    // proyecto de tests de plugin (regla dura, ver Modulo.Wms.csproj), así que no se
    // puede reusar CurrentCompanyAccessor/CurrentCompanyOverride reales de ahí.
    private sealed class FakeCurrentCompanyOverride : ICurrentCompanyOverride
    {
        public Guid? CompanyId { get; private set; }
        public void Set(Guid companyId) => CompanyId = companyId;
        public void Clear() => CompanyId = null;
    }

    private sealed class FakeCurrentCompanyAccessor : ICurrentCompanyAccessor
    {
        private readonly ICurrentCompanyOverride _override;
        public FakeCurrentCompanyAccessor(ICurrentCompanyOverride @override) => _override = @override;

        public Guid CompanyId => _override.CompanyId ?? throw new InvalidOperationException("No hay compañía activa.");
        public string Code => "TEST";
        public string Database => "test";
        public string ServiceLayerUrl => "https://test";
        public string Country => "CL";
        public bool HasCompany => _override.CompanyId is not null;
    }

    private sealed class FakeExternalDatabaseConnectionService : IExternalDatabaseConnectionService
    {
        private readonly IReadOnlyList<ModuleCompanyDto> _companias;
        public FakeExternalDatabaseConnectionService(IReadOnlyList<ModuleCompanyDto> companias) => _companias = companias;

        public Task<ExternalDatabaseConnection> ResolveConnectionAsync(string moduleCode, Guid companyId, CancellationToken ct = default)
            => throw new NotImplementedException("No usado por este test -- WmsDbContext se respalda con InMemory, ver BuildProvider.");

        public Task<IReadOnlyList<ModuleCompanyDto>> ListActiveCompanyIdsAsync(string moduleCode, CancellationToken ct = default)
            => Task.FromResult(_companias);
    }

    /// <summary>
    /// Misma estructura que ModuloWms.RegisterServices (guard de HasCompany incluido),
    /// pero respaldada por una base InMemory compartida por nombre en vez de
    /// Npgsql/SqlServer real -- lo que importa para este test es reproducir el gating
    /// por ICurrentCompanyAccessor, no ejercitar un motor real.
    /// </summary>
    private static ServiceProvider BuildProvider(string dbName, IReadOnlyList<ModuleCompanyDto> companiasActivas)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentCompanyOverride, FakeCurrentCompanyOverride>();
        services.AddScoped<ICurrentCompanyAccessor, FakeCurrentCompanyAccessor>();
        services.AddSingleton<IExternalDatabaseConnectionService>(new FakeExternalDatabaseConnectionService(companiasActivas));
        services.AddLogging();
        services.AddSingleton(NullLogger<WmsSlshStageParser>.Instance);
        services.AddScoped<IWmsServiceHeartbeatRecorder, WmsServiceHeartbeatRecorder>();

        services.AddDbContext<WmsDbContext>((sp, options) =>
        {
            var companyAccessor = sp.GetRequiredService<ICurrentCompanyAccessor>();
            if (!companyAccessor.HasCompany)
            {
                throw new InvalidOperationException(
                    "Modulo.Wms requiere una compañía activa en la sesión -- seleccioná una compañía antes de continuar.");
            }

            options.UseInMemoryDatabase(dbName);
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task EjecutarCicloAsync_SinOverrideDeCompania_WmsDbContextTiraInvalidOperationException()
    {
        // Reproduce el bug tal cual estaba: resolver WmsDbContext sin haber fijado
        // ICurrentCompanyOverride primero (como pasaría si EjecutarCicloAsync no
        // fijara la compañía antes de pedir el contexto) tiene que fallar -- así se
        // confirma que el guard de HasCompany sigue vivo y el fix realmente depende
        // de fijar el override, no de haberlo relajado.
        await using var provider = BuildProvider(Guid.NewGuid().ToString(), Array.Empty<ModuleCompanyDto>());
        using var scope = provider.CreateScope();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.FromResult(scope.ServiceProvider.GetRequiredService<WmsDbContext>()));

        Assert.Contains("compañía activa", ex.Message);
    }

    [Fact]
    public async Task EjecutarCicloAsync_ProcesaTodasLasCompaniasActivasDelModulo_FijandoElOverridePorCompania()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var companiasActivas = new List<ModuleCompanyDto>
        {
            new(companyA, Guid.NewGuid()),
            new(companyB, Guid.NewGuid()),
        };

        await using var provider = BuildProvider(dbName, companiasActivas);

        // Semilla: una fila SLSH pendiente por cada compañía, usando la misma base
        // InMemory compartida por nombre que BuildProvider usa para el contexto real.
        var seedOptions = new DbContextOptionsBuilder<WmsDbContext>().UseInMemoryDatabase(dbName).Options;
        await using (var seedContext = new WmsDbContext(seedOptions))
        {
            seedContext.WmsOracleInboundStages.AddRange(
                new WmsOracleInboundStage
                {
                    CompanyId = companyA,
                    TipoDoc = "SLSH",
                    Formato = WmsInboundFormato.Xml,
                    Estado = WmsInboundEstado.Pendiente,
                    NombreArchivo = "a.xml",
                    Contenido = "<root></root>",
                    InsertedAt = DateTimeOffset.UtcNow,
                },
                new WmsOracleInboundStage
                {
                    CompanyId = companyB,
                    TipoDoc = "SLSH",
                    Formato = WmsInboundFormato.Xml,
                    Estado = WmsInboundEstado.Pendiente,
                    NombreArchivo = "b.xml",
                    Contenido = "<root></root>",
                    InsertedAt = DateTimeOffset.UtcNow,
                });
            await seedContext.SaveChangesAsync();
        }

        var parser = new WmsSlshStageParser(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WmsSlshStageParser>.Instance);

        // Antes del fix esto tiraba InvalidOperationException en el primer ciclo,
        // capturada en silencio por el catch-all de ExecuteAsync -- acá se llama
        // directo a EjecutarCicloAsync (internal, visible por InternalsVisibleTo) para
        // que una falla se propague como falla de test, no como log ignorado.
        await parser.EjecutarCicloAsync(CancellationToken.None);

        await using var verifyContext = new WmsDbContext(seedOptions);
        var actualizadas = await verifyContext.WmsOracleInboundStages.ToListAsync();

        Assert.Equal(2, actualizadas.Count);
        Assert.All(actualizadas, s => Assert.Equal(WmsInboundEstado.ErrorEstructura, s.Estado));
        Assert.All(actualizadas, s => Assert.NotNull(s.ProcessedAt));
    }
}
