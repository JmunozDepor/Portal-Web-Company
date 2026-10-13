using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Implementación real de IExternalDatabaseConnectionService -- resuelve la base externa
/// de (módulo, compañía) contra PortalSaasDbContext.CompanyModuleConnections: Instance
/// (servidor + credencial por defecto) + DatabaseName + credencial propia opcional.
/// Nunca contra HANA/SAP. Sin caché (YAGNI, mismo criterio que antes).
/// </summary>
public sealed class ExternalDatabaseConnectionService : IExternalDatabaseConnectionService
{
    private readonly PortalSaasDbContext _db;
    private readonly ISecretoCifradoService _secretos;

    public ExternalDatabaseConnectionService(PortalSaasDbContext db, ISecretoCifradoService secretos)
    {
        _db = db;
        _secretos = secretos;
    }

    public async Task<ExternalDatabaseConnection> ResolveConnectionAsync(
        string moduleCode,
        Guid companyId,
        CancellationToken ct = default)
    {
        var binding = await _db.CompanyModuleConnections
                .AsNoTracking()
                .Include(b => b.Instance)
                .FirstOrDefaultAsync(b => b.ModuleCode == moduleCode && b.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException(
                $"No hay una base de datos externa asociada al módulo '{moduleCode}' para esta " +
                "compañía -- configurarla desde Administración.");

        if (!binding.Instance.IsActive)
        {
            throw new InvalidOperationException(
                $"La instancia '{binding.Instance.Name}' asignada al módulo '{moduleCode}' está inactiva -- " +
                "activarla o asignar otra desde Administración.");
        }

        var (username, password) = ModuleDatabaseCredentials.Resolve(binding, _secretos);

        return new ExternalDatabaseConnection
        {
            EngineType = binding.Instance.EngineType,
            ConnectionString = ExternalDatabaseConnectionStringBuilder.Build(
                binding.Instance.EngineType,
                binding.Instance.Host,
                binding.Instance.Port,
                binding.DatabaseName,
                username,
                password),
        };
    }

    public async Task<IReadOnlyList<ModuleCompanyDto>> ListActiveCompanyIdsAsync(
        string moduleCode,
        CancellationToken ct = default)
    {
        return await _db.CompanyModuleConnections
            .AsNoTracking()
            .Where(b => b.ModuleCode == moduleCode && b.Instance.IsActive)
            .Select(b => new ModuleCompanyDto(b.CompanyId, b.Company.OrganizationId))
            .Distinct()
            .ToListAsync(ct);
    }
}
