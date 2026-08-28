namespace PortalSaas.Abstractions.Contratos.Integraciones;

public interface IIntegrationFieldMappingService
{
    Task<IntegrationRecord> MapToExternalAsync(Guid integrationDefinitionId, IntegrationRecord registroLocal);

    Task<IntegrationRecord> MapToLocalAsync(Guid integrationDefinitionId, IntegrationRecord registroExterno);
}
