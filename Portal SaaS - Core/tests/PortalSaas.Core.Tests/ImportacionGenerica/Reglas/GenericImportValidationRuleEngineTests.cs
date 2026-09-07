using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica.Reglas;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica.Reglas;

public sealed class GenericImportValidationRuleEngineTests
{
    private static GenericImportRowDto Row(int rowNumber, decimal? quantity = 1, decimal? discountPercent = null) => new()
    {
        RowNumber = rowNumber,
        GroupingKey = "G1",
        IsValid = true,
        Quantity = quantity,
        DiscountPercent = discountPercent,
    };

    [Fact]
    public async Task PositiveQuantityRule_marca_cantidad_no_positiva()
    {
        var rule = new PositiveQuantityRule();
        var rows = new[] { Row(1, quantity: 1), Row(2, quantity: 0), Row(3, quantity: null) };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?>(), CancellationToken.None);

        Assert.False(result.ContainsKey(1));
        Assert.True(result.ContainsKey(2));
        Assert.True(result.ContainsKey(3));
    }

    [Fact]
    public async Task ValidDiscountPercentRule_marca_descuento_fuera_de_rango()
    {
        var rule = new ValidDiscountPercentRule();
        var rows = new[] { Row(1, discountPercent: 10), Row(2, discountPercent: -1), Row(3, discountPercent: 101) };

        var result = await rule.ValidateAsync(rows, GenericImportModule.Sales, new Dictionary<string, object?>(), CancellationToken.None);

        Assert.False(result.ContainsKey(1));
        Assert.True(result.ContainsKey(2));
        Assert.True(result.ContainsKey(3));
    }
}
