using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Servicios;

public sealed class ClosingReportService : IClosingReportService
{
    private readonly RendicionesDbContext _db;
    private readonly ITenantUserAdminService _users;

    public ClosingReportService(RendicionesDbContext db, ITenantUserAdminService users)
    {
        _db = db;
        _users = users;
    }

    public async Task<IReadOnlyList<ClosingReportLine>> GenerateAsync(Guid companyId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var endOfDay = to.Date.AddDays(1).AddTicks(-1);

        var reports = await _db.ExpenseReports
            .Include(r => r.Lines).ThenInclude(d => d.ExpenseType)
            .Where(r => r.CompanyId == companyId && r.Status == "Approved"
                && r.ResolvedAt != null && r.ResolvedAt >= from.Date && r.ResolvedAt <= endOfDay)
            .OrderBy(r => r.ResolvedAt)
            .ToListAsync(ct);

        // Nombres de colaborador: ITenantUserAdminService.ListAsync() ya está acotado a
        // la organización actual (ICurrentUserContext.OrganizationId), se resuelve una
        // sola vez para todo el período en vez de consultar por cada rendición.
        var users = await _users.ListAsync(ct);
        var nameByUserId = users.ToDictionary(u => u.Id, u => u.Username);

        var lines = new List<ClosingReportLine>();
        foreach (var report in reports)
        {
            var employeeName = nameByUserId.GetValueOrDefault(report.UserId, report.UserId.ToString());
            foreach (var line in report.Lines)
            {
                lines.Add(new ClosingReportLine(
                    report.Id,
                    report.Round,
                    employeeName,
                    report.CostCenterCode,
                    report.CostCenterName,
                    line.ExpenseType?.Name ?? "—",
                    line.Date,
                    line.Amount,
                    line.Currency,
                    line.Notes));
            }
        }

        return lines;
    }
}
