using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Servicios;

public static class ReceiptRecompression
{
    private static readonly HashSet<string> RasterMimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp",
    };

    public static bool ShouldRecompress(ExpenseReceipt r, long minBytes) =>
        r.Content is { } c && c.Length >= minBytes && RasterMimes.Contains(r.MimeType ?? "");
}

/// <summary>
/// Pasada única de "descongestión": al arrancar el Host, si
/// RENDICIONES_RECOMPRESS_RECEIPTS_ON_STARTUP = true, reescala y recomprime los
/// comprobantes imagen ya guardados que superen el umbral, sobreescribiéndolos en su
/// lugar. El operador prende la variable, reinicia el Host una vez, revisa el log del
/// resumen y la vuelve a apagar. Soporta RENDICIONES_RECOMPRESS_RECEIPTS_DRY_RUN=true
/// (no escribe, solo proyecta). Idempotente. Mismo patrón per-company que
/// RendicionesReminderBackgroundService (el DbContext de DI depende de un request
/// HTTP en curso, acá no hay ninguno).
/// </summary>
public sealed class ReceiptRecompressionStartupService : IHostedService
{
    private const string ModuleCode = "Rendiciones";
    private const int BatchSize = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReceiptImageProcessor _imageProcessor;
    private readonly ILogger<ReceiptRecompressionStartupService> _logger;

    public ReceiptRecompressionStartupService(
        IServiceScopeFactory scopeFactory,
        IReceiptImageProcessor imageProcessor,
        ILogger<ReceiptRecompressionStartupService> logger)
    {
        _scopeFactory = scopeFactory;
        _imageProcessor = imageProcessor;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!EnvBool("RENDICIONES_RECOMPRESS_RECEIPTS_ON_STARTUP"))
            return;

        var dryRun = EnvBool("RENDICIONES_RECOMPRESS_RECEIPTS_DRY_RUN");
        var minBytes = (long)EnvInt("RENDICIONES_RECOMPRESS_MIN_KB", 500) * 1024;
        int? maxEdge = EnvIntOrNull("RENDICIONES_RECOMPRESS_MAX_EDGE");
        int? quality = EnvIntOrNull("RENDICIONES_RECOMPRESS_QUALITY");

        _logger.LogInformation(
            "Recompresión de comprobantes: iniciando{DryRun} (min {MinKB} KB, maxEdge {MaxEdge}, calidad {Quality}).",
            dryRun ? " [DRY-RUN]" : "", minBytes / 1024,
            maxEdge?.ToString() ?? "default", quality?.ToString() ?? "default");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var externalDb = scope.ServiceProvider.GetRequiredService<IExternalDatabaseConnectionService>();
            var companies = await externalDb.ListActiveCompanyIdsAsync(ModuleCode, ct);

            foreach (var company in companies)
            {
                try
                {
                    await ProcessCompanyAsync(company, externalDb, dryRun, minBytes, maxEdge, quality, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Recompresión de comprobantes: falló la compañía {CompanyId} -- se sigue con las demás.", company.CompanyId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recompresión de comprobantes: fallo general -- se omite en este arranque.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task ProcessCompanyAsync(
        ModuleCompanyDto company, IExternalDatabaseConnectionService externalDb,
        bool dryRun, long minBytes, int? maxEdge, int? quality, CancellationToken ct)
    {
        var connection = await externalDb.ResolveConnectionAsync(ModuleCode, company.CompanyId, ct);

        var optionsBuilder = new DbContextOptionsBuilder<RendicionesDbContext>();
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

        await using var db = new RendicionesDbContext(optionsBuilder.Options);

        int reviewed = 0, hit = 0;
        long saved = 0;
        long lastId = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await db.ExpenseReceipts
                .Where(r => r.CompanyId == company.CompanyId && r.Id > lastId)
                .OrderBy(r => r.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
                break;

            lastId = batch[^1].Id;

            foreach (var r in batch)
            {
                reviewed++;
                if (!ReceiptRecompression.ShouldRecompress(r, minBytes))
                    continue;

                var processed = await _imageProcessor.ProcessAsync(r.FileName, r.MimeType, r.Content, maxEdge, quality, ct);
                if (processed.Content.Length >= r.Content.Length)
                    continue;

                saved += r.Content.Length - processed.Content.Length;
                hit++;

                if (dryRun)
                    continue;

                r.FileName = processed.FileName;
                r.MimeType = processed.MimeType;
                r.Content = processed.Content;
                r.SizeBytes = processed.Content.Length;
            }

            if (!dryRun)
                await db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Recompresión de comprobantes (compañía {CompanyId}){DryRun}: {Hit} de {Reviewed}, {SavedMB:N1} MB {Verbo}.",
            company.CompanyId, dryRun ? " [DRY-RUN]" : "", hit, reviewed,
            saved / (1024d * 1024d), dryRun ? "liberables" : "liberados");
    }

    private static bool EnvBool(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name), "true", StringComparison.OrdinalIgnoreCase);

    private static int EnvInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;

    private static int? EnvIntOrNull(string name) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : null;
}
