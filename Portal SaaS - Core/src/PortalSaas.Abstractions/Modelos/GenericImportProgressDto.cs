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

public sealed record GenericImportDocumentResultDto(string GroupingKey, bool Success, int DocNum, string Message);
