using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Administracion;

/// <summary>
/// Implementación real de ICompanyExternalConnectionService: asigna a cada módulo con
/// RequiresExternalDatabase una Instance de la organización + el nombre de la base, con
/// credencial propia opcional. Alcance por compañía: todo método valida que companyId
/// pertenezca a organizationId (las filas no llevan OrganizationId, ver CLAUDE.md regla
/// dura 2026-08-08). Secreto write-only: se cifra al guardar, ningún DTO lo expone, y en
/// blanco se conserva el existente.
/// </summary>
public sealed class CompanyExternalConnectionService : ICompanyExternalConnectionService
{
    private readonly PortalSaasDbContext _db;
    private readonly ISecretoCifradoService _secretos;
    private readonly Func<IReadOnlyList<IModuloPortal>> _modulos;

    public CompanyExternalConnectionService(PortalSaasDbContext db, ISecretoCifradoService secretos, PluginManager plugins)
        : this(db, secretos, () => plugins.ModulosCargados)
    {
    }

    // Seam de tests: PluginManager no permite inyectar módulos en memoria.
    internal CompanyExternalConnectionService(PortalSaasDbContext db, ISecretoCifradoService secretos, Func<IReadOnlyList<IModuloPortal>> modulos)
    {
        _db = db;
        _secretos = secretos;
        _modulos = modulos;
    }

    public async Task<IReadOnlyList<DatabaseInstanceOptionDto>> ListInstancesAsync(Guid organizationId, Guid companyId, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        return await _db.Instances.AsNoTracking()
            .Where(i => i.OrganizationId == organizationId && i.IsActive)
            .OrderBy(i => i.Name)
            .Select(i => new DatabaseInstanceOptionDto(i.Id, i.Name, i.EngineType, i.Host, i.Port))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ModuleConnectionBindingDto>> ListBindingsAsync(Guid organizationId, Guid companyId, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        var bindings = await _db.CompanyModuleConnections.AsNoTracking()
            .Include(b => b.Instance)
            .Where(b => b.CompanyId == companyId)
            .ToListAsync(ct);

        return _modulos()
            .Where(m => m.RequiresExternalDatabase)
            .OrderBy(m => m.Name)
            .Select(m => Map(m, bindings.FirstOrDefault(b => b.ModuleCode == m.ModuleCode)))
            .ToList();
    }

    public async Task<ModuleConnectionBindingDto?> GetBindingAsync(Guid organizationId, Guid companyId, string moduleCode, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        var modulo = BuscarModuloConBase(moduleCode);
        if (modulo is null)
        {
            return null;
        }

        var binding = await _db.CompanyModuleConnections.AsNoTracking()
            .Include(b => b.Instance)
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.ModuleCode == moduleCode, ct);

        return Map(modulo, binding);
    }

    public async Task SetBindingAsync(Guid organizationId, Guid companyId, string moduleCode, ModuleDatabaseBindingInput input, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        if (BuscarModuloConBase(moduleCode) is null)
        {
            throw new InvalidOperationException($"El módulo '{moduleCode}' no usa una base de datos externa.");
        }

        var databaseName = input.DatabaseName?.Trim();
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("La base de datos es obligatoria.");
        }

        var instanciaValida = await _db.Instances.AsNoTracking()
            .AnyAsync(i => i.Id == input.InstanceId && i.OrganizationId == organizationId && i.IsActive, ct);
        if (!instanciaValida)
        {
            throw new InvalidOperationException("La instancia indicada no existe en la organización o está inactiva.");
        }

