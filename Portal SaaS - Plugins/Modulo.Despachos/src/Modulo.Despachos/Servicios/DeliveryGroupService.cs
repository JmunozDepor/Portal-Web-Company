using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Data;
using Modulo.Despachos.Dominio;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Servicios;

public class DeliveryGroupService : IDeliveryGroupService
{
    private readonly DespachosDbContext _db;
    private readonly ICurrentCompanyAccessor _company;
    private readonly ICurrentUserContext _user;
    private readonly ISapDocumentLookupService _sap;
    private readonly TimeProvider _time;

    public DeliveryGroupService(DespachosDbContext db, ICurrentCompanyAccessor company, ICurrentUserContext user,
        ISapDocumentLookupService sap, TimeProvider time)
    {
        _db = db;
        _company = company;
        _user = user;
        _sap = sap;
        _time = time;
    }

    private Guid CompanyId => _company.CompanyId;
    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    // ---------------------------------------------------------------- Agrupar

    public async Task<DeliveryCandidates> ListCandidatesAsync(string customer, DateOnly fromDate)
    {
        var code = await CustomerCodeAsync(customer);
        var deliveries = (await _sap.ListPendingDeliveriesAsync(code, fromDate)).Where(d => d.IsOpen).ToList();
        var occupied = await OccupiedAsync(deliveries.Select(d => d.DocEntry));
        var free = deliveries.Where(d => !occupied.ContainsKey(d.DocEntry))
            .OrderBy(d => d.DocumentDate).ThenBy(d => d.DocNum).ToList();

        var blocked = new List<BlockedDelivery>();
        var orders = new List<(long Po, SapDeliveryInfo Delivery)>();
        foreach (var d in free)
        {
            if (BlockReason(d, out var po) is { } reason) blocked.Add(new BlockedDelivery(d, reason));
            else orders.Add((po, d));
        }
        var first = deliveries.FirstOrDefault();
        return new DeliveryCandidates(
            first?.CustomerCode ?? code, first?.CustomerName ?? code, first?.CustomerTaxId,
            orders.GroupBy(x => x.Po).OrderBy(g => g.Key)
                .Select(g => new PoCandidates(g.Key, g.Select(x => x.Delivery.OrderDate).FirstOrDefault(o => o != null),
                    g.Select(x => x.Delivery).ToList()))
                .ToList(),
            blocked);
    }

    public async Task<IReadOnlyList<DispatchGroup>> GenerateAsync(DeliveryGroupInput input)
    {
        RequireTerms(input.TransferIndicator, input.DispatchType);
        var chosen = input.DocEntries.ToHashSet();
        var candidates = await ListCandidatesAsync(input.Customer, input.FromDate);
        var orders = candidates.Orders
            .Select(o => o with { Deliveries = o.Deliveries.Where(d => chosen.Contains(d.DocEntry)).ToList() })
            .Where(o => o.Deliveries.Count > 0)
            .ToList();
        if (orders.Count == 0)
            throw new InvalidOperationException("No hay Entregas libres para agrupar con la selección indicada.");

        var number = await _db.DispatchGroups.Where(g => g.CompanyId == CompanyId).MaxAsync(g => (int?)g.Number) ?? 0;
        var sequence = await _db.DispatchGroups.CountAsync(g => g.CompanyId == CompanyId && g.Kind == DispatchGroupKinds.Delivery
            && g.WarehouseCode == candidates.CustomerCode && g.DispatchDate == input.DispatchDate);
        var created = new List<DispatchGroup>();
        foreach (var o in orders)
        {
            var group = new DispatchGroup
            {
                CompanyId = CompanyId, Number = ++number, Kind = DispatchGroupKinds.Delivery,
                WarehouseCode = Cut(candidates.CustomerCode, 50), BranchName = Cut(candidates.CustomerName, 100),
                CustomerTaxId = candidates.CustomerTaxId is { } rut ? Cut(rut, 20) : null,
                CustomerPo = o.CustomerPo, CustomerPoDate = o.OrderDate,
                TransferIndicator = input.TransferIndicator, DispatchType = input.DispatchType,
                DispatchDate = input.DispatchDate, Sequence = ++sequence, Status = DispatchGroupStatuses.Armed,
                CreatedAt = UtcNow, CreatedBy = _user.UserId,
                Transfers = o.Deliveries.Select(ToLine).ToList(),
            };
            _db.DispatchGroups.Add(group);
            created.Add(group);
        }
        await _db.SaveChangesAsync();
        return created;
    }

    /// <summary>Código SAP del cliente: un RUT válido se busca en SAP; cualquier otro texto se usa como CardCode.</summary>
    private async Task<string> CustomerCodeAsync(string customer)
    {
        var text = (customer ?? "").Trim();
        if (text.Length == 0) throw new InvalidOperationException("Indique el cliente (RUT o código SAP).");
        if (!RutChileno.IsValid(text)) return text.ToUpperInvariant();
        var partner = await _sap.FindBusinessPartnerAsync(text)
            ?? throw new InvalidOperationException($"No se encontró el cliente {RutChileno.Normalize(text)} en SAP.");
        return partner.CardCode;
    }

