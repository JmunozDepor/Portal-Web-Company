using System.Globalization;

namespace Modulo.FacturasProveedor.Pages;

/// <summary>
/// Helpers puramente visuales para las vistas: etiqueta en español y clase de badge (design system,
/// badge-status--*) por estado de revisión; rótulos de acciones y tipos de documento. Sin lógica de negocio.
/// </summary>
public static class FacturasProveedorUi
{
    private static readonly CultureInfo Cl = new("es-CL");

    public static string StatusLabel(string status) => status switch
    {
        "new" => "Nueva",
        "warning" => "Aviso",
        "in_review" => "Por decidir",
        "to_send" => "Por enviar",
        "accepted" => "Aceptada",
        "rejected" => "Rechazada",
        "simulated" => "Simulada",
        "external" => "Respondida fuera del portal",
        "expired" => "Vencida (aceptada por ley)",
        "out_of_scope" => "Fuera de alcance",
        _ => status,
    };

    public static string StatusBadgeClass(string status) => status switch
    {
        "warning" or "in_review" => "badge-status--warning",
        "accepted" => "badge-status--success",
        "rejected" or "expired" => "badge-status--danger",
        _ => "badge-status--neutral",
    };

    public static string ActionLabel(string? action) => action switch
    {
        "accept" => "Aceptar",
        "reject" => "Rechazar",
        "warn_then_reject" => "Avisar y rechazar",
        "manual_review" => "Revisión manual",
        "accept_content" => "Acepta contenido",
        "claim_content" => "Reclamo al contenido",
        "claim_partial_goods" => "Falta parcial de mercaderías",
        "claim_total_goods" => "Falta total de mercaderías",
        "goods_receipt" => "Recibo de mercaderías",
        null or "" => "—",
        _ => action,
    };

    public static string DocumentLabel(int dteType) => dteType switch
    {
        33 => "Factura",
        34 => "Factura exenta",
        43 => "Liquidación factura",
        46 => "Factura de compra",
        56 => "Nota de débito",
        61 => "Nota de crédito",
        _ => $"DTE {dteType}",
    };

    /// <summary>Monto en pesos chilenos sin decimales (ej. 2.380.000).</summary>
    public static string Amount(long amount) => amount.ToString("N0", Cl);

    public static string Amount(decimal amount) => amount.ToString("N0", Cl);

    public static string Date(DateOnly? date) => date?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? "—";
}
