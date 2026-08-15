using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Data;
using PortalSaas.Data.Entities.Integraciones;
using PortalSaas.Integrations;
using Xunit;

namespace PortalSaas.Core.Tests.Integraciones;

public class IntegrationSyncHostedServiceTests
{
    private class ConectorFalso : IIntegrationConnector
    {
        public string Tipo => "Sap";
        public bool PushLlamado { get; private set; }

        public Task<IReadOnlyList<IntegrationRecord>> PullAsync(string conectorConfigJson, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IntegrationRecord>>(Array.Empty<IntegrationRecord>());

        public Task PushAsync(string conectorConfigJson, IReadOnlyList<IntegrationRecord> registros, CancellationToken cancellationToken)
        {
            PushLlamado = true;
            return Task.CompletedTask;
        }
    }

    private class SecretoCifradoServiceFalso : ISecretoCifradoService
    {
        public string Encrypt(string plainText) => plainText;

        public string Decrypt(string cipherText) => cipherText;
    }

    private class ReaderFalso : IIntegrationEntityReader
    {
        private readonly List<IntegrationRecord> _registros;
        public List<(Guid CompanyId, bool Exito)> LlamadasDeAck { get; } = new();

        public ReaderFalso(List<IntegrationRecord> registros, string entidadNegocio = "Wms.ConfirmacionTraslado")
        {
            _registros = registros;
            EntidadNegocio = entidadNegocio;
        }

        public string EntidadNegocio { get; }

        public Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IntegrationRecord>>(_registros);

        public Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken)
        {
            LlamadasDeAck.Add((companyId, exito));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task EjecutarCicloAsync_IntegracionVencida_EjecutaYRegistraLog()
    {
        var dbName = Guid.NewGuid().ToString();
        var conectorFalso = new ConectorFalso();
        var readerFalso = new ReaderFalso(new List<IntegrationRecord>(), entidadNegocio: "PickingConfirmado");

        var services = new ServiceCollection();
        services.AddDbContext<PortalSaasDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IIntegrationConnector>(conectorFalso);
        services.AddSingleton<IIntegrationEntityReader>(readerFalso);
        services.AddScoped<IIntegrationFieldMappingService, IntegrationFieldMappingService>();
        services.AddScoped<ISecretoCifradoService, SecretoCifradoServiceFalso>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<IntegrationSyncHostedService>>(NullLogger<IntegrationSyncHostedService>.Instance);
        var proveedor = services.BuildServiceProvider();

        var definicion = new IntegrationDefinition
        {
            Nombre = "Test",
            ModuloOrigen = "Wms",
            EntidadNegocio = "PickingConfirmado",
            ConectorTipo = IntegrationConectorTipo.Sap,
            ConectorConfigCifrado = "{}",
            Direccion = IntegrationDireccion.Subida,
            Activo = true,
            NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        using (var scope = proveedor.CreateScope())
        {
            var contexto = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
            contexto.IntegrationDefinitions.Add(definicion);
            await contexto.SaveChangesAsync();
        }

        var servicio = new IntegrationSyncHostedService(
            proveedor.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IntegrationSyncHostedService>.Instance);

        await servicio.EjecutarCicloAsync(CancellationToken.None);

        using var scopeVerificacion = proveedor.CreateScope();
        var contextoVerificacion = scopeVerificacion.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
        var logs = await contextoVerificacion.IntegrationRunLogs
            .Where(l => l.IntegrationDefinitionId == definicion.Id)
            .ToListAsync();

        Assert.Single(logs);
        Assert.Equal(IntegrationRunResultado.Exito, logs[0].Resultado);
    }

    [Fact]
    public async Task EjecutarCicloAsync_ConReaderYPushExitoso_LlamaAckConExitoTrue()
    {
        var dbName = Guid.NewGuid().ToString();
        var conectorFalso = new ConectorFalso();
        var registroDePrueba = new IntegrationRecord(new Dictionary<string, object?> { ["TipoDocumento"] = "Inventory" });
        var readerFalso = new ReaderFalso(new List<IntegrationRecord> { registroDePrueba });

        var services = new ServiceCollection();
        services.AddDbContext<PortalSaasDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IIntegrationConnector>(conectorFalso);
        services.AddSingleton<IIntegrationEntityReader>(readerFalso);
        services.AddScoped<IIntegrationFieldMappingService, IntegrationFieldMappingService>();
        services.AddScoped<ISecretoCifradoService, SecretoCifradoServiceFalso>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<IntegrationSyncHostedService>>(NullLogger<IntegrationSyncHostedService>.Instance);
        var proveedor = services.BuildServiceProvider();

        var definicion = new IntegrationDefinition
        {
            Nombre = "Test", ModuloOrigen = "Wms", EntidadNegocio = "Wms.ConfirmacionTraslado",
            ConectorTipo = IntegrationConectorTipo.Sap, ConectorConfigCifrado = "{}",
            Direccion = IntegrationDireccion.Subida, Activo = true,
            NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        using (var scope = proveedor.CreateScope())
        {
            var contexto = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
            contexto.IntegrationDefinitions.Add(definicion);
            await contexto.SaveChangesAsync();
        }

        var servicio = new IntegrationSyncHostedService(proveedor.GetRequiredService<IServiceScopeFactory>(), NullLogger<IntegrationSyncHostedService>.Instance);
        await servicio.EjecutarCicloAsync(CancellationToken.None);

        Assert.Single(readerFalso.LlamadasDeAck);
        Assert.True(readerFalso.LlamadasDeAck[0].Exito);
        Assert.True(conectorFalso.PushLlamado);
    }
}
