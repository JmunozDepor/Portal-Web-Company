using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Ventas;

/// <summary>
/// Configuración por tipo de documento de venta -- portado de
/// GenericoVentaService._configuracion en referencia-original/PortalSAP_v2, extraído a
/// clase estática propia desde el principio (a diferencia del original, que lo tenía
/// embebido en el servicio -- mismo criterio ya usado ahí para GenericoCompraCatalogo,
/// aplicado acá de entrada para los 3 motores por consistencia).
///
/// Table/Resource identifican la tabla HANA (lectura de listado) y el recurso Service
/// Layer (lectura de detalle + creación). ExtraFilters resuelve los tipos que comparten
/// tabla/recurso (Factura Deudores/Reserva/Recibo son las tres "OINV"/"Invoices"): al
/// listar se traduce a WHERE, al crear los de IsEqual=true se fijan en el POST -- sin
/// esto, ej. una Factura Reserva se crearía silenciosamente como Factura Deudores común
/// (mismo tipo/recurso SAP, sin el campo que las distingue).
/// </summary>
public static class SalesDocumentTypeCatalog
{
    public sealed record Entry(string Table, string Resource, bool DefaultCanCreate, IReadOnlyList<ExtraFilter> ExtraFilters);

    public sealed record ExtraFilter(string Column, string Value, bool IsEqual);

    private static readonly IReadOnlyList<ExtraFilter> NoExtraFilters = [];

    public static readonly IReadOnlyDictionary<SalesDocumentType, Entry> Entries = new Dictionary<SalesDocumentType, Entry>
    {
        [SalesDocumentType.SalesOrder] = new("ORDR", "Orders", true, NoExtraFilters),
        [SalesDocumentType.CreditNote] = new("ORIN", "CreditNotes", true, NoExtraFilters),
        [SalesDocumentType.CustomerInvoice] = new("OINV", "Invoices", false, [new("isIns", "N", true), new("DocSubType", "IB", false)]),
        [SalesDocumentType.ReserveInvoice] = new("OINV", "Invoices", true, [new("isIns", "Y", true)]),
        [SalesDocumentType.ReturnRequest] = new("ORRR", "ReturnRequest", false, NoExtraFilters),
        [SalesDocumentType.Return] = new("ORDN", "Returns", false, NoExtraFilters),
        [SalesDocumentType.Receipt] = new("OINV", "Invoices", false, [new("isIns", "N", true), new("DocSubType", "IB", true)]),
    };

    public static Entry Resolve(SalesDocumentType type) => Entries[type];
}
