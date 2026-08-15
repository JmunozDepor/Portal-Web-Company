using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Integraciones;
using Xunit;

namespace PortalSaas.Core.Tests.Integraciones;

public class SapDocumentConnectorTests
{
    private sealed class SalesDocumentServiceFalso : ISalesDocumentService
    {
        public Task<bool> CanCreateAsync(SalesDocumentType type, CancellationToken ct = default) => Task.FromResult(true);
        public Task<int> CreateAsync(SalesDocumentType type, string portalUsername, SalesDocumentDto document, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task AddLinesAsync(SalesDocumentType type, int docEntry, IReadOnlyList<SalesDocumentLineDto> newLines, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<SalesDocumentDto?> GetAsync(SalesDocumentType type, int docEntry, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<SalesDocumentListResult> ListAsync(SalesDocumentType type, SalesDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public int GetSapObjectCode(SalesDocumentType type)
            => throw new InvalidOperationException("No debería llamarse en este test.");
    }

    private sealed class PurchaseDocumentServiceFalso : IPurchaseDocumentService
    {
        public Task<bool> CanCreateAsync(PurchaseDocumentType type, CancellationToken ct = default) => Task.FromResult(true);
        public Task<int> CreateAsync(PurchaseDocumentType type, string portalUsername, PurchaseDocumentDto document, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task AddLinesAsync(PurchaseDocumentType type, int docEntry, IReadOnlyList<PurchaseDocumentLineDto> newLines, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<PurchaseDocumentDto?> GetAsync(PurchaseDocumentType type, int docEntry, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<PurchaseDocumentListResult> ListAsync(PurchaseDocumentType type, PurchaseDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public int GetSapObjectCode(PurchaseDocumentType type)
            => throw new InvalidOperationException("No debería llamarse en este test.");
    }

    private sealed class InventoryDocumentServiceFalso : IInventoryDocumentService
    {
        public Task<bool> CanCreateAsync(InventoryDocumentType type, CancellationToken ct = default) => Task.FromResult(true);
        public Task<int> CreateAsync(InventoryDocumentType type, string portalUsername, InventoryDocumentDto document, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task AddLinesAsync(InventoryDocumentType type, int docEntry, IReadOnlyList<InventoryDocumentLineDto> newLines, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<InventoryDocumentDto?> GetAsync(InventoryDocumentType type, int docEntry, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<InventoryDocumentListResult> ListAsync(InventoryDocumentType type, InventoryDocumentFilter? filter = null, int page = 1, int pageSize = 25, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public int GetSapObjectCode(InventoryDocumentType type)
            => throw new InvalidOperationException("No debería llamarse en este test.");
    }

    private static SapDocumentConnector CrearConector()
        => new(new SalesDocumentServiceFalso(), new PurchaseDocumentServiceFalso(), new InventoryDocumentServiceFalso());

    [Fact]
    public void Tipo_EsSap()
    {
        var conector = CrearConector();

        Assert.Equal("Sap", conector.Tipo);
    }

    [Fact]
    public async Task PullAsync_LanzaNotSupportedException()
    {
        var conector = CrearConector();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => conector.PullAsync("{}", CancellationToken.None));
    }

    [Fact]
    public async Task PushAsync_SinTipoDocumento_LanzaNotSupportedException()
    {
        var conector = CrearConector();
        var registros = new List<IntegrationRecord> { new(new Dictionary<string, object?>()) };

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => conector.PushAsync("{}", registros, CancellationToken.None));
        Assert.Contains("TipoDocumento", ex.Message);
    }

    [Theory]
    [InlineData("Sales")]
    [InlineData("Purchase")]
    [InlineData("Inventory")]
    public async Task PushAsync_ConTipoDocumentoConocido_LanzaNotSupportedExceptionDeMapeoPendiente(string tipoDocumento)
    {
        var conector = CrearConector();
        var registros = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["TipoDocumento"] = tipoDocumento }),
        };

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => conector.PushAsync("{}", registros, CancellationToken.None));
        Assert.Contains("mapeo DTO pendiente", ex.Message);
    }

    [Fact]
    public async Task PushAsync_ConTipoDocumentoDesconocido_LanzaNotSupportedExceptionExplicito()
    {
        var conector = CrearConector();
        var registros = new List<IntegrationRecord>
        {
            new(new Dictionary<string, object?> { ["TipoDocumento"] = "Nomina" }),
        };

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => conector.PushAsync("{}", registros, CancellationToken.None));
        Assert.Contains("Nomina", ex.Message);
    }
}
