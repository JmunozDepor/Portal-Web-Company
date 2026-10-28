using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Dominio.Reglas.Catalogo;

namespace Modulo.FacturasProveedor.Tests.Dominio.Reglas;

/// <summary>Datos de ejemplo del spec: OC 4512 de Comercial Textil Andes por $3.200.000, factura del 23-oct.</summary>
internal static class RuleTestData
{
    public const string IssuerRut = "76412553-1";

    public static IReadOnlyList<ISupplierInvoiceRule> Catalog() => new ISupplierInvoiceRule[]
    {
        new OcReferenciadaRule(), new OcMismoProveedorRule(), new OcAntiguedadRule(), new OcSaldoRule(),
        new OcAbiertaRule(), new ProveedorEnSapRule(), new RevisionManualProveedorRule(), new PlazoPorVencerRule(),
    };

    public static InvoiceFacts Invoice(long total = 2_200_000, DateOnly? issueDate = null, params int[] docNums) =>
        new(1, IssuerRut, 33, 18452, issueDate ?? new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 24), total,
            docNums.Length == 0 ? new[] { 4512 } : docNums);

    public static PurchaseOrderFacts Po(int docNum = 4512, string? taxId = IssuerRut, DateOnly? docDate = null, decimal total = 3_200_000,
        decimal alreadyInvoiced = 1_000_000, string status = "open", string cardCode = "P76412553") =>
        new(docNum, 100 + docNum, "Primario", cardCode, taxId, docDate ?? new DateOnly(2026, 10, 12), total, status, alreadyInvoiced);

    public static SupplierFacts Supplier(SupplierProfileFacts? profile = null, bool exists = true, bool active = true) =>
        new(exists, active, exists ? "P76412553" : null, profile);

    public static InvoiceEvaluationContext Ctx(InvoiceFacts? invoice = null, SupplierFacts? supplier = null,
        IReadOnlyList<PurchaseOrderFacts>? pos = null, IReadOnlyList<int>? ambiguous = null) =>
        new(invoice ?? Invoice(), supplier ?? Supplier(), pos ?? new[] { Po() }, Array.Empty<int>(), ambiguous ?? Array.Empty<int>());

    public static EffectiveRuleSettings Settings(ISupplierInvoiceRule rule, string? action = null, int? days = null,
        IReadOnlyDictionary<string, decimal>? parameters = null) =>
        new(true, action ?? rule.DefaultAction, days ?? rule.DefaultActionDays,
            parameters ?? rule.Parameters.ToDictionary(p => p.Key, p => p.DefaultValue));

    /// <summary>Configuración de la empresa con los valores por defecto del catálogo.</summary>
    public static CompanyRuleConfiguration DefaultConfig(string finalAction = RuleActions.Accept) =>
        new(Catalog().ToDictionary(r => r.Code, r => new EffectiveRuleSettings(r.DefaultActive, r.DefaultAction, r.DefaultActionDays,
                r.Parameters.ToDictionary(p => p.Key, p => p.DefaultValue))),
            Catalog().ToDictionary(r => r.Code, r => r.DefaultOrder),
            finalAction);
}
