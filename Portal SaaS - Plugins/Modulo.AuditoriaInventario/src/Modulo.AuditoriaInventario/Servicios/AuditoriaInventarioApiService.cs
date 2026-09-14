using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.AuditoriaInventario.Servicios;

/// <summary>
/// Implementación real de IAuditoriaInventarioApiService (Abstractions) -- resuelto
/// por PortalSaas.Host vía DI para AuditoriaInventarioInboundEndpoints. NUNCA usa el
/// AuditoriaInventarioDbContext registrado por AddDbContext en
/// AuditoriaInventarioModule.RegisterServices (ese depende de
/// ICurrentCompanyAccessor, que exige sesión de portal con Company seleccionada --
/// la PWA no tiene eso). En cambio arma un DbContext manual por companyId explícito,
/// mismo patrón que RendicionesReminderBackgroundService (background sin HTTP
/// session), acá por el mismo motivo: sin sesión HTTP de portal.
/// </summary>
public sealed class AuditoriaInventarioApiService : IAuditoriaInventarioApiService
{
    private const string ModuleCode = "AuditoriaInventario";
    private const int MaestroPageSize = 1000;

    private readonly IExternalDatabaseConnectionService _externalDb;

    public AuditoriaInventarioApiService(IExternalDatabaseConnectionService externalDb)
    {
        _externalDb = externalDb;
    }

    private async Task<AuditoriaInventarioDbContext> CreateDbContextAsync(Guid companyId, CancellationToken ct)
    {
        var connection = await _externalDb.ResolveConnectionAsync(ModuleCode, companyId, ct);

        var optionsBuilder = new DbContextOptionsBuilder<AuditoriaInventarioDbContext>();
        switch (connection.EngineType)
        {
            case ExternalDatabaseEngineType.Postgres:
                optionsBuilder.UseNpgsql(connection.ConnectionString);
                break;
            case ExternalDatabaseEngineType.SqlServer:
                optionsBuilder.UseSqlServer(connection.ConnectionString);
                break;
            default:
                throw new InvalidOperationException($"Motor de base de datos externa no soportado: '{connection.EngineType}'.");
        }

        return new AuditoriaInventarioDbContext(optionsBuilder.Options);
    }

