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
    public sealed record Entry(string Table, string Resource, bool DefaultCanCreate, IReadOnlyList<ExtraFilter> ExtraFilters, int ObjectCode);

    public sealed record ExtraFilter(string Column, string Value, bool IsEqual);

    private static readonly IReadOnlyList<ExtraFilter> NoExtraFilters = [];

    // ObjectCode: código de objeto SAP real de NNM1.ObjectCode -- SalesOrder/CreditNote/
    // Invoices(13)/Return confirmados EMPÍRICAMENTE contra Comercial GE2 real (SQL
    // Server, 26 jul 2026: se cruzó el Series real de documentos ya existentes --
    // Orden de Venta DocEntry 611/612 y Notas/Facturas/Devoluciones reales -- contra
    // NNM1 para leer su ObjectCode real, no un valor supuesto de documentación
    // estándar SAP). ReturnRequest (234000031) también confirmado empíricamente --
    // es un objeto SAP tipo UDO/adicional, no el código estándar de "Return"
    // (documentado acá porque no es obvio). Ver ISeriesCatalogService.
    public static readonly IReadOnlyDictionary<SalesDocumentType, Entry> Entries = new Dictionary<SalesDocumentType, Entry>
    {
        [SalesDocumentType.SalesOrder] = new("ORDR", "Orders", true, NoExtraFilters, ObjectCode: 17),
        [SalesDocumentType.CreditNote] = new("ORIN", "CreditNotes", true, NoExtraFilters, ObjectCode: 14),
        [SalesDocumentType.CustomerInvoice] = new("OINV", "Invoices", false, [new("isIns", "N", true), new("DocSubType", "IB", false)], ObjectCode: 13),
        [SalesDocumentType.ReserveInvoice] = new("OINV", "Invoices", true, [new("isIns", "Y", true)], ObjectCode: 13),
        [SalesDocumentType.ReturnRequest] = new("ORRR", "ReturnRequest", false, NoExtraFilters, ObjectCode: 234000031),
        [SalesDocumentType.Return] = new("ORDN", "Returns", false, NoExtraFilters, ObjectCode: 16),
        [SalesDocumentType.Receipt] = new("OINV", "Invoices", false, [new("isIns", "N", true), new("DocSubType", "IB", true)], ObjectCode: 13),
    };

    public static Entry Resolve(SalesDocumentType type) => Entries[type];
}
