namespace Modulo.Despachos.Models;

/// <summary>Tipos de transportista (columna carriers.kind).</summary>
public static class CarrierKinds
{
    public const string Own = "own";
    public const string ThirdParty = "third_party";
    public const string Marketplace = "marketplace";
    public const string Pickup = "pickup";

    public static readonly IReadOnlyList<string> All = new[] { Own, ThirdParty, Marketplace, Pickup };
}

/// <summary>A qué situación aplica un motivo (rejection_reasons.applies_to).</summary>
public static class RejectionAppliesTo
{
    public const string Rejection = "rejection";
    public const string NotDelivered = "not_delivered";

    public static readonly IReadOnlyList<string> All = new[] { Rejection, NotDelivered };
}

/// <summary>Tipos de documento SAP que se pueden agregar a un manifiesto (document_kind).</summary>
public static class DocumentKinds
{
    public const string Invoice = "invoice";
    public const string TransferGuide = "transfer_guide";

    public static readonly IReadOnlyList<string> All = new[] { Invoice, TransferGuide };

    /// <summary>Nombre plural para mensajes ("facturas", "guías de traslado").</summary>
    public static string PluralLabel(string kind) => kind switch
    {
        Invoice => "facturas",
        TransferGuide => "guías de traslado",
        _ => kind,
    };

    public static string Label(string kind) => kind switch
    {
        Invoice => "Factura",
        TransferGuide => "Guía de traslado",
        _ => kind,
    };
}
