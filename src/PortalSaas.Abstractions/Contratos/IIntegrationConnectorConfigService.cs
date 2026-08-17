namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Permite a un plugin leer la config (ya descifrada) de una IntegrationDefinition
/// activa sin referenciar PortalSaas.Core/PortalSaas.Data directamente -- regla dura
/// del proyecto. Pensado para BackgroundService de un plugin que necesitan la config
/// de un conector (ej. credenciales de un endpoint externo) fuera de un ciclo del
/// motor de integración genérico (que ya resuelve esto internamente vía
/// IntegrationSyncHostedService, sin necesitar este contrato).
/// </summary>
public interface IIntegrationConnectorConfigService
{
    /// <summary>Config JSON descifrada de la primera IntegrationDefinition activa que
    /// matchea (companyId, moduloOrigen, conectorTipo) -- null si no hay ninguna.</summary>
    Task<string?> GetDecryptedConfigAsync(Guid companyId, string moduloOrigen, string conectorTipo, CancellationToken ct = default);
}
