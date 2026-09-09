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

public class WmsExistsReconcilerTests
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
        public Task<ExternalDatabaseConnection> ResolveConnectionAsync(string moduleCode, Guid companyId, string purpose, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ModuleCompanyDto>> ListActiveCompanyIdsAsync(string moduleCode, CancellationToken ct = default) => Task.FromResult(_companias);
    }

    private sealed class FakeIntegrationConnectorConfigService : IIntegrationConnectorConfigService
    {
        private readonly string? _config;
        public FakeIntegrationConnectorConfigService(string? config) => _config = config;
        public Task<string?> GetDecryptedConfigAsync(Guid companyId, string moduloOrigen, string conectorTipo, CancellationToken ct = default) => Task.FromResult(_config);
    }

    private sealed class FakeWmsValidationApiClient : IWmsValidationApiClient
    {
        private readonly WmsStageCheckResult _resultadoDefault;
        private readonly Dictionary<string, WmsStageCheckResult> _resultadosPorEntidad;

        public FakeWmsValidationApiClient(WmsStageCheckResult resultado)
        {
            _resultadoDefault = resultado;
            _resultadosPorEntidad = new Dictionary<string, WmsStageCheckResult>();
        }

        public FakeWmsValidationApiClient(WmsStageCheckResult resultadoDefault, Dictionary<string, WmsStageCheckResult> resultadosPorEntidad)
        {
            _resultadoDefault = resultadoDefault;
            _resultadosPorEntidad = resultadosPorEntidad;
        }

        public Task<WmsStageCheckResult> CheckStageRecordAsync(string lgfApiBaseUrl, string usuario, string clave, string entity, string keyField, string keyValue, string? companyCode, bool filtrarPorUrl, CancellationToken ct = default)
            => Task.FromResult(_resultadosPorEntidad.TryGetValue(entity, out var especifico) ? especifico : _resultadoDefault);
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
        services.AddSingleton(NullLogger<WmsExistsReconciler>.Instance);
        services.AddScoped<IWmsServiceHeartbeatRecorder, WmsServiceHeartbeatRecorder>();
        services.AddMemoryCache();
        services.AddScoped<IWmsRuntimeSettingsService, WmsRuntimeSettingsService>();

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

    private static ServiceProvider BuildProvider(string dbName, IReadOnlyList<ModuleCompanyDto> companiasActivas, string? configJson, WmsStageCheckResult resultadoDefault, Dictionary<string, WmsStageCheckResult> resultadosPorEntidad)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentCompanyOverride, FakeCurrentCompanyOverride>();
        services.AddScoped<ICurrentCompanyAccessor, FakeCurrentCompanyAccessor>();
        services.AddSingleton<IExternalDatabaseConnectionService>(new FakeExternalDatabaseConnectionService(companiasActivas));
        services.AddSingleton<IIntegrationConnectorConfigService>(new FakeIntegrationConnectorConfigService(configJson));
        services.AddSingleton<IWmsValidationApiClient>(new FakeWmsValidationApiClient(resultadoDefault, resultadosPorEntidad));
        services.AddLogging();
        services.AddSingleton(NullLogger<WmsExistsReconciler>.Instance);
        services.AddScoped<IWmsServiceHeartbeatRecorder, WmsServiceHeartbeatRecorder>();
        services.AddMemoryCache();
        services.AddScoped<IWmsRuntimeSettingsService, WmsRuntimeSettingsService>();

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
    public async Task EjecutarCicloAsync_RegistroEncontradoEnEntidadFinal_LoMarcaProcesadoWms()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var configJson = """{"ApiUrl":"https://x","Usuario":"u","Clave":"p","ClientEnvCode":"cli","ParentCompanyCode":"COMP01","LgfApiBaseUrl":"https://x/lgfapi/v10/entity/"}""";
        // Ya no está en stage_item (Oracle terminó de procesarlo) y sí aparece en la entidad final "item".
        var resultadosPorEntidad = new Dictionary<string, WmsStageCheckResult>
        {
            ["stage_item"] = new WmsStageCheckResult(Found: false, StatusId: null, ErrorMessage: null),
            ["item"] = new WmsStageCheckResult(Found: true, StatusId: 90, ErrorMessage: null),
        };

        await using var provider = BuildProvider(dbName, [new(companyId, Guid.NewGuid())], configJson,
            resultadoDefault: new WmsStageCheckResult(Found: false, StatusId: null, ErrorMessage: null),
            resultadosPorEntidad: resultadosPorEntidad);

        var seedOptions = new DbContextOptionsBuilder<WmsDbContext>().UseInMemoryDatabase(dbName).Options;
        await using (var seedContext = new WmsDbContext(seedOptions))
        {
            seedContext.WmsSapStageItems.Add(new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Enviado });
            await seedContext.SaveChangesAsync();
        }

        var reconciler = new WmsExistsReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WmsExistsReconciler>.Instance);

        await reconciler.EjecutarCicloAsync(CancellationToken.None);

        await using var verifyContext = new WmsDbContext(seedOptions);
        var fila = await verifyContext.WmsSapStageItems.SingleAsync();
        Assert.Equal(WmsSapStageStatus.ProcesadoWms, fila.Status);
        Assert.NotNull(fila.SyncedAt);

        var heartbeat = await verifyContext.ServiceHeartbeats.SingleAsync();
        Assert.Equal(companyId, heartbeat.CompanyId);
        Assert.Equal("Wms.ExistsReconciler", heartbeat.ProcessorKey);
        Assert.Equal("OK", heartbeat.Status);
    }

    [Fact]
    public async Task EjecutarCicloAsync_SinConfigParaLaCompania_RegistraHeartbeatSinConfig()
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

        var reconciler = new WmsExistsReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WmsExistsReconciler>.Instance);

        await reconciler.EjecutarCicloAsync(CancellationToken.None);

        await using var verifyContext = new WmsDbContext(seedOptions);
        var fila = await verifyContext.WmsSapStageItems.SingleAsync();
        Assert.Equal(WmsSapStageStatus.Enviado, fila.Status);

        // Sin config activa, el ciclo no debe quedar mudo -- el heartbeat se registra con
        // un status distintivo ("SIN_CONFIG") para que la pantalla "Estado del Servicio"
        // (Task 12) distinga "sin config" de "servicio caído".
        var heartbeat = await verifyContext.ServiceHeartbeats.SingleAsync();
        Assert.Equal(companyId, heartbeat.CompanyId);
        Assert.Equal("Wms.ExistsReconciler", heartbeat.ProcessorKey);
        Assert.Equal("SIN_CONFIG", heartbeat.Status);
    }

    [Fact]
    public async Task EjecutarCicloAsync_NoEncontradoTrasVeinteIntentos_LoMarcaErrorWms()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var configJson = """{"ApiUrl":"https://x","Usuario":"u","Clave":"p","ClientEnvCode":"cli","ParentCompanyCode":"COMP01","LgfApiBaseUrl":"https://x/lgfapi/v10/entity/"}""";
        var resultadoLgfApi = new WmsStageCheckResult(Found: false, StatusId: null, ErrorMessage: null);

        await using var provider = BuildProvider(dbName, [new(companyId, Guid.NewGuid())], configJson, resultadoLgfApi);

        var seedOptions = new DbContextOptionsBuilder<WmsDbContext>().UseInMemoryDatabase(dbName).Options;
        await using (var seedContext = new WmsDbContext(seedOptions))
        {
            seedContext.WmsSapStageItems.Add(new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Enviado });
            seedContext.WmsExportValidations.Add(new WmsExportValidation { CompanyId = companyId, TipoDoc = "Item", Clave = "ITM001", Intentos = 19 });
            await seedContext.SaveChangesAsync();
        }

        var reconciler = new WmsExistsReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WmsExistsReconciler>.Instance);

        await reconciler.EjecutarCicloAsync(CancellationToken.None);

        await using var verifyContext = new WmsDbContext(seedOptions);
        var fila = await verifyContext.WmsSapStageItems.SingleAsync();
        Assert.Equal(WmsSapStageStatus.ErrorWms, fila.Status);

        var validacion = await verifyContext.WmsExportValidations.SingleAsync();
        Assert.Equal(20, validacion.Intentos);
    }

    [Fact]
    public async Task EjecutarCicloAsync_TodaviaEnStage_NoConfirmaNiIncrementaIntentos()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var configJson = """{"ApiUrl":"https://x","Usuario":"u","Clave":"p","ClientEnvCode":"cli","ParentCompanyCode":"COMP01","LgfApiBaseUrl":"https://x/lgfapi/v10/entity/"}""";
        // Todavía está en stage_item -- Oracle no terminó de procesar este envío/reenvío.
        // No debería ni confirmarse contra "item" ni incrementar Intentos.
        var resultadosPorEntidad = new Dictionary<string, WmsStageCheckResult>
        {
            ["stage_item"] = new WmsStageCheckResult(Found: true, StatusId: 50, ErrorMessage: null),
            ["item"] = new WmsStageCheckResult(Found: true, StatusId: 90, ErrorMessage: null),
        };

        await using var provider = BuildProvider(dbName, [new(companyId, Guid.NewGuid())], configJson,
            resultadoDefault: new WmsStageCheckResult(Found: false, StatusId: null, ErrorMessage: null),
            resultadosPorEntidad: resultadosPorEntidad);

        var seedOptions = new DbContextOptionsBuilder<WmsDbContext>().UseInMemoryDatabase(dbName).Options;
        await using (var seedContext = new WmsDbContext(seedOptions))
        {
            seedContext.WmsSapStageItems.Add(new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Enviado });
            seedContext.WmsExportValidations.Add(new WmsExportValidation { CompanyId = companyId, TipoDoc = "Item", Clave = "ITM001", Intentos = 0 });
            await seedContext.SaveChangesAsync();
        }

        var reconciler = new WmsExistsReconciler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WmsExistsReconciler>.Instance);

        await reconciler.EjecutarCicloAsync(CancellationToken.None);

        await using var verifyContext = new WmsDbContext(seedOptions);
        var fila = await verifyContext.WmsSapStageItems.SingleAsync();
        Assert.Equal(WmsSapStageStatus.Enviado, fila.Status);

        var validacion = await verifyContext.WmsExportValidations.SingleAsync();
        Assert.Equal(0, validacion.Intentos);
    }
}
