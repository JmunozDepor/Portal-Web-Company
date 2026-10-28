namespace Modulo.FacturasProveedor.Dominio.Reglas;

/// <summary>
/// Configuración efectiva para un proveedor: la de la empresa más su excepción. Gasto fijo mensual apaga las reglas de
/// OC; un ajuste "no aplica" apaga la regla; "custom" reemplaza acción, días o parámetros (fusionados por clave). Una
/// regla que no se puede desactivar (CanBeDisabled = false) queda siempre activa.
/// </summary>
public static class RuleSettingsResolver
{
    public static CompanyRuleConfiguration Resolve(
        CompanyRuleConfiguration company, SupplierProfileFacts? profile, IEnumerable<ISupplierInvoiceRule> catalog)
    {
        var byRule = new Dictionary<string, EffectiveRuleSettings>();
        foreach (var rule in catalog)
        {
            var s = company.ByRule.TryGetValue(rule.Code, out var configured) ? configured : Defaults(rule);

            if (profile is not null)
            {
                if (profile.Condition == SupplierConditions.FixedMonthly && rule.Scope == RuleScope.OcSuppliers)
                    s = s with { IsActive = false };

                if (profile.Overrides.TryGetValue(rule.Code, out var ov))
                {
                    if (ov.Mode == RuleOverrideModes.NotApplicable)
                    {
                        s = s with { IsActive = false };
                    }
                    else if (ov.Mode == RuleOverrideModes.Custom)
                    {
                        var parameters = s.Parameters.ToDictionary(kv => kv.Key, kv => kv.Value);
                        foreach (var (key, value) in ov.Parameters ?? new Dictionary<string, decimal>()) parameters[key] = value;
                        s = s with
                        {
                            Action = ov.Action ?? s.Action,
                            ActionDays = ov.ActionDays ?? s.ActionDays,
                            Parameters = parameters,
                        };
                    }
                }
            }

            if (!rule.CanBeDisabled) s = s with { IsActive = true };
            byRule[rule.Code] = s;
        }
        return company with { ByRule = byRule };
    }

    public static EffectiveRuleSettings Defaults(ISupplierInvoiceRule rule) =>
        new(rule.DefaultActive, rule.DefaultAction, rule.DefaultActionDays, rule.Parameters.ToDictionary(p => p.Key, p => p.DefaultValue));
}
