using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Servicios;

public sealed class ExpenseReportService : IExpenseReportService
{
    private readonly RendicionesDbContext _db;
    private readonly IExpenseApprovalGroupService _groups;
    private readonly IExpenseFundService _funds;
    private readonly IAttachmentStorageService _attachments;
    private readonly IEmailSenderService _emailSender;
    private readonly IUserContactLookupService _contacts;
    private readonly ILogger<ExpenseReportService> _logger;

    public ExpenseReportService(RendicionesDbContext db, IExpenseApprovalGroupService groups, IExpenseFundService funds,
        IAttachmentStorageService attachments, IEmailSenderService emailSender, IUserContactLookupService contacts,
        ILogger<ExpenseReportService> logger)
    {
        _db = db;
        _groups = groups;
        _funds = funds;
        _attachments = attachments;
        _emailSender = emailSender;
        _contacts = contacts;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ExpenseReport>> ListByUserAsync(Guid companyId, Guid userId, CancellationToken ct = default) =>
        await _db.ExpenseReports
            .Include(r => r.Lines)
            .Where(r => r.CompanyId == companyId && r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<ExpenseReport?> GetAsync(long id, Guid companyId, CancellationToken ct = default) =>
        await _db.ExpenseReports
            .Include(r => r.Lines).ThenInclude(d => d.ExpenseType)
            .FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId, ct);

    public async Task<long> CreateReportAsync(Guid companyId, Guid userId, IReadOnlyList<long> expenseIds, long? expenseFundId,
        string? costCenterCode, string? costCenterName, CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var report = new ExpenseReport
        {
            CompanyId = companyId,
            UserId = userId,
            ExpenseFundId = expenseFundId,
            CostCenterCode = costCenterCode,
            CostCenterName = costCenterName,
        };
        _db.ExpenseReports.Add(report);
        await _db.SaveChangesAsync(ct);

        if (expenseIds.Count > 0)
            await AttachExpensesAsync(report.Id, companyId, expenseIds, ct);

        await transaction.CommitAsync(ct);
        return report.Id;
    }

    public async Task UpdateHeaderAsync(long reportId, Guid companyId, long? expenseFundId,
        string? costCenterCode, string? costCenterName, CancellationToken ct = default)
    {
        var report = await RequireEditableAsync(reportId, companyId, ct);
        report.ExpenseFundId = expenseFundId;
        report.CostCenterCode = costCenterCode;
        report.CostCenterName = costCenterName;
        await _db.SaveChangesAsync(ct);
    }

    public async Task AttachExpensesAsync(long reportId, Guid companyId, IReadOnlyList<long> expenseIds, CancellationToken ct = default)
    {
        if (expenseIds.Count == 0)
            return;

        var report = await RequireEditableAsync(reportId, companyId, ct);

        var expenses = await _db.ExpenseReportLines
            .Where(d => expenseIds.Contains(d.Id) && d.CompanyId == companyId && d.UserId == report.UserId && d.Status == "Loose")
            .ToListAsync(ct);

        if (expenses.Count != expenseIds.Count)
            throw new InvalidOperationException("Alguno de los gastos seleccionados no existe, no es tuyo, o ya no está Loose.");

        if (expenses.Any(g => g.ExpenseTypeId is null))
            throw new InvalidOperationException("Hay gastos sin categoría -- completala antes de agregarlos a un informe (por ejemplo, los importados por OCR que todavía no se revisaron).");

        foreach (var expense in expenses)
        {
            expense.ExpenseReportId = reportId;
            expense.Status = "InReport";
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task DetachExpenseAsync(long reportId, long lineId, Guid companyId, CancellationToken ct = default)
    {
        await RequireEditableAsync(reportId, companyId, ct);

        var line = await _db.ExpenseReportLines
            .FirstOrDefaultAsync(d => d.Id == lineId && d.ExpenseReportId == reportId, ct)
            ?? throw new InvalidOperationException("El gasto no existe en este informe.");

        line.ExpenseReportId = null;
        line.Status = "Loose";
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveReceiptAsync(long reportId, long lineId, Guid companyId, CancellationToken ct = default)
    {
        await RequireEditableAsync(reportId, companyId, ct);

        var line = await _db.ExpenseReportLines
            .FirstOrDefaultAsync(d => d.Id == lineId && d.ExpenseReportId == reportId, ct)
            ?? throw new InvalidOperationException("El gasto no existe en este informe.");

        if (line.ExpenseReceiptId is not { } receiptId)
            return;

        line.ExpenseReceiptId = null;
        await _db.SaveChangesAsync(ct);
        await _attachments.DeleteAsync(receiptId, companyId, ct);
    }

    public async Task DeleteReportAsync(long reportId, Guid companyId, Guid userId, CancellationToken ct = default)
    {
        var report = await _db.ExpenseReports
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == reportId && r.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("El informe no existe o no pertenece a esta compañía.");

        if (report.Status != "Draft")
            throw new InvalidOperationException("Solo se puede eliminar un informe en estado Draft.");

        if (report.UserId != userId)
            throw new InvalidOperationException("Solo quien creó el informe puede eliminarlo.");

        foreach (var line in report.Lines)
        {
            line.ExpenseReportId = null;
            line.Status = "Loose";
        }

        _db.ExpenseReports.Remove(report);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SubmitAsync(long reportId, Guid companyId, CancellationToken ct = default)
    {
        var report = await RequireEditableAsync(reportId, companyId, ct);

        var hasLines = await _db.ExpenseReportLines.AnyAsync(d => d.ExpenseReportId == reportId, ct);
        if (!hasLines)
            throw new InvalidOperationException("La rendición necesita al menos una línea de gasto antes de enviarla.");

        var group = await _groups.GetUserGroupAsync(companyId, report.UserId, ct);
        report.SubmittedAt = DateTimeOffset.UtcNow;

        if (group is null)
        {
            report.Status = "Approved";
            report.ResolvedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            await RecalculateFundIfApplicableAsync(report, companyId, ct);
            return;
        }

        var levels = await _groups.GetLevelsAsync(group.Id, ct);
        var nextLevel = IExpenseApprovalGroupService.ResolveNextLevel(levels, levelFrom: 1, report.UserId);

        report.ExpenseApprovalGroupId = group.Id;
        if (nextLevel is null)
        {
            report.Status = "Approved";
            report.ResolvedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            report.Status = "Pending";
            report.CurrentLevel = nextLevel;
        }

        await _db.SaveChangesAsync(ct);

        if (report.Status == "Approved")
        {
            await RecalculateFundIfApplicableAsync(report, companyId, ct);
        }
        else
        {
            await NotifyApproverAsync(report, levels[nextLevel!.Value], ct);
        }
    }

    public async Task ApproveAsync(long reportId, Guid companyId, Guid approverUserId, string? comment, CancellationToken ct = default)
    {
        var report = await RequirePendingAndApproverAsync(reportId, companyId, approverUserId, ct);
        var resolvedLevel = report.CurrentLevel!.Value;

        _db.ExpenseReportActions.Add(new ExpenseReportAction
        {
            ExpenseReportId = reportId,
            Level = resolvedLevel,
            UserId = approverUserId,
            Decision = "Approved",
            Comment = comment,
        });

        var levels = await _groups.GetLevelsAsync(report.ExpenseApprovalGroupId!.Value, ct);
        var nextLevel = IExpenseApprovalGroupService.ResolveNextLevel(levels, levelFrom: resolvedLevel + 1, report.UserId);

        if (nextLevel is null)
        {
            report.Status = "Approved";
            report.CurrentLevel = null;
            report.ResolvedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            report.CurrentLevel = nextLevel;
        }

        await _db.SaveChangesAsync(ct);

        if (report.Status == "Approved")
        {
            await RecalculateFundIfApplicableAsync(report, companyId, ct);
            await NotifyReportOwnerAsync(report, "Tu informe fue aprobado.", ct);
        }
        else
        {
            await NotifyApproverAsync(report, levels[nextLevel!.Value], ct);
        }
    }

    public async Task RejectAsync(long reportId, Guid companyId, Guid approverUserId, string? comment, CancellationToken ct = default)
    {
        var report = await RequirePendingAndApproverAsync(reportId, companyId, approverUserId, ct);

        _db.ExpenseReportActions.Add(new ExpenseReportAction
        {
            ExpenseReportId = reportId,
            Level = report.CurrentLevel!.Value,
            UserId = approverUserId,
            Decision = "Rejected",
            Comment = comment,
        });

        report.Status = "Rejected";
        report.CurrentLevel = null;
        report.ResolvedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        var motivo = string.IsNullOrWhiteSpace(comment) ? "sin motivo indicado" : System.Net.WebUtility.HtmlEncode(comment);
        await NotifyReportOwnerAsync(report, $"Tu informe fue rechazado. Motivo: {motivo}", ct);
    }

    public async Task ReopenAsync(long reportId, Guid companyId, Guid userId, CancellationToken ct = default)
    {
        var report = await _db.ExpenseReports
            .FirstOrDefaultAsync(r => r.Id == reportId && r.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("La rendición no existe o no pertenece a esta compañía.");

        if (report.Status != "Rejected")
            throw new InvalidOperationException("Solo se puede reabrir una rendición Rejected.");

        if (report.UserId != userId)
            throw new InvalidOperationException("Solo quien creó la rendición puede reabrirla.");

        report.Status = "Draft";
        report.Round += 1;
        report.CurrentLevel = null;
        report.SubmittedAt = null;
        report.ResolvedAt = null;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ExpenseReportAction>> ListHistoryAsync(long reportId, CancellationToken ct = default) =>
        await _db.ExpenseReportActions
            .Where(a => a.ExpenseReportId == reportId)
            .OrderBy(a => a.OccurredAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ExpenseReport>> ListPendingForApproverAsync(Guid companyId, Guid approverUserId, CancellationToken ct = default) =>
        await _db.ExpenseReports
            .Include(r => r.Lines)
            .Where(r => r.CompanyId == companyId && r.Status == "Pending"
                && r.ExpenseApprovalGroupId != null && r.CurrentLevel != null)
            .Join(_db.ExpenseApprovalGroupLevels.Where(n => n.UserId == approverUserId),
                r => new { GroupId = r.ExpenseApprovalGroupId!.Value, Level = r.CurrentLevel!.Value },
                n => new { GroupId = n.ExpenseApprovalGroupId, n.Level },
                (r, n) => r)
            .OrderBy(r => r.SubmittedAt)
            .ToListAsync(ct);

    private async Task<ExpenseReport> RequirePendingAndApproverAsync(long reportId, Guid companyId, Guid approverUserId, CancellationToken ct)
    {
        var report = await _db.ExpenseReports
            .FirstOrDefaultAsync(r => r.Id == reportId && r.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("La rendición no existe o no pertenece a esta compañía.");

        if (report.Status != "Pending" || report.CurrentLevel is null || report.ExpenseApprovalGroupId is null)
            throw new InvalidOperationException("La rendición no está pendiente de aprobación.");

        var levels = await _groups.GetLevelsAsync(report.ExpenseApprovalGroupId.Value, ct);
        if (!levels.TryGetValue(report.CurrentLevel.Value, out var expectedApprover) || expectedApprover != approverUserId)
            throw new InvalidOperationException("No sos el aprobador del nivel actual de esta rendición.");

        return report;
    }

    private async Task RecalculateFundIfApplicableAsync(ExpenseReport report, Guid companyId, CancellationToken ct)
    {
        if (report.ExpenseFundId is { } fundId)
            await _funds.RecalculateStatusAsync(fundId, companyId, ct);
    }

    private async Task<ExpenseReport> RequireEditableAsync(long reportId, Guid companyId, CancellationToken ct)
    {
        var report = await _db.ExpenseReports
            .FirstOrDefaultAsync(r => r.Id == reportId && r.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException("La rendición no existe o no pertenece a esta compañía.");

        if (report.Status != "Draft")
            throw new InvalidOperationException("Solo se puede modificar una rendición en estado Draft.");

        return report;
    }

    /// <summary>Un correo caído nunca debe tumbar una transición de estado real -- loguear y seguir (ver el spec de esta feature).</summary>
    private async Task NotifyApproverAsync(ExpenseReport report, Guid approverUserId, CancellationToken ct) =>
        await NotifyAsync(approverUserId, $"Informe #{report.Id} pendiente de tu aprobación",
            $"<p>Tenés un informe de rendición de gastos (#{report.Id}) esperando tu decisión.</p>", ct);

    private async Task NotifyReportOwnerAsync(ExpenseReport report, string message, CancellationToken ct) =>
        await NotifyAsync(report.UserId, $"Informe #{report.Id} — actualización",
            $"<p>{message}</p>", ct);

    private async Task NotifyAsync(Guid userId, string subject, string htmlBody, CancellationToken ct)
    {
        var contact = await _contacts.GetContactAsync(userId, ct);
        if (contact is null || !contact.EmailNotificationsEnabled)
            return;

        try
        {
            await _emailSender.SendAsync(contact.OrganizationId, new EmailMessage(contact.Email, subject, htmlBody), ct);
        }
        catch (Exception ex)
        {
            // Correo caído (ej. organización sin proveedor configurado) no debe bloquear
            // la transacción de negocio ya confirmada -- ver constraint global del plan.
            _logger.LogWarning(ex, "No se pudo enviar la notificación por correo a {UserId} ({Email}).", userId, contact.Email);
        }
    }
}
