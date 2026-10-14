using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>
/// Arma GenericImportAnalysisDto (Resumen por OC + paneles de Análisis) a partir de los
/// documentos ya validados -- cálculo puro, sin SAP. Una sola fuente para la pantalla de
/// Importar y el Excel de validación (GenerateValidationReportAsync).
/// </summary>
internal static class GenericImportAnalysisBuilder
{
    private const string EmptyValue = "(vacío)";

    internal static GenericImportAnalysisDto Build(IReadOnlyList<GenericImportDocumentDto> documents, GenericImportModule module)
    {
        var rows = documents.SelectMany(d => d.Rows).ToList();
        string? WarehouseOf(GenericImportRowDto r) => module == GenericImportModule.Inventory ? r.SourceWarehouse : r.Warehouse;

        static string Distinct(IEnumerable<string?> values)
        {
            var list = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return string.Join(", ", list);
        }

        static decimal OrderAmount(IEnumerable<GenericImportRowDto> source) =>
            source.Where(r => r.UnitPrice is not null).Sum(r => (r.Quantity ?? 0m) * r.UnitPrice!.Value);

        static decimal SapAmount(IEnumerable<GenericImportRowDto> source) =>
            source.Where(r => r.SapReferencePrice is not null).Sum(r => (r.Quantity ?? 0m) * r.SapReferencePrice!.Value);

        var orders = documents.Select(document =>
        {
            var first = document.Rows[0];
            var comparable = document.Rows.Where(r => r.UnitPrice is not null && r.SapReferencePrice is not null).ToList();
            var comparableSap = SapAmount(comparable);
            return new GenericImportOrderSummaryDto
            {
                GroupingKey = document.GroupingKey,
                CustomerReferenceNumber = Distinct(document.Rows.Select(r => r.CustomerReferenceNumber)),
                BusinessPartnerCardCode = first.BusinessPartnerCardCode,
                BusinessPartnerName = first.BusinessPartnerName,
                Branches = Distinct(document.Rows.Select(r => r.Branch)),
                Lines = document.Rows.Count,
                LinesWithErrors = document.Rows.Count(r => !r.IsValid),
                LinesWithWarnings = document.Rows.Count(r => r.Warnings.Count > 0),
                Units = document.Rows.Sum(r => r.Quantity ?? 0m),
                OrderAmount = OrderAmount(document.Rows),
                SapAmount = SapAmount(document.Rows),
                DiscountPercent = comparableSap > 0 ? Math.Round((comparableSap - OrderAmount(comparable)) / comparableSap * 100m, 2) : null,
                UnitsFulfillable = document.Rows.Sum(r => r.FulfillableQuantity ?? 0m),
                CanCreate = document.CanCreate,
            };
        }).ToList();

        var totals = new GenericImportTotalsDto
        {
            Orders = documents.Count,
            Lines = rows.Count,
            LinesWithErrors = rows.Count(r => !r.IsValid),
            LinesWithWarnings = rows.Count(r => r.Warnings.Count > 0),
            Units = rows.Sum(r => r.Quantity ?? 0m),
            OrderAmount = OrderAmount(rows),
            SapAmount = SapAmount(rows),
            UnitsFulfillable = rows.Sum(r => r.FulfillableQuantity ?? 0m),
        };

        static bool Has(GenericImportRowDto row, GenericImportIssueCategory category) => row.Issues.Any(i => i.Category == category);
        static string Messages(IEnumerable<GenericImportRowDto> source, GenericImportIssueCategory category) =>
            string.Join(" ", source.SelectMany(r => r.Issues).Where(i => i.Category == category).Select(i => i.Message).Distinct());

        var branchIssues = rows.Where(r => Has(r, GenericImportIssueCategory.Branch))
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Branch) ? EmptyValue : r.Branch!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new GenericImportBranchIssueDto(g.Key, Distinct(g.Select(r => r.GroupingKey)), g.Count(), Messages(g, GenericImportIssueCategory.Branch)))
            .OrderBy(b => b.Branch)
            .ToList();

        var itemIssues = rows.Where(r => Has(r, GenericImportIssueCategory.Item))
            .GroupBy(r => r.RawValues.GetValueOrDefault(GenericImportLogicalField.ItemCode)?.Trim() ?? r.ItemCode ?? EmptyValue, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var sample = g.First();
                var description = sample.ItemName ?? sample.RawValues.GetValueOrDefault(GenericImportLogicalField.Description);
                return new GenericImportItemIssueDto(g.Key, description, Distinct(g.Select(r => r.GroupingKey)), g.Count(),
                    g.Sum(r => r.Quantity ?? 0m), Messages(g, GenericImportIssueCategory.Item));
            })
            .OrderBy(i => i.SourceCode)
            .ToList();

        var priceComparisons = rows
            .Where(r => r.UnitPrice is not null && r.SapReferencePrice is not null && r.UnitPrice != r.SapReferencePrice)
            .Select(r => new GenericImportPriceComparisonDto(r.RowNumber, r.GroupingKey, r.ItemCode, r.ItemName ?? r.Description,
                r.Quantity ?? 0m, r.UnitPrice!.Value, r.SapReferencePrice!.Value, Has(r, GenericImportIssueCategory.Price)))
            .OrderByDescending(p => p.OutOfTolerance)
            .ThenByDescending(p => Math.Abs(p.DifferencePercent ?? 0m))
            .ToList();

        var fulfillment = rows.Where(r => r.ItemCode is not null && WarehouseOf(r) is not null && r.StockAvailable is not null)
            .GroupBy(r => (ItemCode: r.ItemCode!, Warehouse: WarehouseOf(r)!))
            .Select(g => new GenericImportFulfillmentDto(g.Key.ItemCode, g.First().ItemName, g.Key.Warehouse,
                g.Sum(r => r.Quantity ?? 0m), g.First().StockAvailable!.Value, g.Sum(r => r.FulfillableQuantity ?? 0m)))
            .OrderBy(f => f.FulfillmentPercent)
            .ThenBy(f => f.ItemCode)
            .ToList();

        // Sucursal/Artículo/Precio tienen panel propio y el stock se ve en Cumplimiento --
        // el resto va acá, sin repetir el mismo mensaje en cada línea de la misma OC
        // (ej. el descuento total de la OC o el socio inactivo se marcan en todas).
        var panelCategories = new HashSet<GenericImportIssueCategory>
        {
            GenericImportIssueCategory.Branch, GenericImportIssueCategory.Item, GenericImportIssueCategory.Price, GenericImportIssueCategory.Stock,
            GenericImportIssueCategory.DuplicateOrder,
        };
        var otherIssues = rows
            .SelectMany(r => r.Issues.Where(i => !panelCategories.Contains(i.Category)).Select(i => (Row: r, Issue: i)))
            .GroupBy(x => (x.Row.GroupingKey, x.Issue.Category, x.Issue.Message))
            .Select(g => new GenericImportOtherIssueDto(g.First().Row.RowNumber, g.Key.GroupingKey, g.Key.Category, g.First().Issue.IsError, g.Key.Message))
            .OrderByDescending(o => o.IsError)
            .ThenBy(o => o.GroupingKey)
            .ThenBy(o => o.RowNumber)
            .ToList();

        var duplicateOrders = documents
            .Select(d => (Document: d, Issues: d.Rows.SelectMany(r => r.Issues).Where(i => i.Category == GenericImportIssueCategory.DuplicateOrder).ToList()))
            .Where(x => x.Issues.Count > 0)
            .Select(x => new GenericImportDuplicateOrderDto(x.Document.GroupingKey, Distinct(x.Document.Rows.Select(r => r.CustomerReferenceNumber)),
                x.Document.Rows.Count(r => r.Issues.Any(i => i.Category == GenericImportIssueCategory.DuplicateOrder)),
                x.Issues.Any(i => i.IsError), string.Join(" ", x.Issues.Select(i => i.Message).Distinct())))
            .ToList();

        return new GenericImportAnalysisDto
        {
            DuplicateOrders = duplicateOrders,
            Orders = orders,
            Totals = totals,
            BranchIssues = branchIssues,
            ItemIssues = itemIssues,
            PriceComparisons = priceComparisons,
            Fulfillment = fulfillment,
            OtherIssues = otherIssues,
        };
    }
}
