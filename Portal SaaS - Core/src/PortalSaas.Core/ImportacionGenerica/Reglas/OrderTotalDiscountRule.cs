using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica.Reglas;

/// <summary>
/// Descuento TOTAL de cada OC (grupo/documento) vs. el precio SAP de sus líneas:
/// (Σ Cantidad × Precio SAP − Σ Cantidad × Precio OC) / Σ Cantidad × Precio SAP × 100
/// no puede superar "maxDiscountPercent" (pedido del dueño del proyecto, 2026-10-14:
/// "como total, no por línea" -- una línea puede venir con más descuento si el total de
/// la orden se mantiene dentro del máximo). Solo cuentan las líneas con los dos precios
/// (SapReferencePrice lo completa GenericImportService.EnrichForAnalysisAsync antes del
/// motor de reglas). El hallazgo se marca en TODAS las líneas de la OC: con severidad
/// Bloqueante, la orden completa no se crea.
/// </summary>
public sealed class OrderTotalDiscountRule : IGenericImportValidationRule
{
    public GenericImportValidationRuleType RuleType => GenericImportValidationRuleType.OrderTotalDiscount;
    public IReadOnlyList<GenericImportModule> ApplicableModules { get; } = [GenericImportModule.Sales, GenericImportModule.Purchase];
    public bool SeverityIsConfigurable => true;

    public Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> ValidateAsync(
        IReadOnlyList<GenericImportRowDto> rows, GenericImportModule module,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var maxDiscountPercent = parameters.TryGetValue("maxDiscountPercent", out var m) && m is not null ? Convert.ToDecimal(m) : 0m;
        var result = new Dictionary<int, IReadOnlyList<string>>();

        foreach (var order in rows.GroupBy(r => r.GroupingKey))
        {
            var comparable = order.Where(r => r.Quantity is not null && r.UnitPrice is not null && r.SapReferencePrice is not null).ToList();
            var sapTotal = comparable.Sum(r => r.Quantity!.Value * r.SapReferencePrice!.Value);
            if (sapTotal <= 0)
            {
                continue;
            }

            var orderTotal = comparable.Sum(r => r.Quantity!.Value * r.UnitPrice!.Value);
            var discountPercent = (sapTotal - orderTotal) / sapTotal * 100m;
            if (discountPercent <= maxDiscountPercent)
            {
                continue;
            }

            IReadOnlyList<string> message =
            [
                $"Descuento total de la OC {discountPercent:N2}% supera el máximo permitido ({maxDiscountPercent:N2}%): " +
                $"total OC {orderTotal:N2} vs. total a precio SAP {sapTotal:N2}.",
            ];
            foreach (var row in order)
            {
                result[row.RowNumber] = message;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<string>>>(result);
    }
}
