using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>El ItemCode resuelto de la fila está Activo en SAP (OITM.validFor = 'Y').</summary>
public sealed class ItemActiveInSapRule : IGenericImportValidationRule
{
    private readonly IItemCatalogService _items;

    public ItemActiveInSapRule(IItemCatalogService items)
    {
        _items = items;
    }

    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.ItemActiveInSap;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = Enum.GetValues<GenericImportModule>();
    public bool SeverityIsConfigurable => true;

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var itemCodes = rows.Where(r => r.ItemCode is not null)
            .Select(r => r.ItemCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (itemCodes.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<string>>();
        }

        var activeStatus = await _items.GetActiveStatusAsync(itemCodes, ct);

        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var row in rows)
        {
            if (row.ItemCode is { } itemCode && activeStatus.TryGetValue(itemCode, out var isActive) && !isActive)
            {
                result[row.RowNumber] = [$"El artículo \"{itemCode}\" está inactivo en SAP."];
            }
        }
        return result;
    }
}
