using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// CRUD de cuentas/credenciales para los servicios externos con capa gratuita (Azure
/// Maps, Azure Document Intelligence) -- puede haber más de una por compañía y tipo de
/// servicio, con fallback automático por prioridad cuando una agota su cuota mensual
/// (ver IExternalServiceProviderSelector).
/// </summary>
public interface IExternalServiceProviderService
{
    Task<IReadOnlyList<ExternalServiceProvider>> ListAsync(Guid companyId, CancellationToken ct = default);

    Task<IReadOnlyList<ExternalServiceProvider>> ListByServiceAsync(Guid companyId, string serviceType, CancellationToken ct = default);

    Task<ExternalServiceProvider?> GetAsync(long id, Guid companyId, CancellationToken ct = default);

    /// <summary>apiKey se cifra acá adentro (ISecretoCifradoService) -- nunca se guarda en texto plano.</summary>
    Task<long> CreateAsync(Guid companyId, string serviceType, string name, string? endpoint, string apiKey, int monthlyLimit, int priority, CancellationToken ct = default);

    /// <summary>apiKey null/vacío no cambia la clave existente -- mismo patrón write-only que Instance/Company del portal.</summary>
    Task UpdateAsync(long id, Guid companyId, string name, string? endpoint, string? apiKey, int monthlyLimit, int priority, bool isActive, CancellationToken ct = default);

    Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default);
}
