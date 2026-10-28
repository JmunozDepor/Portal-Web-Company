namespace Modulo.FacturasProveedor.Dominio.Reglas;

/// <summary>
/// Motor de decisión (puro): aplica la excepción del proveedor, evalúa las reglas activas (salvo las temporales) en su
/// orden y elige la objeción más severa (rechazar &gt; avisar y rechazar &gt; revisión manual; en empate, la de menor
/// orden). Sin objeciones se aplica la acción final configurada. En "siempre revisión manual" la decisión pasa a ser de
/// una persona y lo calculado queda como recomendación.
/// </summary>
public sealed class InvoiceRuleEngine
{
    public InvoiceRuleEngine(IEnumerable<ISupplierInvoiceRule> rules) =>
        Catalog = rules.OrderBy(r => r.DefaultOrder).ToList();

    public IReadOnlyList<ISupplierInvoiceRule> Catalog { get; }

    public ISupplierInvoiceRule? Find(string code) => Catalog.FirstOrDefault(r => r.Code == code);

    public InvoiceDecision Decide(InvoiceEvaluationContext ctx, CompanyRuleConfiguration companyConfig)
    {
        var config = RuleSettingsResolver.Resolve(companyConfig, ctx.Supplier.Profile, Catalog);
        var ordered = Catalog
            .Where(r => r.Scope != RuleScope.Pending)
            .OrderBy(r => config.OrderByRule.TryGetValue(r.Code, out var o) ? o : r.DefaultOrder)
            .ToList();

        var results = new List<RuleResult>();
        (ISupplierInvoiceRule Rule, string Action, int? Days, string? Message)? winner = null;
        foreach (var rule in ordered)
        {
            var settings = config.ByRule[rule.Code];
            if (!settings.IsActive)
            {
                results.Add(new RuleResult(rule.Code, RuleOutcomeKind.NotApplicable, null, null, null));
                continue;
            }

            var outcome = rule.Evaluate(ctx, settings);
            var action = outcome.Kind == RuleOutcomeKind.Objection ? outcome.Action ?? settings.Action : null;
            results.Add(new RuleResult(rule.Code, outcome.Kind, action, outcome.Message, outcome.DataJson));

            if (action is not null && (winner is null || RuleActions.Severity(action) > RuleActions.Severity(winner.Value.Action)))
            {
                var days = action == RuleActions.WarnThenReject ? settings.ActionDays ?? rule.DefaultActionDays ?? 3 : (int?)null;
                winner = (rule, action, days, outcome.Message);
            }
        }

        var decided = winner?.Action ?? config.FinalActionWhenNoObjection;
        if (ctx.Supplier.Profile?.Condition == SupplierConditions.AlwaysManual && decided != RuleActions.ManualReview)
        {
            return new InvoiceDecision(RuleActions.ManualReview, decided, null, winner?.Rule.Code ?? "revision-manual-proveedor",
                winner?.Message ?? "Proveedor con revisión manual obligatoria.", results);
        }

        return new InvoiceDecision(decided, null, winner?.Days, winner?.Rule.Code, winner?.Message, results);
    }
}
