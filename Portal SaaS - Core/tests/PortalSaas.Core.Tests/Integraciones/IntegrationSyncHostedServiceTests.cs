using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities.Integraciones;
using PortalSaas.Integrations;
using Xunit;

namespace PortalSaas.Core.Tests.Integraciones;

public class IntegrationSyncHostedServiceTests
{
    private class ConectorFalso : IIntegrationConnector
    {
        private readonly Func<IReadOnlyList<IntegrationRecord>, IReadOnlyList<IntegrationPushResult>>? _resultadosFactory;
        private readonly IReadOnlyList<IntegrationRecord>? _registrosPull;

        public ConectorFalso(
            Func<IReadOnlyList<IntegrationRecord>, IReadOnlyList<IntegrationPushResult>>? resultadosFactory = null,
            IReadOnlyList<IntegrationRecord>? registrosPull = null)
        {
            _resultadosFactory = resultadosFactory;
            _registrosPull = registrosPull;
        }

        public string Tipo => "Sap";
        public bool PushLlamado { get; private set; }
        public IReadOnlyList<IntegrationRecord>? RegistrosRecibidos { get; private set; }

        public DateTimeOffset? CursorRecibido { get; private set; }

        public Task<IReadOnlyList<IntegrationRecord>> PullAsync(string conectorConfigJson, DateTimeOffset? cursorIncremental, CancellationToken cancellationToken)
        {
            CursorRecibido = cursorIncremental;
            return Task.FromResult(_registrosPull ?? Array.Empty<IntegrationRecord>());
        }

        public string DescribirConsulta(string conectorConfigJson, DateTimeOffset? cursorIncremental = null) => "ConectorFalso (test)";

        public Task<IReadOnlyList<IntegrationPushResult>> PushAsync(string conectorConfigJson, IReadOnlyList<IntegrationRecord> registros, CancellationToken cancellationToken)
        {
            PushLlamado = true;
            RegistrosRecibidos = registros;
            var resultados = _resultadosFactory?.Invoke(registros)
                ?? registros.Select(r => new IntegrationPushResult(r, Exito: true, MensajeError: null)).ToList();
            return Task.FromResult(resultados);
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

        public Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, int? limiteMaximo, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IntegrationRecord>>(_registros);

        public Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken)
        {
            LlamadasDeAck.Add((companyId, exito));
            return Task.CompletedTask;
        }
    }

    private class WriterFalso : IIntegrationEntityWriter
    {
        public WriterFalso(string entidadNegocio)
        {
            EntidadNegocio = entidadNegocio;
        }

        public string EntidadNegocio { get; }
        public List<IntegrationRecord> RegistrosRecibidos { get; } = new();

