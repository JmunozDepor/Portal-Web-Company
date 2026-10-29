using Modulo.Compras.Pages;
using PortalSaas.Abstractions.Modelos;
using Xunit;

namespace PortalSaas.Core.Tests.Compras;

public sealed class PurchaseDocumentRulesTests
{
    private static PurchaseOpenOrderLineDto Open(int docEntry = 10, int lineNum = 0, decimal open = 12, string cardCode = "P1",
        string docType = "I", string? itemCode = "A", string currency = "USD") => new()
    {
        DocEntry = docEntry, DocNum = 10005322, LineNum = lineNum, CardCode = cardCode, DocType = docType,
        ItemCode = itemCode, OpenQuantity = open, DocCurrency = currency,
    };

    private static PurchaseDocumentRules.CopiedLineCheck Check(int index, decimal quantity, string? itemCode = "A", int baseEntry = 10, int baseLine = 0) =>
        new(index, baseEntry, baseLine, itemCode, quantity);

    [Fact]
    public void TaxDocument_Vacio_EsValido() =>
        Assert.Empty(PurchaseDocumentRules.ValidateTaxDocument(null, null, null));

    [Fact]
    public void TaxDocument_SoloFolio_ExigePrefijoEIndicador()
    {
        var fields = PurchaseDocumentRules.ValidateTaxDocument(null, null, 1157501).Select(e => e.Field).ToList();

        Assert.Contains("FolioPrefix", fields);
        Assert.Contains("TaxIndicator", fields);
    }

    [Fact]
    public void TaxDocument_SoloPrefijo_ExigeNumero() =>
        Assert.Equal("FolioNumber", Assert.Single(PurchaseDocumentRules.ValidateTaxDocument("33", "43", null)).Field);

    [Fact]
    public void TaxDocument_Completo_EsValido() =>
        Assert.Empty(PurchaseDocumentRules.ValidateTaxDocument("33", "43", 1157501));

    [Fact]
    public void CopiedLines_LineaYaNoAbierta_Error()
    {
        var error = Assert.Single(PurchaseDocumentRules.ValidateCopiedLines("P1", DocumentLineType.Item, [Check(0, 1, baseLine: 5)], [Open()]));

        Assert.Equal(0, error.LineIndex);
        Assert.Contains("ya no está abierta", error.Message);
    }

    [Fact]
    public void CopiedLines_PedidoDeOtroProveedor_Error() =>
        Assert.Single(PurchaseDocumentRules.ValidateCopiedLines("P1", DocumentLineType.Item, [Check(0, 1)], [Open(cardCode: "OTRO")]));

    [Fact]
    public void CopiedLines_TipoDeLineaDistinto_Error() =>
        Assert.Single(PurchaseDocumentRules.ValidateCopiedLines("P1", DocumentLineType.Item, [Check(0, 1, itemCode: null)], [Open(docType: "S", itemCode: null)]));

    [Fact]
    public void CopiedLines_ArticuloDistinto_Error() =>
        Assert.Single(PurchaseDocumentRules.ValidateCopiedLines("P1", DocumentLineType.Item, [Check(0, 1, itemCode: "A")], [Open(itemCode: "B")]));

    [Fact]
    public void CopiedLines_SuperaPendiente_Error() =>
        Assert.Contains("12", Assert.Single(PurchaseDocumentRules.ValidateCopiedLines("P1", DocumentLineType.Item, [Check(0, 13)], [Open()])).Message);

    [Fact]
    public void CopiedLines_MismaLineaBaseDosVeces_SumaCantidades() =>
        Assert.Equal(1, Assert.Single(PurchaseDocumentRules.ValidateCopiedLines("P1", DocumentLineType.Item, [Check(0, 7), Check(1, 6)], [Open()])).LineIndex);

    [Fact]
    public void CopiedLines_ParcialDentroDelPendiente_Valida() =>
        Assert.Empty(PurchaseDocumentRules.ValidateCopiedLines("P1", DocumentLineType.Item, [Check(0, 5)], [Open()]));

    [Fact]
    public void PriceCheck_ExcluyeLineasCopiadas() =>
        Assert.Equal([0, 2], PurchaseDocumentRules.LinesSubjectToPriceCheck([null, 10, null]));

    [Fact]
    public void BasedOnComment_UnoYVarios()
    {
        Assert.Equal("Basado en Pedidos 10005322.", PurchaseDocumentRules.BasedOnComment([10005322]));
        Assert.Equal("Basado en Pedidos 10005322, 10005323.", PurchaseDocumentRules.BasedOnComment([10005322, 10005323]));
    }

    [Fact]
    public void CopySelection_MonedasDistintas_Error() =>
        Assert.NotNull(PurchaseDocumentRules.CheckCopySelection("P1", [Open(currency: "USD"), Open(docEntry: 11, currency: "CLP")]));

    [Fact]
    public void CopySelection_TiposDistintos_Error() =>
        Assert.NotNull(PurchaseDocumentRules.CheckCopySelection("P1", [Open(), Open(docEntry: 11, docType: "S")]));

    [Fact]
    public void CopySelection_OtroProveedor_Error() =>
        Assert.NotNull(PurchaseDocumentRules.CheckCopySelection("P1", [Open(cardCode: "OTRO")]));

    [Fact]
    public void CopySelection_Valida_Null() =>
        Assert.Null(PurchaseDocumentRules.CheckCopySelection("P1", [Open(), Open(docEntry: 11)]));
}
