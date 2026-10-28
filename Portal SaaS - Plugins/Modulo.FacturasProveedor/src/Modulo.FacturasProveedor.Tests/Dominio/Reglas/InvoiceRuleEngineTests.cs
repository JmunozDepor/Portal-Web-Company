using Modulo.FacturasProveedor.Dominio.Reglas;
using static Modulo.FacturasProveedor.Tests.Dominio.Reglas.RuleTestData;

namespace Modulo.FacturasProveedor.Tests.Dominio.Reglas;

public class InvoiceRuleEngineTests
{
    private static readonly InvoiceRuleEngine Engine = new(Catalog());

    private static SupplierProfileFacts Profile(string condition, Dictionary<string, RuleOverrideFacts>? overrides = null) =>
        new(condition, overrides ?? new Dictionary<string, RuleOverrideFacts>(), 7);

    [Fact]
    public void Engine_RechazoGanaAAvisoYRevision()
    {
        // Sin OC resuelta pero con DocNum ambiguo (revisión forzada), y proveedor inexistente (revisión)... y una OC de
        // otro RUT (rechazo): manda el rechazo.
        var ctx = Ctx(supplier: Supplier(exists: false), pos: new[] { Po(taxId: "77555010-K") }, ambiguous: new[] { 9999 });

        var d = Engine.Decide(ctx, DefaultConfig());

        Assert.Equal(RuleActions.Reject, d.Action);
        Assert.Equal("oc-mismo-proveedor", d.WinningRuleCode);
        Assert.Null(d.RecommendedAction);
    }

    [Fact]
    public void Engine_AvisoGanaARevision_YTraeDias()
    {
        var ctx = Ctx(supplier: Supplier(exists: false), pos: Array.Empty<PurchaseOrderFacts>());

        var d = Engine.Decide(ctx, DefaultConfig());

        Assert.Equal(RuleActions.WarnThenReject, d.Action);
        Assert.Equal(3, d.WarnDays);
        Assert.Equal("oc-referenciada", d.WinningRuleCode);
    }

    [Fact]
    public void Engine_SinObjeciones_UsaAccionFinal()
    {
        var accept = Engine.Decide(Ctx(), DefaultConfig());
        var review = Engine.Decide(Ctx(), DefaultConfig(finalAction: RuleActions.ManualReview));

        Assert.Equal(RuleActions.Accept, accept.Action);
        Assert.Null(accept.WinningRuleCode);
        Assert.Equal(RuleActions.ManualReview, review.Action);
        Assert.DoesNotContain(accept.Results, r => r.RuleCode == "plazo-por-vencer");
    }

    [Fact]
    public void Engine_SiempreManual_RecomiendaYMandaARevision()
    {
        var ctx = Ctx(invoice: Invoice(total: 2_380_000), supplier: Supplier(Profile(SupplierConditions.AlwaysManual)));

        var d = Engine.Decide(ctx, DefaultConfig());

        Assert.Equal(RuleActions.ManualReview, d.Action);
        Assert.Equal(RuleActions.Reject, d.RecommendedAction);
        Assert.Contains(d.Results, r => r.RuleCode == "oc-saldo" && r.Kind == RuleOutcomeKind.Objection);
    }

    [Fact]
    public void Engine_GastoFijo_SinOc_VaARevision()
    {
        var ctx = Ctx(supplier: Supplier(Profile(SupplierConditions.FixedMonthly)), pos: Array.Empty<PurchaseOrderFacts>());

        var d = Engine.Decide(ctx, DefaultConfig());

        Assert.Equal(RuleActions.ManualReview, d.Action);
        Assert.Equal("revision-manual-proveedor", d.WinningRuleCode);
        Assert.Equal(RuleOutcomeKind.NotApplicable, d.Results.Single(r => r.RuleCode == "oc-referenciada").Kind);
    }

    [Fact]
    public void Engine_ReglaInactiva_NoObjeta()
    {
        var config = DefaultConfig();
        var byRule = config.ByRule.ToDictionary(kv => kv.Key, kv => kv.Value);
        byRule["oc-saldo"] = byRule["oc-saldo"] with { IsActive = false };

        var d = Engine.Decide(Ctx(invoice: Invoice(total: 9_000_000)), config with { ByRule = byRule });

        Assert.Equal(RuleActions.Accept, d.Action);
    }

    [Fact]
    public void Resolver_GastoFijo_DesactivaReglasDeOc()
    {
        var resolved = RuleSettingsResolver.Resolve(DefaultConfig(), Profile(SupplierConditions.FixedMonthly), Catalog());

        Assert.False(resolved.ByRule["oc-referenciada"].IsActive);
        Assert.False(resolved.ByRule["oc-saldo"].IsActive);
        Assert.True(resolved.ByRule["proveedor-en-sap"].IsActive);
        Assert.True(resolved.ByRule["plazo-por-vencer"].IsActive);
    }

    [Fact]
    public void Resolver_OverrideCustom_CambiaParametro()
    {
        var overrides = new Dictionary<string, RuleOverrideFacts>
        {
            ["oc-antiguedad"] = new("custom", null, null, new Dictionary<string, decimal> { ["max_days"] = 60 }),
            ["plazo-por-vencer"] = new("custom", RuleActions.Accept, null, null),
            ["oc-mismo-proveedor"] = new("not_applicable", null, null, null),
        };

        var resolved = RuleSettingsResolver.Resolve(DefaultConfig(), Profile(SupplierConditions.Custom, overrides), Catalog());

        Assert.Equal(60, resolved.ByRule["oc-antiguedad"].Parameters["max_days"]);
        Assert.Equal(RuleActions.Reject, resolved.ByRule["oc-antiguedad"].Action);
        Assert.Equal(RuleActions.Accept, resolved.ByRule["plazo-por-vencer"].Action);
        Assert.False(resolved.ByRule["oc-mismo-proveedor"].IsActive);
    }

    [Fact]
    public void Resolver_NoPermiteDesactivarRegla7()
    {
        var overrides = new Dictionary<string, RuleOverrideFacts> { ["revision-manual-proveedor"] = new("not_applicable", null, null, null) };
        var config = DefaultConfig();
        var byRule = config.ByRule.ToDictionary(kv => kv.Key, kv => kv.Value);
        byRule["revision-manual-proveedor"] = byRule["revision-manual-proveedor"] with { IsActive = false };

        var resolved = RuleSettingsResolver.Resolve(config with { ByRule = byRule }, Profile(SupplierConditions.FixedMonthly, overrides), Catalog());

        Assert.True(resolved.ByRule["revision-manual-proveedor"].IsActive);
    }
}
