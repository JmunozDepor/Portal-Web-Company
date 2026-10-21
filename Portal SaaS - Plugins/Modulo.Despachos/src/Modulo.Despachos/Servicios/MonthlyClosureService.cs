using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Data;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Servicios;

public sealed class MonthlyClosureService : IMonthlyClosureService
{
    private static readonly JsonSerializerOptions PayloadJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly DespachosDbContext _db;
    private readonly ICurrentCompanyAccessor _company;
    private readonly ICurrentUserContext _user;
    private readonly TimeProvider _time;

    public MonthlyClosureService(
        DespachosDbContext db, ICurrentCompanyAccessor company, ICurrentUserContext user, TimeProvider time)
    {
        _db = db;
        _company = company;
        _user = user;
        _time = time;
    }

    private Guid CompanyId => _company.CompanyId;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public static string PeriodKey(int year, int month) =>
        string.Create(CultureInfo.InvariantCulture, $"{year:0000}-{month:00}");

    public static string ClosedPeriodMessage(DateOnly date) =>
        $"El período {PeriodKey(date.Year, date.Month)} está cerrado (período cerrado); reábralo en Cierre mensual para modificar este manifiesto.";

    public async Task<bool> IsClosedAsync(DateOnly date)
    {
        var key = PeriodKey(date.Year, date.Month);
        return await _db.MonthlyClosures.AnyAsync(c =>
            c.CompanyId == CompanyId && c.Period == key && c.Status == ClosureStatuses.Closed);
    }

    public async Task<PeriodStatus> GetStatusAsync(int year, int month)
    {
        ValidatePeriod(year, month);
        var row = await FindAsync(year, month);
        var manifests = await PeriodManifestsAsync(year, month);
        var blockers = row?.Status == ClosureStatuses.Closed ? new List<string>() : BlockersOf(manifests);
        return new PeriodStatus(
            row?.Status ?? ClosureStatuses.Open, row?.ClosedAt, row?.ReopenedAt, row?.ReopenReason,
            manifests.Count, manifests.Sum(m => m.CostFinal ?? 0m), blockers);
    }

    public async Task<ClosureResult> CloseAsync(int year, int month)
    {
        ValidatePeriod(year, month);
        var row = await FindAsync(year, month);
        if (row?.Status == ClosureStatuses.Closed)
            return new ClosureResult(false, new[] { $"El período {PeriodKey(year, month)} ya está cerrado." });

        var manifests = await PeriodManifestsAsync(year, month);
        var blockers = BlockersOf(manifests);
        if (blockers.Count > 0) return new ClosureResult(false, blockers);

        var now = UtcNow;
        if (row == null)
        {
            row = new MonthlyClosure { CompanyId = CompanyId, Period = PeriodKey(year, month), CreatedAt = now, CreatedBy = _user.UserId };
            _db.MonthlyClosures.Add(row);
        }
        row.Status = ClosureStatuses.Closed;
        row.ClosedAt = now;
        row.ClosedBy = _user.UserId;
        row.ManifestCount = manifests.Count;
        row.TotalCost = manifests.Sum(m => m.CostFinal ?? 0m);
        row.UpdatedAt = now;
        row.UpdatedBy = _user.UserId;

        foreach (var m in manifests)
        {
            m.CostFrozenAt = now;
            AddEvent(m, ManifestEventTypes.PeriodClosed, new { period = row.Period, costFinal = m.CostFinal });
        }
        await _db.SaveChangesAsync();
        return new ClosureResult(true, Array.Empty<string>());
    }

    public async Task ReopenAsync(int year, int month, string reason)
    {
        ValidatePeriod(year, month);
        if (!_user.IsAdmin)
            throw new InvalidOperationException("Solo un administrador puede reabrir un período cerrado.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Indique el motivo de la reapertura.");
        var row = await FindAsync(year, month);
        if (row?.Status != ClosureStatuses.Closed)
            throw new InvalidOperationException($"El período {PeriodKey(year, month)} no está cerrado.");

        var now = UtcNow;
        var text = reason.Trim();
        row.Status = ClosureStatuses.Reopened;
        row.ReopenedAt = now;
        row.ReopenedBy = _user.UserId;
        row.ReopenReason = text.Length > 500 ? text[..500] : text;
        row.UpdatedAt = now;
        row.UpdatedBy = _user.UserId;
        foreach (var m in await PeriodManifestsAsync(year, month))
        {
            m.CostFrozenAt = null;
            AddEvent(m, ManifestEventTypes.PeriodReopened, new { period = row.Period, reason = text });
        }
        await _db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- Helpers

    private static void ValidatePeriod(int year, int month)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            throw new InvalidOperationException("El período indicado no es válido.");
    }

    private Task<MonthlyClosure?> FindAsync(int year, int month)
    {
        var key = PeriodKey(year, month);
        return _db.MonthlyClosures.FirstOrDefaultAsync(c => c.CompanyId == CompanyId && c.Period == key);
    }

    /// <summary>Manifiestos del período (por dispatch_date), sin los anulados.</summary>
    private async Task<List<Manifest>> PeriodManifestsAsync(int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1);
        return await _db.Manifests
            .Where(m => m.CompanyId == CompanyId && m.DispatchDate >= from && m.DispatchDate < to
                        && m.Status != ManifestStatuses.Annulled)
            .OrderBy(m => m.Number).ToListAsync();
    }

    private static List<string> BlockersOf(List<Manifest> manifests)
    {
        var result = new List<string>();
        void Add(string label, IEnumerable<Manifest> list)
        {
            var nums = list.Select(m => m.Number).OrderBy(n => n).ToList();
            if (nums.Count > 0) result.Add($"{label}: {string.Join(", ", nums)}");
        }
        Add("Manifiestos en borrador", manifests.Where(m => m.Status == ManifestStatuses.Draft));
        Add("Manifiestos despachados sin retorno", manifests.Where(m => m.Status == ManifestStatuses.Dispatched));
        Add("Manifiestos sin costo (sin tarifa o tarifa ambigua, y sin ajuste manual)",
            manifests.Where(m => m.Status != ManifestStatuses.Draft && m.Status != ManifestStatuses.Dispatched
                                 && m.CostStatus != CostStatuses.Ok && m.CostOverride == null));
        return result;
    }

    private void AddEvent(Manifest m, string type, object payload) =>
        _db.ManifestEvents.Add(new ManifestEvent
        {
            CompanyId = CompanyId, ManifestId = m.Id, EventType = type, FromStatus = m.Status, ToStatus = m.Status,
            UserId = _user.UserId, OccurredAt = UtcNow, PayloadJson = JsonSerializer.Serialize(payload, PayloadJson),
        });
}
