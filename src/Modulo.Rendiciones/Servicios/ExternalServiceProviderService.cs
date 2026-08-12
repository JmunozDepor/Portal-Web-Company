using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Servicios;

public sealed class ExternalServiceProviderService : IExternalServiceProviderService
{
    private readonly RendicionesDbContext _db;
    private readonly ISecretoCifradoService _secretos;

    public ExternalServiceProviderService(RendicionesDbContext db, ISecretoCifradoService secretos)
    {
        _db = db;
        _secretos = secretos;
    }

    public async Task<IReadOnlyList<ExternalServiceProvider>> ListAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.ExternalServiceProviders
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.ServiceType).ThenBy(p => p.Priority)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ExternalServiceProvider>> ListByServiceAsync(Guid companyId, string serviceType, CancellationToken ct = default) =>
        await _db.ExternalServiceProviders
            .Where(p => p.CompanyId == companyId && p.ServiceType == serviceType && p.IsActive)
            .OrderBy(p => p.Priority).ThenBy(p => p.Id)
            .ToListAsync(ct);

    public async Task<ExternalServiceProvider?> GetAsync(long id, Guid companyId, CancellationToken ct = default) =>
        await _db.ExternalServiceProviders.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId, ct);

    public async Task<long> CreateAsync(Guid companyId, string serviceType, string name, string? endpoint, string apiKey, int monthlyLimit, int priority, CancellationToken ct = default)
    {
        var provider = new ExternalServiceProvider
        {
            CompanyId = companyId,
            ServiceType = serviceType,
            Name = name,
            Endpoint = endpoint,
            ApiKeyEncrypted = _secretos.Encrypt(apiKey),
            MonthlyLimit = monthlyLimit,
            Priority = priority,
            IsActive = true,
        };
        _db.ExternalServiceProviders.Add(provider);
        await _db.SaveChangesAsync(ct);
        return provider.Id;
    }

    public async Task UpdateAsync(long id, Guid companyId, string name, string? endpoint, string? apiKey, int monthlyLimit, int priority, bool isActive, CancellationToken ct = default)
    {
        var provider = await _db.ExternalServiceProviders.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("El proveedor no existe o no pertenece a esta compañía.");

        provider.Name = name;
        provider.Endpoint = endpoint;
        if (!string.IsNullOrWhiteSpace(apiKey))
            provider.ApiKeyEncrypted = _secretos.Encrypt(apiKey);
        provider.MonthlyLimit = monthlyLimit;
        provider.Priority = priority;
        provider.IsActive = isActive;
        provider.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default)
    {
        var provider = await _db.ExternalServiceProviders.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId, ct);
        if (provider is null)
            return;

        _db.ExternalServiceProviders.Remove(provider);
        await _db.SaveChangesAsync(ct);
    }
}
