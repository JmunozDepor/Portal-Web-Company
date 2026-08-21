using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsStageErrorReconcilerTests
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

    private sealed class FakeExternalDatabaseConnectionService : IExternalDatabaseConnectionService
    {
        private readonly IReadOnlyList<ModuleCompanyDto> _companias;
        public FakeExternalDatabaseConnectionService(IReadOnlyList<ModuleCompanyDto> companias) => _companias = companias;
        public Task<ExternalDatabaseConnection> ResolveConnectionAsync(string moduleCode, Guid companyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ModuleCompanyDto>> ListActiveCompanyIdsAsync(string moduleCode, CancellationToken ct = default) => Task.FromResult(_companias);
    }

    private sealed class FakeIntegrationConnectorConfigService : IIntegrationConnectorConfigService
    {
        private readonly string? _config;
        private readonly IReadOnlyDictionary<Guid, string?>? _configPorCompania;
        public FakeIntegrationConnectorConfigService(string? config) => _config = config;
        public FakeIntegrationConnectorConfigService(IReadOnlyDictionary<Guid, string?> configPorCompania) => _configPorCompania = configPorCompania;
        public Task<string?> GetDecryptedConfigAsync(Guid companyId, string moduloOrigen, string conectorTipo, CancellationToken ct = default)
            => Task.FromResult(_configPorCompania is not null ? _configPorCompania[companyId] : _config);
    }

    private sealed class FakeWmsValidationApiClient : IWmsValidationApiClient
    {
        private readonly WmsStageCheckResult _resultado;
        public FakeWmsValidationApiClient(WmsStageCheckResult resultado) => _resultado = resultado;
        public Task<WmsStageCheckResult> CheckStageRecordAsync(string lgfApiBaseUrl, string usuario, string clave, string entity, string keyField, string keyValue, string? companyCode, bool filtrarPorUrl, CancellationToken ct = default)
            => Task.FromResult(_resultado);
    }

    private static ServiceProvider BuildProvider(string dbName, IReadOnlyList<ModuleCompanyDto> companiasActivas, string? configJson, WmsStageCheckResult resultadoLgfApi)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentCompanyOverride, FakeCurrentCompanyOverride>();
        services.AddScoped<ICurrentCompanyAccessor, FakeCurrentCompanyAccessor>();
        services.AddSingleton<IExternalDatabaseConnectionService>(new FakeExternalDatabaseConnectionService(companiasActivas));
        services.AddSingleton<IIntegrationConnectorConfigService>(new FakeIntegrationConnectorConfigService(configJson));
        services.AddSingleton<IWmsValidationApiClient>(new FakeWmsValidationApiClient(resultadoLgfApi));
        services.AddLogging();
        services.AddSingleton(NullLogger<WmsStageErrorReconciler>.Instance);
        services.AddScoped<IWmsServiceHeartbeatRecorder, WmsServiceHeartbeatRecorder>();

        services.AddDbContext<WmsDbContext>((sp, options) =>
        {
            var companyAccessor = sp.GetRequiredService<ICurrentCompanyAccessor>();
            if (!companyAccessor.HasCompany)
            {
                throw new InvalidOperationException("Modulo.Wms requiere una compañía activa en la sesión -- seleccioná una compañía antes de continuar.");
            }
            options.UseInMemoryDatabase(dbName);
        });

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildProviderConfigPorCompania(string dbName, IReadOnlyList<ModuleCompanyDto> companiasActivas, IReadOnlyDictionary<Guid, string?> configPorCompania, WmsStageCheckResult resultadoLgfApi)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentCompanyOverride, FakeCurrentCompanyOverride>();
        services.AddScoped<ICurrentCompanyAccessor, FakeCurrentCompanyAccessor>();
        services.AddSingleton<IExternalDatabaseConnectionService>(new FakeExternalDatabaseConnectionService(companiasActivas));
        services.AddSingleton<IIntegrationConnectorConfigService>(new FakeIntegrationConnectorConfigService(configPorCompania));
        services.AddSingleton<IWmsValidationApiClient>(new FakeWmsValidationApiClient(resultadoLgfApi));
        services.AddLogging();
        services.AddSingleton(NullLogger<WmsStageErrorReconciler>.Instance);
        services.AddScoped<IWmsServiceHeartbeatRecorder, WmsServiceHeartbeatRecorder>();

        services.AddDbContext<WmsDbContext>((sp, options) =>
        {
            var companyAccessor = sp.GetRequiredService<ICurrentCompanyAccessor>();
            if (!companyAccessor.HasCompany)
            {
                throw new InvalidOperationException("Modulo.Wms requiere una compañía activa en la sesión -- seleccioná una compañía antes de continuar.");
            }
            options.UseInMemoryDatabase(dbName);
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task EjecutarCicloAsync_RegistroEnviadoConStatus101_LoMarcaErrorWms()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var configJson = """{"ApiUrl":"https://x","Usuario":"u","Clave":"p","ClientEnvCode":"cli","ParentCompanyCode":"COMP01","LgfApiBaseUrl":"https://x/lgfapi/v10/entity/"}""";
        var resultadoLgfApi = new WmsStageCheckResult(Found: true, StatusId: 101, ErrorMessage: "Item duplicado");

        await using var provider = BuildProvider(dbName, [new(companyId, Guid.NewGuid())], configJson, resultadoLgfApi);

        var seedOptions = new DbContextOptionsBuilder<WmsDbContext>().UseInMemoryDatabase(dbName).Options;
        await using (var seedContext = new WmsDbContext(seedOptions))
        {
            seedContext.WmsSapStageItems.Add(new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Enviado });
            await seedContext.SaveChangesAsync();
        }

        var reconciler = new WmsStageErrorReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WmsStageErrorReconciler>.Instance);

        await reconciler.EjecutarCicloAsync(CancellationToken.None);

        await using var verifyContext = new WmsDbContext(seedOptions);
        var fila = await verifyContext.WmsSapStageItems.SingleAsync();
        Assert.Equal(WmsSapStageStatus.ErrorWms, fila.Status);
        Assert.Equal("Item duplicado", fila.ErrorMsg);

        var validacion = await verifyContext.WmsExportValidations.SingleAsync();
        Assert.Equal(101, validacion.WmsStatusId);

        var heartbeat = await verifyContext.ServiceHeartbeats.SingleAsync();
        Assert.Equal(companyId, heartbeat.CompanyId);
        Assert.Equal("Wms.StageErrorReconciler", heartbeat.ProcessorKey);
        Assert.Equal("OK", heartbeat.Status);
    }

    [Fact]
    public async Task EjecutarCicloAsync_SinConfigParaLaCompania_NoRompeYNoTocaElRegistro()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var resultadoLgfApi = new WmsStageCheckResult(Found: false, StatusId: null, ErrorMessage: null);

        await using var provider = BuildProvider(dbName, [new(companyId, Guid.NewGuid())], configJson: null, resultadoLgfApi);

        var seedOptions = new DbContextOptionsBuilder<WmsDbContext>().UseInMemoryDatabase(dbName).Options;
        await using (var seedContext = new WmsDbContext(seedOptions))
        {
            seedContext.WmsSapStageItems.Add(new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Enviado });
            await seedContext.SaveChangesAsync();
        }

        var reconciler = new WmsStageErrorReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WmsStageErrorReconciler>.Instance);

        await reconciler.EjecutarCicloAsync(CancellationToken.None);

        await using var verifyContext = new WmsDbContext(seedOptions);
        var fila = await verifyContext.WmsSapStageItems.SingleAsync();
        Assert.Equal(WmsSapStageStatus.Enviado, fila.Status);

        // Sin config activa para la compañía, el ciclo no debe quedar mudo -- el heartbeat
        // se registra con un status distintivo ("SIN_CONFIG") para que la pantalla "Estado
        // del Servicio" (Task 12) distinga "sin config" de "servicio caído".
        var heartbeat = await verifyContext.ServiceHeartbeats.SingleAsync();
        Assert.Equal(companyId, heartbeat.CompanyId);
        Assert.Equal("Wms.StageErrorReconciler", heartbeat.ProcessorKey);
        Assert.Equal("SIN_CONFIG", heartbeat.Status);
    }

    [Fact]
    public async Task EjecutarCicloAsync_PrimeraCompaniaFallaConConfigMalformada_SiguienteCompaniaSeProcesaIgual()
    {
        var companyId1 = Guid.NewGuid();
        var companyId2 = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var configMalformado = "{ esto no es json valido de configuracion ";
        var configValido = """{"ApiUrl":"https://x","Usuario":"u","Clave":"p","ClientEnvCode":"cli","ParentCompanyCode":"COMP01","LgfApiBaseUrl":"https://x/lgfapi/v10/entity/"}""";
        var configPorCompania = new Dictionary<Guid, string?>
        {
            [companyId1] = configMalformado,
            [companyId2] = configValido,
        };
        var resultadoLgfApi = new WmsStageCheckResult(Found: true, StatusId: 101, ErrorMessage: "Item duplicado");

        await using var provider = BuildProviderConfigPorCompania(
            dbName,
            [new(companyId1, Guid.NewGuid()), new(companyId2, Guid.NewGuid())],
            configPorCompania,
            resultadoLgfApi);

        var seedOptions = new DbContextOptionsBuilder<WmsDbContext>().UseInMemoryDatabase(dbName).Options;
        await using (var seedContext = new WmsDbContext(seedOptions))
        {
            seedContext.WmsSapStageItems.Add(new WmsSapStageItem { CompanyId = companyId1, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Enviado });
            seedContext.WmsSapStageItems.Add(new WmsSapStageItem { CompanyId = companyId2, ItemCode = "ITM002", ItemName = "B", Status = WmsSapStageStatus.Enviado });
            await seedContext.SaveChangesAsync();
        }

        var reconciler = new WmsStageErrorReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WmsStageErrorReconciler>.Instance);

        await reconciler.EjecutarCicloAsync(CancellationToken.None);

        await using var verifyContext = new WmsDbContext(seedOptions);
        var fila1 = await verifyContext.WmsSapStageItems.SingleAsync(f => f.CompanyId == companyId1);
        var fila2 = await verifyContext.WmsSapStageItems.SingleAsync(f => f.CompanyId == companyId2);

        Assert.Equal(WmsSapStageStatus.Enviado, fila1.Status);
        Assert.Equal(WmsSapStageStatus.ErrorWms, fila2.Status);
        Assert.Equal("Item duplicado", fila2.ErrorMsg);

        // El heartbeat de cada compañía es independiente: la 1 que revienta con config
        // malformada queda en ERROR (con el detalle de la excepción), la 2 sigue
        // procesándose con normalidad y queda en OK -- confirma que el catch nuevo del
        // heartbeat no interrumpe el resto del ciclo (issue Important #1 de la revisión).
        var heartbeat1 = await verifyContext.ServiceHeartbeats.SingleAsync(h => h.CompanyId == companyId1);
        var heartbeat2 = await verifyContext.ServiceHeartbeats.SingleAsync(h => h.CompanyId == companyId2);
        Assert.Equal("ERROR", heartbeat1.Status);
        Assert.NotNull(heartbeat1.LastError);
        Assert.Equal("OK", heartbeat2.Status);
    }
}
