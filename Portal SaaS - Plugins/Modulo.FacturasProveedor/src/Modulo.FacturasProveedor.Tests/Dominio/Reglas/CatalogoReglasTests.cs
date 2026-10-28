using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Dominio.Reglas.Catalogo;
using static Modulo.FacturasProveedor.Tests.Dominio.Reglas.RuleTestData;

namespace Modulo.FacturasProveedor.Tests.Dominio.Reglas;

public class CatalogoReglasTests
{
    [Fact]
    public void OcReferenciada_SinOc_ObjecionConDias()
    {
        var rule = new OcReferenciadaRule();

        var r = rule.Evaluate(Ctx(pos: Array.Empty<PurchaseOrderFacts>()), Settings(rule));

        Assert.Equal(RuleOutcomeKind.Objection, r.Kind);
        Assert.Null(r.Action); // usa la acción configurada (avisar y rechazar)
        Assert.Contains("no referencia una OC existente en SAP", r.Message);
        Assert.Equal(RuleActions.WarnThenReject, rule.DefaultAction);
        Assert.Equal(3, rule.DefaultActionDays);
    }

    [Fact]
    public void OcReferenciada_ConOc_Cumple()
    {
        var rule = new OcReferenciadaRule();
        Assert.Equal(RuleOutcomeKind.Pass, rule.Evaluate(Ctx(), Settings(rule)).Kind);
    }

    [Fact]
    public void OcReferenciada_Ambigua_ForzaRevision()
    {
        var rule = new OcReferenciadaRule();

        var r = rule.Evaluate(Ctx(ambiguous: new[] { 4512 }), Settings(rule));

        Assert.Equal(RuleOutcomeKind.Objection, r.Kind);
        Assert.Equal(RuleActions.ManualReview, r.Action);
        Assert.Contains("4512", r.Message);
        Assert.Contains("más de una serie", r.Message);
    }

    [Fact]
    public void OcMismoProveedor_RutEnOtroFormato_Cumple()
    {
        var rule = new OcMismoProveedorRule();
        var ctx = Ctx(invoice: Invoice() with { IssuerTaxId = "76.412.553-k" }, pos: new[] { Po(taxId: "76412553-K") });

        Assert.Equal(RuleOutcomeKind.Pass, rule.Evaluate(ctx, Settings(rule)).Kind);
    }

    [Fact]
    public void OcMismoProveedor_OtroRut_Rechaza()
    {
        var rule = new OcMismoProveedorRule();

        var r = rule.Evaluate(Ctx(pos: new[] { Po(taxId: "77555010-K") }), Settings(rule));

        Assert.Equal(RuleOutcomeKind.Objection, r.Kind);
        Assert.Equal(RuleActions.Reject, rule.DefaultAction);
    }

    [Fact]
    public void OcMismoProveedor_OcSinRut_ComparaCardCode()
    {
        var rule = new OcMismoProveedorRule();
        Assert.Equal(RuleOutcomeKind.Pass, rule.Evaluate(Ctx(pos: new[] { Po(taxId: null) }), Settings(rule)).Kind);
        Assert.Equal(RuleOutcomeKind.Objection, rule.Evaluate(Ctx(pos: new[] { Po(taxId: null, cardCode: "P999") }), Settings(rule)).Kind);
    }

    [Fact]
    public void OcAntiguedad_31Dias_Rechaza_30Cumple()
    {
        var rule = new OcAntiguedadRule();
        var issue = new DateOnly(2026, 10, 23);

        var ok = rule.Evaluate(Ctx(invoice: Invoice(issueDate: issue), pos: new[] { Po(docDate: issue.AddDays(-30)) }), Settings(rule));
        var old = rule.Evaluate(Ctx(invoice: Invoice(issueDate: issue), pos: new[] { Po(docDate: issue.AddDays(-31)) }), Settings(rule));

        Assert.Equal(RuleOutcomeKind.Pass, ok.Kind);
        Assert.Equal(RuleOutcomeKind.Objection, old.Kind);
        Assert.Contains("31 días", old.Message);
    }

