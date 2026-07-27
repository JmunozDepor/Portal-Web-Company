namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Resuelve si un tipo de documento de los motores genéricos (Venta/Compra/Inventario)
/// se puede crear desde el portal para la organización activa (ver
/// ICurrentUserContext.OrganizationId) -- override por organización sobre el flag fijo
/// del catálogo estático de cada motor (ver *DocumentTypeCatalog en PortalSaas.Core).
/// Equivalente a IPermiteCrearDocumentoService en referencia-original/PortalSAP_v2,
/// pero resuelto contra la base propia de la plataforma, no contra HANA -- ver
/// PortalSaas.Data.Entities.OrganizationDocumentPermission.
/// </summary>
public interface IOrganizationDocumentPermissionService
{
    /// <summary>
    /// engine: "Sales" | "Purchase" | "Inventory". documentType: el nombre del enum de
    /// tipo de documento del motor (ej. "SalesOrder"). defaultValue: el
    /// DefaultCanCreate del catálogo estático de ese motor, usado si la organización no
    /// tiene ninguna fila de override para esta combinación -- nunca al revés, una
    /// organización sin configuración explícita no queda más permisiva que el default.
    /// </summary>
    Task<bool> IsCreateAllowedAsync(string engine, string documentType, bool defaultValue, CancellationToken ct = default);
}
