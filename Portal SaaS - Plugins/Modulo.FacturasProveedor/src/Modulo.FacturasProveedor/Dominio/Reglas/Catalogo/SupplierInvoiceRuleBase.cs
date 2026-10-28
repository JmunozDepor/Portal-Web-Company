using System.Globalization;

namespace Modulo.FacturasProveedor.Dominio.Reglas.Catalogo;

/// <summary>Valores por defecto comunes del catálogo; cada regla define solo lo suyo.</summary>
public abstract class SupplierInvoiceRuleBase : ISupplierInvoiceRule
{
    protected static readonly IReadOnlyList<RuleParameterDefinition> NoParameters = Array.Empty<RuleParameterDefinition>();
    private static readonly CultureInfo Cl = new("es-CL");

    public abstract string Code { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public virtual RuleScope Scope => RuleScope.OcSuppliers;
    public virtual IReadOnlyList<RuleParameterDefinition> Parameters => NoParameters;
    public abstract IReadOnlyList<string> AllowedActions { get; }
    public abstract string DefaultAction { get; }
    public virtual int? DefaultActionDays => null;
    public virtual bool DefaultActive => true;
    public virtual bool CanBeDisabled => true;
    public abstract int DefaultOrder { get; }

    public abstract RuleOutcome Evaluate(InvoiceEvaluationContext ctx, EffectiveRuleSettings settings);

    protected static string Money(decimal amount) => "$" + amount.ToString("N0", Cl);

    /// <summary>Mismo RUT ignorando puntos, guion y mayúsculas del dígito verificador.</summary>
    protected static bool SameRut(string? a, string? b)
    {
        static string Clean(string? s) => new((s ?? "").Where(c => char.IsDigit(c) || c is 'k' or 'K').Select(char.ToUpperInvariant).ToArray());
        var ca = Clean(a);
        return ca.Length > 1 && ca == Clean(b);
    }
}
