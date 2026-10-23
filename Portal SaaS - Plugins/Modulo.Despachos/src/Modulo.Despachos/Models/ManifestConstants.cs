namespace Modulo.Despachos.Models;

/// <summary>Estados del manifiesto (manifests.status).</summary>
public static class ManifestStatuses
{
    public const string Draft = "draft";

    /// <summary>
    /// Salida sellada y snapshot tomado, pero falta emitir al menos una guía de despacho: el camión no debe
    /// salir. Se reintenta desde la pestaña Guías; bloquea los folios igual que despachado.
    /// </summary>
    public const string PendingGuides = "pending_guides";
    public const string Dispatched = "dispatched";
    public const string Returned = "returned";
    public const string Closed = "closed";
    public const string Annulled = "annulled";

    public static readonly IReadOnlyList<string> All = new[] { Draft, PendingGuides, Dispatched, Returned, Closed, Annulled };
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
    public const string CostRecalculated = "cost_recalculated";
    public const string DocumentCommuneChanged = "document_commune_changed";
    public const string ExtraChargesChanged = "extra_charges_changed";
    public const string GuidesPlanned = "guides_planned";
    public const string GuideIssued = "guide_issued";
    public const string GuideFailed = "guide_failed";
    /// <summary>No se pudo escribir el folio de la guía en la transferencia SAP (Fase B).</summary>
    public const string SapWritebackFailed = "sap_writeback_failed";
}

/// <summary>Estado de una guía de despacho del manifiesto (transport_guides.status).</summary>
public static class TransportGuideStatuses
{
    /// <summary>Planificada, aún sin intento de emisión.</summary>
    public const string Planned = "planned";
    /// <summary>En curso en la pasarela (folio sin confirmar o proveedor caído): se reintenta.</summary>
    public const string Pending = "pending";
    public const string Issued = "issued";
    /// <summary>El proveedor la rechazó (datos): se corrige y se reintenta.</summary>
    public const string Failed = "failed";
    /// <summary>El manifiesto se anuló antes de emitirla.</summary>
    public const string Abandoned = "abandoned";
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

/// <summary>Estados de una agrupación (despachos_dispatch_groups.status).</summary>
public static class DispatchGroupStatuses
{
    public const string Armed = "armed";
    public const string InManifest = "in_manifest";
    public const string Dispatched = "dispatched";
    public const string Annulled = "annulled";

    /// <summary>
    /// Estados en que la agrupación retiene sus transferencias (no se agregan sueltas ni a otra agrupación). Una vez
    /// despachada manda la regla del manifiesto: una transferencia "no entregada" vuelve a quedar libre.
    /// </summary>
    public static readonly IReadOnlyList<string> Holding = new[] { Armed, InManifest };

    public static string Label(string status) => status switch
    {
        Armed => "Armada",
        InManifest => "En manifiesto",
        Dispatched => "Despachada",
        Annulled => "Anulada",
        _ => status,
    };
}
