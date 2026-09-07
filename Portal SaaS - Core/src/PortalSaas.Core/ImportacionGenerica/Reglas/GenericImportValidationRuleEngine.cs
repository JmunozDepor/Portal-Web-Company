using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

public interface IGenericImportValidationRuleEngine
{
    /// <summary>
    /// Corre las reglas estructurales fijas (siempre, Severity = Block) + las
    /// configurables activas de `assignments` (respetando su Severity), y devuelve las
    /// filas con Errors/Warnings/IsValid actualizados. Una regla no aplicable al
    /// `module` recibido no corre aunque esté en `assignments` -- defensa en
    /// profundidad, el motor no confía ciegamente en la configuración guardada.
    /// </summary>
    Task<IReadOnlyList<GenericImportRowDto>> ApplyAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyList<GenericImportValidationRuleAssignmentDto> assignments, CancellationToken ct = default);
}

/// <summary>Ver IGenericImportValidationRuleEngine. Recibe TODAS las implementaciones de IGenericImportValidationRule vía DI (IEnumerable, ver Program.cs).</summary>
public sealed class GenericImportValidationRuleEngine : IGenericImportValidationRuleEngine
{
    private readonly IReadOnlyList<IGenericImportValidationRule> _allRules;

    public GenericImportValidationRuleEngine(IEnumerable<IGenericImportValidationRule> allRules)
    {
        _allRules = allRules.ToList();
    }

    public async Task<IReadOnlyList<GenericImportRowDto>> ApplyAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyList<GenericImportValidationRuleAssignmentDto> assignments, CancellationToken ct = default)
    {
        var errorsByRow = new Dictionary<int, List<string>>();
        var warningsByRow = new Dictionary<int, List<string>>();

        void AddMessages(IReadOnlyDictionary<int, IReadOnlyList<string>> messages, bool isBlock)
        {
            var target = isBlock ? errorsByRow : warningsByRow;
            foreach (var (rowNumber, list) in messages)
            {
                if (!target.TryGetValue(rowNumber, out var bucket))
                {
                    bucket = [];
                    target[rowNumber] = bucket;
                }
                bucket.AddRange(list);
            }
        }

        foreach (var rule in _allRules.Where(r => !r.SeverityIsConfigurable && r.ApplicableModules.Contains(module)))
        {
            var messages = await rule.ValidateAsync(rows, module, new Dictionary<string, object?>(), ct);
            AddMessages(messages, isBlock: true);
        }

        foreach (var assignment in assignments.Where(a => a.IsActive))
        {
            var rule = _allRules.FirstOrDefault(r => r.SeverityIsConfigurable && r.RuleType == assignment.RuleType);
            if (rule is null || !rule.ApplicableModules.Contains(module))
            {
                continue;
            }

            var messages = await rule.ValidateAsync(rows, module, assignment.Parameters, ct);
            AddMessages(messages, isBlock: assignment.Severity == GenericImportValidationSeverity.Block);
        }

        return rows.Select(row =>
        {
            var newErrors = errorsByRow.TryGetValue(row.RowNumber, out var errs) ? row.Errors.Concat(errs).ToList() : row.Errors;
            var newWarnings = warningsByRow.TryGetValue(row.RowNumber, out var warns) ? (IReadOnlyList<string>)warns : row.Warnings;
            return newErrors == row.Errors && newWarnings == row.Warnings
                ? row
                : row with { Errors = newErrors, Warnings = newWarnings, IsValid = newErrors.Count == 0 };
        }).ToList();
    }
}
