namespace PortalSaas.Abstractions.Modelos;

public enum GenericImportFormatCheckSeverity
{
    Ok,
    Info,
    Warning,
    Error,
}

/// <summary>
/// Un hallazgo de la validación de un formato de importación contra SAP -- ver
/// IGenericImportFormatValidator. Area agrupa en pantalla ("Campos núcleo", "Campos de
/// usuario", "Valores fijos", "Envío simulado"...).
/// </summary>
public sealed record GenericImportFormatCheckDto(GenericImportFormatCheckSeverity Severity, string Area, string Message);

/// <summary>Resultado de "Probar con archivo" -- esquema + vista previa con ESE formato + envío simulado del primer documento válido.</summary>
public sealed record GenericImportFormatTestResultDto
{
    public IReadOnlyList<GenericImportFormatCheckDto> Checks { get; init; } = [];
    public GenericImportResultDto? Preview { get; init; }

    /// <summary>Grupo del documento que se simuló (el primero sin errores de fila); null si ninguno se pudo simular.</summary>
    public string? SimulatedGroupingKey { get; init; }
    public SapSimulationResult? Simulation { get; init; }
}