        public Task EscribirAsync(Guid companyId, IReadOnlyList<IntegrationRecord> registros, CancellationToken cancellationToken)
        {
            RegistrosRecibidos.AddRange(registros);
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
        services.AddScoped<ICurrentCompanyOverride, CurrentCompanyOverride>();
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
    public async Task EjecutarCicloAsync_ConIntervaloMinutos_ReprogramaNextRunAtHaciaAdelante()
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
        services.AddScoped<ICurrentCompanyOverride, CurrentCompanyOverride>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<IntegrationSyncHostedService>>(NullLogger<IntegrationSyncHostedService>.Instance);
        var proveedor = services.BuildServiceProvider();

        var definicion = new IntegrationDefinition
        {
            Nombre = "Test", ModuloOrigen = "Wms", EntidadNegocio = "PickingConfirmado",
            ConectorTipo = IntegrationConectorTipo.Sap, ConectorConfigCifrado = "{}",
            Direccion = IntegrationDireccion.Subida, Activo = true,
            IntervaloMinutos = 10,
            NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        using (var scope = proveedor.CreateScope())
        {
            var contexto = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
            contexto.IntegrationDefinitions.Add(definicion);
            await contexto.SaveChangesAsync();
        }

        var antesDeCorrer = DateTimeOffset.UtcNow;
        var servicio = new IntegrationSyncHostedService(proveedor.GetRequiredService<IServiceScopeFactory>(), NullLogger<IntegrationSyncHostedService>.Instance);
        await servicio.EjecutarCicloAsync(CancellationToken.None);

        using var scopeVerificacion = proveedor.CreateScope();
        var contextoVerificacion = scopeVerificacion.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
        var actualizada = await contextoVerificacion.IntegrationDefinitions.SingleAsync(d => d.Id == definicion.Id);

        Assert.NotNull(actualizada.NextRunAt);
        // Reprogramada ~10 min hacia adelante desde el fin de la corrida (con margen para el tiempo de ejecución del test).
        Assert.True(actualizada.NextRunAt >= antesDeCorrer.AddMinutes(9));
        Assert.True(actualizada.NextRunAt <= DateTimeOffset.UtcNow.AddMinutes(11));
    }

    [Fact]
    public async Task EjecutarCicloAsync_SinIntervalo_DejaNextRunAtEnNull()
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
        services.AddScoped<ICurrentCompanyOverride, CurrentCompanyOverride>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<IntegrationSyncHostedService>>(NullLogger<IntegrationSyncHostedService>.Instance);
        var proveedor = services.BuildServiceProvider();

        var definicion = new IntegrationDefinition
        {
            Nombre = "Test", ModuloOrigen = "Wms", EntidadNegocio = "PickingConfirmado",
            ConectorTipo = IntegrationConectorTipo.Sap, ConectorConfigCifrado = "{}",
            Direccion = IntegrationDireccion.Subida, Activo = true,
            IntervaloMinutos = null,
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

        using var scopeVerificacion = proveedor.CreateScope();
        var contextoVerificacion = scopeVerificacion.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
        var actualizada = await contextoVerificacion.IntegrationDefinitions.SingleAsync(d => d.Id == definicion.Id);

        Assert.Null(actualizada.NextRunAt);
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
        services.AddScoped<ICurrentCompanyOverride, CurrentCompanyOverride>();
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

    [Fact]
    public async Task EjecutarCicloAsync_ConPushParcial_AckIndividualYLogParcial()
    {
        var dbName = Guid.NewGuid().ToString();
        var registroOk = new IntegrationRecord(new Dictionary<string, object?> { ["TipoDocumento"] = "Inventory", ["Id"] = 1 });
        var registroFalla = new IntegrationRecord(new Dictionary<string, object?> { ["TipoDocumento"] = "Inventory", ["Id"] = 2 });
        var conectorFalso = new ConectorFalso(registros => registros
            .Select(r => r == registroFalla
                ? new IntegrationPushResult(r, Exito: false, MensajeError: "Falló este registro puntual")
                : new IntegrationPushResult(r, Exito: true, MensajeError: null))
            .ToList());
        var readerFalso = new ReaderFalso(new List<IntegrationRecord> { registroOk, registroFalla });

        var services = new ServiceCollection();
        services.AddDbContext<PortalSaasDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IIntegrationConnector>(conectorFalso);
        services.AddSingleton<IIntegrationEntityReader>(readerFalso);
        services.AddScoped<IIntegrationFieldMappingService, IntegrationFieldMappingService>();
        services.AddScoped<ISecretoCifradoService, SecretoCifradoServiceFalso>();
        services.AddScoped<ICurrentCompanyOverride, CurrentCompanyOverride>();
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

        // El lote completo llega al conector -- ya no se filtra/mapea antes de PushAsync
        // (ver Fix 1), y ninguno de los dos registros se pierde en el ack aunque uno falle.
        Assert.Equal(2, conectorFalso.RegistrosRecibidos?.Count);
        Assert.Equal(2, readerFalso.LlamadasDeAck.Count);
        Assert.Contains(readerFalso.LlamadasDeAck, a => a.Exito);
        Assert.Contains(readerFalso.LlamadasDeAck, a => !a.Exito);

        using var scopeVerificacion = proveedor.CreateScope();
        var contextoVerificacion = scopeVerificacion.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
        var log = await contextoVerificacion.IntegrationRunLogs
            .SingleAsync(l => l.IntegrationDefinitionId == definicion.Id);

        Assert.Equal(IntegrationRunResultado.Parcial, log.Resultado);
        Assert.Equal(1, log.RegistrosProcesados);
        Assert.Equal(1, log.RegistrosConError);
    }

    [Fact]
    public async Task EjecutarCicloAsync_DireccionBajadaConWriterYPullExitoso_EscribeYRegistraLog()
    {
        var dbName = Guid.NewGuid().ToString();
        var registrosPull = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["ItemCode"] = "A1" }),
            new(new Dictionary<string, object?> { ["ItemCode"] = "A2" }),
        };
        var conectorFalso = new ConectorFalso(registrosPull: registrosPull);
        var writerFalso = new WriterFalso(entidadNegocio: "SapWms.Item");

        var services = new ServiceCollection();
        services.AddDbContext<PortalSaasDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IIntegrationConnector>(conectorFalso);
        services.AddSingleton<IIntegrationEntityWriter>(writerFalso);
        services.AddScoped<IIntegrationFieldMappingService, IntegrationFieldMappingService>();
        services.AddScoped<ISecretoCifradoService, SecretoCifradoServiceFalso>();
        services.AddScoped<ICurrentCompanyOverride, CurrentCompanyOverride>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<IntegrationSyncHostedService>>(NullLogger<IntegrationSyncHostedService>.Instance);
        var proveedor = services.BuildServiceProvider();

