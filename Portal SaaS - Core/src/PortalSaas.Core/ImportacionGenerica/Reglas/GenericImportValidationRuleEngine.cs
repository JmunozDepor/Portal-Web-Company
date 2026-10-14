using Microsoft.Extensions.Logging;
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

    private readonly ILogger<GenericImportValidationRuleEngine>? _logger;

    public GenericImportValidationRuleEngine(IEnumerable<IGenericImportValidationRule> allRules, ILogger<GenericImportValidationRuleEngine>? logger = null)
    {
        _allRules = allRules.ToList();
        _logger = logger;
    }

    /// <summary>Categoría del hallazgo según la regla que lo produjo -- decide su panel en el análisis.</summary>
    internal static GenericImportIssueCategory CategoryOf(GenericImportValidationRuleType ruleType) => ruleType switch
    {
        GenericImportValidationRuleType.PositiveQuantity => GenericImportIssueCategory.Quantity,
        GenericImportValidationRuleType.ValidDiscountPercent => GenericImportIssueCategory.Price,
        GenericImportValidationRuleType.CustomerActiveInSap => GenericImportIssueCategory.BusinessPartner,
        GenericImportValidationRuleType.ItemActiveInSap => GenericImportIssueCategory.Item,
        GenericImportValidationRuleType.PriceVsFixedList => GenericImportIssueCategory.Price,
        GenericImportValidationRuleType.PriceVsCustomerList => GenericImportIssueCategory.Price,
        GenericImportValidationRuleType.StockAvailable => GenericImportIssueCategory.Stock,
        GenericImportValidationRuleType.CustomerBranchValid => GenericImportIssueCategory.Branch,
        GenericImportValidationRuleType.OrderTotalDiscount => GenericImportIssueCategory.OrderTotal,
        GenericImportValidationRuleType.DuplicateOrderInSap => GenericImportIssueCategory.DuplicateOrder,
        _ => GenericImportIssueCategory.Other,
    };

    public async Task<IReadOnlyList<GenericImportRowDto>> ApplyAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyList<GenericImportValidationRuleAssignmentDto> assignments, CancellationToken ct = default)
    {
        var errorsByRow = new Dictionary<int, List<string>>();
        var warningsByRow = new Dictionary<int, List<string>>();
        var issuesByRow = new Dictionary<int, List<GenericImportIssueDto>>();

        void AddMessages(IReadOnlyDictionary<int, IReadOnlyList<string>> messages, bool isBlock, GenericImportValidationRuleType? ruleType)
        {
            var target = isBlock ? errorsByRow : warningsByRow;
            var category = ruleType is { } type ? CategoryOf(type) : GenericImportIssueCategory.Other;
            foreach (var (rowNumber, list) in messages)
            {
                if (!target.TryGetValue(rowNumber, out var bucket))
                {
                    bucket = [];
                    target[rowNumber] = bucket;
                }
                bucket.AddRange(list);

                if (!issuesByRow.TryGetValue(rowNumber, out var issueBucket))
                {
                    issueBucket = [];
                    issuesByRow[rowNumber] = issueBucket;
                }
                issueBucket.AddRange(list.Select(message => new GenericImportIssueDto(category, isBlock, message)));
            }
        }

        foreach (var rule in _allRules.Where(r => !r.SeverityIsConfigurable && r.ApplicableModules.Contains(module)))
        {
            var messages = await RunSafelyAsync(rule, rows, module, new Dictionary<string, object?>(), ct);
            AddMessages(messages, isBlock: true, rule.RuleType);
        }

        foreach (var assignment in assignments.Where(a => a.IsActive))
        {
            var rule = _allRules.FirstOrDefault(r => r.SeverityIsConfigurable && r.RuleType == assignment.RuleType);
            if (rule is null || !rule.ApplicableModules.Contains(module))
            {
                continue;
            }

            var messages = await RunSafelyAsync(rule, rows, module, assignment.Parameters, ct);
            var failed = messages is FailedRuleMessages;
            // Una regla que no pudo correr (ej. SAP rechazó su consulta) se informa como
            // ADVERTENCIA aunque esté en Bloqueante -- no es un hallazgo del archivo.
            AddMessages(messages, isBlock: !failed && assignment.Severity == GenericImportValidationSeverity.Block,
                failed ? (GenericImportValidationRuleType?)null : rule.RuleType);
        }

        return rows.Select(row =>
        {
            var newErrors = errorsByRow.TryGetValue(row.RowNumber, out var errs) ? row.Errors.Concat(errs).ToList() : row.Errors;
            var newWarnings = warningsByRow.TryGetValue(row.RowNumber, out var warns) ? (IReadOnlyList<string>)warns : row.Warnings;
            var newIssues = issuesByRow.TryGetValue(row.RowNumber, out var ruleIssues) ? row.Issues.Concat(ruleIssues).ToList() : row.Issues;
            return newErrors == row.Errors && newWarnings == row.Warnings
                ? row
                : row with { Errors = newErrors, Warnings = newWarnings, Issues = newIssues, IsValid = newErrors.Count == 0 };
        }).ToList();
    }

    /// <summary>Marca el resultado de una regla que falló al ejecutarse -- ver RunSafelyAsync.</summary>
    private sealed class FailedRuleMessages : Dictionary<int, IReadOnlyList<string>>;

    /// <summary>
    /// Una excepción dentro de una regla (ej. SAP rechaza su consulta) ya no tumba toda la
    /// vista previa (antes: página de error genérica): se registra en el log y se avisa
    /// como advertencia en todas las filas -- el análisis la muestra una sola vez por OC.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> RunSafelyAsync(IGenericImportValidationRule rule,
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        try
        {
            return await rule.ValidateAsync(rows, module, parameters, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "La regla de validación {RuleType} falló al ejecutarse.", rule.RuleType);
            IReadOnlyList<string> message = [$"No se pudo ejecutar la validación \"{rule.RuleType}\": {ex.Message}"];
            var failed = new FailedRuleMessages();
            foreach (var row in rows)
            {
                failed[row.RowNumber] = message;
            }
            return failed;
        }
    }
}
