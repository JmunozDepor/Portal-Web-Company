namespace PortalSaas.Abstractions.Modelos;

/// <summary>Categoría de un hallazgo de la validación -- decide en qué panel del análisis aparece (ver GenericImportAnalysisDto).</summary>
public enum GenericImportIssueCategory
{
    BusinessPartner,
    Branch,
    Item,
    Warehouse,
    Quantity,
    Price,
    Stock,
    OrderTotal,
    DuplicateOrder,
    Accounting,
    Other,
}

/// <summary>Un hallazgo de una fila: estructural (GenericImportService.ProcessRow) o de una regla configurable (IsError = regla en Bloqueante).</summary>
public sealed record GenericImportIssueDto(GenericImportIssueCategory Category, bool IsError, string Message);

/// <summary>
/// Análisis de la vista previa, precalculado en Core (GenericImportAnalysisBuilder) para
/// que la pantalla de Importar y el Excel de validación muestren exactamente lo mismo.
/// Dos niveles (pedido del dueño del proyecto, 2026-10-14): Resumen por OC (la OC = el
/// grupo/documento, agrupación máxima) y paneles de Análisis por tipo de hallazgo.
/// </summary>
public sealed record GenericImportAnalysisDto
{
    public IReadOnlyList<GenericImportOrderSummaryDto> Orders { get; init; } = [];
    public GenericImportTotalsDto Totals { get; init; } = new();
    public IReadOnlyList<GenericImportBranchIssueDto> BranchIssues { get; init; } = [];
    public IReadOnlyList<GenericImportItemIssueDto> ItemIssues { get; init; } = [];
    public IReadOnlyList<GenericImportPriceComparisonDto> PriceComparisons { get; init; } = [];
    public IReadOnlyList<GenericImportFulfillmentDto> Fulfillment { get; init; } = [];

    /// <summary>OC del archivo que ya están cargadas en SAP (regla DuplicateOrderInSap), una por OC.</summary>
    public IReadOnlyList<GenericImportDuplicateOrderDto> DuplicateOrders { get; init; } = [];

    /// <summary>Hallazgos que no caen en Sucursal/Artículo/Precio/Cumplimiento (socio, cantidad, almacén, contabilidad, descuento total de la OC...).</summary>
    public IReadOnlyList<GenericImportOtherIssueDto> OtherIssues { get; init; } = [];
}

public sealed record GenericImportTotalsDto
{
    public int Orders { get; init; }
    public int Lines { get; init; }
    public int LinesWithErrors { get; init; }
    public int LinesWithWarnings { get; init; }
    public decimal Units { get; init; }

    /// <summary>Σ Cantidad × Precio del archivo (solo líneas con precio).</summary>
    public decimal OrderAmount { get; init; }

    /// <summary>Σ Cantidad × Precio SAP (solo líneas con precio SAP).</summary>
    public decimal SapAmount { get; init; }

    public decimal UnitsFulfillable { get; init; }
}

/// <summary>Una OC = un grupo de la vista previa = un documento SAP.</summary>
public sealed record GenericImportOrderSummaryDto
{
    public string GroupingKey { get; init; } = string.Empty;
    public string? CustomerReferenceNumber { get; init; }
    public string? BusinessPartnerCardCode { get; init; }
    public string? BusinessPartnerName { get; init; }
    public string? Branches { get; init; }
    public int Lines { get; init; }
    public int LinesWithErrors { get; init; }
    public int LinesWithWarnings { get; init; }
    public decimal Units { get; init; }
    public decimal OrderAmount { get; init; }
    public decimal SapAmount { get; init; }

    /// <summary>
    /// Descuento total de la OC respecto del precio SAP: (Σ SAP − Σ OC) / Σ SAP × 100,
    /// solo sobre líneas que tienen los dos precios. Null si ninguna línea es comparable.
    /// Negativo = la OC paga más que la lista.
    /// </summary>
    public decimal? DiscountPercent { get; init; }

    public decimal UnitsFulfillable { get; init; }
    public bool CanCreate { get; init; }
}

public sealed record GenericImportBranchIssueDto(string Branch, string Orders, int Lines, string Message);

/// <summary>SourceCode = lo que traía el archivo (ItemCode, barra o SKU según el formato).</summary>
public sealed record GenericImportItemIssueDto(string SourceCode, string? Description, string Orders, int Lines, decimal Units, string Message);

public sealed record GenericImportPriceComparisonDto(
    int RowNumber,
    string GroupingKey,
    string? ItemCode,
    string? Description,
    decimal Quantity,
    decimal OrderPrice,
    decimal SapPrice,
    /// <summary>true = la regla de precio configurada marcó esta línea (fuera de tolerancia); false = diferencia dentro de tolerancia o sin regla de precio activa.</summary>
    bool OutOfTolerance)
{
    public decimal Difference => OrderPrice - SapPrice;
    public decimal? DifferencePercent => SapPrice == 0 ? null : Math.Round((OrderPrice - SapPrice) / SapPrice * 100m, 2);
}

/// <summary>Cumplimiento por artículo + almacén: unidades pedidas vs. stock disponible (en stock − comprometido).</summary>
public sealed record GenericImportFulfillmentDto(
    string ItemCode,
    string? Description,
    string Warehouse,
    decimal UnitsOrdered,
    decimal StockAvailable,
    decimal UnitsFulfillable)
{
    public decimal FulfillmentPercent => UnitsOrdered == 0 ? 100m : Math.Round(UnitsFulfillable / UnitsOrdered * 100m, 1);
}

public sealed record GenericImportDuplicateOrderDto(string GroupingKey, string? CustomerReferenceNumber, int Lines, bool IsError, string Message);

public sealed record GenericImportOtherIssueDto(int RowNumber, string GroupingKey, GenericImportIssueCategory Category, bool IsError, string Message);