        var definicion = new IntegrationDefinition
        {
            Nombre = "Test", ModuloOrigen = "Wms", EntidadNegocio = "SapWms.Item",
            ConectorTipo = IntegrationConectorTipo.Sap, ConectorConfigCifrado = "{}",
            Direccion = IntegrationDireccion.Bajada, Activo = true,
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

        Assert.Equal(2, writerFalso.RegistrosRecibidos.Count);

        using var scopeVerificacion = proveedor.CreateScope();
        var contextoVerificacion = scopeVerificacion.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
        var log = await contextoVerificacion.IntegrationRunLogs
            .SingleAsync(l => l.IntegrationDefinitionId == definicion.Id);

        Assert.Equal(IntegrationRunResultado.Exito, log.Resultado);
        Assert.Equal(2, log.RegistrosProcesados);
    }

    [Fact]
    public async Task EjecutarCicloAsync_DireccionBajadaExitosa_PasaCursorAlConectorYLoAvanzaTrasElExito()
    {
        var dbName = Guid.NewGuid().ToString();
        var conectorFalso = new ConectorFalso(registrosPull: new List<IntegrationRecord>());
        var writerFalso = new WriterFalso(entidadNegocio: "SapWms.Item");

        var services = new ServiceCollection();
        services.AddDbContext<PortalSaasDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IIntegrationConnector>(conectorFalso);
        services.AddSingleton<IIntegrationEntityWriter>(writerFalso);
        services.AddScoped<IIntegrationFieldMappingService, IntegrationFieldMappingService>();
        services.AddScoped<ISecretoCifradoService, SecretoCifradoServiceFalso>();
        services.AddScoped<ICurrentCompanyOverride, CurrentCompanyOverride>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<IntegrationSyncHostedService>>(NullLogger<IntegrationSyncHostedService>.Instance);
        var proveedor = services.BuildServiceProvider();

        var cursorPrevio = DateTimeOffset.UtcNow.AddDays(-1);
        var definicion = new IntegrationDefinition
        {
            Nombre = "Test", ModuloOrigen = "Wms", EntidadNegocio = "SapWms.Item",
            ConectorTipo = IntegrationConectorTipo.Sap, ConectorConfigCifrado = "{}",
            Direccion = IntegrationDireccion.Bajada, Activo = true,
            NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UltimaSincronizacionExitosa = cursorPrevio,
        };

        using (var scope = proveedor.CreateScope())
        {
            var contexto = scope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
            contexto.IntegrationDefinitions.Add(definicion);
            await contexto.SaveChangesAsync();
        }

        var antesDeCorrer = DateTimeOffset.UtcNow;
        var servicio = new IntegrationSyncHostedService(proveedor.GetRequiredService<IServiceScopeFactory>(), NullLogger<IntegrationSyncHostedService>.Instance);
        await servicio.EjecutarCicloAsync(CancellationToken.None);

        Assert.Equal(cursorPrevio, conectorFalso.CursorRecibido);

        using var scopeVerificacion = proveedor.CreateScope();
        var contextoVerificacion = scopeVerificacion.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
        var definicionActualizada = await contextoVerificacion.IntegrationDefinitions.SingleAsync(d => d.Id == definicion.Id);

        Assert.NotNull(definicionActualizada.UltimaSincronizacionExitosa);
        Assert.True(definicionActualizada.UltimaSincronizacionExitosa >= antesDeCorrer);
    }

    [Fact]
    public async Task EjecutarCicloAsync_DireccionBajadaSinWriterRegistrado_LanzaYRegistraError()
    {
        var dbName = Guid.NewGuid().ToString();
        var conectorFalso = new ConectorFalso();

        var services = new ServiceCollection();
        services.AddDbContext<PortalSaasDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IIntegrationConnector>(conectorFalso);
        services.AddScoped<IIntegrationFieldMappingService, IntegrationFieldMappingService>();
        services.AddScoped<ISecretoCifradoService, SecretoCifradoServiceFalso>();
        services.AddScoped<ICurrentCompanyOverride, CurrentCompanyOverride>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<IntegrationSyncHostedService>>(NullLogger<IntegrationSyncHostedService>.Instance);
        var proveedor = services.BuildServiceProvider();

        var definicion = new IntegrationDefinition
        {
            Nombre = "Test", ModuloOrigen = "Wms", EntidadNegocio = "Entidad.Inexistente",
            ConectorTipo = IntegrationConectorTipo.Sap, ConectorConfigCifrado = "{}",
            Direccion = IntegrationDireccion.Bajada, Activo = true,
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

        using var scopeVerificacion = proveedor.CreateScope();
        var contextoVerificacion = scopeVerificacion.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
        var log = await contextoVerificacion.IntegrationRunLogs
            .SingleAsync(l => l.IntegrationDefinitionId == definicion.Id);

        Assert.Equal(IntegrationRunResultado.Error, log.Resultado);
        Assert.Contains("No hay writer registrado", log.DetalleError);
    }
}
