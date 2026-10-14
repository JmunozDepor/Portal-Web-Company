using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica;
using PortalSaas.Core.ImportacionGenerica.Reglas;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica;

/// <summary>Análisis de la vista previa (Resumen por OC + paneles) y regla de descuento total de la OC.</summary>
public sealed class GenericImportAnalysisTests
{
    private static GenericImportRowDto Row(int rowNumber, string group, decimal quantity, decimal? price, decimal? sapPrice,
        string itemCode = "A1", string? branch = "1202", params GenericImportIssueDto[] issues) => new()
    {
        RowNumber = rowNumber,
        GroupingKey = group,
        CustomerReferenceNumber = group,
        BusinessPartnerCardCode = "C1",
        Branch = branch,
        ItemCode = itemCode,
        ItemName = "Artículo " + itemCode,
        Warehouse = "W1",
        Quantity = quantity,
        UnitPrice = price,
        SapReferencePrice = sapPrice,
        IsValid = !issues.Any(i => i.IsError),
        Errors = issues.Where(i => i.IsError).Select(i => i.Message).ToList(),
        Warnings = issues.Where(i => !i.IsError).Select(i => i.Message).ToList(),
        Issues = issues,
    };

    private static IReadOnlyList<GenericImportDocumentDto> Documents(params GenericImportRowDto[] rows) =>
        rows.GroupBy(r => r.GroupingKey).Select(g => new GenericImportDocumentDto { GroupingKey = g.Key, Rows = g.ToList() }).ToList();

    // ------------------------------------------------ regla OrderTotalDiscount

    private static Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> RunOrderTotal(decimal maxDiscount, params GenericImportRowDto[] rows) =>
        new OrderTotalDiscountRule().ValidateAsync(rows, GenericImportModule.Sales,
            new Dictionary<string, object?> { ["maxDiscountPercent"] = maxDiscount }, CancellationToken.None);

    [Fact]
    public async Task Descuento_total_de_la_OC_sobre_el_maximo_marca_todas_sus_lineas()
    {
        // OC-1: SAP 2×100 + 1×100 = 300, OC 2×80 + 1×90 = 250 -> 16,67% > 10%.
        // OC-2: SAP 100, OC 95 -> 5%, dentro del máximo.
        var result = await RunOrderTotal(10m,
            Row(1, "OC-1", 2, 80, 100), Row(2, "OC-1", 1, 90, 100), Row(3, "OC-2", 1, 95, 100));

        Assert.Equal([1, 2], result.Keys.Order());
        Assert.Contains("16,67%", result[1][0].Replace('.', ','));
    }

    [Fact]
    public async Task Una_linea_con_mucho_descuento_no_bloquea_si_el_total_esta_dentro()
    {
        // Línea 1 con 30% de descuento, pero el total de la OC queda en 6% (SAP 1000, OC 940).
        var result = await RunOrderTotal(10m, Row(1, "OC-1", 1, 70, 100), Row(2, "OC-1", 9, 96.67m, 100));

        Assert.Empty(result);
    }

    [Fact]
    public async Task Lineas_sin_precio_SAP_no_cuentan_en_el_total()
    {
        var result = await RunOrderTotal(10m, Row(1, "OC-1", 1, 50, null), Row(2, "OC-1", 1, 100, 100));

        Assert.Empty(result);
    }

    // ------------------------------------------------ análisis

    [Fact]
    public void Resumen_por_OC_con_totales_y_descuento()
    {
        var analysis = GenericImportAnalysisBuilder.Build(Documents(
            Row(1, "OC-1", 2, 80, 100), Row(2, "OC-1", 1, 90, 100), Row(3, "OC-2", 3, 10, null)), GenericImportModule.Sales);

        Assert.Equal(2, analysis.Totals.Orders);
        Assert.Equal(6m, analysis.Totals.Units);
        Assert.Equal(280m, analysis.Totals.OrderAmount);
        Assert.Equal(300m, analysis.Totals.SapAmount);

        var oc1 = analysis.Orders.Single(o => o.GroupingKey == "OC-1");
        Assert.Equal(250m, oc1.OrderAmount);
        Assert.Equal(16.67m, oc1.DiscountPercent);
        Assert.Null(analysis.Orders.Single(o => o.GroupingKey == "OC-2").DiscountPercent);
    }

