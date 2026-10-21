namespace Modulo.Despachos.Pages;

/// <summary>
/// Helpers puramente visuales para las vistas: clase de badge y etiqueta en español por
/// estado de manifiesto/documento. Sin lógica de negocio.
/// </summary>
public static class DespachosUi
{
    public static string StatusBadgeClass(string status) => status switch
    {
        "Dispatched" or "Pending" => "dsp-badge--warning",
        "Returned" or "Delivered" or "Closed" => "dsp-badge--success",
        "Rejected" or "NotDelivered" or "Cancelled" => "dsp-badge--danger",
        "PartiallyReturned" => "dsp-badge--accent",
        _ => "dsp-badge--neutral", // Draft y desconocidos
    };

    public static string StatusLabel(string status) => status switch
    {
        "Draft" => "Borrador",
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
}