        var binding = await _db.CompanyModuleConnections
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.ModuleCode == moduleCode, ct);

        string? usuario = null;
        string? claveCifrada = null;
        if (input.UseOwnCredentials)
        {
            usuario = input.TechnicalUsername?.Trim();
            if (string.IsNullOrWhiteSpace(usuario))
            {
                throw new InvalidOperationException("Ingresa el usuario propio del módulo.");
            }

            if (!string.IsNullOrWhiteSpace(input.TechnicalSecretKey))
            {
                claveCifrada = _secretos.Encrypt(input.TechnicalSecretKey);
            }
            else if (binding?.TechnicalSecretKey is { } claveExistente)
            {
                claveCifrada = claveExistente;
            }
            else
            {
                throw new InvalidOperationException("Ingresa la clave propia del módulo.");
            }
        }

        if (binding is null)
        {
            binding = new CompanyModuleConnection { CompanyId = companyId, ModuleCode = moduleCode };
            _db.CompanyModuleConnections.Add(binding);
        }

        binding.InstanceId = input.InstanceId;
        binding.DatabaseName = databaseName;
        binding.TechnicalUsername = usuario;
        binding.TechnicalSecretKey = claveCifrada;
        binding.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task ClearBindingAsync(Guid organizationId, Guid companyId, string moduleCode, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        var binding = await _db.CompanyModuleConnections
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.ModuleCode == moduleCode, ct);
        if (binding is null)
        {
            return;
        }

        _db.CompanyModuleConnections.Remove(binding);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ConnectionTestResultDto> TestBindingAsync(Guid organizationId, Guid companyId, string moduleCode, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        var binding = await _db.CompanyModuleConnections.AsNoTracking()
            .Include(b => b.Instance)
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.ModuleCode == moduleCode, ct)
            ?? throw new InvalidOperationException("El módulo no tiene una base de datos asignada en esta compañía.");

        var (usuario, password) = ModuleDatabaseCredentials.Resolve(binding, _secretos);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var cs = ExternalDatabaseConnectionStringBuilder.Build(
                binding.Instance.EngineType, binding.Instance.Host, binding.Instance.Port,
                binding.DatabaseName, usuario, password, timeoutSeconds: 5);

            switch (binding.Instance.EngineType)
            {
                case InstanceEngineType.Postgres:
                {
                    await using var conn = new Npgsql.NpgsqlConnection(cs);
                    await conn.OpenAsync(cts.Token);
                    break;
                }
                case InstanceEngineType.SqlServer:
                {
                    await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cs);
                    await conn.OpenAsync(cts.Token);
                    break;
                }
                case InstanceEngineType.Hana:
                {
                    await using var conn = new global::Sap.Data.Hana.HanaConnection(cs);
                    await conn.OpenAsync(cts.Token);
                    break;
                }
                default:
                    return new ConnectionTestResultDto(false, $"Motor no soportado: {binding.Instance.EngineType}", sw.ElapsedMilliseconds);
            }

            return new ConnectionTestResultDto(true, null, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // Error admin-only. Los builders no vuelcan credenciales en ex.Message, pero
            // se sanea por si acaso.
            var mensaje = string.IsNullOrEmpty(password) ? ex.Message : ex.Message.Replace(password, "***");
            return new ConnectionTestResultDto(false, mensaje, sw.ElapsedMilliseconds);
        }
    }

    private IModuloPortal? BuscarModuloConBase(string moduleCode) =>
        _modulos().FirstOrDefault(m => m.ModuleCode == moduleCode && m.RequiresExternalDatabase);

    private async Task EnsureCompanyAsync(Guid organizationId, Guid companyId, CancellationToken ct)
    {
        var ok = await _db.Companies.AsNoTracking()
            .AnyAsync(c => c.Id == companyId && c.OrganizationId == organizationId, ct);
        if (!ok)
        {
            throw new InvalidOperationException("Compañía no encontrada en la organización.");
        }
    }

    private static ModuleConnectionBindingDto Map(IModuloPortal modulo, CompanyModuleConnection? b) => new(
        modulo.ModuleCode,
        modulo.Name,
        b?.InstanceId,
        b?.Instance.Name,
        b?.DatabaseName,
        b?.TechnicalUsername,
        b?.TechnicalSecretKey is not null);
}
