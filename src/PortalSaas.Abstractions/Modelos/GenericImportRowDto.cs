namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Una fila del archivo ya cruzada contra los catálogos y la configuración vigente -- ver
/// IGenericImportService.ProcessFileAsync. Todos los campos núcleo que no aplican al
/// LineType de la configuración (ej. Account en una fila de Artículo) quedan null sin
/// ser un error. UserFieldsHeader/UserFieldsLine usan SapFieldName como clave y ya vienen
/// parseados al tipo declarado. Portado de FilaImportacionGenericaDto.
/// </summary>
public sealed record GenericImportRowDto
{
    /// <summary>Fila del archivo (1 = primera fila de datos, sin contar el encabezado) -- para la bitácora de errores.</summary>
    public int RowNumber { get; init; }

    /// <summary>Valor de la columna de agrupamiento (o una clave fija única si la configuración no define GroupingColumn) -- determina en qué documento cae esta fila.</summary>
    public string GroupingKey { get; init; } = string.Empty;

    public string? CustomerReferenceNumber { get; init; }
    public string? Branch { get; init; }

    public string? ItemCode { get; init; }
    public string? ItemName { get; init; }
    public string? Description { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? DiscountPercent { get; init; }
    public string? Warehouse { get; init; }
    public string? WarehouseName { get; init; }
    public string? Account { get; init; }
    public string? AccountName { get; init; }
    public string? CostCenter { get; init; }
    public string? CostCenterName { get; init; }
    public string? Dimension2 { get; init; }
    public string? Dimension2Name { get; init; }
    public string? Dimension3 { get; init; }
    public string? Dimension3Name { get; init; }

    // Solo Module = Inventory.
    public string? SourceWarehouse { get; init; }
    public string? SourceWarehouseName { get; init; }
    public string? DestinationWarehouse { get; init; }
    public string? DestinationWarehouseName { get; init; }

    /// <summary>Precio de referencia de SAP (informativo) -- nunca se postea, solo se muestra si UnitPrice vino vacío.</summary>
    public decimal? SapReferencePrice { get; init; }

    public IReadOnlyDictionary<string, object?> UserFieldsHeader { get; init; } = new Dictionary<string, object?>();
    public IReadOnlyDictionary<string, object?> UserFieldsLine { get; init; } = new Dictionary<string, object?>();

    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>Grupo de filas que va a generar un único documento SAP -- GroupingKey identifica el grupo (ver GenericImportConfigDto.GroupingColumn).</summary>
public sealed record GenericImportDocumentDto
{
    public string GroupingKey { get; init; } = string.Empty;
    public IReadOnlyList<GenericImportRowDto> Rows { get; init; } = [];

    /// <summary>true solo si TODAS las filas del grupo son válidas -- un documento no se crea parcialmente.</summary>
    public bool CanCreate => Rows.Count > 0 && Rows.All(r => r.IsValid);
}

/// <summary>Resultado de procesar el archivo completo -- ver IGenericImportService.ProcessFileAsync.</summary>
public sealed record GenericImportResultDto
{
    /// <summary>false si no hay configuración (ni del socio ni estándar de la organización) o el archivo no corresponde a las columnas esperadas -- Documents queda vacío en ese caso.</summary>
    public bool HasValidConfig { get; init; }

    public string? ErrorMessage { get; init; }

    public IReadOnlyList<GenericImportDocumentDto> Documents { get; init; } = [];
}
