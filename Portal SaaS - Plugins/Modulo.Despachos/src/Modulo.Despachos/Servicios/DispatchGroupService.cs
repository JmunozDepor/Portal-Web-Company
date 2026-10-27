using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modulo.Despachos.Data;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Servicios;

public class DispatchGroupService : IDispatchGroupService
{
    private readonly DespachosDbContext _db;
    private readonly ICurrentCompanyAccessor _company;
    private readonly ICurrentUserContext _user;
    private readonly ISapDocumentLookupService _sap;
    private readonly IMasterDataService _master;
    private readonly TimeProvider _time;
    private readonly INotificationService _notifications;
    private readonly ILogger<DispatchGroupService>? _logger;

    public DispatchGroupService(
        DespachosDbContext db, ICurrentCompanyAccessor company, ICurrentUserContext user, ISapDocumentLookupService sap,
        IMasterDataService master, TimeProvider time, INotificationService notifications, ILogger<DispatchGroupService>? logger = null)
    {
        _db = db;
        _company = company;
        _user = user;
        _sap = sap;
        _master = master;
        _time = time;
        _notifications = notifications;
        _logger = logger;
    }

    private Guid CompanyId => _company.CompanyId;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    // ---------------------------------------------------------------- Generar

    public async Task<IReadOnlyList<BranchCandidates>> ListCandidatesAsync(DateOnly fromDate, string? warehouseCode = null)
    {
        var branches = (await _master.ListBranchWarehousesAsync())
            .Where(b => string.IsNullOrWhiteSpace(warehouseCode) || b.WarehouseCode == warehouseCode.Trim())
            .ToDictionary(b => b.WarehouseCode, StringComparer.OrdinalIgnoreCase);
        if (branches.Count == 0) return Array.Empty<BranchCandidates>();

        var transfers = (await _sap.ListPendingTransfersAllAsync(fromDate))
            .Where(t => !t.IsCanceled && branches.ContainsKey(t.WarehouseCode))
            .ToList();
        var occupied = await OccupiedAsync(transfers.Select(t => t.DocEntry));

        return transfers
            .Where(t => !occupied.ContainsKey(t.DocEntry))
            .GroupBy(t => branches[t.WarehouseCode])
            .OrderBy(g => g.Key.WarehouseCode, StringComparer.Ordinal)
            .Select(g => new BranchCandidates(g.Key, g.OrderBy(t => t.DocumentDate).ThenBy(t => t.DocNum).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<DispatchGroup>> GenerateAsync(DateOnly dispatchDate, DateOnly fromDate, IReadOnlyCollection<int> docEntries)
    {
        var chosen = docEntries.ToHashSet();
        var candidates = (await ListCandidatesAsync(fromDate))
            .Select(c => new BranchCandidates(c.Branch, c.Transfers.Where(t => chosen.Contains(t.DocEntry)).ToList()))
            .Where(c => c.Transfers.Count > 0)
            .ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException("No hay transferencias libres para generar agrupaciones con la selección indicada.");

        var number = (await _db.DispatchGroups.Where(g => g.CompanyId == CompanyId).MaxAsync(g => (int?)g.Number) ?? 0);
        var created = new List<DispatchGroup>();
        foreach (var c in candidates)
        {
            var sequence = await _db.DispatchGroups.CountAsync(g => g.CompanyId == CompanyId && g.Kind == DispatchGroupKinds.Transfer
                && g.WarehouseCode == c.Branch.WarehouseCode && g.DispatchDate == dispatchDate) + 1;
            var group = new DispatchGroup
            {
                CompanyId = CompanyId, Number = ++number, WarehouseCode = c.Branch.WarehouseCode, BranchName = c.Branch.Name,
                DispatchDate = dispatchDate, Sequence = sequence, Status = DispatchGroupStatuses.Armed,
                CreatedAt = UtcNow, CreatedBy = _user.UserId,
                Transfers = c.Transfers.Select(ToLine).ToList(),
            };
            _db.DispatchGroups.Add(group);
            created.Add(group);
        }
        await _db.SaveChangesAsync();
        await NotifyGeneratedAsync(dispatchDate, created);
        return created;
    }

    private DispatchGroupTransfer ToLine(SapTransferInfo t) => new()
    {
        CompanyId = CompanyId, SapDocEntry = t.DocEntry, SapDocNum = t.DocNum,
        Lpn = t.Lpn is { Length: > 30 } l ? l[..30] : t.Lpn, Units = t.Units, DocumentDate = t.DocumentDate,
    };

    /// <summary>Aviso accesorio: nunca deshace lo generado.</summary>
    private async Task NotifyGeneratedAsync(DateOnly dispatchDate, List<DispatchGroup> created)
    {
        try
        {
            var branches = created.Select(g => g.WarehouseCode).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var message = $"{created.Count} agrupaciones listas para el {dispatchDate:dd-MM} ({branches} sucursales).";
            var url = $"/despachos/agrupar-traslados?dispatchDate={dispatchDate:yyyy-MM-dd}";
            foreach (var r in await _master.ListNotificationRecipientsAsync())
            {
                try { await _notifications.TryEnqueueAsync(r.UserId, "Agrupaciones listas", message, NotificationSeverity.Info, url); }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "No se pudo avisar al usuario {UserId} de las agrupaciones generadas.", r.UserId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "No se pudo enviar el aviso de agrupaciones generadas.");
        }
    }

    // ---------------------------------------------------------------- Consulta

    public async Task<DispatchGroup?> GetAsync(long id) =>
        await _db.DispatchGroups.Include(g => g.Transfers)
            .FirstOrDefaultAsync(g => g.CompanyId == CompanyId && g.Id == id);

    public async Task<DispatchGroup?> GetByNumberAsync(int number) =>
        await _db.DispatchGroups.Include(g => g.Transfers)
            .FirstOrDefaultAsync(g => g.CompanyId == CompanyId && g.Number == number);

    public async Task<IReadOnlyList<DispatchGroupListItem>> ListAsync(DispatchGroupFilter filter)
    {
        var q = _db.DispatchGroups.Include(g => g.Transfers).Where(g => g.CompanyId == CompanyId && g.Kind == filter.Kind);
        if (filter.DispatchDate is { } d) q = q.Where(g => g.DispatchDate == d);
        if (filter.From is { } from) q = q.Where(g => g.DispatchDate >= from);
        if (filter.To is { } to) q = q.Where(g => g.DispatchDate <= to);
        if (!string.IsNullOrWhiteSpace(filter.WarehouseCode)) q = q.Where(g => g.WarehouseCode == filter.WarehouseCode.Trim());
        if (!string.IsNullOrWhiteSpace(filter.Status)) q = q.Where(g => g.Status == filter.Status);
        var groups = await q.OrderByDescending(g => g.DispatchDate).ThenBy(g => g.WarehouseCode).ThenBy(g => g.Sequence)
            .Take(500).ToListAsync();

        // Manifiesto vigente (no anulado) de cada agrupación, por sus documentos.
        var ids = groups.Select(g => g.Id).ToList();
        var manifests = await (from doc in _db.ManifestDocuments
                               join m in _db.Manifests on doc.ManifestId equals m.Id
                               where doc.CompanyId == CompanyId && doc.DispatchGroupId != null && ids.Contains(doc.DispatchGroupId.Value)
                                     && m.Status != ManifestStatuses.Annulled
                               select new { GroupId = doc.DispatchGroupId!.Value, m.Id, m.Number })
            .ToListAsync();
        var byGroup = manifests.GroupBy(x => x.GroupId).ToDictionary(g => g.Key, g => g.First());

        return groups.Select(g =>
        {
            byGroup.TryGetValue(g.Id, out var m);
            return new DispatchGroupListItem(g.Id, g.Number, DispatchGroupCode.Format(g.Number, g.Kind), g.WarehouseCode, g.BranchName,
                g.DispatchDate, g.Sequence, g.Status, g.Transfers.Count, g.Transfers.Sum(t => t.Units), m?.Id, m?.Number);
        }).ToList();
    }

    // ---------------------------------------------------------------- Edición

    public async Task<string> AddLpnAsync(long groupId, string lpn)
    {
        var g = await RequireArmedAsync(groupId);
        var text = (lpn ?? "").Trim().ToUpperInvariant();
        if (text.Length == 0) throw new InvalidOperationException("Escanee o digite un LPN.");
        var t = await _sap.FindTransferByLpnAsync(text)
            ?? throw new InvalidOperationException($"No se encontró el LPN {text} en SAP.");
        if (t.IsCanceled)
            throw new InvalidOperationException($"La transferencia {t.DocNum} del LPN {text} está anulada en SAP.");
        if (!string.Equals(t.WarehouseCode, g.WarehouseCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"El LPN {text} va a la bodega {t.WarehouseCode} ({t.WarehouseName}), no a la sucursal de esta agrupación.");
        if ((await OccupiedAsync(new[] { t.DocEntry })).TryGetValue(t.DocEntry, out var reason))
            throw new InvalidOperationException($"El LPN {text} {reason}.");

        g.Transfers.Add(ToLine(t));
        Touch(g);
        await _db.SaveChangesAsync();
        return $"LPN {text} agregado (transferencia {t.DocNum}, {t.Units:0.##} unidades).";
    }

    public async Task RemoveTransferAsync(long groupId, long transferId)
    {
        var g = await RequireArmedAsync(groupId);
        var line = g.Transfers.FirstOrDefault(t => t.Id == transferId)
            ?? throw new InvalidOperationException("La transferencia no está en esta agrupación.");
        _db.DispatchGroupTransfers.Remove(line);
        Touch(g);
        await _db.SaveChangesAsync();
    }

    public async Task AnnulAsync(long groupId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Indique el motivo de la anulación.");
        var g = await RequireArmedAsync(groupId);
        g.Status = DispatchGroupStatuses.Annulled;
        g.AnnulReason = reason.Trim().Length > 200 ? reason.Trim()[..200] : reason.Trim();
        Touch(g);
        await _db.SaveChangesAsync();
    }

    private void Touch(DispatchGroup g)
    {
        g.UpdatedAt = UtcNow;
        g.UpdatedBy = _user.UserId;
    }

    private async Task<DispatchGroup> RequireArmedAsync(long groupId)
    {
        // Solo agrupaciones de traslados: las de Entregas se editan con IDeliveryGroupService.
        var g = await GetAsync(groupId) is { Kind: DispatchGroupKinds.Transfer } found
            ? found : throw new InvalidOperationException("La agrupación no existe.");
        var code = DispatchGroupCode.Format(g.Number);
        return g.Status switch
        {
            DispatchGroupStatuses.Armed => g,
            DispatchGroupStatuses.InManifest => throw new InvalidOperationException(
                $"La agrupación {code} ya está en un manifiesto; quítela del manifiesto para modificarla."),
            DispatchGroupStatuses.Dispatched => throw new InvalidOperationException($"La agrupación {code} está despachada."),
            _ => throw new InvalidOperationException($"La agrupación {code} está anulada."),
        };
    }

    /// <summary>
    /// Transferencias ocupadas (DocEntry → motivo): en una agrupación activa o en un manifiesto no anulado sin marcar
    /// "no entregado" (misma regla que el manifiesto).
    /// </summary>
    private async Task<Dictionary<int, string>> OccupiedAsync(IEnumerable<int> docEntries)
    {
        var entries = docEntries.Distinct().ToList();
        var result = new Dictionary<int, string>();
        if (entries.Count == 0) return result;

        var holding = DispatchGroupStatuses.Holding.ToArray();
        var inGroups = await (from t in _db.DispatchGroupTransfers
                              join g in _db.DispatchGroups on t.DispatchGroupId equals g.Id
                              where t.CompanyId == CompanyId && g.CompanyId == CompanyId && entries.Contains(t.SapDocEntry)
                                    && g.Kind == DispatchGroupKinds.Transfer && holding.Contains(g.Status)
                              select new { t.SapDocEntry, g.Number }).ToListAsync();
        foreach (var x in inGroups) result.TryAdd(x.SapDocEntry, $"ya está en la agrupación {DispatchGroupCode.Format(x.Number)}");

        var inManifests = await (from d in _db.ManifestDocuments
                                 join m in _db.Manifests on d.ManifestId equals m.Id
                                 where d.CompanyId == CompanyId && m.CompanyId == CompanyId
                                       && d.DocumentKind == DocumentKinds.Transfer && entries.Contains(d.SapDocEntry)
                                       && m.Status != ManifestStatuses.Annulled && d.DeliveryStatus != DeliveryStatuses.NotDelivered
                                 select new { d.SapDocEntry, m.Number }).ToListAsync();
        foreach (var x in inManifests) result.TryAdd(x.SapDocEntry, $"ya está en el manifiesto N° {x.Number}");
        return result;
    }
}
