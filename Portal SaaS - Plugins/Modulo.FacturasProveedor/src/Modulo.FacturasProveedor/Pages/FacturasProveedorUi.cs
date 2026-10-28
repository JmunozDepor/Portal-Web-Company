using System.Globalization;
using Modulo.FacturasProveedor.Dominio;

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
        "not_respondable" => "No admite respuesta",
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
        52 => "Guía de despacho",
        56 => "Nota de débito",
        61 => "Nota de crédito",
        _ => $"DTE {dteType}",
    };

    /// <summary>Nombre del tipo de documento como lo informa el SII (columna "Documento" de la bandeja).</summary>
    public static string DocumentTypeName(int dteType) => dteType switch
    {
        33 => "Factura electrónica",
        34 => "Factura exenta electrónica",
        43 => "Liquidación factura electrónica",
        46 => "Factura de compra electrónica",
        52 => "Guía de despacho electrónica",
        56 => "Nota de débito electrónica",
        61 => "Nota de crédito electrónica",
        _ => $"DTE {dteType}",
    };

    /// <summary>Nombre oficial del SII, como va en el recuadro rojo del documento (y en el visor de FACELE).</summary>
    public static string OfficialDocumentName(int dteType) => dteType switch
    {
        33 => "FACTURA ELECTRÓNICA",
        34 => "FACTURA NO AFECTA O EXENTA ELECTRÓNICA",
        43 => "LIQUIDACIÓN FACTURA ELECTRÓNICA",
        46 => "FACTURA DE COMPRA ELECTRÓNICA",
        52 => "GUÍA DE DESPACHO ELECTRÓNICA",
        56 => "NOTA DE DÉBITO ELECTRÓNICA",
        61 => "NOTA DE CRÉDITO ELECTRÓNICA",
        _ => $"DTE {dteType}",
    };

    /// <summary>RUT con puntos (76012093-6 → 76.012.093-6); tal cual si no tiene la forma número-dígito.</summary>
    public static string FormatTaxId(string taxId)
    {
        var dash = taxId.LastIndexOf('-');
        if (dash <= 0 || !long.TryParse(taxId[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return taxId;
        return $"{number.ToString("N0", Cl)}{taxId[dash..]}";
    }

    /// <summary>Forma de pago del XML (FmaPago del SII): 1 contado, 2 crédito, 3 sin costo.</summary>
    public static string PaymentFormCodeLabel(int? code) => code switch
    {
        1 => "Contado",
        2 => "Crédito",
        3 => "Sin costo",
        null => "—",
        _ => code.Value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Tipo de un documento referenciado: 801 = orden de compra; un DTE por su nombre; otro código tal cual (ej. HES).</summary>
    public static string ReferenceTypeName(string type) =>
        type == "801" ? "Orden de compra"
        : int.TryParse(type, NumberStyles.None, CultureInfo.InvariantCulture, out var dteType) ? DocumentTypeName(dteType)
        : type;

    /// <summary>Código de referencia del SII (CodRef): 1 anula, 2 corrige texto, 3 corrige montos.</summary>
    public static string ReferenceCodeLabel(int? code) => code switch
    {
        1 => "Anula documento",
        2 => "Corrige texto",
        3 => "Corrige montos",
        null => "—",
        _ => code.Value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Forma de pago del operador (FACELE: CONTADO, CREDITO, SIN_COSTO) en español.</summary>
    public static string PaymentFormLabel(string? form) => form switch
    {
        null or "" => "—",
        "CONTADO" => "Contado",
        "CREDITO" => "Crédito",
        "SIN_COSTO" => "Sin costo",
        _ => form,
    };

    /// <summary>Fecha y hora de registro en el SII (hora de Chile); solo la fecha si el operador no informó la hora.</summary>
    public static string RegisteredLabel(DateTimeOffset? registeredAt, DateOnly receivedOn) =>
        registeredAt is { } at ? ChileDateTime(at) : Date(receivedOn);

    /// <summary>
    /// Tooltip del estado (como el de FACELE): hora de registro en el SII y, si el documento admite respuesta, cuánto queda
    /// del plazo legal contado desde esa hora ("7 días, 21 horas y 52 minutos") o cuándo venció.
    /// </summary>
    public static string DeadlineTooltip(DateTimeOffset? registeredAt, DateOnly legalDeadlineOn, int legalDays, bool respondable, DateTimeOffset now)
    {
        if (registeredAt is not { } at) return respondable ? $"Vence el {Date(legalDeadlineOn)}" : "Sin hora de registro en el SII";
        var registered = $"Registro SII: {ChileDateTime(at)}";
        if (!respondable) return registered;
        var deadline = at.AddDays(legalDays);
        var left = deadline - now;
        if (left <= TimeSpan.Zero) return $"{registered} · Plazo vencido el {ChileDateTime(deadline)}";
        return $"{registered} · Plazo para responder: {Count(left.Days, "día", "días")}, {Count(left.Hours, "hora", "horas")} y "
               + Count(left.Minutes, "minuto", "minutos");
    }

    private static string ChileDateTime(DateTimeOffset at) =>
        ChileClock.ToLocal(at.UtcDateTime).ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    /// <summary>Monto en pesos chilenos sin decimales (ej. 2.380.000).</summary>
    public static string Amount(long amount) => amount.ToString("N0", Cl);

    public static string Amount(decimal amount) => amount.ToString("N0", Cl);

    public static string Date(DateOnly? date) => date?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? "—";
}
