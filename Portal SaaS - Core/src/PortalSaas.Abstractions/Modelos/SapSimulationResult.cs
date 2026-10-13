namespace PortalSaas.Abstractions.Modelos;

/// <summary>Resultado de un envío simulado a Service Layer -- ver ISapSession.SimulatePostAsync.</summary>
public enum SapSimulationOutcome
{
    /// <summary>SAP aceptó el documento (y se revirtió): el JSON es válido para crear.</summary>
    Valid,

    /// <summary>SAP rechazó el documento -- Message trae el error real de Service Layer.</summary>
    Rejected,

    /// <summary>No se pudo determinar (respuesta inesperada o error de conexión) -- Message trae el detalle.</summary>
    Inconclusive,
}

/// <summary>
/// RequestJson: el cuerpo EXACTO que se envió (mismo que usaría la creación real), para
/// poder revisarlo en pantalla aunque SAP lo haya aceptado.
/// </summary>
public sealed record SapSimulationResult(SapSimulationOutcome Outcome, string Message, string RequestJson);
