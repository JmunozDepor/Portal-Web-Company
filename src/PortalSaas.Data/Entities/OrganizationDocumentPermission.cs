namespace PortalSaas.Data.Entities;

/// <summary>
/// Override por organización de si un tipo de documento de los motores genéricos
/// (Venta/Compra/Inventario) se puede crear desde el portal -- reemplaza el flag fijo
/// <c>DefaultCanCreate</c> del catálogo estático de cada motor (ver
/// SalesDocumentTypeCatalog/PurchaseDocumentTypeCatalog/InventoryDocumentTypeCatalog en
/// PortalSaas.Core) cuando existe una fila acá para la combinación
/// (OrganizationId, Engine, DocumentType). Sin fila, se usa el default del catálogo --
/// nunca al revés (una organización sin configuración explícita no queda más permisiva
/// que el default, mismo criterio "falla hacia lo más estricto" del resto del proyecto).
///
/// Equivalente a PERMITE_CREAR_DOCUMENTO en referencia-original/PortalSAP_v2, pero vive
/// en la base propia de la plataforma (organizations), no en el SAP del cliente -- la
/// config de plataforma no debería depender de que el SAP del cliente esté disponible
/// (ver CLAUDE.md).
///
/// Engine/DocumentType son string libre, no un enum -- PortalSaas.Data nunca referencia
/// PortalSaas.Abstractions (ver PortalSaas.Data.csproj), así que no puede usar
/// DocumentLineType/SalesDocumentType/etc. directamente. Ver
/// PortalSaas.Core.Comercial.DocumentEngineNames para los valores reales que usa cada
/// motor (espejo de PortalSaas.Abstractions.Modelos.*DocumentType.ToString()).
/// </summary>
public sealed class OrganizationDocumentPermission
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    /// <summary>"Sales" | "Purchase" | "Inventory".</summary>
    public string Engine { get; set; } = null!;

    /// <summary>Nombre del enum de tipo de documento del motor (ej. "SalesOrder", "PurchaseQuotation", "StockTransfer").</summary>
    public string DocumentType { get; set; } = null!;

    public bool CanCreate { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
