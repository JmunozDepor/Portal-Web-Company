using System.Globalization;
using System.Text.Json;
using PortalSaas.Abstractions.Modelos.Dte;

namespace PortalSaas.Core.DocumentosElectronicos;

/// <summary>(De)serialización de las columnas JSON de dte_received (texto plano: motor dual, sin jsonb).</summary>
internal static class DteReceivedJson
{
    public static IReadOnlyList<DteReceivedReference> References(string? json) =>
        string.IsNullOrWhiteSpace(json) ? Array.Empty<DteReceivedReference>()
            : JsonSerializer.Deserialize<List<DteReceivedReference>>(json) ?? new List<DteReceivedReference>();

    public static string References(IReadOnlyList<DteReceivedReference> refs) => JsonSerializer.Serialize(refs);

    public static IReadOnlyList<DtePurchaseOrderRef> PurchaseOrderRefs(string? json) =>
        string.IsNullOrWhiteSpace(json) ? Array.Empty<DtePurchaseOrderRef>()
            : JsonSerializer.Deserialize<List<DtePurchaseOrderRef>>(json) ?? new List<DtePurchaseOrderRef>();

    public static string PurchaseOrderRefs(IReadOnlyList<DtePurchaseOrderRef> refs) => JsonSerializer.Serialize(refs);

    /// <summary>OC informadas en el DTE: referencias tipo 801 con folio numérico (el DocNum de SAP), sin repetir.</summary>
    public static IReadOnlyList<DtePurchaseOrderRef> PurchaseOrdersFromReferences(IReadOnlyList<DteReceivedReference> refs, DateTimeOffset at) =>
        refs.Where(r => r.Type.Trim() == "801")
            .Select(r => int.TryParse(r.Folio.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (int?)null)
            .Where(n => n is > 0)
            .Distinct()
            .Select(n => new DtePurchaseOrderRef(n!.Value, DtePurchaseOrderRefSources.Dte, null, at))
            .ToList();
}
