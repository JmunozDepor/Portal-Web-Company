namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Selección de la cabecera del wizard -- qué motor (Venta/Compra/Inventario), qué tipo
/// de documento concreto (nombre de SalesDocumentType/PurchaseDocumentType/
/// InventoryDocumentType, solo los que tienen CanCreate = true), qué tipo de línea, y el
/// socio de negocio (Cliente o Proveedor según Module, vacío para Inventario) para el
/// que se va a resolver la configuración (estándar o su excepción puntual). Portado de
/// ParametrosImportacionGenericaDto.
/// </summary>
public sealed record GenericImportParametersDto(
    Guid OrganizationId,
    GenericImportModule Module,
    string DocumentType,
    GenericImportLineType LineType,
    string? BusinessPartnerCardCode);
