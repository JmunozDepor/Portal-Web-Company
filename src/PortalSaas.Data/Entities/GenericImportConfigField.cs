namespace PortalSaas.Data.Entities;

/// <summary>
/// Una columna mapeada de una GenericImportConfig -- ExcelColumn nulo cuando el campo se
/// resuelve solo con FixedValue. UserFieldId solo aplica cuando LogicalField = "UserField"
/// (ver GenericImportUserField). Reemplazado por completo en cada Create/Update de la
/// configuración (borra y vuelve a crear, mismo criterio que ItemCrossReferenceService.SyncAsync)
/// -- el volumen de campos por configuración es chico, no vale la pena diffing.
/// </summary>
public sealed class GenericImportConfigField
{
    public int Id { get; set; }

    public int ConfigId { get; set; }
    public GenericImportConfig Config { get; set; } = null!;

    /// <summary>Nombre del enum GenericImportLogicalField (ej. "ItemCode", "Quantity", "UserField").</summary>
    public string LogicalField { get; set; } = null!;

    public string? ExcelColumn { get; set; }

    public bool IsRequired { get; set; }

    public string? FixedValue { get; set; }

    public int? UserFieldId { get; set; }
    public GenericImportUserField? UserField { get; set; }
}
