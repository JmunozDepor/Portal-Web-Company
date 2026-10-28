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

    /// <summary>
    /// Zona del negocio (Chile). Se busca por el id de Windows y luego por el de IANA, así
    /// corre tanto en IIS/Windows como en Linux. Si no existe ninguna, cae a UTC.
    /// </summary>
    private static readonly TimeZoneInfo BusinessZone = ResolveBusinessZone();

    private static TimeZoneInfo ResolveBusinessZone()
    {
        foreach (var id in new[] { "Pacific SA Standard Time", "America/Santiago" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // probar el siguiente id
            }
        }
        return TimeZoneInfo.Utc;
    }

    /// <summary>
    /// Límites UTC del período [inicio, fin) para columnas timestamptz. Las fechas del
    /// filtro son días calendario de la zona del negocio: se convierte el inicio del primer
    /// día y el inicio del día siguiente al último. Así el filtro nunca compara un DateTime
    /// sin zona contra timestamptz (Npgsql lo rechaza) y un día se corta a la hora local.
    /// </summary>
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtcExclusive) PeriodBoundsUtc(DateTime fromDay, DateTime toDay)
    {
        var startLocal = DateTime.SpecifyKind(fromDay.Date, DateTimeKind.Unspecified);
        var endLocal = DateTime.SpecifyKind(toDay.Date.AddDays(1), DateTimeKind.Unspecified);

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, BusinessZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, BusinessZone);

        return (new DateTimeOffset(startUtc, TimeSpan.Zero), new DateTimeOffset(endUtc, TimeSpan.Zero));
    }

    public async Task<IReadOnlyList<ClosingReportLine>> GenerateAsync(Guid companyId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var (startUtc, endUtcExclusive) = PeriodBoundsUtc(from.Date, to.Date);

        var reports = await _db.ExpenseReports
            .Include(r => r.Lines).ThenInclude(d => d.ExpenseType)
            .Where(r => r.CompanyId == companyId && r.Status == "Approved"
                && r.ResolvedAt != null && r.ResolvedAt >= startUtc && r.ResolvedAt < endUtcExclusive)
            .OrderBy(r => r.ResolvedAt)
            .ToListAsync(ct);

        // Nombres de colaborador: ITenantUserAdminService.ListAsync() ya está acotado a
        // la organización actual (ICurrentUserContext.OrganizationId), se resuelve una
        // sola vez para todo el período en vez de consultar por cada rendición.
        var users = await _users.ListAsync(ct);
        var nameByUserId = users.DistinctBy(u => u.Id).ToDictionary(u => u.Id, u => u.FullName ?? u.Username);

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
