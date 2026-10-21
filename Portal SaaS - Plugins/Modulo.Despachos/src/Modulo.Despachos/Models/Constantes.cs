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
