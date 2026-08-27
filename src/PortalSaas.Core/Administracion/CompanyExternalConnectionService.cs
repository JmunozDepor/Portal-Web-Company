using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Administracion;

/// <summary>
/// Implementación real de ICompanyExternalConnectionService contra PortalSaasDbContext.
/// Alcance por compañía: todo método valida que companyId pertenezca a organizationId
/// (Company.OrganizationId), sin fallback a un alcance más amplio -- las entidades
/// nuevas no llevan OrganizationId, ver CLAUDE.md (regla dura 2026-08-08).
/// El secreto es write-only: CreateAsync lo cifra, UpdateAsync re-cifra solo si viene
/// no vacío, los DTO de lectura nunca lo exponen. TestAsync es un stub acá (Task 5).
/// </summary>
public sealed class CompanyExternalConnectionService : ICompanyExternalConnectionService
{
    private readonly PortalSaasDbContext _db;
    private readonly ISecretoCifradoService _secretos;
    private readonly PluginManager _plugins;

    public CompanyExternalConnectionService(PortalSaasDbContext db, ISecretoCifradoService secretos, PluginManager plugins)
    {
        _db = db;
        _secretos = secretos;
        _plugins = plugins;
    }

    public async Task<IReadOnlyList<ExternalConnectionDto>> ListAsync(Guid organizationId, Guid companyId, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        var filas = await _db.CompanyExternalConnections.AsNoTracking()
            .Where(c => c.CompanyId == companyId)
            .OrderBy(c => c.Nombre)
            .ToListAsync(ct);

        return filas.Select(Map).ToList();
    }

    public async Task<ExternalConnectionDto?> GetAsync(Guid organizationId, Guid companyId, long id, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        var fila = await _db.CompanyExternalConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId, ct);

