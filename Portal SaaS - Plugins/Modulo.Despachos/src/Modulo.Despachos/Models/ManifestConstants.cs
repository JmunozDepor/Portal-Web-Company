namespace Modulo.Despachos.Models;

/// <summary>Estados del manifiesto (manifests.status).</summary>
public static class ManifestStatuses
{
    public const string Draft = "draft";
    public const string Dispatched = "dispatched";
    public const string Returned = "returned";
    public const string Closed = "closed";
    public const string Annulled = "annulled";

    public static readonly IReadOnlyList<string> All = new[] { Draft, Dispatched, Returned, Closed, Annulled };
}

/// <summary>Estado de entrega de cada documento dentro del manifiesto (manifest_documents.delivery_status).</summary>
public static class DeliveryStatuses
{
    public const string Pending = "pending";
    public const string Dispatched = "dispatched";
    public const string Delivered = "delivered";
    public const string PartiallyDelivered = "partially_delivered";
    public const string Rejected = "rejected";
    public const string NotDelivered = "not_delivered";
}

/// <summary>Cómo se capturó el documento (manifest_documents.capture_method).</summary>
public static class CaptureMethods
{
    public const string Pdf417 = "pdf417";
    public const string Manual = "manual";
}

/// <summary>Tipos de evento de la bitácora del manifiesto (manifest_events.event_type).</summary>
public static class ManifestEventTypes
{
    public const string Created = "created";
    public const string DocumentAdded = "document_added";
    public const string DocumentRemoved = "document_removed";
    public const string Dispatched = "dispatched";
    public const string VehicleChanged = "vehicle_changed";
    public const string ReturnRegistered = "return_registered";
    public const string Closed = "closed";
    public const string Annulled = "annulled";
    public const string CostOverridden = "cost_overridden";
    public const string Printed = "printed";
    public const string Reprinted = "reprinted";
    public const string PeriodClosed = "period_closed";
    public const string PeriodReopened = "period_reopened";
}

/// <summary>Estado del cálculo de costo del manifiesto (manifests.cost_status).</summary>
public static class CostStatuses
{
    public const string Ok = "ok";
    public const string NoRate = "no_rate";
    public const string Ambiguous = "ambiguous";
}

/// <summary>Estados del cierre mensual (monthly_closures.status).</summary>
public static class ClosureStatuses
{
    public const string Open = "open";
    public const string Closed = "closed";
    public const string Reopened = "reopened";
}
