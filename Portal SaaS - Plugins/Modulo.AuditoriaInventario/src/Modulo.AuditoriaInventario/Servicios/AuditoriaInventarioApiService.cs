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
    private readonly IDiferenciaEngine _diferenciaEngine;

    public AuditoriaInventarioApiService(IExternalDatabaseConnectionService externalDb, IDiferenciaEngine diferenciaEngine)
    {
        _externalDb = externalDb;
        _diferenciaEngine = diferenciaEngine;
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

        var user = await db.CaptureUsers.FirstOrDefaultAsync(u => u.CompanyId == companyId && u.Username == username && u.IsActive, ct);
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

    public async Task<int> GetProductosCountAsync(Guid companyId, CancellationToken ct = default)
    {
        await using var db = await CreateDbContextAsync(companyId, ct);
        return await db.Products.CountAsync(p => p.CompanyId == companyId, ct);
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

    public async Task<CaptureAjustesDto> GetAjustesCapturaAsync(Guid companyId, CancellationToken ct = default)
    {
        await using var db = await CreateDbContextAsync(companyId, ct);

        var fila = await db.CaptureSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CompanyId == companyId, ct);

        // Sin fila configurada = todavía nadie tocó Configuración de captura -- los
        // tres formatos quedan habilitados (mismo criterio permisivo que el resto
        // del maestro offline-first, ver comentario en CaptureSettings).
        return fila is null
            ? new CaptureAjustesDto(true, true, true)
            : new CaptureAjustesDto(fila.AllowEan8, fila.AllowUpcA, fila.AllowEan13);
    }

    public async Task<IReadOnlyList<CaptureUsuarioDto>> GetUsuariosAsync(Guid companyId, CancellationToken ct = default)
    {
        await using var db = await CreateDbContextAsync(companyId, ct);

        return await db.CaptureUsers
            .Where(u => u.CompanyId == companyId && u.IsActive)
            .OrderBy(u => u.Username)
            .Select(u => new CaptureUsuarioDto(u.Username, u.FullName))
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

    /// <summary>La PWA ya no crea sesiones (ver UpsertSesionAsync) -- se suma a una de estas, creadas desde Pages/AvanceConteo.</summary>
    public async Task<IReadOnlyList<CaptureSesionAbiertaDto>> GetSesionesAbiertasAsync(Guid companyId, CancellationToken ct = default)
    {
        await using var db = await CreateDbContextAsync(companyId, ct);

        return await db.InventorySessions
            .Where(s => s.CompanyId == companyId && s.Status == "ACTIVE")
            .OrderByDescending(s => s.StartedAt)
            .Select(s => new CaptureSesionAbiertaDto(s.Id, s.BranchId, s.InventoryNumber, s.StartedAt, s.ValidateAgainstMaster))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Solo transiciona una sesión creada desde el portal -- 2026-10-15: la PWA dejó
    /// de poder crear sesiones (antes hacía create-on-sync acá mismo), así que un Id
    /// inexistente ahora es un error, no un alta implícita. Una sesión ya CLOSED
    /// tampoco admite más cambios ("una vez cerrada queda bloqueada", pedido
    /// explícito del dueño del proyecto) -- salvo el reenvío idempotente del mismo
    /// cierre (reintento de red tras un 200 que no llegó a confirmarse en el cliente).
    /// </summary>
    public async Task UpsertSesionAsync(Guid companyId, long captureUserId, CaptureSesionUpsert request, CancellationToken ct = default)
    {
        await using var db = await CreateDbContextAsync(companyId, ct);

        var existing = await db.InventorySessions.FirstOrDefaultAsync(s => s.Id == request.Id, ct);

        if (existing is null)
        {
            throw new InvalidOperationException(
                "La tarea de conteo no existe en el servidor. Las tareas se crean desde el portal, sincronizá la lista de tareas abiertas e intentá de nuevo.");
        }

        if (existing.CompanyId != companyId)
        {
            throw new InvalidOperationException("La sesión no pertenece a esta compañía.");
        }

        if (existing.Status == "CLOSED")
        {
            if (request.Status == "CLOSED")
            {
                return; // reintento idempotente del mismo cierre -- nada que hacer.
            }
            throw new InvalidOperationException("La sesión ya está cerrada, no se puede modificar.");
        }

        // existing.Status ya se validó distinto de CLOSED arriba, así que cualquier
        // transición a CLOSED acá es un cierre genuino (primera vez), no un reenvío.
        existing.Status = request.Status;
        existing.ClosedAt = request.Status == "CLOSED" ? DateTimeOffset.UtcNow : existing.ClosedAt;

        if (request.Status == "CLOSED")
        {
            await _diferenciaEngine.CalcularDiferenciasAsync(db, existing.Id, ct);
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

        // Todo el lote se rechaza junto si CUALQUIER captura apunta a una sesión que
        // no existe o ya no está ACTIVE -- nunca se descartan filas puntuales en
        // silencio (eso dejaría a la PWA creyendo que sincronizó algo que el
        // servidor en realidad tiró, ver UploadCapturasBatchAsync en el Host).
        var sessionIdsReferenciados = capturas.Select(c => c.SessionId).Distinct().ToList();
        var estadosPorSesion = await db.InventorySessions
            .Where(s => sessionIdsReferenciados.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Status, ct);

        foreach (var sessionId in sessionIdsReferenciados)
        {
            if (!estadosPorSesion.TryGetValue(sessionId, out var status))
            {
                throw new InvalidOperationException($"La sesión '{sessionId}' no existe en el servidor.");
            }
            if (status != "ACTIVE")
            {
                throw new InvalidOperationException($"La sesión '{sessionId}' ya está cerrada, no admite más capturas.");
            }
        }

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