        return fila is null ? null : Map(fila);
    }

    public async Task<long> CreateAsync(Guid organizationId, Guid companyId, ExternalConnectionEditModel model, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);
        ValidarModelo(model);

        if (ExternalConnectionType.IsDatabase(model.Tipo) && string.IsNullOrWhiteSpace(model.TechnicalSecretKey))
        {
            throw new InvalidOperationException("El secreto es obligatorio al crear una conexión de base de datos.");
        }

        var nombreNormalizado = model.Nombre.Trim();
        await EnsureNombreUnicoAsync(companyId, nombreNormalizado, id: null, ct);

        var entity = new CompanyExternalConnection
        {
            CompanyId = companyId,
            Nombre = nombreNormalizado,
            Tipo = model.Tipo,
            Host = model.Host,
            BaseUrl = model.BaseUrl,
            Port = model.Port,
            DatabaseName = model.DatabaseName,
            TechnicalUsername = model.TechnicalUsername,
            TechnicalSecretKey = string.IsNullOrWhiteSpace(model.TechnicalSecretKey)
                ? null
                : _secretos.Encrypt(model.TechnicalSecretKey),
            ConfiguracionExtra = model.ConfiguracionExtra,
            IsActive = model.IsActive,
        };

        _db.CompanyExternalConnections.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task UpdateAsync(Guid organizationId, Guid companyId, long id, ExternalConnectionEditModel model, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);
        ValidarModelo(model);

        var entity = await _db.CompanyExternalConnections
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("Conexión no encontrada en la compañía.");

        var nombreNormalizado = model.Nombre.Trim();
        await EnsureNombreUnicoAsync(companyId, nombreNormalizado, id, ct);

        entity.Nombre = nombreNormalizado;
        entity.Tipo = model.Tipo;
        entity.Host = model.Host;
        entity.BaseUrl = model.BaseUrl;
        entity.Port = model.Port;
        entity.DatabaseName = model.DatabaseName;
        entity.TechnicalUsername = model.TechnicalUsername;
        entity.ConfiguracionExtra = model.ConfiguracionExtra;
        entity.IsActive = model.IsActive;

        if (!string.IsNullOrWhiteSpace(model.TechnicalSecretKey))
        {
            entity.TechnicalSecretKey = _secretos.Encrypt(model.TechnicalSecretKey);
        }

        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid organizationId, Guid companyId, long id, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        var entity = await _db.CompanyExternalConnections
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId, ct);
        if (entity is null)
        {
            return;
        }

        var enUso = await _db.CompanyModuleConnections.CountAsync(b => b.ConnectionId == id, ct);
        if (enUso > 0)
        {
            throw new InvalidOperationException($"La conexión está en uso por {enUso} módulo(s).");
        }

        _db.CompanyExternalConnections.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ConnectionTestResultDto> TestAsync(Guid organizationId, Guid companyId, long id, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);
        var c = await _db.CompanyExternalConnections.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("Conexión no encontrada en la compañía.");

        var password = string.IsNullOrEmpty(c.TechnicalSecretKey) ? "" : _secretos.Decrypt(c.TechnicalSecretKey);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            switch (c.Tipo)
            {
                case ExternalConnectionType.DbPostgres:
                {
                    var cs = new Npgsql.NpgsqlConnectionStringBuilder
                    {
                        Host = c.Host,
                        Port = c.Port ?? 5432,
                        Database = c.DatabaseName,
                        Username = c.TechnicalUsername,
                        Password = password,
                        Timeout = 5,
                    }.ConnectionString;
                    await using var conn = new Npgsql.NpgsqlConnection(cs);
                    await conn.OpenAsync(cts.Token);
                    break;
                }
                case ExternalConnectionType.DbSqlServer:
                {
                    var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
                    {
                        DataSource = $"{c.Host},{c.Port}",
                        InitialCatalog = c.DatabaseName,
                        UserID = c.TechnicalUsername,
                        Password = password,
                        ConnectTimeout = 5,
                        TrustServerCertificate = true,
                    }.ConnectionString;
                    await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cs);
                    await conn.OpenAsync(cts.Token);
                    break;
                }
                case ExternalConnectionType.DbHana:
                {
                    // No hay un helper de connection string HANA reutilizable en Core para
                    // host/puerto/usuario sueltos -- SapConnectionStringFactory arma la
                    // cadena a partir de Company.Instance, no de estos campos. Se usa el
                    // shape del brief (Server=host:port;UID=...;PWD=...).
                    var cs = $"Server={c.Host}:{c.Port};UID={c.TechnicalUsername};PWD={password}";
                    await using var conn = new global::Sap.Data.Hana.HanaConnection(cs);
                    await conn.OpenAsync(cts.Token);
                    break;
                }
                case ExternalConnectionType.HttpApi:
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    using var resp = await http.GetAsync(c.BaseUrl, cts.Token);
                    if ((int)resp.StatusCode >= 500)
                    {
                        return new ConnectionTestResultDto(false, $"HTTP {(int)resp.StatusCode}", sw.ElapsedMilliseconds);
                    }
                    break;
                }
                default:
                    return new ConnectionTestResultDto(false, $"Tipo no soportado: {c.Tipo}", sw.ElapsedMilliseconds);
            }

            return new ConnectionTestResultDto(true, null, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // Error admin-only. La contraseña descifrada nunca se incluye en ninguna
            // cadena de conexión que aparezca en ex.Message (los builders no vuelcan
            // credenciales), pero se sanea por si acaso.
            var mensaje = string.IsNullOrEmpty(password) ? ex.Message : ex.Message.Replace(password, "***");
            return new ConnectionTestResultDto(false, mensaje, sw.ElapsedMilliseconds);
        }
    }

    public async Task<IReadOnlyList<ModuleConnectionBindingDto>> ListBindingsAsync(Guid organizationId, Guid companyId, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        var bindings = await _db.CompanyModuleConnections.AsNoTracking()
            .Include(b => b.Connection)
            .Where(b => b.CompanyId == companyId)
            .ToListAsync(ct);

        var result = new List<ModuleConnectionBindingDto>();
        foreach (var modulo in _plugins.ModulosCargados)
        {
            var reqs = modulo.ExternalConnectionRequirements.Count > 0
                ? modulo.ExternalConnectionRequirements
                : new[] { new ExternalConnectionRequirement("Default", "Conexión principal", ExternalConnectionKind.Database, true) };

            foreach (var req in reqs)
            {
                var b = bindings.FirstOrDefault(x => x.ModuleCode == modulo.ModuleCode && x.Purpose == req.Purpose);
                result.Add(new ModuleConnectionBindingDto(
                    modulo.ModuleCode,
                    modulo.Name,
                    req.Purpose,
                    req.DisplayName,
                    req.Kind,
                    req.Required,
                    b?.ConnectionId,
                    b?.Connection.Nombre));
            }
        }

        return result;
    }

    public async Task SetBindingAsync(Guid organizationId, Guid companyId, string moduleCode, string purpose, long connectionId, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        var conexion = await _db.CompanyExternalConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("La conexión indicada no existe en la compañía.");

        var purposeNormalizado = string.IsNullOrWhiteSpace(purpose) ? "Default" : purpose;

        var binding = await _db.CompanyModuleConnections
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.ModuleCode == moduleCode && b.Purpose == purposeNormalizado, ct);

        if (binding is null)
        {
            _db.CompanyModuleConnections.Add(new CompanyModuleConnection
            {
                CompanyId = companyId,
                ModuleCode = moduleCode,
                Purpose = purposeNormalizado,
                ConnectionId = conexion.Id,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            binding.ConnectionId = conexion.Id;
            binding.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task ClearBindingAsync(Guid organizationId, Guid companyId, string moduleCode, string purpose, CancellationToken ct = default)
    {
        await EnsureCompanyAsync(organizationId, companyId, ct);

        var purposeNormalizado = string.IsNullOrWhiteSpace(purpose) ? "Default" : purpose;

        var binding = await _db.CompanyModuleConnections
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.ModuleCode == moduleCode && b.Purpose == purposeNormalizado, ct);
        if (binding is null)
        {
            return;
        }

        _db.CompanyModuleConnections.Remove(binding);
        await _db.SaveChangesAsync(ct);
    }

    private async Task EnsureCompanyAsync(Guid organizationId, Guid companyId, CancellationToken ct)
    {
        var ok = await _db.Companies.AsNoTracking()
            .AnyAsync(c => c.Id == companyId && c.OrganizationId == organizationId, ct);
        if (!ok)
        {
            throw new InvalidOperationException("Compañía no encontrada en la organización.");
        }
    }

    private async Task EnsureNombreUnicoAsync(Guid companyId, string nombre, long? id, CancellationToken ct)
    {
        var nombreLower = nombre.ToLowerInvariant();
        var existe = await _db.CompanyExternalConnections
            .Where(c => c.CompanyId == companyId && (id == null || c.Id != id))
            .AnyAsync(c => c.Nombre.ToLower() == nombreLower, ct);
        if (existe)
        {
            throw new InvalidOperationException("Ya existe una conexión con ese nombre en la compañía.");
        }
    }

    private static void ValidarModelo(ExternalConnectionEditModel m)
    {
        if (string.IsNullOrWhiteSpace(m.Nombre))
        {
            throw new InvalidOperationException("El nombre es obligatorio.");
        }

        if (!ExternalConnectionType.All.Contains(m.Tipo))
        {
            throw new InvalidOperationException($"Tipo de conexión inválido: '{m.Tipo}'.");
        }

        if (ExternalConnectionType.IsDatabase(m.Tipo))
        {
            if (string.IsNullOrWhiteSpace(m.Host) || m.Port is null or <= 0
                || string.IsNullOrWhiteSpace(m.DatabaseName) || string.IsNullOrWhiteSpace(m.TechnicalUsername))
            {
                throw new InvalidOperationException("Host, puerto, base y usuario son obligatorios para una conexión de base de datos.");
            }
        }
        else if (m.Tipo == ExternalConnectionType.HttpApi)
        {
            if (string.IsNullOrWhiteSpace(m.BaseUrl))
            {
                throw new InvalidOperationException("La URL base es obligatoria para una conexión HTTP.");
            }
        }
    }

    private static ExternalConnectionDto Map(CompanyExternalConnection c) => new(
        c.Id,
        c.CompanyId,
        c.Nombre,
        c.Tipo,
        c.Host,
        c.BaseUrl,
        c.Port,
        c.DatabaseName,
        c.TechnicalUsername,
        c.ConfiguracionExtra,
        c.IsActive);
}
