namespace PortalSaas.Data.Entities;

/// <summary>
/// Cabecera de configuración de Modulo.ImportacionGenerica, por organización --
/// equivalente a CONFIGURACION_IMPORTACION_GENERICA en referencia-original/PortalSAP_v2,
/// pero vive en la base propia de la plataforma (organization_id), no en HANA -- mismo
/// criterio que OrganizationDocumentPermission (la config de plataforma no debería
/// depender de que el SAP del cliente esté disponible).
///
/// BusinessPartnerCardCode null = formato ESTÁNDAR (fallback) de la
/// Organización+Module+DocumentType+LineType; una fila con CardCode puntual es la
/// excepción de un cliente/proveedor cuyo archivo no sigue el estándar (ver
/// IGenericImportConfigService.ResolveAsync). Module/LineType/PriceSource son string
/// libre -- PortalSaas.Data nunca referencia PortalSaas.Abstractions.
/// </summary>
public sealed class GenericImportConfig
{
    public int Id { get; set; }

    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    /// <summary>"Sales" | "Purchase" | "Inventory".</summary>
    public string Module { get; set; } = null!;

    /// <summary>Nombre del enum de tipo de documento del motor (ej. "SalesOrder", "PurchaseQuotation", "StockTransfer").</summary>
    public string DocumentType { get; set; } = null!;

    /// <summary>"Item" | "Service".</summary>
    public string LineType { get; set; } = null!;

    /// <summary>Null = configuración estándar de la organización; con valor = excepción de ese socio de negocio puntual.</summary>
    public string? BusinessPartnerCardCode { get; set; }

    /// <summary>Null = todo el archivo genera un único documento; con valor, cada valor distinto de esa columna genera su propio documento.</summary>
    public string? GroupingColumn { get; set; }

    public bool SkuIsCustomerOwn { get; set; }

    public string Alias { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    /// <summary>"BusinessPartner" | "System" -- solo aplica a líneas de Artículo en Venta/Compra.</summary>
    public string PriceSource { get; set; } = "BusinessPartner";

    public int? SystemPriceListCode { get; set; }

    public List<GenericImportConfigField> Fields { get; set; } = [];
}
