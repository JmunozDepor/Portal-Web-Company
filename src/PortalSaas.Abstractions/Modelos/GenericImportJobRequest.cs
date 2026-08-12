namespace PortalSaas.Abstractions.Modelos;

/// <summary>Trabajo encolado para GenericImportBackgroundService -- mismos parámetros que ya recibía CreateDocumentsAsync cuando se llamaba directo desde el request HTTP.</summary>
public sealed record GenericImportJobRequest(
    string JobId,
    string PortalUsername,
    GenericImportParametersDto Parameters,
    IReadOnlyList<GenericImportDocumentDto> Documents);
