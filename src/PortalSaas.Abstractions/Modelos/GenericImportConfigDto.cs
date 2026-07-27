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
/// representa el formato ESTÁNDAR (fallback) de la Organización+Module+DocumentType+
/// LineType; una fila con CardCode puntual es la excepción de un cliente/proveedor cuyo
/// archivo no sigue el estándar (ver IGenericImportConfigService.ResolveAsync).
/// GroupingColumn null = todo el archivo genera un único documento; con valor, cada
/// valor distinto de esa columna genera su propio documento. PriceSource/
/// SystemPriceListCode solo aplican a líneas de Artículo en Venta/Compra (Servicio no
/// tiene ItemCode, Inventario no tiene precio). Portado de
/// ConfiguracionImportacionGenericaDto -- la única diferencia real es que acá cuelga de
/// OrganizationId (plataforma multi-tenant propia), no de EmpresaCodigo/HANA como el
/// original (mono-tenant).
/// </summary>
public sealed record GenericImportConfigDto(
    int Id,
    Guid OrganizationId,
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
    int? SystemPriceListCode = null);
