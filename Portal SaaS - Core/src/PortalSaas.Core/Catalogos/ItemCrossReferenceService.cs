using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Sap;

namespace PortalSaas.Core.Catalogos;

/// <summary>
/// Paridad SKU-cliente ↔ artículo SAP -- portado de ParidadCatalogoService en
/// referencia-original/PortalSAP_v2. SyncAsync borra todo lo existente del cliente y
/// crea lo nuevo (mismo criterio que la referencia: más simple que un diff, y el
/// volumen por cliente es chico). A diferencia de los demás catálogos de este
/// namespace (solo lectura vía IHanaService), este necesita crear/borrar filas -- usa
/// Service Layer (ISapConnectionProvider), el recurso estándar AlternateCatNum no
/// tiene un camino de escritura directo a HANA razonable.
/// </summary>
public sealed class ItemCrossReferenceService : IItemCrossReferenceService
{
    private const int BatchSize = 20;

    private readonly ISapConnectionProvider _sap;
    private readonly ILogger<ItemCrossReferenceService> _logger;

    public ItemCrossReferenceService(ISapConnectionProvider sap, ILogger<ItemCrossReferenceService> logger)
    {
        _sap = sap;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ItemCrossReferenceDto>> ListAsync(string customerCardCode, CancellationToken ct = default)
    {
        var session = await _sap.GetConnectionAsync(ct);
        var filter = ODataFilterHelper.Eq("CardCode", customerCardCode);
        var records = await session.GetAsync<List<SapAlternateCatNum>>("AlternateCatNum", filter, ct: ct) ?? [];

        return records
            .Select(r => new ItemCrossReferenceDto(r.Substitute ?? "", r.Description, r.U_GSP_CATALOGDESC, r.ItemCode ?? ""))
            .ToList();
    }

    public async Task<(int Deleted, int Created)> SyncAsync(string customerCardCode, IReadOnlyList<ItemCrossReferenceDto> items, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(customerCardCode))
        {
            throw new ArgumentException("El código de cliente no puede estar vacío.", nameof(customerCardCode));
        }

        if (items.Count == 0)
        {
            return (0, 0);
        }

        var session = await _sap.GetConnectionAsync(ct);

        var uniqueSkus = items
            .Where(i => !string.IsNullOrWhiteSpace(i.Sku))
            .Select(i => i.Sku.Trim())
            .Distinct()
            .ToList();

        var recordsToDelete = new List<SapAlternateCatNum>();
        for (var i = 0; i < uniqueSkus.Count; i += BatchSize)
        {
            var batch = uniqueSkus.Skip(i).Take(BatchSize).ToList();
            var skuCondition = string.Join(" or ", batch.Select(sku => ODataFilterHelper.Eq("Substitute", sku)));
            var filter = $"{ODataFilterHelper.Eq("CardCode", customerCardCode)} and ({skuCondition})";

            var found = await session.GetAsync<List<SapAlternateCatNum>>("AlternateCatNum", filter, ct: ct);
            if (found is not null)
            {
                recordsToDelete.AddRange(found);
            }
        }

        var deleted = 0;
        foreach (var record in recordsToDelete)
        {
            try
            {
                var key = $"AlternateCatNum(ItemCode='{ODataFilterHelper.Escape(record.ItemCode)}'," +
                    $"CardCode='{ODataFilterHelper.Escape(record.CardCode)}',Substitute='{ODataFilterHelper.Escape(record.Substitute)}')";
                await session.DeleteAsync(key, ct);
                deleted++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar la paridad {Substitute} del cliente {CustomerCardCode}.", record.Substitute, customerCardCode);
            }
        }

        var created = 0;
        foreach (var item in items.Where(i => !string.IsNullOrWhiteSpace(i.Sku) && !string.IsNullOrWhiteSpace(i.ItemCode)))
        {
            try
            {
                var body = new
                {
                    ItemCode = item.ItemCode.Trim(),
                    CardCode = customerCardCode.Trim(),
                    Substitute = item.Sku.Trim(),
                    Description = item.CustomerDescription?.Trim() ?? "",
                    U_GSP_CATALOGDESC = item.Department?.Trim() ?? "",
                };
                await session.PostAsync<object>("AlternateCatNum", body, ct);
                created++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear la paridad {Sku} del cliente {CustomerCardCode}.", item.Sku, customerCardCode);
            }
        }

        return (deleted, created);
    }
}
