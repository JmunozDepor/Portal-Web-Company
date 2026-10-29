using System.Globalization;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Compras.Pages;

/// <summary>
/// Reglas puras de las facturas de proveedor (documento tributario y "Copiar de
/// Pedido") que aplica DetailGenericPurchaseDocumentModelBase -- separadas de la página
/// para poder probarlas sin HttpContext. Field de RuleError es el nombre de la propiedad
/// de InputModel; LineIndex, el índice de la línea en Input.Lines.
/// </summary>
public static class PurchaseDocumentRules
{
    public sealed record RuleError(string Field, string Message);

    public sealed record LineRuleError(int LineIndex, string Message);

    public sealed record CopiedLineCheck(int LineIndex, int BaseEntry, int BaseLine, string? ItemCode, decimal Quantity);

    /// <summary>Margen de redondeo de cantidades (no una tolerancia de negocio).</summary>
    private const decimal QuantityEpsilon = 0.0001m;

    /// <summary>Opcional (la reserva de importación no lo trae), pero si se empieza el folio se exige completo, con su Indicador.</summary>
    public static IReadOnlyList<RuleError> ValidateTaxDocument(string? indicator, string? folioPrefix, int? folioNumber)
    {
        var hasPrefix = !string.IsNullOrWhiteSpace(folioPrefix);
        var hasNumber = folioNumber is not null;
        if (!hasPrefix && !hasNumber)
        {
            return [];
        }

        var errors = new List<RuleError>();
        if (!hasPrefix)
        {
            errors.Add(new("FolioPrefix", "Ingresa el prefijo del folio."));
        }

        if (!hasNumber)
        {
            errors.Add(new("FolioNumber", "Ingresa el número de folio."));
        }
        else if (folioNumber <= 0)
        {
            errors.Add(new("FolioNumber", "El número de folio debe ser mayor a 0."));
        }

        if (string.IsNullOrWhiteSpace(indicator))
        {
            errors.Add(new("TaxIndicator", "Elige el indicador del documento."));
        }

        return errors;
    }

    /// <summary>
    /// Líneas copiadas contra lo que SIGUE pendiente en SAP al momento de crear: otro
    /// documento pudo consumir el Pedido entre la copia y el envío. Varias líneas sobre la
    /// misma línea base suman su cantidad.
    /// </summary>
    public static IReadOnlyList<LineRuleError> ValidateCopiedLines(string supplierCardCode, DocumentLineType documentLineType,
        IReadOnlyList<CopiedLineCheck> lines, IReadOnlyList<PurchaseOpenOrderLineDto> openLines)
    {
        var errors = new List<LineRuleError>();
        var used = new Dictionary<(int, int), decimal>();

        foreach (var line in lines)
        {
            var open = openLines.FirstOrDefault(o => o.DocEntry == line.BaseEntry && o.LineNum == line.BaseLine);
            if (open is null)
            {
                errors.Add(new(line.LineIndex, $"Línea {line.LineIndex + 1}: la línea del Pedido ya no está abierta (otro documento la usó o el Pedido se cerró)."));
                continue;
            }

            if (!string.Equals(open.CardCode, supplierCardCode?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new(line.LineIndex, $"Línea {line.LineIndex + 1}: el Pedido {open.DocNum} es de otro proveedor."));
                continue;
            }

            if (open.LineType != documentLineType)
            {
                errors.Add(new(line.LineIndex, $"Línea {line.LineIndex + 1}: el Pedido {open.DocNum} es de {LineTypeName(open.LineType)} y el documento de {LineTypeName(documentLineType)}."));
                continue;
            }

            if (open.LineType == DocumentLineType.Item
                && !string.Equals(open.ItemCode?.Trim(), line.ItemCode?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new(line.LineIndex, $"Línea {line.LineIndex + 1}: el artículo no coincide con el del Pedido {open.DocNum} ({open.ItemCode})."));
                continue;
            }

            var key = (line.BaseEntry, line.BaseLine);
            var total = used.GetValueOrDefault(key) + line.Quantity;
            used[key] = total;
            if (total > open.OpenQuantity + QuantityEpsilon)
            {
                errors.Add(new(line.LineIndex, $"Línea {line.LineIndex + 1}: supera la cantidad pendiente del Pedido {open.DocNum} ({open.OpenQuantity.ToString("N2", CultureInfo.GetCultureInfo("es-CL"))})."));
            }
        }

        return errors;
    }

    /// <summary>
    /// Índices de las líneas que pasan por la validación de precio contra lista/precio
    /// especial: las copiadas de un Pedido no, su precio ya viene acordado ahí.
    /// </summary>
    public static IReadOnlyList<int> LinesSubjectToPriceCheck(IReadOnlyList<int?> baseEntries) =>
        baseEntries.Select((baseEntry, index) => (baseEntry, index)).Where(x => x.baseEntry is null).Select(x => x.index).ToList();

    /// <summary>Mismo texto que escribe SAP al copiar ("Basado en Pedidos 10005322.").</summary>
    public static string BasedOnComment(IReadOnlyList<int> orderDocNums) =>
        $"Basado en Pedidos {string.Join(", ", orderDocNums)}.";

    /// <summary>Pedidos elegidos en "Copiar de Pedido": del proveedor del documento, misma moneda y mismo tipo de línea. Null = válida.</summary>
    public static string? CheckCopySelection(string supplierCardCode, IReadOnlyList<PurchaseOpenOrderLineDto> lines)
    {
        if (lines.Count == 0)
        {
            return "Los Pedidos elegidos ya no tienen líneas pendientes.";
        }

        if (lines.Any(l => !string.Equals(l.CardCode, supplierCardCode?.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return "Hay Pedidos de otro proveedor.";
        }

        if (lines.Select(l => l.DocCurrency ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            return "Los Pedidos elegidos están en monedas distintas: cópialos por separado.";
        }

        if (lines.Select(l => l.LineType).Distinct().Count() > 1)
        {
            return "No se pueden mezclar Pedidos de Artículo con Pedidos de Servicio.";
        }

        return null;
    }

    private static string LineTypeName(DocumentLineType type) => type == DocumentLineType.Service ? "Servicio" : "Artículo";
}
