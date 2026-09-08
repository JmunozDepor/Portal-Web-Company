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
/// Misma estrategia que WmsSlshStageParserTests: arma un contenedor DI real (en vez de
/// instanciar WmsDbContext directo) porque lo que importa acá es reproducir el gating por
/// ICurrentCompanyAccessor.HasCompany -- sin ICurrentCompanyOverride.Set(...) fijado antes
/// de resolver el contexto (un BackgroundService no tiene HttpContext), la fábrica de
/// WmsDbContext tira InvalidOperationException en cada ciclo.
/// </summary>
public class WmsSvshStageParserTests
{
    private const string XmlValido = """
        <Message>
          <Header></Header>
          <ib_shipment>
            <ib_shipment_hdr><shipment_nbr>ASN2001</shipment_nbr></ib_shipment_hdr>
            <ib_shipment_dtl>
              <shipment_dtl_cust_field_1>1250000001</shipment_dtl_cust_field_1>
              <shipment_dtl_cust_field_2>900</shipment_dtl_cust_field_2>
              <item_part_a>ITEM-Z</item_part_a>
              <received_qty>3</received_qty>
            </ib_shipment_dtl>
          </ib_shipment>
        </Message>
        """;

    // Fakes mínimos de las dos piezas de Abstractions que la fábrica de WmsDbContext
    // (ver ModuloWms.cs) necesita -- no se referencia PortalSaas.Core desde este
    // proyecto de tests de plugin (regla dura, ver Modulo.Wms.csproj).
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

        public Task<ExternalDatabaseConnection> ResolveConnectionAsync(string moduleCode, Guid companyId, string purpose, CancellationToken ct = default)
            => throw new NotImplementedException("No usado por este test -- WmsDbContext se respalda con InMemory, ver BuildProvider.");

