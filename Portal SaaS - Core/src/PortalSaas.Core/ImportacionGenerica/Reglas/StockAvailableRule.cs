using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>
/// Demanda agregada de TODO el archivo (suma de Quantity de todas las filas, de
/// TODOS los documentos) por (ItemCode, Bodega) vs. disponible real (OnHand -
/// IsCommited). Bodega = Warehouse en Venta/Compra, SourceWarehouse en Inventario
/// (el que efectivamente descuenta stock -- DestinationWarehouse no se valida).
/// "tolerancePercent" se aplica sobre la DEMANDA (no sobre el precio, a diferencia de
/// las reglas de precio) -- ej. 10% permite que la demanda supere el disponible hasta
/// en un 10% sin marcar la fila.
/// </summary>
public sealed class StockAvailableRule : IGenericImportValidationRule
{
    private readonly IItemStockService _stock;

    public StockAvailableRule(IItemStockService stock)
    {
        _stock = stock;
    }

    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.StockAvailable;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = Enum.GetValues<GenericImportModule>();
    public bool SeverityIsConfigurable => true;

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var tolerancePercent = parameters.TryGetValue("tolerancePercent", out var t) && t is not null ? Convert.ToDecimal(t) : 0m;

        string? ResolveWarehouse(GenericImportRowDto row) => module == GenericImportModule.Inventory ? row.SourceWarehouse : row.Warehouse;

        var relevantRows = rows.Where(r => r.ItemCode is not null && ResolveWarehouse(r) is not null && r.Quantity is not null).ToList();
        if (relevantRows.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }

        var demandaPorPar = relevantRows
            .GroupBy(r => (ItemCode: r.ItemCode!, Warehouse: ResolveWarehouse(r)!), TupleComparer.Instance)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity!.Value), TupleComparer.Instance);

        var disponible = await _stock.GetAvailableStockAsync(
            demandaPorPar.Keys.Select(k => (k.ItemCode, WhsCode: k.Warehouse)).ToList(), ct);

        var pares_con_deficit = new HashSet<(string ItemCode, string Warehouse)>(TupleComparer.Instance);
        foreach (var (clave, demanda) in demandaPorPar)
        {
            var stockDisponible = disponible.GetValueOrDefault((clave.ItemCode, clave.Warehouse), 0m);
            var tolerancia = demanda * (tolerancePercent / 100m);
            if (demanda > stockDisponible + tolerancia)
            {
                pares_con_deficit.Add(clave);
            }
        }

        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var row in relevantRows)
        {
            var clave = (ItemCode: row.ItemCode!, Warehouse: ResolveWarehouse(row)!);
            if (!pares_con_deficit.Contains(clave))
            {
                continue;
            }

            var demanda = demandaPorPar[clave];
            var stockDisponible = disponible.GetValueOrDefault((clave.ItemCode, clave.Warehouse), 0m);
            result[row.RowNumber] =
            [
                $"Demanda total de \"{clave.ItemCode}\" en bodega \"{clave.Warehouse}\": {demanda:N2} -- disponible: {stockDisponible:N2} (faltan {demanda - stockDisponible:N2}).",
            ];
        }
        return result;
    }

    private sealed class TupleComparer : IEqualityComparer<(string ItemCode, string Warehouse)>
    {
        public static readonly TupleComparer Instance = new();
        public bool Equals((string ItemCode, string Warehouse) x, (string ItemCode, string Warehouse) y) =>
            string.Equals(x.ItemCode, y.ItemCode, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Warehouse, y.Warehouse, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string ItemCode, string Warehouse) obj) =>
            HashCode.Combine(obj.ItemCode.ToUpperInvariant(), obj.Warehouse.ToUpperInvariant());
    }
}
