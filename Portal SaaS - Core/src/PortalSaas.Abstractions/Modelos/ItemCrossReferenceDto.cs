namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Paridad SKU-cliente ↔ artículo SAP (recurso estándar de Service Layer
/// AlternateCatNum) -- portado de ParidadItemDto en referencia-original/PortalSAP_v2.
/// Prerrequisito para el importador masivo (docs/08-BRECHA-FUNCIONAL-VS-PORTALSAP-V2.md
/// §2.3): sin esto, una fila de Excel que trae el SKU propio del cliente en vez del
/// ItemCode de SAP no se puede resolver.
/// </summary>
public sealed record ItemCrossReferenceDto(
    string Sku,
    string? CustomerDescription,
    string? Department,
    string ItemCode);
