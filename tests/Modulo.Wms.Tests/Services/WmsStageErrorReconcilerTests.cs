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
        public FakeIntegrationConnectorConfigService(string? config) => _config = config;
        public Task<string?> GetDecryptedConfigAsync(Guid companyId, string moduloOrigen, string conectorTipo, CancellationToken ct = default) => Task.FromResult(_config);
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
    }
}
