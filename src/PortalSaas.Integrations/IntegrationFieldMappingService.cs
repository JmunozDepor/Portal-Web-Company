using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Data;

namespace PortalSaas.Integrations;

public class IntegrationFieldMappingService : IIntegrationFieldMappingService
{
    private readonly PortalSaasDbContext _contexto;

    public IntegrationFieldMappingService(PortalSaasDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<IntegrationRecord> MapToExternalAsync(Guid integrationDefinitionId, IntegrationRecord registroLocal)
    {
        var mapeos = await ObtenerMapeosAsync(integrationDefinitionId);
        var campos = new Dictionary<string, object?>();
        foreach (var mapeo in mapeos)
        {
            campos[mapeo.CampoExterno] = registroLocal[mapeo.CampoLocal];
        }
        return new IntegrationRecord(campos);
    }

    public async Task<IntegrationRecord> MapToLocalAsync(Guid integrationDefinitionId, IntegrationRecord registroExterno)
    {
        var mapeos = await ObtenerMapeosAsync(integrationDefinitionId);
        var campos = new Dictionary<string, object?>();
        foreach (var mapeo in mapeos)
        {
            campos[mapeo.CampoLocal] = registroExterno[mapeo.CampoExterno];
        }
        return new IntegrationRecord(campos);
    }

    private async Task<List<Data.Entities.Integraciones.IntegrationFieldMapping>> ObtenerMapeosAsync(Guid integrationDefinitionId)
    {
        return await _contexto.IntegrationFieldMappings
            .Where(m => m.IntegrationDefinitionId == integrationDefinitionId)
            .ToListAsync();
    }
}