        public Task<IReadOnlyList<ModuleCompanyDto>> ListActiveCompanyIdsAsync(string moduleCode, CancellationToken ct = default)
            => Task.FromResult(_companias);
    }

    /// <summary>
    /// Misma estructura que ModuloWms.RegisterServices (guard de HasCompany incluido),
    /// pero respaldada por una base InMemory compartida por nombre en vez de
    /// Npgsql/SqlServer real.
    /// </summary>
    private static ServiceProvider BuildProvider(string dbName, IReadOnlyList<ModuleCompanyDto> companiasActivas)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentCompanyOverride, FakeCurrentCompanyOverride>();
        services.AddScoped<ICurrentCompanyAccessor, FakeCurrentCompanyAccessor>();
        services.AddSingleton<IExternalDatabaseConnectionService>(new FakeExternalDatabaseConnectionService(companiasActivas));
        services.AddLogging();
        services.AddSingleton(NullLogger<WmsSvshStageParser>.Instance);
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
    public async Task EjecutarCicloAsync_ArchivoSvshPendiente_SeAplanaYQuedaEnAplanado()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var companiasActivas = new List<ModuleCompanyDto>
        {
            new(companyId, Guid.NewGuid()),
        };

        await using var provider = BuildProvider(dbName, companiasActivas);

        var seedOptions = new DbContextOptionsBuilder<WmsDbContext>().UseInMemoryDatabase(dbName).Options;
        await using (var seedContext = new WmsDbContext(seedOptions))
        {
            seedContext.WmsOracleInboundStages.Add(new WmsOracleInboundStage
            {
                CompanyId = companyId,
                TipoDoc = "SVSH",
                Formato = WmsInboundFormato.Xml,
                NombreArchivo = "svsh_test.xml",
                HashArchivo = "hash-svsh-1",
                Contenido = XmlValido,
                Estado = WmsInboundEstado.Pendiente,
                InsertedAt = DateTimeOffset.UtcNow,
            });
            await seedContext.SaveChangesAsync();
        }

        var parser = new WmsSvshStageParser(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WmsSvshStageParser>.Instance);

        await parser.EjecutarCicloAsync(CancellationToken.None);

        await using var verifyContexto = new WmsDbContext(seedOptions);

        var stage = await verifyContexto.WmsOracleInboundStages.SingleAsync();
        Assert.Equal(WmsInboundEstado.Aplanado, stage.Estado);

        var fila = await verifyContexto.WmsOracleStageSvsh.SingleAsync();
        Assert.Equal("ASN2001", fila.shipment_nbr);
        Assert.Equal("ITEM-Z", fila.item_part_a);
        Assert.Equal(WmsSvshStatus.Pendiente, fila.Status);

        var heartbeat = await verifyContexto.ServiceHeartbeats.SingleAsync();
        Assert.Equal(companyId, heartbeat.CompanyId);
        Assert.Equal("Wms.SvshStageParser", heartbeat.ProcessorKey);
        Assert.Equal("OK", heartbeat.Status);
        Assert.NotNull(heartbeat.LastRunAt);
    }

    [Fact]
    public async Task EjecutarCicloAsync_ProcesaTodasLasCompaniasActivasDelModulo_SinFugaEntreCompanias()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        const string xmlCompaniaA = """
            <Message>
              <Header></Header>
              <ib_shipment>
                <ib_shipment_hdr><shipment_nbr>ASN-A-001</shipment_nbr></ib_shipment_hdr>
                <ib_shipment_dtl>
                  <shipment_dtl_cust_field_1>1250000001</shipment_dtl_cust_field_1>
                  <shipment_dtl_cust_field_2>900</shipment_dtl_cust_field_2>
                  <item_part_a>ITEM-A</item_part_a>
                  <received_qty>3</received_qty>
                </ib_shipment_dtl>
              </ib_shipment>
            </Message>
            """;

        const string xmlCompaniaB = """
            <Message>
              <Header></Header>
              <ib_shipment>
                <ib_shipment_hdr><shipment_nbr>ASN-B-002</shipment_nbr></ib_shipment_hdr>
                <ib_shipment_dtl>
                  <shipment_dtl_cust_field_1>1250000002</shipment_dtl_cust_field_1>
                  <shipment_dtl_cust_field_2>901</shipment_dtl_cust_field_2>
                  <item_part_a>ITEM-B</item_part_a>
                  <received_qty>7</received_qty>
                </ib_shipment_dtl>
              </ib_shipment>
            </Message>
            """;

        var companiasActivas = new List<ModuleCompanyDto>
        {
            new(companyA, Guid.NewGuid()),
            new(companyB, Guid.NewGuid()),
        };

        await using var provider = BuildProvider(dbName, companiasActivas);

        // Semilla: un archivo SVSH pendiente por cada compañía, con contenido distinto,
        // usando la misma base InMemory compartida por nombre que BuildProvider usa
        // para el contexto real.
        var seedOptions = new DbContextOptionsBuilder<WmsDbContext>().UseInMemoryDatabase(dbName).Options;
        await using (var seedContext = new WmsDbContext(seedOptions))
        {
            seedContext.WmsOracleInboundStages.AddRange(
                new WmsOracleInboundStage
                {
                    CompanyId = companyA,
                    TipoDoc = "SVSH",
                    Formato = WmsInboundFormato.Xml,
                    NombreArchivo = "svsh_a.xml",
                    HashArchivo = "hash-svsh-a",
                    Contenido = xmlCompaniaA,
                    Estado = WmsInboundEstado.Pendiente,
                    InsertedAt = DateTimeOffset.UtcNow,
                },
                new WmsOracleInboundStage
                {
                    CompanyId = companyB,
                    TipoDoc = "SVSH",
                    Formato = WmsInboundFormato.Xml,
                    NombreArchivo = "svsh_b.xml",
                    HashArchivo = "hash-svsh-b",
                    Contenido = xmlCompaniaB,
                    Estado = WmsInboundEstado.Pendiente,
                    InsertedAt = DateTimeOffset.UtcNow,
                });
            await seedContext.SaveChangesAsync();
        }

        var parser = new WmsSvshStageParser(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WmsSvshStageParser>.Instance);

        // Llama directo a EjecutarCicloAsync (internal) para que el ciclo itere ambas
        // compañías, fijando ICurrentCompanyOverride.Set(...) por scope antes de resolver
        // WmsDbContext en cada una -- así se confirma que no hay fuga de datos ni de
        // contexto entre compañías.
        await parser.EjecutarCicloAsync(CancellationToken.None);

        await using var verifyContexto = new WmsDbContext(seedOptions);

        var stages = await verifyContexto.WmsOracleInboundStages.ToListAsync();
        Assert.Equal(2, stages.Count);
        Assert.All(stages, s => Assert.Equal(WmsInboundEstado.Aplanado, s.Estado));
        Assert.All(stages, s => Assert.NotNull(s.ProcessedAt));

        var filas = await verifyContexto.WmsOracleStageSvsh.ToListAsync();
        Assert.Equal(2, filas.Count);

        var stageA = stages.Single(s => s.CompanyId == companyA);
        var stageB = stages.Single(s => s.CompanyId == companyB);

        var filaA = filas.Single(f => f.ParentId == stageA.Id);
        var filaB = filas.Single(f => f.ParentId == stageB.Id);

        Assert.Equal("ASN-A-001", filaA.shipment_nbr);
        Assert.Equal("ITEM-A", filaA.item_part_a);

        Assert.Equal("ASN-B-002", filaB.shipment_nbr);
        Assert.Equal("ITEM-B", filaB.item_part_a);

        var heartbeats = await verifyContexto.ServiceHeartbeats.ToListAsync();
        Assert.Equal(2, heartbeats.Count);
        Assert.All(heartbeats, h => Assert.Equal("Wms.SvshStageParser", h.ProcessorKey));
        Assert.All(heartbeats, h => Assert.Equal("OK", h.Status));
        Assert.Contains(heartbeats, h => h.CompanyId == companyA);
        Assert.Contains(heartbeats, h => h.CompanyId == companyB);
    }
}
