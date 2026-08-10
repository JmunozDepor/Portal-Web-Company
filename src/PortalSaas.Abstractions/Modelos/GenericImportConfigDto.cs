namespace PortalSaas.Abstractions.Modelos;

/// <summary>Una columna mapeada de la configuración -- ExcelColumn nulo cuando el campo se resuelve solo con FixedValue (ej. un almacén único fijo para todo el archivo, sin columna propia). UserFieldId solo aplica cuando LogicalField = UserField.</summary>
public sealed record GenericImportConfigFieldDto(
    int Id,
    GenericImportLogicalField LogicalField,
    string? ExcelColumn,
    bool IsRequired,
    string? FixedValue,
    int? UserFieldId);

/// <summary>
/// Cabecera de configuración de importación genérica -- BusinessPartnerCardCode null
/// representa el formato ESTÁNDAR (fallback) de la Company+Module+DocumentType+
/// LineType; una fila con CardCode puntual es la excepción de un cliente/proveedor cuyo
/// archivo no sigue el estándar (ver IGenericImportConfigService.ResolveAsync).
/// GroupingColumn null = todo el archivo genera un único documento; con valor, cada
/// valor distinto de esa columna genera su propio documento. PriceSource/
/// SystemPriceListCode solo aplican a líneas de Artículo en Venta/Compra (Servicio no
/// tiene ItemCode, Inventario no tiene precio). Portado de
/// ConfiguracionImportacionGenericaDto -- cuelga de CompanyId (no OrganizationId, ver
/// GenericImportConfig -- regla dura de que todo plugin que dependa de SAP se
/// personaliza por Company), la contraparte más cercana al EmpresaCodigo/HANA del
/// original (mono-tenant) sigue siendo la Company, no la Organization.
/// </summary>
public sealed record GenericImportConfigDto(
    int Id,
    Guid CompanyId,
    GenericImportModule Module,
    string DocumentType,
    GenericImportLineType LineType,
    string? BusinessPartnerCardCode,
    string? GroupingColumn,
    bool SkuIsCustomerOwn,
    string Alias,
    bool IsActive,
    IReadOnlyList<GenericImportConfigFieldDto> Fields,
    GenericImportPriceSource PriceSource = GenericImportPriceSource.BusinessPartner,
    int? SystemPriceListCode = null,
    /// <summary>
    /// true = cada fila del Excel trae su propio socio de negocio (columna mapeada a
    /// GenericImportLogicalField.BusinessPartnerCardCode), en vez de un único socio fijo
    /// para todo el archivo -- carga multi-socio (portado de
    /// ConfiguracionImportacionGenericaDto.SocioNegocioDesdeArchivo). Default false, sin
    /// cambio de comportamiento para configuraciones existentes.
    /// </summary>
    bool BusinessPartnerFromFile = false);
