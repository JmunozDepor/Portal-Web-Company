namespace PortalSaas.Data.Entities;

/// <summary>
/// Cabecera de configuración de Modulo.ImportacionGenerica, por COMPAÑÍA -- equivalente
/// a CONFIGURACION_IMPORTACION_GENERICA en referencia-original/PortalSAP_v2 (que ya era
/// por Empresa/CardCode SAP), pero vive en la base propia de la plataforma (company_id),
/// no en HANA -- mismo criterio que OrganizationDocumentPermission (la config de
/// plataforma no debería depender de que el SAP del cliente esté disponible).
///
/// CompanyId, no OrganizationId -- regla dura del proyecto (ver CLAUDE.md, "Toda tabla
/// bajo un plugin que dependa de datos SAP se personaliza por Company, nunca por
/// Organization directo"): el layout de columnas de un Excel de importación es propio de
/// la Company (cada Company puede tener su propio SAP con UDFs/series/almacenes
/// distintos, aunque compartan Organization), igual que ya ocurre con
/// OrganizationDocumentPermission/UserMenuProfile. Company ya resuelve a Organization
/// (Company.OrganizationId) -- no hace falta duplicar OrganizationId acá.
///
/// BusinessPartnerCardCode null = formato ESTÁNDAR (fallback) de la
/// Company+Module+DocumentType+LineType; una fila con CardCode puntual es la excepción
/// de un cliente/proveedor cuyo archivo no sigue el estándar (ver
/// IGenericImportConfigService.ResolveAsync). Module/LineType/PriceSource son string
/// libre -- PortalSaas.Data nunca referencia PortalSaas.Abstractions.
/// </summary>
public sealed class GenericImportConfig
{
    public int Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

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

    /// <summary>true = carga multi-socio, cada fila del Excel trae su propio CardCode -- ver GenericImportLogicalField.BusinessPartnerCardCode. Default false.</summary>
    public bool BusinessPartnerFromFile { get; set; }

    public List<GenericImportConfigField> Fields { get; set; } = [];
}
