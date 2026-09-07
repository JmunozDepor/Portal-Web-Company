using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>Descuento entre 0 y 100 -- estructural, siempre activa, severidad fija en Block.</summary>
public sealed class ValidDiscountPercentRule : IGenericImportValidationRule
{
    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.ValidDiscountPercent;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = [GenericImportModule.Sales, GenericImportModule.Purchase];
    public bool SeverityIsConfigurable => false;

    public Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var row in rows)
        {
            if (row.DiscountPercent is { } discount && (discount < 0 || discount > 100))
            {
                result[row.RowNumber] = ["PorcentajeDescuento debe estar entre 0 y 100."];
            }
        }
        return Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<string>>>(result);
    }
}
