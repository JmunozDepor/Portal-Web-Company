using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>Cantidad &gt; 0 -- estructural, siempre activa, severidad fija en Block. Reemplaza al PositiveQuantityRule viejo (ImportacionGenerica/IGenericImportValidationRule.cs).</summary>
public sealed class PositiveQuantityRule : IGenericImportValidationRule
{
    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.PositiveQuantity;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = Enum.GetValues<GenericImportModule>();
    public bool SeverityIsConfigurable => false;

    public Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var row in rows)
        {
            if (row.Quantity is null or <= 0)
            {
                result[row.RowNumber] = ["Cantidad debe ser mayor a 0."];
            }
        }
        return Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<string>>>(result);
    }
}
