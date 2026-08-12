using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Recordatorio diario de informes pendientes por aprobador -- recorre TODAS las
/// compañías con Rendiciones activo (ListActiveCompanyIdsAsync, ver
/// docs/superpowers/specs/2026-08-11-flujo-aprobacion-notificaciones-design.md),
/// resolviendo un RendicionesDbContext manual por compañía (no puede usar el
/// registrado por DI: ese depende de ICurrentCompanyAccessor, que exige un request
/// HTTP en curso -- acá no hay ninguno).
/// </summary>
public sealed class RendicionesReminderBackgroundService : BackgroundService
{
    private const string ModuleCode = "Rendiciones";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RendicionesReminderBackgroundService> _logger;

    public RendicionesReminderBackgroundService(IServiceScopeFactory scopeFactory, ILogger<RendicionesReminderBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo en el ciclo del recordatorio diario de Rendiciones -- se reintenta en el próximo ciclo.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var externalDb = sp.GetRequiredService<IExternalDatabaseConnectionService>();
        var contacts = sp.GetRequiredService<IUserContactLookupService>();
        var emailSender = sp.GetRequiredService<IEmailSenderService>();

        var companies = await externalDb.ListActiveCompanyIdsAsync(ModuleCode, ct);

        foreach (var company in companies)
        {
            try
            {
                await ProcessCompanyAsync(company, externalDb, contacts, emailSender, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo procesar el recordatorio diario para la compañía {CompanyId} -- se sigue con las demás.", company.CompanyId);
            }
        }
    }

    private async Task ProcessCompanyAsync(
        ModuleCompanyDto company,
        IExternalDatabaseConnectionService externalDb,
        IUserContactLookupService contacts,
        IEmailSenderService emailSender,
        CancellationToken ct)
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

        var settings = await db.RendicionesSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (settings is null || !settings.ReminderEnabled)
            return;

        var nowLocal = TimeOnly.FromDateTime(DateTime.Now);
        var todayLocal = DateOnly.FromDateTime(DateTime.Now);

        // Ventana de +/- la mitad del intervalo de polling alrededor de la hora
        // configurada -- evita depender de que el ciclo caiga justo en el minuto exacto.
        var withinWindow = Math.Abs((nowLocal.ToTimeSpan() - settings.ReminderHour.ToTimeSpan()).TotalMinutes) <= PollInterval.TotalMinutes / 2;
        if (!withinWindow)
            return;

        var alreadySentToday = await db.ReminderLogs.AsNoTracking().AnyAsync(x => x.SentDate == todayLocal, ct);
        if (alreadySentToday)
            return;

        var pending = await db.ExpenseReports
            .Where(r => r.CompanyId == company.CompanyId && r.Status == "Pending"
                && r.ExpenseApprovalGroupId != null && r.CurrentLevel != null)
            .ToListAsync(ct);

        if (pending.Count > 0)
        {
            var groupIds = pending.Select(r => r.ExpenseApprovalGroupId!.Value).Distinct().ToList();
            var levels = await db.ExpenseApprovalGroupLevels
                .Where(l => groupIds.Contains(l.ExpenseApprovalGroupId))
                .ToListAsync(ct);

            var levelsByGroup = levels
                .GroupBy(l => l.ExpenseApprovalGroupId)
                .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<int, Guid>)g.ToDictionary(l => l.Level, l => l.UserId));

            var byApprover = ReminderGrouping.GroupPendingByApprover(pending, levelsByGroup);

            foreach (var (approverUserId, reports) in byApprover)
            {
                var contact = await contacts.GetContactAsync(approverUserId, ct);
                if (contact is null || !contact.EmailNotificationsEnabled)
                    continue;

                var body = $"<p>Tenés {reports.Count} informe(s) de rendición de gastos pendientes de tu aprobación:</p><ul>"
                    + string.Join("", reports.Select(r => $"<li>Informe #{r.Id}</li>"))
                    + "</ul>";

                try
                {
                    await emailSender.SendAsync(contact.OrganizationId, new EmailMessage(contact.Email, "Recordatorio: informes pendientes de aprobar", body), ct);
                }
                catch (Exception)
                {
                    // Mismo criterio que ExpenseReportService: un correo caído no debe
                    // frenar el resto de la ronda de recordatorios.
                }
            }
        }

        db.ReminderLogs.Add(new ReminderLog { SentDate = todayLocal });
        await db.SaveChangesAsync(ct);
    }
}
