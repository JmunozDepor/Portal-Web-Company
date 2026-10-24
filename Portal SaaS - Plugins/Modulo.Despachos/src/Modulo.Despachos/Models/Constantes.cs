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

/// <summary>
/// Consultas SAP configurables (sap_document_queries.document_kind): las de búsqueda por folio
/// (<see cref="DocumentKinds"/>) más la de líneas de factura para las guías Res. 154.
/// </summary>
public static class SapQueryKinds
{
    public const string InvoiceLines = "invoice_lines";

    // Fase B: transferencias de stock a bodegas sucursal.
    public const string TransferByLpn = "transfer_by_lpn";
    public const string TransferPending = "transfer_pending";
    /// <summary>Pendientes a todas las bodegas (agrupaciones); el portal filtra por bodegas sucursal.</summary>
    public const string TransferPendingAll = "transfer_pending_all";
    public const string TransferLines = "transfer_lines";
    public const string Warehouses = "warehouses";

    public static readonly IReadOnlyList<string> All = new[]
    {
        DocumentKinds.Invoice, DocumentKinds.TransferGuide, InvoiceLines,
        TransferByLpn, TransferPending, TransferPendingAll, TransferLines, Warehouses,
    };

    public static string Label(string kind) => kind switch
    {
        InvoiceLines => "Líneas de factura",
        TransferByLpn => "Transferencia por LPN",
        TransferPending => "Transferencias pendientes",
        TransferPendingAll => "Transferencias pendientes (todas las sucursales)",
        TransferLines => "Líneas de transferencia",
        Warehouses => "Bodegas",
        _ => DocumentKinds.Label(kind),
    };
}

/// <summary>
/// Cómo se resumen las líneas de la guía de despacho: por SKU (ItemCode, una línea por talla) o por modelo
/// (código sin talla, columna opcional "model_code" de la consulta de líneas; sin modelo, la línea queda por SKU).
/// </summary>
public static class GuideLineGroupings
{
    public const string Sku = "sku";
    public const string Model = "model";

    public static readonly IReadOnlyList<string> All = new[] { Sku, Model };

    public static string Label(string grouping) => grouping switch
    {
        Model => "Por modelo (código sin talla)",
        _ => "Por SKU (código de artículo)",
    };
}

/// <summary>
/// Dónde va el código del artículo en la guía: en la columna Código (CdgItem) o al inicio del detalle (NmbItem),
/// para plantillas PDF cuya columna Código es angosta y lo superpone al detalle.
/// </summary>
public static class GuideItemCodePlacements
{
    public const string Column = "column";
    public const string Name = "name";

    public static readonly IReadOnlyList<string> All = new[] { Column, Name };

    public static string Label(string placement) => placement switch
    {
        Name => "Al inicio del detalle",
        _ => "En la columna Código",
    };
}

/// <summary>Tipos de documento SAP que se pueden agregar a un manifiesto (document_kind).</summary>
public static class DocumentKinds
{
    public const string Invoice = "invoice";
    public const string TransferGuide = "transfer_guide";

    /// <summary>
    /// Transferencia de stock a una bodega sucursal (OWTR, Fase B): entra por LPN o en bloque, no por folio, por eso
    /// no está en <see cref="All"/> (la lista de tipos buscables por folio).
    /// </summary>
    public const string Transfer = "transfer";

    /// <summary>Guía manual (GM-) agregada al manifiesto: no se busca por folio, entra por su código.</summary>
    public const string ManualGuide = "manual_guide";

    public static readonly IReadOnlyList<string> All = new[] { Invoice, TransferGuide };

    /// <summary>Nombre plural para mensajes ("facturas", "guías de traslado").</summary>
    public static string PluralLabel(string kind) => kind switch
    {
        Invoice => "facturas",
        TransferGuide => "guías de traslado",
        Transfer => "transferencias",
        ManualGuide => "guías manuales",
        _ => kind,
    };

    public static string Label(string kind) => kind switch
    {
        Invoice => "Factura",
        TransferGuide => "Guía de traslado",
        Transfer => "Transferencia",
        ManualGuide => "Guía manual",
        _ => kind,
    };
}

/// <summary>
/// Escritura del folio de la guía en la transferencia SAP (manifest_documents.sap_writeback_status).
/// null = no aplica o no hay nada configurado para escribir.
/// </summary>
public static class SapWritebackStatuses
{
    public const string Ok = "ok";
    public const string Pending = "pending";
}
