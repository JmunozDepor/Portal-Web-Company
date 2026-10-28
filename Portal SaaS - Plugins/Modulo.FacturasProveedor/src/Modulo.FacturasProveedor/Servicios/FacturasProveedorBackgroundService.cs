using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulo.FacturasProveedor.Data;
using Modulo.FacturasProveedor.Dominio;
using Modulo.FacturasProveedor.Models;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.FacturasProveedor.Servicios;

/// <summary>
/// Cada 5 minutos, por cada compañía con el módulo activo (ListActiveCompanyIdsAsync): un scope con
/// ICurrentCompanyOverride, evaluación de la bandeja (InvoiceReviewService) y vencimientos/envíos (DueActionsService).
/// Si está habilitado, a partir de las 08:00 de Chile manda una vez al día un correo a cada aprobador con sus
/// pendientes (patrón del recordatorio de Rendiciones). Una compañía con error no corta a las demás.
/// </summary>
public sealed class FacturasProveedorBackgroundService : BackgroundService
{
    private const string ModuleCode = "FacturasProveedor";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FacturasProveedorBackgroundService> _logger;

    public FacturasProveedorBackgroundService(IServiceScopeFactory scopeFactory, ILogger<FacturasProveedorBackgroundService> logger)
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
                IReadOnlyList<ModuleCompanyDto> companies;
                using (var scope = _scopeFactory.CreateScope())
                {
                    companies = await scope.ServiceProvider.GetRequiredService<IExternalDatabaseConnectionService>()
                        .ListActiveCompanyIdsAsync(ModuleCode, stoppingToken);
                }
                await RunForCompaniesAsync(companies.Select(c => c.CompanyId).ToList(), id => ProcessCompanyAsync(id, stoppingToken), _logger);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Facturas de Proveedor: falló el ciclo -- se reintenta en el próximo.");
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

    /// <summary>Recorre las compañías; una excepción en una se registra y se sigue con las demás.</summary>
    internal static async Task RunForCompaniesAsync(IReadOnlyList<Guid> companyIds, Func<Guid, Task> work, ILogger logger)
    {
        foreach (var companyId in companyIds)
        {
            try
            {
                await work(companyId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Facturas de Proveedor: no se pudo procesar la compañía {CompanyId} -- se sigue con las demás.", companyId);
            }
        }
    }

    /// <summary>
    /// Un ciclo de una compañía: evaluación y después vencimientos/envíos. Si la evaluación falla (SAP caído, una consulta
    /// desactivada, concurrencia), se descarta lo que dejó a medias y los vencimientos, envíos y escalamientos corren
    /// igual: el último día no puede depender de que SAP responda.
    /// </summary>
    internal static async Task<(ReviewCycleResult? Cycle, DueRunResult Due)> RunCycleAsync(IInvoiceReviewService reviews,
        IDueActionsService due, Action discardPendingChanges, ILogger logger, Guid companyId, CancellationToken ct)
    {
        ReviewCycleResult? cycle = null;
        try
        {
            cycle = await reviews.EvaluatePendingAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            discardPendingChanges();
            logger.LogWarning(ex, "Facturas de Proveedor (compañía {CompanyId}): falló la evaluación; se procesan igual vencimientos y envíos.", companyId);
        }
        return (cycle, await due.RunAsync(ct));
    }

    private async Task ProcessCompanyAsync(Guid companyId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);

        // Sin recepción habilitada en la compañía no hay nada que evaluar (ni que enviar).
        if (!(await sp.GetRequiredService<IDteInboxService>().GetStatusAsync(ct)).ReceptionEnabled) return;

        var (cycle, due) = await RunCycleAsync(sp.GetRequiredService<IInvoiceReviewService>(), sp.GetRequiredService<IDueActionsService>(),
            () => sp.GetRequiredService<FacturasProveedorDbContext>().ChangeTracker.Clear(), _logger, companyId, ct);
        if ((cycle?.Created ?? 0) > 0 || (cycle?.Changed ?? 0) > 0 || due.Sent > 0 || due.Failed > 0)
        {
            _logger.LogInformation(
                "Facturas de Proveedor (compañía {CompanyId}): {Created} nueva(s), {Changed} con decisión nueva, {Sent} respondida(s), {Failed} con falla.",
                companyId, cycle?.Created ?? 0, cycle?.Changed ?? 0, due.Sent, due.Failed);
        }
        await SendDailyDigestAsync(sp, companyId, ct);
    }

    private static async Task SendDailyDigestAsync(IServiceProvider sp, Guid companyId, CancellationToken ct)
    {
        var db = sp.GetRequiredService<FacturasProveedorDbContext>();
        var settings = await db.Settings.FirstOrDefaultAsync(s => s.CompanyId == companyId, ct);
        var nowChile = ChileClock.ToLocal(sp.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime);
        var today = DateOnly.FromDateTime(nowChile);
        if (settings is not { DailyEmailDigest: true } || settings.LastDigestOn >= today || nowChile.Hour < 8) return;

        var pending = await db.InvoiceReviews.AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Status == ReviewStatuses.InReview && r.AssignedGroupId != null)
            .ToListAsync(ct);
        var groups = sp.GetRequiredService<IApproverGroupService>();
        var byUser = new Dictionary<Guid, List<FpInvoiceReview>>();
        foreach (var group in pending.GroupBy(r => r.AssignedGroupId!.Value))
        {
            foreach (var userId in await groups.GetMemberIdsAsync(group.Key, ct))
            {
                if (!byUser.TryGetValue(userId, out var list)) byUser[userId] = list = new();
                list.AddRange(group);
            }
        }

        var contacts = sp.GetRequiredService<IUserContactLookupService>();
        var email = sp.GetRequiredService<IEmailSenderService>();
        foreach (var (userId, reviews) in byUser)
        {
            var contact = await contacts.GetContactAsync(userId, ct);
            if (contact is not { EmailNotificationsEnabled: true } || string.IsNullOrWhiteSpace(contact.Email)) continue;
            var body = new StringBuilder("<p>Tienes facturas de proveedor por decidir:</p><ul>");
            foreach (var r in reviews.OrderBy(r => r.LegalDeadlineOn))
                body.Append("<li>").Append(WebUtility.HtmlEncode(InvoiceReviewService.Describe(r)))
                    .Append(" — vence el ").Append(r.LegalDeadlineOn.ToString("dd-MM-yyyy")).Append("</li>");
            body.Append("</ul><p>Revísalas en el portal: Facturas de Proveedor › Mis aprobaciones.</p>");
            try
            {
                await email.SendAsync(contact.OrganizationId, new EmailMessage(contact.Email, "Facturas de proveedor por decidir", body.ToString()), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort: el correo diario no puede cortar el ciclo; la campana ya avisó.
            }
        }

        settings.LastDigestOn = today;
        await db.SaveChangesAsync(ct);
    }
}