    [Fact]
    public void OcSaldo_ParcialDentroDelSaldo_Cumple()
    {
        var rule = new OcSaldoRule();
        Assert.Equal(RuleOutcomeKind.Pass, rule.Evaluate(Ctx(invoice: Invoice(total: 2_200_000)), Settings(rule)).Kind);
    }

    [Fact]
    public void OcSaldo_SuperaSaldo_Rechaza()
    {
        var rule = new OcSaldoRule();

        var r = rule.Evaluate(Ctx(invoice: Invoice(total: 2_380_000)), Settings(rule));

        Assert.Equal(RuleOutcomeKind.Objection, r.Kind);
        Assert.Contains("2.200.000", r.Message);
        Assert.Contains("\"balance\":2200000", r.DataJson);
        Assert.Contains("\"invoiceTotal\":2380000", r.DataJson);
    }

    [Fact]
    public void OcSaldo_Tolerancia1Pct_Cumple()
    {
        var rule = new OcSaldoRule();
        var settings = Settings(rule, parameters: new Dictionary<string, decimal> { ["tolerance_pct"] = 1, ["tolerance_amount"] = 0 });

        Assert.Equal(RuleOutcomeKind.Pass, rule.Evaluate(Ctx(invoice: Invoice(total: 2_222_000)), settings).Kind);
    }

    [Fact]
    public void OcSaldo_VariasOc_SumaSaldos()
    {
        var rule = new OcSaldoRule();
        var ctx = Ctx(invoice: Invoice(total: 3_000_000), pos: new[] { Po(4512), Po(4513, alreadyInvoiced: 2_400_000) });

        Assert.Equal(RuleOutcomeKind.Pass, rule.Evaluate(ctx, Settings(rule)).Kind); // 2.200.000 + 800.000
    }

    [Fact]
    public void OcAbierta_Cerrada_Objecion_YVieneInactiva()
    {
        var rule = new OcAbiertaRule();
        Assert.False(rule.DefaultActive);
        Assert.Equal(RuleOutcomeKind.Objection, rule.Evaluate(Ctx(pos: new[] { Po(status: "closed") }), Settings(rule)).Kind);
    }

    [Fact]
    public void ProveedorEnSap_NoExiste_Revision()
    {
        var rule = new ProveedorEnSapRule();

        var r = rule.Evaluate(Ctx(supplier: Supplier(exists: false)), Settings(rule));

        Assert.Equal(RuleOutcomeKind.Objection, r.Kind);
        Assert.Equal(RuleActions.ManualReview, rule.DefaultAction);
        Assert.Equal(RuleScope.AllSuppliers, rule.Scope);
    }

    [Fact]
    public void RevisionManual_GastoFijo_Objecion_YNoSeDesactiva()
    {
        var rule = new RevisionManualProveedorRule();
        var profile = new SupplierProfileFacts(SupplierConditions.FixedMonthly, new Dictionary<string, RuleOverrideFacts>(), 7);

        Assert.Equal(RuleOutcomeKind.Objection, rule.Evaluate(Ctx(supplier: Supplier(profile)), Settings(rule)).Kind);
        Assert.Equal(RuleOutcomeKind.Pass, rule.Evaluate(Ctx(), Settings(rule)).Kind);
        Assert.False(rule.CanBeDisabled);
    }

    [Fact]
    public void PlazoPorVencer_EsTemporal()
    {
        var rule = new PlazoPorVencerRule();
        Assert.Equal(RuleScope.Pending, rule.Scope);
        Assert.Equal(RuleActions.Reject, rule.DefaultAction);
        Assert.Equal(new[] { RuleActions.Accept, RuleActions.Reject }, rule.AllowedActions);
        Assert.Equal(RuleOutcomeKind.NotApplicable, rule.Evaluate(Ctx(), Settings(rule)).Kind);
    }

    [Fact]
    public void Catalogo_CodigosYOrdenDelSpec()
    {
        Assert.Equal(
            new[] { "oc-referenciada", "oc-mismo-proveedor", "oc-antiguedad", "oc-saldo", "oc-abierta", "proveedor-en-sap",
                    "revision-manual-proveedor", "plazo-por-vencer" },
            Catalog().OrderBy(r => r.DefaultOrder).Select(r => r.Code));
        Assert.All(Catalog(), r => Assert.Contains(r.DefaultAction, r.AllowedActions));
    }
}
