using System.Text.Json;

namespace Modulo.FacturasProveedor.Dominio.Reglas.Catalogo;

/// <summary>1 · La factura referencia (801 o asociación manual) al menos una OC que existe en SAP.</summary>
public sealed class OcReferenciadaRule : SupplierInvoiceRuleBase
{
    public override string Code => "oc-referenciada";
    public override string Name => "OC referenciada existe en SAP";
    public override string Description => "Busca la referencia 801 (o la OC asociada a mano) por DocNum.";
    public override IReadOnlyList<string> AllowedActions { get; } = new[] { RuleActions.WarnThenReject, RuleActions.Reject, RuleActions.ManualReview };
    public override string DefaultAction => RuleActions.WarnThenReject;
    public override int? DefaultActionDays => 3;
    public override int DefaultOrder => 1;

    public override RuleOutcome Evaluate(InvoiceEvaluationContext ctx, EffectiveRuleSettings settings)
    {
        if (ctx.AmbiguousDocNums.Count > 0)
        {
            return RuleOutcome.Objection(
                $"La OC {string.Join(", ", ctx.AmbiguousDocNums)} existe en más de una serie en SAP: hay que revisarla a mano.",
                forcedAction: RuleActions.ManualReview);
        }
        return ctx.PurchaseOrders.Count > 0
            ? RuleOutcome.Pass
            : RuleOutcome.Objection("La factura no referencia una OC existente en SAP.");
    }
}

/// <summary>2 · Toda OC referenciada pertenece al proveedor que emite.</summary>
public sealed class OcMismoProveedorRule : SupplierInvoiceRuleBase
{
    public override string Code => "oc-mismo-proveedor";
    public override string Name => "OC del mismo proveedor";
    public override string Description => "El RUT del socio de la OC es el RUT del emisor de la factura.";
    public override IReadOnlyList<string> AllowedActions { get; } = new[] { RuleActions.Reject, RuleActions.ManualReview };
    public override string DefaultAction => RuleActions.Reject;
    public override int DefaultOrder => 2;

    public override RuleOutcome Evaluate(InvoiceEvaluationContext ctx, EffectiveRuleSettings settings)
    {
        if (ctx.PurchaseOrders.Count == 0) return RuleOutcome.NotApplicable;
        var foreign = ctx.PurchaseOrders.Where(po => po.SupplierTaxId is { Length: > 0 } rut
                ? !SameRut(rut, ctx.Invoice.IssuerTaxId)
                : po.CardCode != ctx.Supplier.CardCode)
            .Select(po => po.DocNum).ToList();
        return foreign.Count == 0
            ? RuleOutcome.Pass
            : RuleOutcome.Objection($"La OC {string.Join(", ", foreign)} es de otro proveedor.");
    }
}

/// <summary>3 · Días entre la fecha de la OC y la emisión de la factura ≤ máximo.</summary>
public sealed class OcAntiguedadRule : SupplierInvoiceRuleBase
{
    public override string Code => "oc-antiguedad";
    public override string Name => "Antigüedad máxima de la OC";
    public override string Description => "Desde la fecha de la OC hasta la emisión de la factura.";
    public override IReadOnlyList<RuleParameterDefinition> Parameters { get; } =
        new[] { new RuleParameterDefinition("max_days", "Máximo de días", 30, 1, 365, "días") };
    public override IReadOnlyList<string> AllowedActions { get; } = new[] { RuleActions.Reject, RuleActions.ManualReview, RuleActions.WarnThenReject };
    public override string DefaultAction => RuleActions.Reject;
    public override int? DefaultActionDays => 3;
    public override int DefaultOrder => 3;

    public override RuleOutcome Evaluate(InvoiceEvaluationContext ctx, EffectiveRuleSettings settings)
    {
        if (ctx.PurchaseOrders.Count == 0) return RuleOutcome.NotApplicable;
        var max = (int)settings.Param("max_days", 30);
        var oldest = ctx.PurchaseOrders
            .Select(po => (po.DocNum, Days: ctx.Invoice.IssueDate.DayNumber - po.DocDate.DayNumber))
            .OrderByDescending(x => x.Days).First();
        return oldest.Days <= max
            ? RuleOutcome.Pass
            : RuleOutcome.Objection($"La OC {oldest.DocNum} tiene {oldest.Days} días (máx. {max}).");
    }
}

/// <summary>4 · Total de la factura ≤ saldo (en total, con IVA o exento) de las OC referenciadas.</summary>
public sealed class OcSaldoRule : SupplierInvoiceRuleBase
{
    public override string Code => "oc-saldo";
    public override string Name => "Monto dentro del saldo de la OC";
    public override string Description => "Total de la OC (con IVA o exento) menos las facturas ya aceptadas que la referencian.";
    public override IReadOnlyList<RuleParameterDefinition> Parameters { get; } = new[]
    {
        new RuleParameterDefinition("tolerance_pct", "Tolerancia", 0, 0, 20, "%"),
        new RuleParameterDefinition("tolerance_amount", "Tolerancia en monto", 0, 0, 10_000_000, "$"),
    };
    public override IReadOnlyList<string> AllowedActions { get; } = new[] { RuleActions.Reject, RuleActions.ManualReview };
    public override string DefaultAction => RuleActions.Reject;
    public override int DefaultOrder => 4;