    /// <summary>Motivo por el que la Entrega no se puede agrupar, o null con su OC numérica.</summary>
    private static string? BlockReason(SapDeliveryInfo d, out long po)
    {
        po = 0;
        if (string.IsNullOrWhiteSpace(d.CustomerPo)) return "La Entrega no tiene orden de compra (pedido sin N° de referencia).";
        if (d.PoCount > 1) return "La Entrega tiene líneas de más de una OC; sepárela en SAP.";
        // La referencia 801 de la guía lleva el folio numérico (FolioRef de la plataforma es long).
        if (!long.TryParse(d.CustomerPo.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out po) || po <= 0)
            return $"La OC «{d.CustomerPo.Trim()}» no es numérica.";
        return null;
    }

    private DispatchGroupTransfer ToLine(SapDeliveryInfo d) => new()
    {
        CompanyId = CompanyId, SapDocEntry = d.DocEntry, SapDocNum = d.DocNum,
        Lpn = d.Lpn is { Length: > 30 } l ? l[..30] : d.Lpn, Units = d.Units, DocumentDate = d.DocumentDate,
    };

    private static void RequireTerms(int transferIndicator, int dispatchType)
    {
        if (transferIndicator is < 1 or > 9) throw new InvalidOperationException("Indicador de traslado inválido.");
        if (dispatchType is < 1 or > 3) throw new InvalidOperationException("Tipo de despacho inválido.");
    }

    private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;

    // ---------------------------------------------------------------- Edición

    public async Task<string> AddLpnAsync(long groupId, string lpn)
    {
        var g = await RequireArmedAsync(groupId);
        var text = (lpn ?? "").Trim().ToUpperInvariant();
        if (text.Length == 0) throw new InvalidOperationException("Escanee o digite un LPN.");
        var d = await _sap.FindDeliveryByLpnAsync(text)
            ?? throw new InvalidOperationException($"No se encontró el LPN {text} en SAP.");
        if (!d.IsOpen)
            throw new InvalidOperationException($"La Entrega {d.DocNum} del LPN {text} ya no está abierta en SAP (facturada, cerrada o anulada).");
        if (!string.Equals(d.CustomerCode, g.WarehouseCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"El LPN {text} es de otro cliente.");
        if (BlockReason(d, out var po) is { } reason) throw new InvalidOperationException($"El LPN {text}: {reason}");
        if (po != g.CustomerPo)
            throw new InvalidOperationException($"El LPN {text} es de la OC {po}, no de la OC {g.CustomerPo} de esta agrupación.");
        if ((await OccupiedAsync(new[] { d.DocEntry })).TryGetValue(d.DocEntry, out var busy))
            throw new InvalidOperationException($"El LPN {text} {busy}.");

        g.Transfers.Add(ToLine(d));
        Touch(g);
        await _db.SaveChangesAsync();
        return $"LPN {text} agregado (Entrega {d.DocNum}, {d.Units:0.##} unidades).";
    }

    public async Task RemoveDeliveryAsync(long groupId, long lineId)
    {
        var g = await RequireArmedAsync(groupId);
        var line = g.Transfers.FirstOrDefault(t => t.Id == lineId)
            ?? throw new InvalidOperationException("La Entrega no está en esta agrupación.");
        _db.DispatchGroupTransfers.Remove(line);
        Touch(g);
        await _db.SaveChangesAsync();
    }

    public async Task SetTermsAsync(long groupId, int transferIndicator, int dispatchType)
    {
        RequireTerms(transferIndicator, dispatchType);
        var g = await RequireArmedAsync(groupId);
        g.TransferIndicator = transferIndicator;
        g.DispatchType = dispatchType;
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
        var g = await _db.DispatchGroups.Include(x => x.Transfers)
                    .FirstOrDefaultAsync(x => x.CompanyId == CompanyId && x.Id == groupId && x.Kind == DispatchGroupKinds.Delivery)
                ?? throw new InvalidOperationException("La agrupación no existe.");
        var code = DispatchGroupCode.Format(g.Number, g.Kind);
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
    /// Entregas ocupadas (DocEntry → motivo): en una agrupación de entregas activa o como Entrega en un manifiesto no
    /// anulado sin marcar "no entregado". Solo cuenta Entregas: el DocEntry de una transferencia es otro documento.
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
                                    && g.Kind == DispatchGroupKinds.Delivery && holding.Contains(g.Status)
                              select new { t.SapDocEntry, g.Number }).ToListAsync();
        foreach (var x in inGroups)
            result.TryAdd(x.SapDocEntry, $"ya está en la agrupación {DispatchGroupCode.Format(x.Number, DispatchGroupKinds.Delivery)}");

        var inManifests = await (from d in _db.ManifestDocuments
                                 join m in _db.Manifests on d.ManifestId equals m.Id
                                 where d.CompanyId == CompanyId && m.CompanyId == CompanyId
                                       && d.DocumentKind == DocumentKinds.Delivery && entries.Contains(d.SapDocEntry)
                                       && m.Status != ManifestStatuses.Annulled && d.DeliveryStatus != DeliveryStatuses.NotDelivered
                                 select new { d.SapDocEntry, m.Number }).ToListAsync();
        foreach (var x in inManifests) result.TryAdd(x.SapDocEntry, $"ya está en el manifiesto N° {x.Number}");
        return result;
    }
}