    public async Task<CaptureLoginResult?> LoginAsync(Guid companyId, string username, string password, CancellationToken ct = default)
    {
        await using var db = await CreateDbContextAsync(companyId, ct);

        var user = await db.CaptureUsers.FirstOrDefaultAsync(u => u.Username == username && u.IsActive, ct);
        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt))
        {
            return null;
        }

        var token = new CaptureAuthToken
        {
            CompanyId = companyId,
            CaptureUserId = user.Id,
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(12),
        };

        db.CaptureAuthTokens.Add(token);
        await db.SaveChangesAsync(ct);

        return new CaptureLoginResult(token.Token, token.ExpiresAt, user.FullName ?? user.Username);
    }

    /// <summary>
    /// NO sabe a priori a qué compañía pertenece el token -- por eso este método no
    /// puede resolver un DbContext por companyId (todavía no lo tiene). Recorre las
    /// compañías con este módulo activo (ListActiveCompanyIdsAsync, mismo mecanismo
    /// que el recordatorio de Rendiciones) hasta encontrar el token. Aceptable para
    /// el volumen esperado (cantidad de compañías, no de tokens); si en el futuro
    /// esto no escala, la alternativa es que el token mismo codifique el companyId
    /// (ej. prefijo del token) para evitar el recorrido.
    /// </summary>
    public async Task<CaptureTokenInfo?> ResolveTokenAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var companies = await _externalDb.ListActiveCompanyIdsAsync(ModuleCode, ct);

        foreach (var company in companies)
        {
            await using var db = await CreateDbContextAsync(company.CompanyId, ct);
            var row = await db.CaptureAuthTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Token == token, ct);
            if (row is not null)
            {
                return row.ExpiresAt < DateTimeOffset.UtcNow ? null : new CaptureTokenInfo(row.CompanyId, row.CaptureUserId);
            }
        }

        return null;
    }

    public async Task<CaptureMaestroPage> GetProductosAsync(Guid companyId, long afterId, CancellationToken ct = default)
    {
        await using var db = await CreateDbContextAsync(companyId, ct);

        var items = await db.Products
            .Where(p => p.CompanyId == companyId && p.Id > afterId)
            .OrderBy(p => p.Id)
            .Take(MaestroPageSize)
            .Select(p => new CaptureProductoDto(p.Id, p.Barcode, p.ProductCode, p.Description, p.Brand, p.Line))
            .ToListAsync(ct);

        return new CaptureMaestroPage(items, items.Count == MaestroPageSize);
    }

    public async Task<IReadOnlyList<CaptureSucursalDto>> GetSucursalesAsync(Guid companyId, CancellationToken ct = default)
    {
        await using var db = await CreateDbContextAsync(companyId, ct);

        return await db.Branches
            .Where(b => b.CompanyId == companyId && b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new CaptureSucursalDto(b.Id, b.BranchCode, b.Name))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CaptureSectorDto>> GetSectoresAsync(Guid companyId, long? branchId, CancellationToken ct = default)
    {
        await using var db = await CreateDbContextAsync(companyId, ct);

        return await db.InventorySectors
            .Where(s => s.CompanyId == companyId && s.IsActive && (s.BranchId == null || s.BranchId == branchId))
            .OrderBy(s => s.Name)
            .Select(s => new CaptureSectorDto(s.Id, s.Name))
            .ToListAsync(ct);
    }

    public async Task UpsertSesionAsync(Guid companyId, long captureUserId, CaptureSesionUpsert request, CancellationToken ct = default)
    {
        await using var db = await CreateDbContextAsync(companyId, ct);

        var existing = await db.InventorySessions.FirstOrDefaultAsync(s => s.Id == request.Id, ct);

        if (existing is null)
        {
            db.InventorySessions.Add(new InventorySession
            {
                Id = request.Id,
                CompanyId = companyId,
                BranchId = request.BranchId,
                InventoryNumber = request.InventoryNumber,
                ResponsibleUserId = captureUserId,
                StartedAt = request.StartedAt,
                Status = request.Status,
                ValidateAgainstMaster = request.ValidateAgainstMaster,
            });
        }
        else if (existing.CompanyId != companyId)
        {
            throw new InvalidOperationException("La sesión no pertenece a esta compañía.");
        }
        else
        {
            existing.Status = request.Status;
            existing.ClosedAt = request.Status == "CLOSED" ? DateTimeOffset.UtcNow : existing.ClosedAt;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<int> UploadCapturasAsync(Guid companyId, long captureUserId, IReadOnlyList<CaptureItemDto> capturas, CancellationToken ct = default)
    {
        if (capturas.Count == 0)
        {
            return 0;
        }

        await using var db = await CreateDbContextAsync(companyId, ct);

        var ids = capturas.Select(c => c.Id).ToList();
        var existentes = await db.InventoryCaptures.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        var now = DateTimeOffset.UtcNow;
        var procesadas = 0;

        foreach (var c in capturas)
        {
            if (existentes.TryGetValue(c.Id, out var existente))
            {
                existente.Quantity = c.Quantity;
                existente.SyncedAt = now;
                procesadas++;
                continue;
            }

            db.InventoryCaptures.Add(new InventoryCapture
            {
                Id = c.Id,
                SessionId = c.SessionId,
                SectorId = c.SectorId,
                Barcode = c.Barcode,
                ProductCode = c.ProductCode,
                Quantity = c.Quantity,
                InMaster = c.InMaster,
                CapturedByUserId = captureUserId,
                CapturedAt = c.CapturedAt,
                SyncedAt = now,
            });
            procesadas++;
        }

        await db.SaveChangesAsync(ct);
        return procesadas;
    }
}
