using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulo.Wms.Data;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;
using Xunit;

namespace Modulo.Wms.Tests.Services;

/// <summary>
/// Mismo patrón de contenedor DI real que WmsSlshStageParserTests (no existe un
/// WmsTestServiceProviderFactory compartido en este proyecto de tests) -- fakes
/// mínimos de ICurrentCompanyOverride/ICurrentCompanyAccessor respaldando un
/// WmsDbContext InMemory, para reproducir el gating por HasCompany que
/// WmsServiceHeartbeatRecorder hereda al depender de WmsDbContext.
/// </summary>
public class WmsServiceHeartbeatRecorderTests
{
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

    private static ServiceProvider BuildProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentCompanyOverride, FakeCurrentCompanyOverride>();
        services.AddScoped<ICurrentCompanyAccessor, FakeCurrentCompanyAccessor>();
        services.AddLogging();

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
    public async Task RecordAsync_PrimeraVez_InsertaFila()
    {
        var companyId = Guid.NewGuid();
        var provider = BuildProvider(Guid.NewGuid().ToString());

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();

        var recorder = new WmsServiceHeartbeatRecorder(contexto);
        await recorder.RecordAsync(companyId, "Wms.SlshStageParser", "OK");

        var fila = await contexto.ServiceHeartbeats.SingleAsync();
        Assert.Equal("OK", fila.Status);
        Assert.NotNull(fila.LastRunAt);
    }

    [Fact]
    public async Task RecordAsync_SegundaVezMismaClave_ActualizaEnVezDeDuplicar()
    {
        var companyId = Guid.NewGuid();
        var provider = BuildProvider(Guid.NewGuid().ToString());

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
        var contexto = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var recorder = new WmsServiceHeartbeatRecorder(contexto);

        await recorder.RecordAsync(companyId, "Wms.SlshStageParser", "OK");
        await recorder.RecordAsync(companyId, "Wms.SlshStageParser", "ERROR", "fallo de red");

        var filas = await contexto.ServiceHeartbeats.ToListAsync();
        Assert.Single(filas);
        Assert.Equal("ERROR", filas[0].Status);
        Assert.Equal("fallo de red", filas[0].LastError);
    }
}
