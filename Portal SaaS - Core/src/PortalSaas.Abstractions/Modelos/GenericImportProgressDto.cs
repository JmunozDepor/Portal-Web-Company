namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Progreso de un trabajo de IGenericImportService.CreateDocumentsAsync -- deliberadamente
/// propio (agnóstico de Venta/Compra/Inventario). Portado de ProgresoImportacionGenericaDto.
/// </summary>
public sealed record GenericImportProgressDto(
    string Message,
    int Current,
    int Total,
    bool Success,
    string? Details,
    bool Finished,
    IReadOnlyList<GenericImportDocumentResultDto>? Results = null);

/// <summary>
/// Resultado de UNA OC al confirmar -- reporte final de la importación (2026-10-15): se
/// informan TODAS las OC del archivo, las creadas (Status = Created, con DocEntry/DocNum/
/// total real de SAP), las que SAP rechazó (SapError) y las que no se enviaron por errores
/// de validación (Skipped). Los datos de cabecera salen del mismo análisis de la vista
/// previa (GenericImportOrderSummaryDto).
/// </summary>
public sealed record GenericImportDocumentResultDto(string GroupingKey, bool Success, int DocNum, string Message)
{
    /// <summary>"Created" | "SapError" | "Skipped".</summary>
    public string Status { get; init; } = Success ? "Created" : "SapError";

    public int DocEntry { get; init; }
    public string? CustomerReferenceNumber { get; init; }
    public string? Branches { get; init; }
    public string? BusinessPartnerCardCode { get; init; }
    public string? BusinessPartnerName { get; init; }
    public string? SalesEmployee { get; init; }
    public int Lines { get; init; }
    public decimal Units { get; init; }

    /// <summary>Σ Cantidad × Precio del archivo.</summary>
    public decimal OrderAmount { get; init; }

    /// <summary>DocTotal real del documento creado en SAP (con impuestos) -- null si no se creó o el tipo no tiene total (Inventario).</summary>
    public decimal? SapDocTotal { get; init; }
}
