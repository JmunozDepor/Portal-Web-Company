using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities.Integraciones;

namespace PortalSaas.Integrations;

public class IntegrationConnectorConfigService : IIntegrationConnectorConfigService
{
    private readonly PortalSaasDbContext _contexto;
    private readonly ISecretoCifradoService _secretoCifradoService;

    public IntegrationConnectorConfigService(PortalSaasDbContext contexto, ISecretoCifradoService secretoCifradoService)
    {
        _contexto = contexto;
        _secretoCifradoService = secretoCifradoService;
    }

    public async Task<string?> GetDecryptedConfigAsync(Guid companyId, string moduloOrigen, string conectorTipo, CancellationToken ct = default)
    {
        if (!Enum.TryParse<IntegrationConectorTipo>(conectorTipo, out var tipoEnum))
        {
            return null;
        }

        var definicion = await _contexto.IntegrationDefinitions
            .Where(d => d.CompanyId == companyId && d.ModuloOrigen == moduloOrigen && d.ConectorTipo == tipoEnum && d.Activo)
            .FirstOrDefaultAsync(ct);

        if (definicion is null || string.IsNullOrEmpty(definicion.ConectorConfigCifrado))
        {
            return null;
        }

        return _secretoCifradoService.Decrypt(definicion.ConectorConfigCifrado);
    }
}
