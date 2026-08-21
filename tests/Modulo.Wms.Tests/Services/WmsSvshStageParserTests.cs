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
    }
}
