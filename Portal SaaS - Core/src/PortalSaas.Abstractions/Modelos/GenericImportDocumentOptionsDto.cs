namespace PortalSaas.Abstractions.Modelos;

/// <summary>De dónde sale el almacén de las líneas de Artículo (Venta/Compra).</summary>
public enum GenericImportWarehouseSource
{
    /// <summary>Columna del archivo o valor fijo de la grilla de campos (comportamiento de siempre).</summary>
    Mapping,

    /// <summary>
    /// Campo de usuario del maestro del socio (OCRD.&lt;WarehouseUserField&gt;, ej. U_NX_WhsCode).
    /// Si el socio no lo tiene completo, cae a la columna/valor fijo de la grilla.
    /// </summary>
    BusinessPartnerField,

    /// <summary>
    /// Se pide en la cabecera de Importar (un almacén para todas las líneas), precargado
    /// con el del cliente (OCRD.&lt;WarehouseUserField&gt;). La fila Almacén de la grilla
    /// queda desactivada (2026-10-21, formato genérico "solo detalle").
    /// </summary>
    Screen,
}

/// <summary>De dónde sale la sucursal (ShipToCode) del documento -- Venta e Inventario.</summary>
public enum GenericImportBranchSource
{
    /// <summary>Columna del archivo o valor fijo de la grilla de campos (comportamiento de siempre).</summary>
    Mapping,

    /// <summary>Se pide en la cabecera de Importar (direcciones de despacho del cliente elegido), una para todo el archivo.</summary>
    Screen,
}

/// <summary>Cómo se asigna el vendedor (SlpCode) de los documentos de Venta.</summary>
public enum GenericImportSalesPersonMode
{
    /// <summary>El vendedor por defecto del socio (OCRD.SlpCode) -- comportamiento de siempre.</summary>
    BusinessPartner,

    /// <summary>Un vendedor fijo definido en el formato (SalesPersonCode).</summary>
    Fixed,

    /// <summary>Se pide en la cabecera de Importar (precargado con el vendedor del socio).</summary>
    MultiSeller,
}

/// <summary>
/// Opciones del documento a crear, por formato (2026-10-15): origen del almacén y modo de
/// asignación del vendedor. Se guardan aparte de Create/Update (ver
/// IGenericImportConfigService.SaveDocumentOptionsAsync), mismo criterio que las reglas.
/// </summary>
public sealed record GenericImportDocumentOptionsDto(
    GenericImportWarehouseSource WarehouseSource = GenericImportWarehouseSource.Mapping,
    string? WarehouseUserField = null,
    GenericImportSalesPersonMode SalesPersonMode = GenericImportSalesPersonMode.BusinessPartner,
    int? SalesPersonCode = null,
    // Analizar stock disponible y cumplimiento de unidades en la vista previa (consulta a
    // OITW por artículo/almacén). Opcional (2026-10-15): con archivos de miles de líneas
    // es la consulta más pesada. La regla "Stock disponible" corre aparte si está activa.
    bool AnalyzeStock = false,
    GenericImportBranchSource BranchSource = GenericImportBranchSource.Mapping)
{
    /// <summary>Campo de usuario sugerido para el almacén del cliente.</summary>
    public const string DefaultWarehouseUserField = "U_NX_WhsCode";

    public static GenericImportDocumentOptionsDto Default { get; } = new();
}
