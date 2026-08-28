using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Data;
using PortalSaas.Data.Entities.Integraciones;
using PortalSaas.Integrations;
using Xunit;

namespace PortalSaas.Core.Tests.Integraciones;

public class IntegrationFieldMappingServiceTests
{
    private static PortalSaasDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PortalSaasDbContext(options);
    }

    [Fact]
    public async Task MapToExternalAsync_TraduceCampoLocalACampoExterno()
    {
        await using var contexto = CrearContexto();
        var definicion = new IntegrationDefinition
        {
            Nombre = "Wms a SAP",
            ModuloOrigen = "Wms",
            EntidadNegocio = "PickingConfirmado",
            ConectorTipo = IntegrationConectorTipo.Sap,
            ConectorConfigCifrado = "config",
            Direccion = IntegrationDireccion.Subida,
        };
        definicion.Mapeos.Add(new IntegrationFieldMapping
        {
            CampoLocal = "NumeroPedido",
            CampoExterno = "DocEntry",
            Obligatorio = true,
        });
        contexto.IntegrationDefinitions.Add(definicion);
        await contexto.SaveChangesAsync();

        var servicio = new IntegrationFieldMappingService(contexto);
        var registroLocal = new IntegrationRecord(new Dictionary<string, object?>
        {
            ["NumeroPedido"] = "12345",
        });

        var resultado = await servicio.MapToExternalAsync(definicion.Id, registroLocal);

        Assert.Equal("12345", resultado["DocEntry"]);
    }
}
