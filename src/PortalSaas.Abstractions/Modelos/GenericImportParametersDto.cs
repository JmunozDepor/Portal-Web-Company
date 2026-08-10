namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Selección de la cabecera del wizard -- qué motor (Venta/Compra/Inventario), qué tipo
/// de documento concreto (nombre de SalesDocumentType/PurchaseDocumentType/
/// InventoryDocumentType, solo los que tienen CanCreate = true), qué tipo de línea, y el
/// socio de negocio (Cliente o Proveedor según Module, vacío para Inventario) para el
/// que se va a resolver la configuración (estándar o su excepción puntual). Sin
/// OrganizationId/CompanyId a propósito -- IGenericImportConfigService/
/// IGenericImportUserFieldService resuelven la Company activa ellos mismos vía
/// ICurrentCompanyAccessor (mismo patrón que ISalesDocumentService y el resto de los
/// motores genéricos: la Company nunca viaja como parámetro explícito, siempre es
/// ambiente de la sesión). Portado de ParametrosImportacionGenericaDto.
/// </summary>
public sealed record GenericImportParametersDto(
    GenericImportModule Module,
    string DocumentType,
    GenericImportLineType LineType,
    string? BusinessPartnerCardCode);