    [Fact]
    public void Paneles_separan_sucursal_articulo_precio_y_otros()
    {
        var branch = new GenericImportIssueDto(GenericImportIssueCategory.Branch, false, "La sucursal \"ABC123\" no pertenece al cliente \"C1\".");
        var item = new GenericImportIssueDto(GenericImportIssueCategory.Item, true, "El código de barras \"5063\" no corresponde a ningún artículo.");
        var price = new GenericImportIssueDto(GenericImportIssueCategory.Price, false, "Precio importado distinto.");
        var orderTotal = new GenericImportIssueDto(GenericImportIssueCategory.OrderTotal, true, "Descuento total de la OC 20% supera el máximo.");

        var analysis = GenericImportAnalysisBuilder.Build(Documents(
            Row(1, "OC-1", 1, 18812, 21137, branch: "ABC123", issues: [branch, orderTotal]),
            Row(2, "OC-1", 2, 17795, null, itemCode: "X", issues: [item, orderTotal]),
            Row(3, "OC-1", 3, 19994, 19994, issues: [price, orderTotal]),
            Row(4, "OC-1", 1, 100, 110, itemCode: "B2", issues: [orderTotal])), GenericImportModule.Sales);

        var branchIssue = Assert.Single(analysis.BranchIssues);
        Assert.Equal("ABC123", branchIssue.Branch);

        Assert.Single(analysis.ItemIssues);

        // Comparación: solo líneas con los dos precios y distintos; la marcada por la regla primero.
        Assert.Equal([1, 4], analysis.PriceComparisons.Select(p => p.RowNumber).Order());
        Assert.All(analysis.PriceComparisons, p => Assert.False(p.OutOfTolerance));

        // El descuento total de la OC aparece una sola vez en "Otras", no una por línea.
        var other = Assert.Single(analysis.OtherIssues);
        Assert.Equal(GenericImportIssueCategory.OrderTotal, other.Category);
    }

    [Fact]
    public void Cumplimiento_agrupa_por_articulo_y_almacen()
    {
        var rows = new[]
        {
            Row(1, "OC-1", 3, 10, 10) with { StockAvailable = 4, FulfillableQuantity = 3 },
            Row(2, "OC-1", 2, 10, 10) with { StockAvailable = 4, FulfillableQuantity = 1 },
            Row(3, "OC-1", 5, 10, 10, itemCode: "B2") with { StockAvailable = 0, FulfillableQuantity = 0 },
        };

        var analysis = GenericImportAnalysisBuilder.Build(Documents(rows), GenericImportModule.Sales);

        Assert.Equal(2, analysis.Fulfillment.Count);
        var a1 = analysis.Fulfillment.Single(f => f.ItemCode == "A1");
        Assert.Equal(5m, a1.UnitsOrdered);
        Assert.Equal(4m, a1.UnitsFulfillable);
        Assert.Equal(80m, a1.FulfillmentPercent);
        Assert.Equal("B2", analysis.Fulfillment[0].ItemCode); // 0% primero
        Assert.Equal(4m, analysis.Totals.UnitsFulfillable);
    }

    // ------------------------------------------------ motor: categoría por regla

    [Fact]
    public async Task El_motor_categoriza_los_hallazgos_segun_la_regla()
    {
        var engine = new GenericImportValidationRuleEngine([new OrderTotalDiscountRule()]);
        var rows = new[] { Row(1, "OC-1", 1, 50, 100) };
        var assignments = new[]
        {
            new GenericImportValidationRuleAssignmentDto(0, GenericImportValidationRuleType.OrderTotalDiscount, GenericImportValidationSeverity.Block, true,
                new Dictionary<string, object?> { ["maxDiscountPercent"] = 10m }),
        };

        var result = await engine.ApplyAsync(rows, GenericImportModule.Sales, assignments);

        var issue = Assert.Single(result[0].Issues);
        Assert.Equal(GenericImportIssueCategory.OrderTotal, issue.Category);
        Assert.True(issue.IsError);
        Assert.False(result[0].IsValid);
    }

    private sealed class FailingRule : IGenericImportValidationRule
    {
        public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.CustomerBranchValid;
        public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = [GenericImportModule.Sales];
        public bool SeverityIsConfigurable => true;

        public Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(IReadOnlyList<GenericImportRowDto> rows,
            GenericImportModule module, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct) =>
            throw new InvalidOperationException("invalid column name: AddressType");
    }

    [Fact]
    public async Task Una_regla_que_falla_contra_SAP_avisa_sin_tumbar_la_vista_previa()
    {
        // Caso real (2026-10-14): CustomerBranchValid consultaba CRD1."AddressType" (no
        // existe, es "AdresType") y la excepción botaba toda la vista previa.
        var engine = new GenericImportValidationRuleEngine([new FailingRule()]);
        var assignments = new[]
        {
            new GenericImportValidationRuleAssignmentDto(0, GenericImportValidationRuleType.CustomerBranchValid, GenericImportValidationSeverity.Block, true,
                new Dictionary<string, object?>()),
        };

        var result = await engine.ApplyAsync([Row(1, "OC-1", 1, 10, 10)], GenericImportModule.Sales, assignments);

        Assert.True(result[0].IsValid);
        var warning = Assert.Single(result[0].Warnings);
        Assert.Contains("No se pudo ejecutar la validación", warning);
        Assert.Equal(GenericImportIssueCategory.Other, Assert.Single(result[0].Issues).Category);
    }
}
