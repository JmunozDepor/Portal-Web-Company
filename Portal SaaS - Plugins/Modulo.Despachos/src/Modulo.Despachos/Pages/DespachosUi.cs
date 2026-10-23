namespace Modulo.Despachos.Pages;

/// <summary>
/// Helpers puramente visuales para las vistas: clase de badge y etiqueta en español por
/// estado de manifiesto/documento. Sin lógica de negocio.
/// </summary>
public static class DespachosUi
{
    /// <summary>
    /// Traduce el valor guardado en la base (manifests.status / delivery_status, snake_case) a la clave
    /// que usan StatusBadgeClass y StatusLabel.
    /// </summary>
    public static string StatusKey(string status) => status switch
    {
        "draft" => "Draft",
        "pending_guides" => "PendingGuides",
        "dispatched" => "Dispatched",
        "returned" => "Returned",
        "closed" => "Closed",
        "annulled" => "Cancelled",
        "pending" => "Pending",
        "delivered" => "Delivered",
        "partially_delivered" => "PartiallyReturned",
        "rejected" => "Rejected",
        "not_delivered" => "NotDelivered",
        _ => status,
    };

    public static string StatusBadgeClass(string status) => status switch
    {
        "Dispatched" or "Pending" or "PendingGuides" => "dsp-badge--warning",
        "Returned" or "Delivered" or "Closed" => "dsp-badge--success",
        "Rejected" or "NotDelivered" or "Cancelled" => "dsp-badge--danger",
        "PartiallyReturned" => "dsp-badge--accent",
        _ => "dsp-badge--neutral", // Draft y desconocidos
    };

    public static string StatusLabel(string status) => status switch
    {
        "Draft" => "Borrador",
        "PendingGuides" => "Guías pendientes",
        "Dispatched" => "Despachado",
        "PartiallyReturned" => "Retorno parcial",
        "Returned" => "Retornado",
        "Closed" => "Cerrado",
        "Cancelled" => "Anulado",
        "Pending" => "Pendiente",
        "Delivered" => "Entregado",
        "Rejected" => "Rechazado",
        "NotDelivered" => "No entregado",
        _ => status,
    };

    /// <summary>Clase del badge de una pre-guía (despachos_dispatch_groups.status).</summary>
    public static string DispatchGroupBadgeClass(string status) => status switch
    {
        Models.DispatchGroupStatuses.InManifest => "dsp-badge--warning",
        Models.DispatchGroupStatuses.Dispatched => "dsp-badge--success",
        Models.DispatchGroupStatuses.Annulled => "dsp-badge--danger",
        _ => "dsp-badge--neutral",
    };

    /// <summary>Etiqueta del estado de una guía de despacho (transport_guides.status).</summary>
    public static string GuideStatusLabel(string status) => status switch
    {
        "planned" => "Planificada",
        "pending" => "Pendiente",
        "issued" => "Emitida",
        "failed" => "Rechazada",
        "abandoned" => "Anulada",
        _ => status,
    };

    public static string GuideStatusBadgeClass(string status) => status switch
    {
        "issued" => "dsp-badge--success",
        "pending" => "dsp-badge--warning",
        "failed" => "dsp-badge--danger",
        _ => "dsp-badge--neutral",
    };

    public static string CostStatusLabel(string status) => status switch
    {
        "ok" => "Calculado",
        "no_rate" => "Sin tarifa",
        "ambiguous" => "Tarifa ambigua",
        _ => status,
    };

    public static string CarrierKindLabel(string kind) => kind switch
    {
        "own" => "Propio",
        "third_party" => "Tercero",
        "marketplace" => "Marketplace",
        "pickup" => "Retiro del cliente",
        _ => kind,
    };

    public static string RejectionAppliesToLabel(string appliesTo) => appliesTo switch
    {
        "rejection" => "Rechazo",
        "not_delivered" => "No entregado",
        _ => appliesTo,
    };
}
