namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Campos "núcleo" del importador genérico -- cada uno tiene resolución propia contra SAP
/// (existencia de artículo/paridad, precio de lista del socio de negocio, etc, ver
/// IGenericImportService), por eso son un catálogo fijo en código (agregar uno nuevo es
/// código + build, no una pantalla de administración) -- a diferencia de UserField, el valor
/// especial que delega en el catálogo administrable de campos de usuario (organization-scoped)
/// para campos que viajan como valor plano, sin lógica de negocio. Portado de
/// CampoLogicoImportacionGenerica.
/// </summary>
public enum GenericImportLogicalField
{
    // Cabecera
    CustomerReferenceNumber, // NumAtCard
    Branch,                 // ShipToCode -- solo aplica a GenericImportModule.Sales

    /// <summary>
    /// Socio de negocio de LA FILA -- solo se resuelve cuando
    /// GenericImportConfigDto.BusinessPartnerFromFile = true (carga multi-socio: cada
    /// fila trae su propio CardCode, en vez de un único socio fijo para todo el
    /// archivo). Al ser parte del enum, aparece automáticamente como fila mapeable en
    /// "Mapeo de campos núcleo" de Configuración, sin código nuevo ahí. Portado de
    /// CampoLogicoImportacionGenerica.SocioNegocioCardCode.
    /// </summary>
    BusinessPartnerCardCode,

    // Línea -- comunes a Artículo y Servicio
    Quantity,
    UnitPrice,          // vacío = SAP asigna el precio de la lista del socio de negocio
    DiscountPercent,

    // Línea -- solo GenericImportLineType.Item
    ItemCode,           // ItemCode directo o SKU de paridad, según GenericImportConfigDto.SkuIsCustomerOwn
    Warehouse,          // WarehouseCode

    // Línea -- solo GenericImportLineType.Service
    Description,
    Account,
    CostCenter,
    Dimension2,
    Dimension3,

    // Línea -- solo GenericImportModule.Inventory (traslado: siempre artículo, nunca
    // "servicio", necesita almacén de origen Y destino en vez de un único Warehouse).
    SourceWarehouse,
    DestinationWarehouse,

    // Marcador: la fila de detalle delega en UserFieldId en vez de resolver un campo núcleo.
    UserField,
}
