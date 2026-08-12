namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Progreso de un trabajo de IGenericImportService.CreateDocumentsAsync -- deliberadamente
/// propio (agnóstico de Venta/Compra/Inventario). Portado de ProgresoImportacionGenericaDto.
///
/// Cambio a clase (no record) para soportar tanto el constructor como object initializer,
/// incluyendo propiedades simplificadas para persistencia en BD (TotalRows, ProcessedRows, Status).
/// </summary>
public sealed class GenericImportProgressDto
{
    // Constructor para compatibilidad con llamadas existentes (GenericImportService)
    public GenericImportProgressDto(
        string message,
        int current,
        int total,
        bool success,
        string? details,
        bool finished,
        IReadOnlyList<GenericImportDocumentResultDto>? results = null)
    {
        Message = message;
        Current = current;
        Total = total;
        Success = success;
        Details = details;
        Finished = finished;
        Results = results;
    }

    // Constructor parameterless para object initializer (tests, persistencia)
    public GenericImportProgressDto() { }

    // Propiedades originales
    public string Message { get; set; } = string.Empty;
    public int Current { get; set; }
    public int Total { get; set; }
    public bool Success { get; set; }
    public string? Details { get; set; }
    public bool Finished { get; set; }
    public IReadOnlyList<GenericImportDocumentResultDto>? Results { get; set; }

    // Propiedades simplificadas para persistencia
    public int TotalRows { get; set; }
    public int ProcessedRows { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed record GenericImportDocumentResultDto(string GroupingKey, bool Success, int DocNum, string Message);