    public override RuleOutcome Evaluate(InvoiceEvaluationContext ctx, EffectiveRuleSettings settings)
    {
        if (ctx.PurchaseOrders.Count == 0) return RuleOutcome.NotApplicable;
        var ocTotal = ctx.PurchaseOrders.Sum(po => po.TotalLocal);
        var already = ctx.PurchaseOrders.Sum(po => po.AlreadyInvoicedTotal);
        var balance = ocTotal - already;
        var tolerance = Math.Max(Math.Max(balance, 0) * settings.Param("tolerance_pct", 0) / 100m, settings.Param("tolerance_amount", 0));
        if (ctx.Invoice.TotalAmount <= balance + tolerance) return RuleOutcome.Pass;

        var data = JsonSerializer.Serialize(new { ocTotal, alreadyInvoiced = already, balance, invoiceTotal = ctx.Invoice.TotalAmount });
        return RuleOutcome.Objection($"Supera el saldo de la OC ({Money(balance)}).", data);
    }
}

/// <summary>5 · Las OC no están cerradas ni canceladas en SAP (sugerida, inactiva por defecto).</summary>
public sealed class OcAbiertaRule : SupplierInvoiceRuleBase
{
    public override string Code => "oc-abierta";
    public override string Name => "OC abierta en SAP";
    public override string Description => "No cerrada ni cancelada.";
    public override IReadOnlyList<string> AllowedActions { get; } = new[] { RuleActions.ManualReview, RuleActions.Reject };
    public override string DefaultAction => RuleActions.ManualReview;
    public override bool DefaultActive => false;
    public override int DefaultOrder => 5;

    public override RuleOutcome Evaluate(InvoiceEvaluationContext ctx, EffectiveRuleSettings settings)
    {
        if (ctx.PurchaseOrders.Count == 0) return RuleOutcome.NotApplicable;
        var notOpen = ctx.PurchaseOrders.FirstOrDefault(po => po.Status != "open");
        return notOpen is null
            ? RuleOutcome.Pass
            : RuleOutcome.Objection($"La OC {notOpen.DocNum} está {(notOpen.Status == "canceled" ? "cancelada" : "cerrada")} en SAP.");
    }
}

/// <summary>6 · El RUT emisor existe como proveedor activo en SAP.</summary>
public sealed class ProveedorEnSapRule : SupplierInvoiceRuleBase
{
    public override string Code => "proveedor-en-sap";
    public override string Name => "Proveedor existe en SAP";
    public override string Description => "El RUT emisor existe como proveedor activo en SAP.";
    public override RuleScope Scope => RuleScope.AllSuppliers;
    public override IReadOnlyList<string> AllowedActions { get; } = new[] { RuleActions.ManualReview, RuleActions.Reject };
    public override string DefaultAction => RuleActions.ManualReview;
    public override int DefaultOrder => 6;

    public override RuleOutcome Evaluate(InvoiceEvaluationContext ctx, EffectiveRuleSettings settings)
    {
        if (!ctx.Supplier.ExistsInSap) return RuleOutcome.Objection($"El proveedor {ctx.Invoice.IssuerTaxId} no existe en SAP.");
        return ctx.Supplier.IsActiveInSap ? RuleOutcome.Pass : RuleOutcome.Objection("El proveedor está inactivo en SAP.");
    }
}

/// <summary>7 · Proveedor marcado como gasto fijo o "siempre revisión manual" (no se puede desactivar).</summary>
public sealed class RevisionManualProveedorRule : SupplierInvoiceRuleBase
{
    public override string Code => "revision-manual-proveedor";
    public override string Name => "Revisión manual por proveedor";
    public override string Description => "Gasto fijo mensual o \"siempre revisión manual\".";
    public override RuleScope Scope => RuleScope.MarkedSuppliers;
    public override IReadOnlyList<string> AllowedActions { get; } = new[] { RuleActions.ManualReview };
    public override string DefaultAction => RuleActions.ManualReview;
    public override bool CanBeDisabled => false;
    public override int DefaultOrder => 7;

    public override RuleOutcome Evaluate(InvoiceEvaluationContext ctx, EffectiveRuleSettings settings) =>
        ctx.Supplier.Profile?.Condition switch
        {
            SupplierConditions.FixedMonthly => RuleOutcome.Objection("Gasto fijo mensual: requiere aprobación manual."),
            SupplierConditions.AlwaysManual => RuleOutcome.Objection("Proveedor con revisión manual obligatoria."),
            _ => RuleOutcome.Pass,
        };
}

/// <summary>
/// 8 · Factura pendiente (en revisión o en aviso) cuando faltan N días para el fin del plazo legal: aceptar o rechazar.
/// Es temporal: no participa de la evaluación, la ejecuta el orquestador (DueActionPlanner).
/// </summary>
public sealed class PlazoPorVencerRule : SupplierInvoiceRuleBase
{
    public const string RuleCode = "plazo-por-vencer";

    public override string Code => RuleCode;
    public override string Name => "Plazo por vencer sin decisión";
    public override string Description => "Actúa sobre las facturas en revisión o en aviso cuando se acerca el fin del plazo legal.";
    public override RuleScope Scope => RuleScope.Pending;
    public override IReadOnlyList<RuleParameterDefinition> Parameters { get; } =
        new[] { new RuleParameterDefinition("days_before", "Actuar cuando falten", 0, 0, 5, "días") };
    public override IReadOnlyList<string> AllowedActions { get; } = new[] { RuleActions.Accept, RuleActions.Reject };
    public override string DefaultAction => RuleActions.Reject;
    public override int DefaultOrder => 8;

    public override RuleOutcome Evaluate(InvoiceEvaluationContext ctx, EffectiveRuleSettings settings) => RuleOutcome.NotApplicable;

    internal static int DaysBefore(EffectiveRuleSettings settings) =>
        (int)Math.Round(settings.Param("days_before", 0), MidpointRounding.AwayFromZero);
}
